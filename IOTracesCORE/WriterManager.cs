using IOTracesCORE.cloudstorage;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using ZstdSharp;

namespace IOTracesCORE
{
    class WriterManager
    {
        private string dir_path;
        private bool is_dev_mode;
        private bool is_low_overhead;
        private string fs_filepath;
        private string block_filepath;
        private string mr_filepath;
        private string nw_filepath;
        private string fs_snap_filepath;
        private string process_snap_filepath;

        private readonly StringBuilder fs_sb;
        private readonly StringBuilder block_sb;
        private readonly StringBuilder mr_sb;
        private readonly StringBuilder nw_sb;
        private readonly StringBuilder fs_snap_sb;
        private readonly StringBuilder process_snap_sb;

        // Each StringBuilder + its file-rotation state is guarded by a per-trace-type
        // lock. Most trace types are written only from the single ETW Process() thread,
        // but the filesystem type is also written from the FilesystemHandlers flush
        // timer (a thread-pool thread) when a deferred filename finally resolves, and
        // the snapshot types are written from their own snapper threads. StringBuilder
        // is not thread-safe, and FlushWrite mutates the rotating filepath fields, so
        // every append + flush sequence must hold the matching lock.
        private readonly object fs_lock = new();
        private readonly object block_lock = new();
        private readonly object mr_lock = new();
        private readonly object nw_lock = new();
        private readonly object fs_snap_lock = new();
        private readonly object process_snap_lock = new();

        private object GetLock(string tracetype) => tracetype switch
        {
            "disk" => block_lock,
            "memory" => mr_lock,
            "network" => nw_lock,
            "process" => process_snap_lock,
            "filesystem_snapshot" => fs_snap_lock,
            _ => fs_lock, // "filesystem" and any fallback
        };

        private int fs_snap_part_counter = 1;
        private string empty_filename_filepath;

        private ObjectStorageHandler obj_storage;

        private const double MEMORY_PRESSURE_RATIO = 0.01;
        // Max in-memory buffer per stream before a flush. On flush the ETW consumer thread
        // only copies this StringBuilder to a string (pure CPU) and hands it to the
        // background thread, which does BOTH the raw-CSV write and the compression — so the
        // size no longer bounds any disk-I/O stall on the consumer thread (see _compressThread),
        // only the transient string copy and the chunk/file granularity.
        private const long ABSOLUTE_MAX_BYTES = 64L * 1024 * 1024; // 64 MB
        private static readonly TimeSpan MIN_FLUSH_INTERVAL = TimeSpan.FromSeconds(10);
        private static DateTime _lastFlushUtc = DateTime.UtcNow;

        // ── Background raw-write + compression ────────────────────────────────
        // Both the raw-CSV write and the zstd compression of a multi-MB chunk must run
        // OFF the ETW Process() consumer thread. The raw write is the subtle one: it lands
        // on the same storage device the tracer is observing, so a real device stall
        // (observed: EBS write p99 ~15s, laptop NVMe ~9s) blocks the consumer for seconds,
        // the kernel real-time buffers overflow, and once the consumer is chronically
        // behind the kernel drops the highest-volume provider (FileIO) wholesale for the
        // rest of the session — the "fs/ truncated to a startup burst" pathology. So the
        // flush path only snapshots the buffered rows to a string (pure CPU) under the
        // lock and hands them to this single background thread, which writes the raw CSV,
        // compresses it, and queues it for upload. Unbounded queue: Add never blocks the
        // ETW thread (we prefer transient heap growth to permanently dropped kernel events).
        // Content is non-null only for the high-volume continuous streams whose raw write is
        // deferred to this thread; for low-volume snapshot streams the raw file is already on
        // disk (written inline) and Content is null, meaning "compress the existing file".
        private readonly record struct CompressJob(
            string RawPath, string? Content, bool NeedsHeader, string TraceType, bool IsFinalFsSnap, int TotalParts);
        private readonly BlockingCollection<CompressJob> _compressQueue = new();
        private readonly Thread _compressThread;

        // ── Off-thread filesystem formatting ──────────────────────────────────
        // The fs/ stream is ~99% of event volume and is what overflows the kernel
        // buffers. CSV-formatting each event (CsvHelper, 23 fields) is pure CPU but,
        // done inline on the single ETW Process() consumer thread, it caps how fast
        // that thread can drain the kernel buffers — so a sustained high-rate burst
        // (e.g. a VS Code install at ~30k ev/s on 2 vCPUs) overflows the buffers and
        // the kernel drops FileIO wholesale, regardless of where the disk write lands.
        // So the ETW callback now only extracts the event fields (unavoidably on its
        // thread, since TraceEvent data is recycled) and enqueues the FilesystemTrace;
        // this background thread does the formatting + lock + append + flush-trigger.
        // The consumer thread's per-event cost drops to a non-blocking TryAdd, so it keeps
        // draining the kernel buffers at line rate.
        //
        // The queue is BOUNDED. An unbounded queue (v2.4.7) hid the failure: when the
        // formatter could not keep up, the kernel honestly reported 0 lost events (the
        // consumer kept up) while the queue grew without bound and the fs file silently
        // froze. Bounding it turns that into an EXPLICIT, COUNTED drop (TryAdd fail ->
        // TraceStats.IncFsFormatDropped), surfaced in the manifest, and keeps memory finite.
        // Capacity is generous (seconds of burst headroom) but finite.
        //
        // N parallel formatter threads: FormatAsCsv (CsvHelper, 23 fields) is the
        // parallelizable per-event cost; only the cheap append+flush-trigger is serialized
        // under fs_lock. So formatting throughput scales past one core — the v2.4.7 single
        // formatter could not keep up with a ~30k ev/s burst on a multi-vCPU box.
        private const int FS_FORMAT_QUEUE_CAPACITY = 250_000;
        private readonly BlockingCollection<FilesystemTrace> _fsFormatQueue =
            new(FS_FORMAT_QUEUE_CAPACITY);
        private readonly Thread[] _fsFormatThreads;

        // Cache GC memory info to avoid querying it on every event write
        private static long _cachedAvailableMemory = 0;
        private static DateTime _memCacheTime = DateTime.MinValue;
        private static readonly TimeSpan MEM_CACHE_TTL = TimeSpan.FromSeconds(5);

        private static long GetAvailableMemoryCached()
        {
            if (DateTime.UtcNow - _memCacheTime > MEM_CACHE_TTL)
            {
                _cachedAvailableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                _memCacheTime = DateTime.UtcNow;
            }
            return _cachedAvailableMemory;
        }

        private bool is_anonymous;
        private bool is_upload_automatically;
        public static int amount_compressed_file = 0;
        public static int disk_event_counter = 0;
        public static long file_event_counter = 0;
        public static int memory_event_counter = 0;
        public static int fs_snapshot_file_count = 0;
        public static bool fs_snapshot_complete = false;
        public static TimeSpan active_session = TimeSpan.FromSeconds(0);
        public static TimeSpan trace_duration = TimeSpan.FromSeconds(0);

        private readonly DateTime _sessionStartUtc = DateTime.UtcNow;

        // Set once the shutdown drain begins so the periodic manifest refresh stops and
        // cannot overwrite the authoritative final manifest with a non-final one.
        private volatile bool _finalizing = false;

        public WriterManager(string dirpath, bool is_anonymous, bool upload, ObjectStorageHandler obj, bool dev_mode = false, bool low_overhead = false)
        {
            amount_compressed_file = 0;
            utils.TraceStats.Reset();

            fs_sb = new StringBuilder();
            block_sb = new StringBuilder();
            mr_sb = new StringBuilder();
            nw_sb = new StringBuilder();
            fs_snap_sb = new StringBuilder();
            process_snap_sb = new StringBuilder();

            obj_storage = obj;
            is_upload_automatically = upload;
            is_dev_mode = dev_mode;
            is_low_overhead = low_overhead;


            dir_path = $"{dirpath}\\{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            fs_filepath = GenerateFilePath("fs");
            block_filepath = GenerateFilePath("block");
            mr_filepath = GenerateFilePath("mr");
            nw_filepath = GenerateFilePath("nw");
            process_snap_filepath = GenerateFilePath("process");
            fs_snap_filepath = GenerateFilePathWithPart("filesystem_snapshot", fs_snap_part_counter);

            string tmp_dir = Path.Combine(dirpath, "tmp");
            empty_filename_filepath = Path.Combine(tmp_dir, $"empty_filenames_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
            this.is_anonymous = is_anonymous;

            _compressThread = new Thread(CompressLoop)
            {
                IsBackground = true,
                Name = "trace-compressor"
            };
            _compressThread.Start();

            // BlockingCollection supports multiple concurrent consumers, so several
            // formatter threads can drain the one queue in parallel. Leave headroom for the
            // ETW consumer + compressor + snapper threads; at least 1, at most 4.
            int nFormatters = Math.Clamp(Environment.ProcessorCount - 2, 1, 4);
            _fsFormatThreads = new Thread[nFormatters];
            for (int i = 0; i < nFormatters; i++)
            {
                _fsFormatThreads[i] = new Thread(FsFormatLoop)
                {
                    IsBackground = true,
                    Name = $"fs-formatter-{i}"
                };
                _fsFormatThreads[i].Start();
            }

            StartEventRateDetector();
        }

        private void CompressLoop()
        {
            foreach (var job in _compressQueue.GetConsumingEnumerable())
            {
                try
                {
                    ProcessCompressJob(job);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[compressor] Error compressing {job.RawPath}: {ex.Message}");
                }
            }
        }

        // Compress one rotated raw CSV and queue it for upload. Runs on the dedicated
        // _compressThread, never on the ETW Process() thread. Mirrors the upload/rename
        // semantics the inline flush used to perform.
        private void ProcessCompressJob(CompressJob job)
        {
            // For deferred high-volume streams the raw write happens here (off the ETW
            // consumer thread); snapshot streams already wrote their raw file inline.
            if (job.Content != null)
                WriteRawCsv(job.RawPath, job.Content, job.NeedsHeader, job.TraceType);
            string compressed_fp = CompressFile(job.RawPath);

            if (job.IsFinalFsSnap && job.TraceType.Equals("filesystem_snapshot"))
            {
                string dir = Path.GetDirectoryName(compressed_fp) ?? dir_path;
                string filename = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(compressed_fp)); // Remove .csv.zst
                string newFilename = $"{filename}_complete_parts{job.TotalParts}.csv.zst";
                string newPath = Path.Combine(dir, newFilename);

                File.Move(compressed_fp, newPath);
                compressed_fp = newPath;
                Debug.WriteLine($"Filesystem snapshot completed with {job.TotalParts} parts. Final file: {newPath}");
            }

            if (is_upload_automatically)
            {
                // High-volume continuous trace types are buffered locally and uploaded in
                // 100 MB / 20 min batches. Snapshot types keep their part/complete file
                // semantics, so they upload directly.
                if (IsBufferedTraceType(job.TraceType))
                {
                    obj_storage.BufferFile(compressed_fp);
                }
                else
                {
                    obj_storage.QueueFile(compressed_fp);
                }
            }

            WriteStatus();
        }

        public void InitiateDirectory()
        {
            EnsureDirectoryExists(dir_path);
            string? fs_folder = Path.GetDirectoryName(fs_filepath) ?? throw new Exception("Invalid directory path.");
            string? block_folder = Path.GetDirectoryName(block_filepath) ?? throw new Exception("Invalid directory path.");
            string? mr_folder = Path.GetDirectoryName(mr_filepath) ?? throw new Exception("Invalid directory path.");
            string? nw_folder = Path.GetDirectoryName(nw_filepath) ?? throw new Exception("Invalid directory path.");
            string? proc_snap_folder = Path.GetDirectoryName(process_snap_filepath) ?? throw new Exception("Invalid directory path.");
            string? fs_snap_folder = Path.GetDirectoryName(fs_snap_filepath) ?? throw new Exception("Invalid directory path.");


            EnsureDirectoryExists(fs_folder);
            EnsureDirectoryExists(block_folder);
            EnsureDirectoryExists(mr_folder);
            EnsureDirectoryExists(proc_snap_folder);
            EnsureDirectoryExists(fs_snap_folder);
            EnsureDirectoryExists(nw_folder);

            Console.WriteLine("File output: {0}", this.dir_path);

            // Write an initial manifest so the session's schema/start are on disk (and
            // uploaded) even if the process is killed before a clean shutdown. Refreshed
            // periodically by EventRateDetector and finalized in CompressAllAsync.
            WriteManifest(final: false, upload: true);
        }

        /// <summary>
        /// Writes the per-session manifest.json (authoritative schema + counters,
        /// including the live ETW lost-event count). Called at start, periodically
        /// during the session, and once at shutdown (final: stop time + dead probes).
        /// </summary>
        /// <param name="final">Final manifest (stop time, dead-probe detection).</param>
        /// <param name="upload">
        /// Queue the manifest for upload. The final manifest always uploads. Periodic
        /// and initial manifests upload too so a session that is killed before clean
        /// shutdown still has a recent manifest (with EventsLost) server-side — the
        /// reason truncated captures previously had no manifest at all.
        /// </param>
        public void WriteManifest(bool final, bool upload = false)
        {
            try
            {
                string json = utils.TraceManifest.Build(
                    final, PathHasher.deviceId, is_anonymous, is_upload_automatically,
                    _sessionStartUtc, final ? DateTime.UtcNow : (DateTime?)null, is_low_overhead);

                // Own subfolder so the upload path's trace_type (derived from the
                // parent folder name) is a clean "manifest", alongside fs/, block/, etc.
                string manifestDir = Path.Combine(dir_path, "manifest");
                EnsureDirectoryExists(manifestDir);
                string path = Path.Combine(manifestDir, "manifest.json");

                // Write to a temp file then atomically replace, so a periodic refresh can
                // never be observed (by a reader or the uploader) as a torn/partial JSON.
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(tmp, path, overwrite: true);

                if ((final || upload) && is_upload_automatically)
                {
                    obj_storage.QueueFile(path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to write manifest: {ex.Message}");
            }
        }

        private void StartEventRateDetector()
        {
            Debug.WriteLine("Starting event rate detector thread...");
            Thread eventRateThread = new(EventRateDetector)
            {
                IsBackground = true
            };
            eventRateThread.Start();
        }

        // How often the manifest is refreshed + re-uploaded mid-session, so a killed
        // capture still has a recent manifest (counters + live EventsLost) server-side.
        private const int MANIFEST_REFRESH_SECONDS = 30;
        // Max seconds buffered continuous-stream rows may sit unflushed before a
        // timer-driven flush forces them out regardless of size. Bounds how much low-rate
        // data is stranded in memory (and lost on an unclean kill) when the size threshold
        // is never reached. Timer-driven, so it also fires after a burst goes silent — when
        // no new event would ever re-trigger the event-driven size check.
        private const int IDLE_FLUSH_SECONDS = 15;

        private void EventRateDetector()
        {
            int secondsSinceManifest = 0;
            int secondsSinceIdleFlush = 0;
            while (true)
            {
                Thread.Sleep(1000);
                // Atomically read-and-reset so increments racing on the ETW thread are not lost.
                int events_in_interval = Interlocked.Exchange(ref disk_event_counter, 0);
                //Debug.WriteLine($"Rate: {events_in_interval}");
                if (events_in_interval > 100)
                {
                    active_session += TimeSpan.FromSeconds(1);
                }

                if (++secondsSinceIdleFlush >= IDLE_FLUSH_SECONDS)
                {
                    secondsSinceIdleFlush = 0;
                    // Skip during the shutdown drain (CompressAllAsync flushes everything).
                    if (!_finalizing)
                    {
                        try { FlushIdleBuffers(); }
                        catch (Exception ex) { Debug.WriteLine($"[idle-flush] {ex.Message}"); }
                    }
                }

                if (++secondsSinceManifest >= MANIFEST_REFRESH_SECONDS)
                {
                    secondsSinceManifest = 0;
                    // Skip once shutdown's final manifest is being written (see _finalizing).
                    if (!_finalizing)
                    {
                        try { WriteManifest(final: false, upload: true); }
                        catch (Exception ex) { Debug.WriteLine($"[manifest] periodic refresh failed: {ex.Message}"); }
                    }
                }
            }
        }

        // Flush any non-empty continuous-stream buffer (fs/disk/memory/network) so low-rate
        // or post-burst data is not stranded in memory until a size-triggered flush. Snapshot
        // streams (process, filesystem_snapshot) are excluded — their part-counter /
        // completeness file naming relies on their own flush points. Runs on the
        // EventRateDetector thread (never the ETW consumer thread).
        private void FlushIdleBuffers()
        {
            FlushIfBuffered(fs_sb, fs_filepath, "filesystem");
            FlushIfBuffered(block_sb, block_filepath, "disk");
            FlushIfBuffered(mr_sb, mr_filepath, "memory");
            FlushIfBuffered(nw_sb, nw_filepath, "network");
        }

        // Flush one continuous-stream buffer iff it holds data. The length is re-checked
        // under the lock so this never emits an empty (header-only) shard and never races a
        // concurrent size-triggered flush from a formatter/writer thread. The cheap
        // unsynchronized pre-check avoids waiting on the reconnect gate when already empty.
        private void FlushIfBuffered(StringBuilder sb, string filepath, string tracetype)
        {
            if (sb.Length == 0) return;
            ObjectStorageHandler.ResumeGate.Wait();
            lock (GetLock(tracetype))
            {
                if (sb.Length > 0)
                    FlushWriteLocked(sb, filepath, tracetype);
            }
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "\"\"";

            if (field.Contains(',') || field.Contains('\n') || field.Contains('\r') || field.Contains('"'))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }

            return $"\"{field}\"";
        }

        public void Write(FilesystemInfo fs)
        {
            ObjectStorageHandler.ResumeGate.Wait();
            lock (fs_snap_lock)
            {
                fs_snapshot_file_count++;
                utils.TraceStats.IncFilesystemSnapshot();
                fs_snap_sb.Append(fs.FormatAsCsv());
                if (IsTimeToFlush(fs_snap_sb, true))
                {
                    FlushWriteLocked(fs_snap_sb, fs_snap_filepath, "filesystem_snapshot");
                }
            }
        }

        public void Write(IEnumerable<ProcessInfo> pcs)
        {
            ObjectStorageHandler.ResumeGate.Wait();
            lock (process_snap_lock)
            {
                foreach (var pc in pcs)
                {
                    if (pc.Name.Equals("IOTracesCORE"))
                    {
                        continue;
                    }
                    utils.TraceStats.AddProcessSnapshotRows(1);
                    process_snap_sb.Append(pc.FormatAsCsv());
                }

                if (process_snap_sb.Length > 0)
                {
                    FlushWriteLocked(process_snap_sb, process_snap_filepath, "process");
                }
            }
        }

        // Called on the ETW Process() consumer thread (and the FilesystemHandlers flush
        // timer for late-resolved filenames). Does only the cheap drop-our-own-events
        // guards, then hands the event to the background formatter — the expensive CSV
        // formatting + lock + append + flush all run off this thread (see _fsFormatThreads)
        // so the consumer keeps draining the kernel buffers and FileIO is not dropped.
        public void Write(FilesystemTrace data)
        {
            if (data.Comm.Equals("IOTracesCORE")) return;
            if (data.Filename != null && data.Filename.Contains("IOTracer", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Non-blocking: the ETW consumer thread must never block here. If the bounded
            // queue is full (formatters can't keep up), record an explicit, counted drop so
            // the loss is visible in the manifest instead of silently lost. Also publish the
            // live queue depth so the manifest shows whether the formatters are keeping up.
            bool added;
            try { added = _fsFormatQueue.TryAdd(data, 0); }   // 0 timeout => never blocks the ETW thread
            catch (InvalidOperationException)
            {
                // Queue already marked complete (shutdown). Format inline as a fallback so
                // a late event (e.g. an in-flight deferred-filename flush) is not lost.
                FormatAndAppendFs(data);
                return;
            }
            if (added) utils.TraceStats.NoteFsFormatQueueDepth(_fsFormatQueue.Count);
            else utils.TraceStats.IncFsFormatDropped();
        }

        private void FsFormatLoop()
        {
            foreach (var data in _fsFormatQueue.GetConsumingEnumerable())
            {
                try { FormatAndAppendFs(data); }
                catch (Exception ex) { Debug.WriteLine($"[fs-formatter] {ex.Message}"); }
            }
        }

        // The hot-path work moved off the ETW consumer thread: CSV-format the event, then
        // (waiting on the reconnect gate OUTSIDE the lock) append under fs_lock and flush
        // at the threshold. Runs on the _fsFormatThreads workers (synchronized by fs_lock),
        // or inline only as the shutdown-race fallback in Write().
        private void FormatAndAppendFs(FilesystemTrace data)
        {
            string csv = data.FormatAsCsv(is_anonymous);
            ObjectStorageHandler.ResumeGate.Wait();
            lock (fs_lock)
            {
                file_event_counter += 1;
                utils.TraceStats.IncFilesystem();
                fs_sb.Append(csv);
                if (IsTimeToFlush(fs_sb))
                {
                    FlushWriteLocked(fs_sb, fs_filepath, "filesystem");
                }
            }
        }

        public void Write(DiskTrace data)
        {
            if (data.Comm.Equals("IOTracesCORE"))
            {
                return;
            }

            ObjectStorageHandler.ResumeGate.Wait();
            lock (block_lock)
            {
                Interlocked.Increment(ref disk_event_counter);
                utils.TraceStats.IncDisk();
                block_sb.Append(data.FormatAsCsv());
                if (IsTimeToFlush(block_sb))
                {
                    FlushWriteLocked(block_sb, block_filepath, "disk");
                }
            }
        }

        public void Write(NetworkTrace data)
        {
            if (data.Comm.Equals("IOTracesCORE"))
            {
                return;
            }

            ObjectStorageHandler.ResumeGate.Wait();
            lock (nw_lock)
            {
                utils.TraceStats.IncNetworkRow();
                nw_sb.Append(data.FormatAsCsv());
                if (IsTimeToFlush(nw_sb, lowThreshold: true))
                {
                    Debug.WriteLine("Flushing network trace");
                    FlushWriteLocked(nw_sb, nw_filepath, "network");
                }
            }
        }

        public void Write(MemoryTrace data)
        {
            if (data.Comm.Equals("IOTracesCORE"))
            {
                return;
            }

            ObjectStorageHandler.ResumeGate.Wait();
            lock (mr_lock)
            {
                memory_event_counter += 1;
                utils.TraceStats.IncMemory();
                mr_sb.Append(data.FormatAsCsv());
                // Debug.WriteLine(data.FormatAsCsv());
                if (IsTimeToFlush(mr_sb))
                {
                    FlushWriteLocked(mr_sb, mr_filepath, "memory");
                }
            }
        }

        public void LogEmptyFilename(DateTime ts, string op, int pid, int tid, string comm)
        {
            if (!is_dev_mode) return;

            try
            {
                string? dir = Path.GetDirectoryName(empty_filename_filepath);
                if (!string.IsNullOrEmpty(dir))
                {
                    EnsureDirectoryExists(dir);
                }

                string logEntry = $"{ts:yyyy-MM-dd HH:mm:ss.ffffff} | Op: {op} | PID: {pid} | TID: {tid} | Comm: {comm}\n";
                using (var writer = new StreamWriter(empty_filename_filepath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    writer.Write(logEntry);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error logging empty filename: {ex.Message}");
            }
        }

        /// <summary>
        /// Public flush entry point for callers that are not already holding the
        /// per-trace-type lock (shutdown flushing, snapshot finalization). Waits on
        /// the reconnect gate first — never while holding the lock — then performs
        /// the flush under the matching lock.
        /// </summary>
        public void FlushWrite(StringBuilder sb, string filepath, string tracetype, bool isFinalFsSnap = false)
        {
            // Block all writer threads (ETW, fsSnapper, psSnapper) while the
            // upload worker is reconnecting. The gate is reset on disconnect and
            // set again once internet connectivity is restored. The wait happens
            // outside the lock so a multi-minute reconnect can never pin the lock.
            ObjectStorageHandler.ResumeGate.Wait();
            lock (GetLock(tracetype))
            {
                FlushWriteLocked(sb, filepath, tracetype, isFinalFsSnap);
            }
        }

        /// <summary>
        /// Rotates the trace file and hands the buffered rows to the background worker.
        /// For high-volume continuous streams the raw-CSV write AND compression both run
        /// off this thread (only an in-memory string copy stays under the lock), so a slow
        /// disk cannot stall the ETW consumer and cause kernel FileIO event loss; for
        /// low-volume snapshot streams the raw CSV is still written inline and only
        /// compression is deferred. The caller MUST already hold <see cref="GetLock"/>
        /// for <paramref name="tracetype"/> and must have waited on
        /// <see cref="ObjectStorageHandler.ResumeGate"/>.
        /// </summary>
        private void FlushWriteLocked(StringBuilder sb, string filepath, string tracetype, bool isFinalFsSnap = false)
        {
            string old_fp;

            if (tracetype.Equals("filesystem"))
            {
                old_fp = fs_filepath;
                fs_filepath = GenerateFilePath("fs");
            }
            else if (tracetype.Equals("disk"))
            {
                old_fp = block_filepath;
                block_filepath = GenerateFilePath("block");
            }
            else if (tracetype.Equals("memory"))
            {
                old_fp = mr_filepath;
                mr_filepath = GenerateFilePath("mr");
            }
            else if (tracetype.Equals("process"))
            {
                old_fp = process_snap_filepath;
                process_snap_filepath = GenerateFilePath("process");
            }
            else if (tracetype.Equals("filesystem_snapshot"))
            {
                // For filesystem snapshot, rotate the file with part numbers
                old_fp = fs_snap_filepath;
                fs_snap_filepath = GenerateFilePathWithPart("filesystem_snapshot", ++fs_snap_part_counter);
            }
            else if (tracetype.Equals("network"))
            {
                old_fp = nw_filepath;
                nw_filepath = GenerateFilePath("nw");
            }
            else
            {
                return;
            }
            _lastFlushUtc = DateTime.UtcNow;

            // Capture fs_snap_part_counter now; it may advance before the job runs.
            int totalParts = fs_snap_part_counter - 1;
            CompressJob job;

            if (IsBufferedTraceType(tracetype))
            {
                // High-volume continuous stream (filesystem/disk/memory/network): this is the
                // burst that overflows the kernel buffers, so the ETW consumer thread must do
                // NO disk I/O here. Snapshot the buffered rows to a string (pure CPU at memory
                // bandwidth) and defer BOTH the raw-CSV write and the compression to the
                // background thread. old_fp is a freshly-rotated path that does not exist yet,
                // so the chunk always needs a header to stay self-describing.
                string content = sb.ToString();
                sb.Clear();
                job = new CompressJob(old_fp, content, NeedsHeader: true, tracetype, isFinalFsSnap, totalParts);
            }
            else
            {
                // Low-volume snapshot streams (process, filesystem_snapshot): write the raw CSV
                // inline as before. These never drive the kernel-buffer overflow, and their
                // part-counter / abort-delete / completeness file semantics rely on the raw
                // file existing synchronously once the flush returns. Only compression defers.
                bool needsHeader = !File.Exists(old_fp) || new FileInfo(old_fp).Length == 0;
                using (var writer = new StreamWriter(old_fp, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    if (needsHeader)
                    {
                        string header = utils.TraceManifest.HeaderLine(tracetype);
                        if (!string.IsNullOrEmpty(header))
                            writer.Write(header + "\n");
                    }
                    writer.Write(sb);
                }
                sb.Clear();
                job = new CompressJob(old_fp, Content: null, NeedsHeader: false, tracetype, isFinalFsSnap, totalParts);
            }

            try
            {
                _compressQueue.Add(job);
            }
            catch (InvalidOperationException)
            {
                // Queue already marked complete (shutdown). Write (if deferred) + compress
                // inline as a fallback so the final chunk is not lost.
                try { ProcessCompressJob(job); }
                catch (Exception ex) { Debug.WriteLine($"Error writing/compressing file {old_fp}: {ex.Message}"); }
            }
        }

        // Writes one rotated chunk's buffered rows to a fresh raw CSV (schema header first).
        // Runs on the background _compressThread, or inline only during the shutdown drain —
        // NEVER on the ETW consumer thread, so a slow disk here cannot stall kernel-buffer
        // draining and cause FileIO event loss.
        private static void WriteRawCsv(string path, string content, bool needsHeader, string tracetype)
        {
            using var writer = new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (needsHeader)
            {
                string header = utils.TraceManifest.HeaderLine(tracetype);
                if (!string.IsNullOrEmpty(header))
                    writer.Write(header + "\n");
            }
            writer.Write(content);
        }

        public void DirectWrite(string file_out_path, string input)
        {
            string out_path = $"{dir_path}\\system_spec\\{file_out_path}";

            string? dir = Path.GetDirectoryName(out_path);
            if (!string.IsNullOrEmpty(dir))
            {
                EnsureDirectoryExists(dir);
            }

            using (var writer = new StreamWriter(out_path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(input);
            }

            Interlocked.Increment(ref amount_compressed_file);

            if (is_upload_automatically)
            {
                obj_storage.QueueFile(out_path);
            }

        }

        /// <summary>
        /// Continuous, high-volume trace types whose chunks are batched into a
        /// local buffer before upload. Snapshot types (process, filesystem_snapshot)
        /// are excluded because they rely on per-part / completeness file naming.
        /// </summary>
        private static bool IsBufferedTraceType(string tracetype)
        {
            return tracetype switch
            {
                "filesystem" => true,
                "disk" => true,
                "memory" => true,
                "network" => true,
                _ => false,
            };
        }

        private static bool IsTimeToFlush(StringBuilder sb, bool isSnap = false, bool lowThreshold = false)
        {
            long sbBytes = sb.Length * sizeof(char);

            long availableMemory = GetAvailableMemoryCached();

            long adaptiveLimit = (long)(availableMemory * MEMORY_PRESSURE_RATIO);

            if (isSnap)
                adaptiveLimit /= 4;

            adaptiveLimit = Math.Min(adaptiveLimit, ABSOLUTE_MAX_BYTES);

            if (sbBytes >= adaptiveLimit)
                return true;

            if (DateTime.UtcNow - _lastFlushUtc >= MIN_FLUSH_INTERVAL)
            {
                // For low-threshold trace types (e.g. network), flush any buffered data
                if (lowThreshold && sbBytes > 0)
                    return true;

                if (sbBytes > adaptiveLimit / 4)
                    return true;
            }

            return false;
        }


        public static string CompressFile(string filepath)
        {
            if (!File.Exists(filepath))
            {
                throw new FileNotFoundException("Input file does not exist.", filepath);
            }

            string compressed_fp = $"{filepath}.zst";

            using (var input = File.OpenRead(filepath))
            using (var output = File.Create(compressed_fp))
            using (var compressor = new CompressionStream(output))
            {
                input.CopyTo(compressor);
            }

            Interlocked.Increment(ref amount_compressed_file);

            File.Delete(Path.GetFullPath(filepath));

            return compressed_fp;
        }



        public void CompressRun()
        {
            if (is_upload_automatically)
            {
                // Don't delete the local trace directory while uploads are still
                // pending (e.g. the network dropped during the shutdown drain) — that
                // would permanently lose the un-uploaded final batch. Leave it on disk
                // for recovery instead.
                if (obj_storage.HasQueuedFiles)
                {
                    Console.WriteLine($"[WriterManager] Upload incomplete; leaving trace data in {dir_path} for recovery.");
                    return;
                }
                Directory.Delete(dir_path, true);
                return;
            }

            string zipPath = $"{dir_path}_temp.zip";
            string output_dir = $"{dir_path}.zip.zst";

            try
            {
                if (!Directory.Exists(dir_path))
                {
                    Debug.WriteLine($"Directory not found: {dir_path}");
                    return;
                }

                ZipFile.CreateFromDirectory(dir_path, zipPath);

                byte[] zipData = File.ReadAllBytes(zipPath);

                using (var compressor = new Compressor())
                {
                    var compressedData = compressor.Wrap(zipData);
                    File.WriteAllBytes(output_dir, compressedData.ToArray());
                }

                Debug.WriteLine($"Compressed entire run to {output_dir}");

                Directory.Delete(dir_path, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Compression failed: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }
                }
                catch
                {
                }
            }
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (Directory.Exists(path))
                return;

            if (File.Exists(path))
            {
                Debug.WriteLine($"Warning: File exists at directory path '{path}', renaming...");
                string backupPath = path + ".backup_" + DateTime.UtcNow.Ticks;
                File.Move(path, backupPath);
                Debug.WriteLine($"Moved conflicting file to: {backupPath}");
            }

            Directory.CreateDirectory(path);
        }

        public async Task CompressAllAsync()
        {
            Debug.WriteLine("Compressing all remaining data...");

            // Stop periodic (non-final) manifest refreshes so the authoritative final
            // manifest written below cannot be overwritten by a racing periodic one.
            _finalizing = true;

            // Drain the fs formatter first: it is what fills fs_sb, so flushing fs_sb
            // before the formatter is idle would drop every still-queued event. By now
            // the ETW session and the handlers' flush timer are stopped (see Tracer), so
            // no new events are arriving; CompleteAdding + Join formats the remainder.
            _fsFormatQueue.CompleteAdding();
            foreach (var t in _fsFormatThreads) t.Join();

            if (fs_sb.Length > 0) FlushWrite(fs_sb, fs_filepath, "filesystem");
            if (block_sb.Length > 0) FlushWrite(block_sb, block_filepath, "disk");
            if (mr_sb.Length > 0) FlushWrite(mr_sb, mr_filepath, "memory");
            if (process_snap_sb.Length > 0) FlushWrite(process_snap_sb, process_snap_filepath, "process");
            if (fs_snap_sb.Length > 0) FlushWrite(fs_snap_sb, fs_snap_filepath, "filesystem_snapshot");
            if (nw_sb.Length > 0) FlushWrite(nw_sb, nw_filepath, "network");
            Debug.WriteLine("Flushed all StringBuilders.");

            // Wait for the background compressor to finish every queued chunk before we
            // drain the upload buffers/queue below — the compressor is what feeds them
            // (BufferFile/QueueFile). After CompleteAdding the loop exits once drained.
            _compressQueue.CompleteAdding();
            _compressThread.Join();

            // Filesystem snapshot compression is now handled by FinalizeFilesystemSnapshot

            WriteStatus();

            // Finalize the manifest (stop time, per-stream counts, ETW lost events,
            // dead probes) and queue it before the buffers/queue are drained.
            WriteManifest(final: true);

            // Push any partially-filled local buffers into the upload queue so the
            // final ClearQueue uploads them before shutdown.
            obj_storage.FlushAllBuffers();

            await obj_storage.ClearQueue();
            ConfigClasses.SaveTracemetaConfiguration(active_session + trace_duration, file_event_counter);

            CompressRun();
        }

        public void FinalizeFilesystemSnapshot(bool isComplete)
        {
            Debug.WriteLine($"Finalizing filesystem snapshot (complete: {isComplete})...");

            if (isComplete)
            {
                fs_snapshot_complete = true;
            }

            if (!isComplete)
            {
                lock (fs_snap_lock)
                {
                    fs_snap_sb.Clear();
                }
                // Snapshot was interrupted - delete all part files
                Debug.WriteLine("Snapshot was incomplete. Deleting all filesystem snapshot files...");

                try
                {
                    string snapshotDir = Path.Combine(dir_path, "filesystem_snapshot");
                    if (Directory.Exists(snapshotDir))
                    {
                        var files = Directory.GetFiles(snapshotDir, "filesystem_snapshot_part*.csv*");
                        foreach (var file in files)
                        {
                            try
                            {
                                File.Delete(file);
                                Debug.WriteLine($"Deleted incomplete snapshot file: {file}");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error deleting file {file}: {ex.Message}");
                            }
                        }
                        Debug.WriteLine($"Deleted {files.Length} incomplete snapshot file(s).");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error cleaning up incomplete snapshot files: {ex.Message}");
                }

                return;
            }

            FlushWrite(fs_snap_sb, fs_snap_filepath, "filesystem_snapshot", true);
        }

        public void FinalizeProcessSnapshot(bool isComplete)
        {
            Debug.WriteLine($"Finalizing process snapshot (complete: {isComplete})...");

            if (!isComplete)
            {
                // Snapshot was interrupted - delete all process snapshot files
                Debug.WriteLine("Process snapshot was incomplete. Deleting all process snapshot files...");

                try
                {
                    string snapshotDir = Path.Combine(dir_path, "process");
                    if (Directory.Exists(snapshotDir))
                    {
                        var files = Directory.GetFiles(snapshotDir, "process_*.csv*");
                        foreach (var file in files)
                        {
                            try
                            {
                                File.Delete(file);
                                Debug.WriteLine($"Deleted incomplete process snapshot file: {file}");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error deleting file {file}: {ex.Message}");
                            }
                        }
                        Debug.WriteLine($"Deleted {files.Length} incomplete process snapshot file(s).");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error cleaning up incomplete process snapshot files: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine("Process snapshot completed successfully.");
            }
        }


        // Monotonic suffix so two rotations within the same second never collide on a
        // filename. Collisions matter more now that compression is deferred: the
        // background compressor reads + deletes a rotated raw file asynchronously, so a
        // reused path could be appended-to while it is being compressed.
        private static int _filePathSeq = 0;

        /// <summary>Process-wide, thread-safe monotonic counter feeding rotation filenames.</summary>
        internal static int NextFilePathSeq() => Interlocked.Increment(ref _filePathSeq);

        /// <summary>
        /// Builds the relative trace filename (.\type\...csv). The <paramref name="seq"/>
        /// disambiguates rotations that land in the same millisecond, so distinct seq
        /// values always yield distinct names. Pure + side-effect-free for testability.
        /// </summary>
        internal static string BuildTraceFileName(string type, int seq, string deviceId, DateTime utc, int partNumber = -1)
        {
            string part = partNumber >= 0 ? $"part{partNumber:D4}_" : "";
            return $".\\{type}\\{type}_{part}{utc:yyyyMMdd_HHmmssfff}_{seq}_{deviceId}.csv";
        }

        private string GenerateFilePath(string type)
        {
            return Path.Combine(dir_path,
                BuildTraceFileName(type, NextFilePathSeq(), PathHasher.deviceId, DateTime.UtcNow));
        }

        private string GenerateFilePathWithPart(string type, int partNumber)
        {
            return Path.Combine(dir_path,
                BuildTraceFileName(type, NextFilePathSeq(), PathHasher.deviceId, DateTime.UtcNow, partNumber));
        }

        private static void WriteStatus()
        {
            //Console.Clear();
            //Console.WriteLine("Press CTRL + C to exit, or close the console window!");
            string stat = $"{DateTime.Now} | File Compressed: {amount_compressed_file}";
            Console.WriteLine(stat);
        }
    }
}
