using IOTracesCORE.utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace IOTracesCORE.cloudstorage
{
    internal class ObjectStorageHandler
    {

        private R2Client r2Client;
        private ConcurrentQueue<string> uploadQueue = new();
        public static int UploadedFiles = 0;
        public static long LastFileEvent;

        // ── Local upload buffering ────────────────────────────────────────────
        // Rather than uploading every flushed trace chunk individually (which
        // produces many small R2 objects), compressed chunks are appended into a
        // per-trace-type buffer file on local disk. The buffer is only queued for
        // upload once it reaches MaxBufferBytes or MaxBufferAge, at which point it
        // is uploaded as a single file. zstd frames concatenate into a valid
        // single stream, so appended chunks remain decompressible as one file.
        private const long MaxBufferBytes = 100L * 1024 * 1024; // 100 MB
        private static readonly TimeSpan MaxBufferAge = TimeSpan.FromMinutes(20);

        private readonly object bufferLock = new();
        // Keyed by trace-type folder (which is also the R2 trace_type prefix).
        private readonly Dictionary<string, BufferState> buffers = new();
        private static int bufferSeq = 0;

        private sealed class BufferState
        {
            public string BufferPath = "";
            public long Bytes;
            public DateTime StartedUtc;
        }

        // ── Connection state ──────────────────────────────────────────────────
        /// <summary>Set (signalled) while connected; Reset (blocking) while reconnecting.</summary>
        public static ManualResetEventSlim ResumeGate = new ManualResetEventSlim(true);

        /// <summary>True when the last upload succeeded / internet is reachable.</summary>
        public static volatile bool IsConnected = true;

        /// <summary>Human-readable connection status string for the tray UI.</summary>
        public static string ConnectionStatus = "Connected";

        /// <summary>Number of reconnect attempts made in the current disconnect window.</summary>
        public static int RetryAttempts = 0;

        /// <summary>
        /// Registered by <see cref="IOTracesCORE.Tracer"/> so the upload worker
        /// can signal it to stop the active ETW session.
        /// </summary>
        public static Action? OnStopSessionRequested;

        /// <summary>
        /// Fired whenever the connection state changes (disconnect or reconnect).
        /// Argument is the new <see cref="ConnectionStatus"/> string.
        /// </summary>
        public static event Action<string>? OnConnectionStateChanged;

        private const string TestUrl = "https://io-tracer-worker.1a1a11a.workers.dev/connection-test.txt";
        private const int RetryIntervalSeconds = 10;

        // ─────────────────────────────────────────────────────────────────────

        public ObjectStorageHandler()
        {
            r2Client = new();
        }

        public async Task UploadFile(string filepath)
        {
            FileInfo fi = new FileInfo(filepath);
            await r2Client.PutObject(fi);
            File.Delete(filepath);

            LastFileEvent = WriterManager.file_event_counter;
        }

        public void QueueFile(string filepath)
        {
            uploadQueue.Enqueue(filepath);
        }

        /// <summary>True while any file is still waiting to be uploaded.</summary>
        public bool HasQueuedFiles => !uploadQueue.IsEmpty;

        /// <summary>
        /// Appends a compressed trace chunk to the per-trace-type local buffer.
        /// The buffer is queued for upload as a single file once it reaches
        /// <see cref="MaxBufferBytes"/>; the time-based (<see cref="MaxBufferAge"/>)
        /// flush is driven by <see cref="FlushAgedBuffers"/> from the upload worker.
        /// </summary>
        public void BufferFile(string compressedChunkPath)
        {
            // Track whether the chunk made it into the buffer so the catch block
            // only re-queues it directly when the copy itself failed — re-queuing
            // after a successful copy would upload the same data twice.
            bool copySucceeded = false;
            try
            {
                string? dir = Path.GetDirectoryName(compressedChunkPath);
                if (string.IsNullOrEmpty(dir))
                {
                    // No folder to key on — fall back to direct upload.
                    QueueFile(compressedChunkPath);
                    return;
                }

                lock (bufferLock)
                {
                    if (!buffers.TryGetValue(dir, out var state) || !File.Exists(state.BufferPath))
                    {
                        state = new BufferState
                        {
                            BufferPath = NewBufferPath(dir),
                            Bytes = 0,
                            StartedUtc = DateTime.UtcNow
                        };
                        buffers[dir] = state;
                    }

                    // Read the chunk size up front so we can track the buffer size by
                    // summing appended chunks instead of stat-ing the buffer each time.
                    long chunkSize = new FileInfo(compressedChunkPath).Length;

                    // Remember the buffer length before appending so a failed copy
                    // (e.g. disk full) can be rolled back — otherwise a partial zstd
                    // frame would corrupt the whole concatenated buffer.
                    long resumeLength = File.Exists(state.BufferPath)
                        ? new FileInfo(state.BufferPath).Length
                        : 0;
                    try
                    {
                        using (var src = File.OpenRead(compressedChunkPath))
                        using (var dst = new FileStream(state.BufferPath, FileMode.Append, FileAccess.Write))
                        {
                            src.CopyTo(dst);
                        }
                    }
                    catch
                    {
                        TruncateBuffer(state.BufferPath, resumeLength);
                        throw;
                    }
                    copySucceeded = true;

                    // A failed delete leaves a harmless leftover chunk but must not
                    // disrupt the buffer state or trigger a duplicate direct upload.
                    try
                    {
                        File.Delete(compressedChunkPath);
                    }
                    catch (Exception deleteEx)
                    {
                        Debug.WriteLine($"Failed to delete chunk {compressedChunkPath} after buffering: {deleteEx.Message}");
                    }

                    state.Bytes += chunkSize;

                    if (state.Bytes >= MaxBufferBytes)
                    {
                        FlushBufferLocked(dir, state);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error buffering {compressedChunkPath}: {ex.Message}");
                // Don't lose data — upload the chunk directly if it never made it
                // into the buffer.
                if (!copySucceeded && File.Exists(compressedChunkPath))
                {
                    QueueFile(compressedChunkPath);
                }
            }
        }

        /// <summary>Queues any buffers older than <see cref="MaxBufferAge"/> for upload.</summary>
        public void FlushAgedBuffers()
        {
            lock (bufferLock)
            {
                var now = DateTime.UtcNow;
                foreach (var kv in buffers.ToList())
                {
                    if (now - kv.Value.StartedUtc >= MaxBufferAge)
                    {
                        FlushBufferLocked(kv.Key, kv.Value);
                    }
                }
            }
        }

        /// <summary>Queues all pending buffers for upload regardless of size/age (shutdown).</summary>
        public void FlushAllBuffers()
        {
            lock (bufferLock)
            {
                foreach (var kv in buffers.ToList())
                {
                    FlushBufferLocked(kv.Key, kv.Value);
                }
            }
        }

        // Caller must hold bufferLock.
        private void FlushBufferLocked(string dir, BufferState state)
        {
            buffers.Remove(dir);
            if (!File.Exists(state.BufferPath))
            {
                return;
            }

            // Trust the on-disk size rather than the tracked counter so we never
            // queue an empty file; clean up any zero-byte buffer instead.
            long length = new FileInfo(state.BufferPath).Length;
            if (length > 0)
            {
                Debug.WriteLine($"Flushing buffer {state.BufferPath} ({length} bytes) for upload.");
                QueueFile(state.BufferPath);
            }
            else
            {
                try { File.Delete(state.BufferPath); } catch { }
            }
        }

        // Roll a buffer file back to a known-good length, discarding a partially
        // appended frame. Best-effort: a failure here is logged, not propagated.
        private static void TruncateBuffer(string bufferPath, long length)
        {
            try
            {
                if (!File.Exists(bufferPath))
                {
                    return;
                }
                using var fs = new FileStream(bufferPath, FileMode.Open, FileAccess.Write);
                fs.SetLength(length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to roll back buffer {bufferPath} to {length} bytes: {ex.Message}");
            }
        }

        private static string NewBufferPath(string dir)
        {
            // Folder name doubles as the R2 trace_type, so name the buffer after it
            // to stay consistent with the per-chunk file naming convention.
            string traceType = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(traceType))
            {
                traceType = "trace";
            }
            int seq = Interlocked.Increment(ref bufferSeq);
            string name = $"{traceType}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}_{seq}_{PathHasher.deviceId}.csv.zst";
            return Path.Combine(dir, name);
        }

        public async Task ClearQueue()
        {
            // Drain each currently-queued file exactly once. On failure the file is
            // re-queued (not dropped) so it stays tracked and on disk; the shutdown
            // path checks HasQueuedFiles and skips deleting the local trace directory
            // when uploads remain, preventing silent loss of the final batch on a
            // transient network failure during shutdown.
            Debug.WriteLine("Clearing upload queue");
            int pending = uploadQueue.Count;
            for (int i = 0; i < pending; i++)
            {
                if (!uploadQueue.TryDequeue(out var filepath))
                    break;

                try
                {
                    Debug.WriteLine($"Uploading {filepath}");
                    await UploadFile(filepath);
                    Debug.WriteLine($"Uploaded {filepath}");
                    UploadedFiles++;

                    // Unlock reward after first successful upload
                    RewardManager.Instance.UnlockReward();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Upload] Error uploading {filepath} during shutdown drain: {ex.Message}");
                    // Keep the file for recovery instead of dropping it.
                    if (File.Exists(filepath))
                        QueueFile(filepath);

                    // Stop draining on the first failure: if one upload fails (e.g. the
                    // network is down) the rest will time out too, hanging shutdown for
                    // pending-count × the per-upload timeout. The remaining files stay
                    // queued and on disk, so HasQueuedFiles keeps the directory for recovery.
                    break;
                }
            }
        }

        public async Task UploadWorkerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                // Drive the time-based buffer flush so buffers that stop receiving
                // data are still uploaded once they exceed MaxBufferAge.
                FlushAgedBuffers();

                if (uploadQueue.Count > 0)
                {
                    if (uploadQueue.TryDequeue(out var filepath))
                    {
                        Debug.WriteLine($"File queued: {uploadQueue.Count}");
                        try
                        {
                            Debug.WriteLine($"Uploading {filepath}");
                            await UploadFile(filepath);
                            Debug.WriteLine($"Uploaded {filepath}");
                            UploadedFiles++;

                            // Restore connected state in case we were recovering
                            if (!IsConnected)
                            {
                                MarkConnected();
                            }

                            // Unlock reward after first successful upload
                            RewardManager.Instance.UnlockReward();
                        }
                        catch (Exception ex) when (!ct.IsCancellationRequested)
                        {
                            Debug.WriteLine($"Error uploading {filepath}: {ex}");

                            // Re-queue the file so it is not lost
                            QueueFile(filepath);

                            // Signal the tracer to stop the ETW session and enter reconnect mode
                            await EnterReconnectModeAsync(ct);
                        }
                    }
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(5000, ct).ConfigureAwait(false);
                }
            }
        }

        public void UploadThread(CancellationToken ct)
        {
            Debug.WriteLine("uploader thread started");
            Task.Run(() => UploadWorkerAsync(ct), ct);
            Task.Run(() => HeartbeatAsync(ct), ct);
        }

        /// <summary>
        /// Runs independently of the upload queue. Pings the worker endpoint every
        /// <see cref="HeartbeatIntervalSeconds"/> seconds so a dropped connection is
        /// detected quickly even when there is nothing queued to upload.
        /// </summary>
        private async Task HeartbeatAsync(CancellationToken ct)
        {
            const int HeartbeatIntervalSeconds = 15;
            Debug.WriteLine("[Heartbeat] started.");

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }

                // Only check if we currently think we're connected — the upload
                // worker owns the reconnect logic once disconnected.
                if (!IsConnected) continue;

                bool ok = await CheckInternetAsync().ConfigureAwait(false);
                if (!ok && IsConnected && !ct.IsCancellationRequested)
                {
                    Debug.WriteLine("[Heartbeat] Connectivity lost — entering reconnect mode.");
                    await EnterReconnectModeAsync(ct).ConfigureAwait(false);
                }
            }

            Debug.WriteLine("[Heartbeat] stopped.");
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Stops the ETW session, waits for connectivity to return (retrying every
        /// <see cref="RetryIntervalSeconds"/> seconds), then re-opens the resume gate
        /// so the <see cref="IOTracesCORE.Tracer"/> outer loop can restart the session.
        /// </summary>
        private async Task EnterReconnectModeAsync(CancellationToken ct)
        {
            // Block the tracer's restart gate
            ResumeGate.Reset();
            IsConnected = false;
            RetryAttempts = 0;
            ConnectionStatus = "Reconnecting…";
            OnConnectionStateChanged?.Invoke(ConnectionStatus);

            // Ask the Tracer to stop the current ETW kernel session
            Debug.WriteLine("[Upload] Connection lost — stopping ETW session.");
            OnStopSessionRequested?.Invoke();

            // Retry loop
            while (!ct.IsCancellationRequested)
            {
                RetryAttempts++;
                ConnectionStatus = $"Reconnecting… (attempt {RetryAttempts})";
                Debug.WriteLine($"[Upload] {ConnectionStatus}");
                OnConnectionStateChanged?.Invoke(ConnectionStatus);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(RetryIntervalSeconds), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (await CheckInternetAsync().ConfigureAwait(false))
                {
                    MarkConnected();
                    return;
                }
            }
        }

        private void MarkConnected()
        {
            IsConnected = true;
            ConnectionStatus = "Connected";
            RetryAttempts = 0;
            Debug.WriteLine("[Upload] Connection restored — resuming ETW session.");
            OnConnectionStateChanged?.Invoke(ConnectionStatus);
            // Unblock the Tracer outer loop so it can restart the ETW session
            ResumeGate.Set();
        }

        private async Task<bool> CheckInternetAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var response = await http.GetAsync(TestUrl).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Upload] Internet check failed: {ex.Message}");
                return false;
            }
        }
    }

}