using IOTracesCORE;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace IOTracesCORE.handlers
{
    class FilesystemHandlers : IDisposable
    {
        private readonly WriterManager wm;
        private readonly ProcessCommandLineCache processCache;
        // When true, op_end (per-operation completion) rows are suppressed to cut volume.
        private readonly bool lowOverheadLogging;

        private readonly ConcurrentDictionary<ulong, string> nameByObj = new();
        private readonly ConcurrentDictionary<ulong, ConcurrentQueue<(DateTime addedAt, Action<string> emit)>>
            _pending = new();
        // Track alternate keys (FileObject, etc) for each FileKey so we can drain all related pending queues.
        // The inner set is a ConcurrentDictionary (used as a set) so it can be safely mutated by the ETW
        // callback thread while the flush-timer thread enumerates and removes entries during cleanup.
        private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, byte>> _keyAliases = new();
        private readonly System.Threading.Timer _flushTimer;
        private static readonly TimeSpan MaxPendingAge = TimeSpan.FromMilliseconds(100);

        // ── Load-shed aggregation (fs_summary stream) ──────────────────────────────────
        // Under sustained backpressure (see LoadShedder), low-value metadata ops are shed
        // from the full fs/ stream and instead accumulated here per (pid, filename, op),
        // flushed once a minute. Mirrors the nw stream's aggregate-then-flush pattern:
        // _summaryLock is held briefly on the ETW hot path (AggregateShed); _summaryWriteLock
        // serializes the per-minute row-write phase so wm.Write (which can block on the upload
        // ResumeGate) never stalls the hot path.
        private sealed class FsAgg { public string Comm = ""; public long Count; public int IdleWindows; }
        private readonly ConcurrentDictionary<(int Pid, string Name, string Op), FsAgg> _summary = new();
        private readonly object _summaryLock = new();
        private readonly object _summaryWriteLock = new();
        private readonly System.Threading.Timer _summaryTimer;
        private static readonly TimeSpan SummaryFlushInterval = TimeSpan.FromMinutes(1);

        public FilesystemHandlers(WriterManager wm, ProcessCommandLineCache processCache,
            bool lowOverheadLogging = false)
        {
            this.wm = wm;
            this.processCache = processCache;
            this.lowOverheadLogging = lowOverheadLogging;
            _flushTimer = new System.Threading.Timer(FlushStalePending, null,
                TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
            _summaryTimer = new System.Threading.Timer(_ => FlushSummaryWindow(), null,
                SummaryFlushInterval, SummaryFlushInterval);
        }

        public void Dispose()
        {
            _flushTimer?.Dispose();
            // Stop the summary timer (waiting out any in-flight callback) then flush the final
            // partial window so the last minute of aggregated counts is not lost on shutdown.
            using (var done = new System.Threading.ManualResetEvent(false))
            {
                if (_summaryTimer != null && _summaryTimer.Dispose(done)) done.WaitOne();
            }
            FlushSummaryWindow();
        }

        // Analytic priority for load shedding. HIGH is never shed; MED (close/cleanup) and LOW
        // (getattr/op_end/dir_enum/...) are aggregated into fs_summary under backpressure.
        private static FsPriority PriorityOf(string op) => op switch
        {
            "read" or "write" or "create" or "delete" or "rename" or "flush"
                or "name" or "file_create" or "file_delete" or "file_rundown" => FsPriority.High,
            "close" or "cleanup" => FsPriority.Med,
            _ => FsPriority.Low,
        };

        // Fold a shed event into the per-(pid,file,op) aggregate instead of emitting a full
        // FilesystemTrace row (which allocates a CsvWriter — the dominant per-event cost). The
        // filename is captured HERE, at aggregation time, because OnClose evicts nameByObj so a
        // flush-time lookup would miss a since-closed file.
        private void AggregateShed(int pid, string proc, string name, string op)
        {
            TraceStats.IncFsSummaryEvent();
            lock (_summaryLock)
            {
                var agg = _summary.GetOrAdd((pid, name ?? "", op), _ => new FsAgg { Comm = proc ?? "" });
                if (string.IsNullOrEmpty(agg.Comm) && !string.IsNullOrEmpty(proc)) agg.Comm = proc;
                agg.Count++;
                agg.IdleWindows = 0;
            }
        }

        // Emit one fs_summary row per (pid,file,op) that saw events this window; reset counts;
        // evict entries idle for several windows to bound memory. Build rows under _summaryLock,
        // then write under _summaryWriteLock (NOT _summaryLock) so wm.Write can't stall AggregateShed.
        private void FlushSummaryWindow()
        {
            List<FsSummaryTrace>? rows = null;
            lock (_summaryLock)
            {
                var now = DateTime.Now;
                foreach (var kvp in _summary)
                {
                    var agg = kvp.Value;
                    if (agg.Count == 0)
                    {
                        if (++agg.IdleWindows >= 5) _summary.TryRemove(kvp.Key, out _);
                        continue;
                    }
                    long c = agg.Count;
                    agg.Count = 0;
                    agg.IdleWindows = 0;
                    (rows ??= new List<FsSummaryTrace>()).Add(
                        new FsSummaryTrace(now, kvp.Key.Pid, agg.Comm, kvp.Key.Name, kvp.Key.Op, c));
                }
            }
            if (rows != null)
            {
                lock (_summaryWriteLock)
                {
                    foreach (var row in rows) wm.Write(row);
                }
            }
        }

        /// <summary>
        /// Drops all cross-event correlation state (filename cache, pending emit
        /// queue, and key aliases). Call on ETW session restart so kernel
        /// FileObject / FileKey pointers reused by the new session are never
        /// matched against stale entries from the previous session, which would
        /// otherwise emit wrong filenames. Pending events from the old session can
        /// never be drained (their FileIOName events won't arrive again), so they
        /// are discarded rather than later flushed with empty names.
        /// </summary>
        public void Reset()
        {
            nameByObj.Clear();
            _pending.Clear();
            _keyAliases.Clear();
            // Clear the load-shed lag/level so a new session doesn't start in a shed state
            // inherited from a stale lag. The fs_summary aggregate is keyed by name (restart-safe)
            // and its idle-eviction bounds it, so it is intentionally left to keep accumulating.
            LoadShedder.Reset();
        }

        // Single chokepoint for every FileIO handler's "should I trace this?" gate. Counts
        // the raw event the kernel delivered (received) before any filtering, and separately
        // the ones ProcessFilter rejects (excluded process), so the manifest can tell whether
        // a sparse fs/ stream is the kernel delivering little vs. us discarding most of it.
        private bool ShouldTrace(int pid, string processName)
        {
            TraceStats.IncFilesystemReceived();
            bool ok = ProcessFilter.ShouldTrace(pid, processName);
            if (!ok) TraceStats.IncFilesystemFilteredByProcess();
            return ok;
        }

        private static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Trim();

        private static bool IsIgnored(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Contains("IOTracer", StringComparison.OrdinalIgnoreCase)) return true;
            // Wildcard characters are never valid in Windows file paths — these are leaked
            // directory enumeration search patterns (e.g. "Get-WmiObject*" from PowerShell dir_enum events).
            if (s.IndexOfAny(new[] { '*', '?' }) >= 0) return true;
            return false;
        }

        // Marks a path as a directory with a trailing '\' when it has no extension. Applied ONLY
        // to dir_enum / dir_notify, whose target genuinely IS a directory being listed/watched —
        // NOT to the general resolution path. The extension test is just a guess, so applying it to
        // every op mislabels extensionless FILES (README, LICENSE, Makefile, a downloaded blob) as
        // directories; for those the operation already tells you it's file I/O. The directory-listing
        // ops carry no such per-row signal, so the marker stays useful there.
        internal static string MarkDirectoryIfNoExtension(string s)
        {
            var n = Clean(s);
            if (string.IsNullOrEmpty(n)) return n;
            if (n.EndsWith("\\", StringComparison.Ordinal) || n.EndsWith("/", StringComparison.Ordinal)) return n;

            // No extension => treat as directory and mark with a trailing '\'.
            // Avoid changing drive letters and alternate data streams (":" after the last path separator).
            if (Path.GetExtension(n).Length != 0) return n;
            var lastSep = Math.Max(n.LastIndexOf('\\'), n.LastIndexOf('/'));
            var lastColon = n.LastIndexOf(':');
            if (lastColon > lastSep) return n;

            return n + "\\";
        }

        private void LogEmptyFilenameIfNeeded(string name, DateTime ts, string op, int pid, int tid, string proc)
        {
            if (string.IsNullOrEmpty(name))
            {
                wm.LogEmptyFilename(ts, op, pid, tid, proc);
            }
        }

        private string Resolve(ulong key, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return n;
            return nameByObj.TryGetValue(key, out var cached) ? cached : "";
        }

        // Tries key1 first, then key2 as fallback — handles events where ETW FileKey vs
        // FileObject meaning differs across event types (create vs read/write/close).
        // Cross-populates the other key when found, so future lookups find the name via either key.
        private string Resolve(ulong key1, ulong key2, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return n;

            if (nameByObj.TryGetValue(key1, out var cached))
            {
                if (key2 != 0 && key2 != key1) nameByObj.TryAdd(key2, cached);
                return cached;
            }

            if (nameByObj.TryGetValue(key2, out cached))
            {
                if (key1 != 0 && key1 != key2) nameByObj.TryAdd(key1, cached);
                return cached;
            }

            return "";
        }

        private void EnqueuePending(ulong fileKey, ulong fileObject, Action<string> emit)
        {
            ulong key = fileKey != 0 ? fileKey : fileObject;
            if (key == 0) { emit(""); return; }

            // Track both keys as aliases so DrainPending can find them via either key
            if (fileKey != 0 && fileObject != 0 && fileKey != fileObject)
            {
                _keyAliases.GetOrAdd(fileKey, _ => new ConcurrentDictionary<ulong, byte>()).TryAdd(fileObject, 0);
                _keyAliases.GetOrAdd(fileObject, _ => new ConcurrentDictionary<ulong, byte>()).TryAdd(fileKey, 0);
            }

            _pending.GetOrAdd(key, _ => new ConcurrentQueue<(DateTime, Action<string>)>())
                    .Enqueue((DateTime.UtcNow, emit));
        }

        private void DrainPending(ulong fileKey, string resolvedName)
        {
            if (fileKey == 0) return;

            // Emit the resolved name as-is. Directory marking (trailing '\') is applied by the
            // dir_enum / dir_notify deferred closures themselves, not here, so file ops keep their
            // real (possibly extensionless) names.
            var cleanedName = resolvedName;

            // Primary drain: the exact key from the name event
            if (_pending.TryRemove(fileKey, out var queue))
            {
                while (queue.TryDequeue(out var item))
                {
                    item.emit(cleanedName);
                }
            }

            // Secondary drain: the alternate keys recorded for THIS same file — i.e. the
            // FileKey/FileObject pair captured together in a single EnqueuePending call. Those keys
            // are safely correlated with fileKey, so we drain their queues and cache the name under
            // them for future lookups.
            //
            // We deliberately do NOT scan the rest of _pending. That dictionary is global and holds
            // pending events for every file; a key we have not explicitly correlated with this file
            // may belong to a completely different file, so assigning this resolvedName to it would
            // corrupt the filename in the output trace.
            if (_keyAliases.TryRemove(fileKey, out var aliases))
            {
                foreach (var altKey in aliases.Keys)
                {
                    // Drop the reverse mapping (altKey -> fileKey) so _keyAliases does not grow unbounded.
                    _keyAliases.TryRemove(altKey, out _);

                    if (altKey != 0 && !string.IsNullOrEmpty(resolvedName))
                        nameByObj.TryAdd(altKey, resolvedName);

                    if (_pending.TryRemove(altKey, out queue))
                    {
                        while (queue.TryDequeue(out var item))
                        {
                            item.emit(cleanedName);
                        }
                    }
                }
            }
        }

        // Removes a key's alias set along with the reverse mappings that point back at it,
        // so _keyAliases does not accumulate stale entries after a pending event is resolved or flushed.
        private void RemoveAliases(ulong key)
        {
            if (_keyAliases.TryRemove(key, out var aliases))
            {
                foreach (var altKey in aliases.Keys)
                    _keyAliases.TryRemove(altKey, out _);
            }
        }

        private void FlushStalePending(object? state)
        {
            var cutoff = DateTime.UtcNow - MaxPendingAge;
            foreach (var kvp in _pending)
            {
                if (!kvp.Value.TryPeek(out var oldest) || oldest.addedAt > cutoff) continue;
                if (!_pending.TryRemove(kvp.Key, out var queue)) continue;
                RemoveAliases(kvp.Key);
                nameByObj.TryGetValue(kvp.Key, out var cachedName);
                var finalName = cachedName ?? "";
                while (queue.TryDequeue(out var item))
                {
                    item.emit(finalName);
                }
            }
        }

        // Emit for simple operations with basic enhanced fields
        // ── Lightweight mode: modern Microsoft-Windows-Kernel-File adapter ──────────────
        // In lightweight mode the FILE session enables the modern manifest provider with a REDUCED
        // keyword set (read/write/create/name/delete/rename only), so the kernel never generates
        // the metadata-op mass — ~3x fewer FileIO events at the source. The events differ from the
        // legacy NT-Kernel-Logger FileIO, so this single dispatcher adapts them (fields read by
        // name, robustly) into the SAME FilesystemTrace output via the existing Resolve / pending /
        // shed / Emit path. Filename resolution mirrors the legacy model: NameCreate gives
        // FileKey->path, Create gives FileObject->path, and Read/Write (keyed on FileKey) resolve
        // from that cache or defer. Metadata ops (getattr/close/cleanup/dir_enum/fsctl) and
        // map_file are intentionally absent in lightweight (recorded as logging_mode="lightweight").
        public void OnModernFileEvent(TraceEvent d)
        {
            string ev = d.EventName ?? "";
            int slash = ev.LastIndexOf('/');
            string op = (slash >= 0 ? ev.Substring(slash + 1) : ev).ToLowerInvariant();

            switch (op)
            {
                case "create":
                {
                    string name = Clean(MStr(d, "FileName"));
                    ulong fobj = MUlong(d, "FileObject");
                    if (!string.IsNullOrEmpty(name) && fobj != 0) nameByObj[fobj] = name;
                    if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
                    EmitCreate(d.TimeStamp, "create", d.ProcessID, d.ThreadID, d.ProcessName,
                        Resolve(fobj, name), 0,
                        MInt(d, "CreateOptions"), MInt(d, "ShareAccess"), 0,
                        MUlong(d, "Irp"), fobj, MInt(d, "CreateAttributes"));
                    return;
                }
                case "namecreate":
                {
                    // FileKey->path mapping (regardless of process filter, so all files index).
                    string name = Clean(MStr(d, "FileName"));
                    ulong key = MUlong(d, "FileKey");
                    if (!string.IsNullOrEmpty(name) && key != 0)
                    {
                        nameByObj[key] = name;
                        DrainPending(key, name);
                    }
                    return;
                }
                case "namedelete":
                    return; // mapping teardown; cache is bounded/cleared on session reset
                case "read":
                case "write":
                {
                    if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
                    ulong key = MUlong(d, "FileKey"); ulong fobj = MUlong(d, "FileObject");
                    int size = (int)MLong(d, "IOSize");
                    long offset = MLong(d, "ByteOffset");
                    ulong irp = MUlong(d, "Irp"); int ioflags = MInt(d, "IOFlags");
                    var name = Resolve(key, fobj, "");
                    if (!string.IsNullOrEmpty(name))
                    {
                        EmitReadWrite(d.TimeStamp, op, d.ProcessID, d.ThreadID, d.ProcessName, name, size, offset, irp, key, ioflags);
                        return;
                    }
                    var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
                    EnqueuePending(key, fobj, resolvedName =>
                    {
                        LogEmptyFilenameIfNeeded(resolvedName, ts, op, pid, tid, proc);
                        EmitReadWrite(ts, op, pid, tid, proc, resolvedName, size, offset, irp, key, ioflags);
                    });
                    return;
                }
                case "deletepath":
                {
                    if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
                    string name = Clean(MStr(d, "FilePath"));            // self-resolving inline path
                    Emit(d.TimeStamp, "delete", d.ProcessID, d.ThreadID, d.ProcessName, name, 0, MUlong(d, "Irp"), MUlong(d, "FileKey"));
                    return;
                }
                case "renamepath":
                case "setlinkpath":
                {
                    if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
                    string name = Clean(MStr(d, "FilePath"));            // self-resolving inline path
                    Emit(d.TimeStamp, "rename", d.ProcessID, d.ThreadID, d.ProcessName, name, 0, MUlong(d, "Irp"), MUlong(d, "FileKey"));
                    return;
                }
                // create-new-file / op_end / anything else: not enabled in the reduced keyword set
            }
        }

        // Payload accessors for the modern provider (field names per its manifest). Each is
        // best-effort: a missing/odd field yields a benign default rather than throwing on the
        // hot path, so a manifest-version field rename degrades gracefully (validated on the VM).
        private static string MStr(TraceEvent d, string f) { try { return d.PayloadByName(f) as string; } catch { return null; } }
        private static long MLong(TraceEvent d, string f) { try { var v = d.PayloadByName(f); return v == null ? 0 : Convert.ToInt64(v); } catch { return 0; } }
        private static int MInt(TraceEvent d, string f) { try { var v = d.PayloadByName(f); return v == null ? 0 : Convert.ToInt32(v); } catch { return 0; } }
        private static ulong MUlong(TraceEvent d, string f) { try { var v = d.PayloadByName(f); return v == null ? 0 : Convert.ToUInt64(v); } catch { return 0; } }

        private void Emit(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            ulong? irp = null, ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            // Under backpressure, shed low/med-value ops into the fs_summary aggregate instead of
            // emitting a full row (HIGH ops never shed; all ops feed the lag signal). See LoadShedder.
            if (LoadShedder.ShouldShed(PriorityOf(op), ts)) { AggregateShed(pid, proc, name, op); return; }
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                null, null, null, null, null, null, null, irp, fileKey, null, null, processCache.Get(pid)));
        }

        // Extended emit for Create operations with all flags
        // NOTE: DesiredAccess is NOT captured here because it is not available in Windows ETW FileIO events.
        // Both the NT Kernel Logger (FileIOCreateTraceData) and Microsoft-Windows-Kernel-File provider
        // do not include DesiredAccess in their event schemas. The available fields are:
        // CreateOptions, ShareAccess, CreateDisposition, FileAttributes, FileName.
        // To capture DesiredAccess, a minifilter driver would be required (like Process Monitor uses).
        private void EmitCreate(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            int createOptions, int shareAccess, int createDisposition, ulong irp, ulong fileKey,
            int fileAttributes)
        {
            if (IsIgnored(name)) return;
            // Under backpressure, shed low/med-value ops into the fs_summary aggregate instead of
            // emitting a full row (HIGH ops never shed; all ops feed the lag signal). See LoadShedder.
            if (LoadShedder.ShouldShed(PriorityOf(op), ts)) { AggregateShed(pid, proc, name, op); return; }
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                FileIOFlags.FormatCreateOptions(createOptions),
                FileIOFlags.FormatShareAccess(shareAccess),
                FileIOFlags.FormatCreateDisposition(createDisposition),
                null, null, null, null, irp, fileKey,
                FileIOFlags.FormatFileAttributes(fileAttributes),
                null, processCache.Get(pid)));
        }

        // Extended emit for Read/Write operations with offset and IoFlags
        private void EmitReadWrite(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            long offset, ulong irp, ulong fileKey, int ioFlags)
        {
            if (IsIgnored(name)) return;
            // Under backpressure, shed low/med-value ops into the fs_summary aggregate instead of
            // emitting a full row (HIGH ops never shed; all ops feed the lag signal). See LoadShedder.
            if (LoadShedder.ShouldShed(PriorityOf(op), ts)) { AggregateShed(pid, proc, name, op); return; }
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                null, null, null, offset, null, null, null, irp, fileKey, null,
                FileIOFlags.FormatIoFlags(ioFlags), processCache.Get(pid)));
        }

        // Extended emit for MapFile operations with view size
        private void EmitMapFile(DateTime ts, string op, int pid, int tid, string proc, string name, ulong viewSize,
            ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            // Under backpressure, shed low/med-value ops into the fs_summary aggregate instead of
            // emitting a full row (HIGH ops never shed; all ops feed the lag signal). See LoadShedder.
            if (LoadShedder.ShouldShed(PriorityOf(op), ts)) { AggregateShed(pid, proc, name, op); return; }
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, 0,
                null, null, null, null, (long)viewSize, null, null, null, fileKey, null, null, processCache.Get(pid)));
        }

        // Extended emit for Query/SetInfo operations with info class
        private void EmitWithInfoClass(DateTime ts, string op, int pid, int tid, string proc, string name,
            int infoClass, ulong? irp = null, ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            // Under backpressure, shed low/med-value ops into the fs_summary aggregate instead of
            // emitting a full row (HIGH ops never shed; all ops feed the lag signal). See LoadShedder.
            if (LoadShedder.ShouldShed(PriorityOf(op), ts)) { AggregateShed(pid, proc, name, op); return; }
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, 0,
                null, null, null, null, null, FileIOFlags.FormatInfoClass(infoClass), null,
                irp, fileKey, null, null, processCache.Get(pid)));
        }

        // fs_control events carry an FSCTL code in InfoClass, not a FILE_INFORMATION_CLASS value
        private void EmitFsControl(DateTime ts, string op, int pid, int tid, string proc, string name,
            int fsctlCode, ulong? irp = null, ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            // Under backpressure, shed low/med-value ops into the fs_summary aggregate instead of
            // emitting a full row (HIGH ops never shed; all ops feed the lag signal). See LoadShedder.
            if (LoadShedder.ShouldShed(PriorityOf(op), ts)) { AggregateShed(pid, proc, name, op); return; }
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, 0,
                null, null, null, null, null, null, FileIOFlags.FormatFsctlCode(fsctlCode),
                irp, fileKey, null, null, processCache.Get(pid)));
        }


        public void OnRead(FileIOReadWriteTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                EmitReadWrite(d.TimeStamp, "read", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, d.IoSize, d.Offset, d.IrpPtr, d.FileKey, d.IoFlags);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var size = d.IoSize; var offset = d.Offset; var irp = d.IrpPtr; var fk = d.FileKey; var flags = d.IoFlags;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "read", pid, tid, proc);
                EmitReadWrite(ts, "read", pid, tid, proc, resolvedName, size, offset, irp, fk, flags);
            });
        }

        public void OnWrite(FileIOReadWriteTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                EmitReadWrite(d.TimeStamp, "write", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, d.IoSize, d.Offset, d.IrpPtr, d.FileKey, d.IoFlags);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var size = d.IoSize; var offset = d.Offset; var irp = d.IrpPtr; var fk = d.FileKey; var flags = d.IoFlags;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "write", pid, tid, proc);
                EmitReadWrite(ts, "write", pid, tid, proc, resolvedName, size, offset, irp, fk, flags);
            });
        }

        public void OnFlush(FileIOSimpleOpTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "flush", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "flush", pid, tid, proc);
                Emit(ts, "flush", pid, tid, proc, resolvedName, 0, irp, fk);
            });
        }

        public void OnDirEnum(FileIODirEnumTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            // d.FileName is the search pattern (e.g. "*.txt", "Get-WmiObject*"), not the directory path.
            // Resolve the directory name from the cache instead.
            var name = Resolve(d.FileKey, d.FileObject, "");
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "dir_enum", d.ProcessID, d.ThreadID, d.ProcessName,
                    MarkDirectoryIfNoExtension(name), 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "dir_enum", pid, tid, proc);
                Emit(ts, "dir_enum", pid, tid, proc, MarkDirectoryIfNoExtension(resolvedName), 0, irp, fk);
            });
        }

        public void OnCreate(FileIOCreateTraceData d)
        {
            var name = Clean(d.FileName);
            // Store under FileObject (the only key available in create events).
            // FileIOName fires shortly after and stores under FileKey for read/write lookups.
            // Cache population happens regardless of process filter so all files are indexed.
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileObject] = name;

            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;

            // Capturing CreateOptions, ShareAccess, CreateDisposition, and FileAttributes
            // Note: FileIOCreateTraceData uses FileObject as file identifier
            EmitCreate(d.TimeStamp, "create", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), 0,
                (int)d.CreateOptions, (int)d.ShareAccess, (int)d.CreateDisposition,
                d.IrpPtr, d.FileObject, (int)d.FileAttributes);
        }

        public void OnFileCreate(FileIONameTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "file_create", d.ProcessID, d.ThreadID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnDelete(FileIOInfoTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "delete", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "delete", pid, tid, proc);
                Emit(ts, "delete", pid, tid, proc, resolvedName, 0, irp, fk);
            });
        }

        public void OnFileDelete(FileIONameTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "file_delete", d.ProcessID, d.ThreadID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnClose(FileIOSimpleOpTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "close", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, 0, d.IrpPtr, d.FileKey);
            }
            else
            {
                // Defer if name is empty—will be resolved by OnName or flushed by timer
                var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
                var irp = d.IrpPtr; var fk = d.FileKey;
                EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
                {
                    LogEmptyFilenameIfNeeded(resolvedName, ts, "close", pid, tid, proc);
                    Emit(ts, "close", pid, tid, proc, resolvedName, 0, irp, fk);
                });
            }

            // Evict AFTER enqueuing so OnName can drain pending before keys are removed
            nameByObj.TryRemove(d.FileKey, out _);
            nameByObj.TryRemove(d.FileObject, out _);
        }

        public void OnRename(FileIOInfoTraceData d)
        {
            var name = Clean(d.FileName);
            // Cache population happens regardless of process filter so all files are indexed.
            if (!string.IsNullOrEmpty(name))
            {
                nameByObj[d.FileKey] = name;
                nameByObj[d.FileObject] = name;
            }

            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var resolvedName = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(resolvedName))
            {
                Emit(d.TimeStamp, "rename", d.ProcessID, d.ThreadID, d.ProcessName,
                    resolvedName, 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, finalName =>
            {
                LogEmptyFilenameIfNeeded(finalName, ts, "rename", pid, tid, proc);
                Emit(ts, "rename", pid, tid, proc, finalName, 0, irp, fk);
            });
        }

        public void OnCleanup(FileIOSimpleOpTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "cleanup", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "cleanup", pid, tid, proc);
                Emit(ts, "cleanup", pid, tid, proc, resolvedName, 0, irp, fk);
            });
        }

        public void OnDirNotify(FileIODirEnumTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            // Same as dir_enum: d.FileName is a filter pattern, not the directory path.
            var name = Resolve(d.FileKey, d.FileObject, "");
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "dir_notify", d.ProcessID, d.ThreadID, d.ProcessName,
                    MarkDirectoryIfNoExtension(name), 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "dir_notify", pid, tid, proc);
                Emit(ts, "dir_notify", pid, tid, proc, MarkDirectoryIfNoExtension(resolvedName), 0, irp, fk);
            });
        }

        public void OnFileRundown(FileIONameTraceData d)
        {
            var name = Clean(d.FileName);
            // Cache population happens regardless of process filter so all files are indexed.
            if (!string.IsNullOrEmpty(name))
            {
                nameByObj[d.FileKey] = name;
                DrainPending(d.FileKey, name);
            }

            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "file_rundown", d.ProcessID, d.ThreadID, d.ProcessName, name, 0);
        }

        public void OnFSControl(FileIOInfoTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                EmitFsControl(d.TimeStamp, "fs_control", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, d.InfoClass, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var fsctlCode = d.InfoClass; var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "fs_control", pid, tid, proc);
                EmitFsControl(ts, "fs_control", pid, tid, proc, resolvedName, fsctlCode, irp, fk);
            });
        }

        public void OnMapFile(MapFileTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "map_file", d.ProcessID, d.ThreadID, d.ProcessName,
                Clean(d.FileName), d.ViewSize, d.FileKey);
        }

        public void OnMapFileDCStart(MapFileTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "map_file_dc_start", d.ProcessID, d.ThreadID, d.ProcessName,
                Clean(d.FileName), d.ViewSize, d.FileKey);
        }

        public void OnMapFileDCStop(MapFileTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "map_file_dc_stop", d.ProcessID, d.ThreadID, d.ProcessName,
                Clean(d.FileName), d.ViewSize, d.FileKey);
        }

        public void OnName(FileIONameTraceData d)
        {
            var name = Clean(d.FileName);
            // Cache population happens regardless of process filter so all files are indexed.
            if (!string.IsNullOrEmpty(name))
            {
                nameByObj[d.FileKey] = name;
                DrainPending(d.FileKey, name);
            }

            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "name", d.ProcessID, d.ThreadID, d.ProcessName, name, 0);
        }

        public void OnQueryInfo(FileIOInfoTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                EmitWithInfoClass(d.TimeStamp, "query_info", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, d.InfoClass, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var infoClass = d.InfoClass; var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "query_info", pid, tid, proc);
                EmitWithInfoClass(ts, "query_info", pid, tid, proc, resolvedName, infoClass, irp, fk);
            });
        }

        public void OnSetInfo(FileIOInfoTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                EmitWithInfoClass(d.TimeStamp, "set_info", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, d.InfoClass, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var infoClass = d.InfoClass; var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "set_info", pid, tid, proc);
                EmitWithInfoClass(ts, "set_info", pid, tid, proc, resolvedName, infoClass, irp, fk);
            });
        }

        public void OnUnmapFile(MapFileTraceData d)
        {
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "unmap_file", d.ProcessID, d.ThreadID, d.ProcessName,
                Clean(d.FileName), d.ViewSize, d.FileKey);
        }

        // The FileIO OperationEnd event reports the final NTSTATUS of a completed IRP.
        // It carries only the IRP pointer, status, and completing thread — no FileKey or
        // FileName — so we cannot resolve a path here. We emit a lightweight "op_end" row
        // that consumers join back to the originating read/write/create by the `irp`
        // column: the result code is in `nt_status`, and the operation's latency is
        // (op_end timestamp − start-event timestamp).
        //
        // Emitted for every completed operation by default. This roughly doubles fs row
        // volume, so the "lightweight logging" mode (lowOverheadLogging) suppresses op_end
        // entirely (it also drops the memory keywords at the session level — see Tracer).
        public void OnOperationEnd(FileIOOpEndTraceData d)
        {
            if (lowOverheadLogging) return;   // low-overhead mode: skip per-operation completion events
            if (!ShouldTrace(d.ProcessID, d.ProcessName)) return;
            // op_end is LOW priority and the single highest-volume FileIO row; shed it (aggregate
            // by pid, no filename) first under backpressure. See LoadShedder.
            if (LoadShedder.ShouldShed(FsPriority.Low, d.TimeStamp))
            {
                AggregateShed(d.ProcessID, d.ProcessName, "", "op_end");
                return;
            }
            wm.Write(new FilesystemTrace(
                d.TimeStamp, "op_end", d.ProcessID, d.ThreadID, d.ProcessName, "", 0,
                null, null, null, null, null, null, null, d.IrpPtr, null, null, null,
                processCache.Get(d.ProcessID), d.NtStatus));
        }
    }
}
