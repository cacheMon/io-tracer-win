using Amazon.S3;
using Amazon.S3.Model;
using IOTracesCORE.utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.cloudstorage
{
    internal class ObjectStorageHandler
    {

        private R2Client r2Client;
        private ConcurrentQueue<string> uploadQueue = new();
        public static int UploadedFiles = 0;
        private TimeSpan LastActiveHours = TimeSpan.FromSeconds(0);
        public static int LastFileEvent;

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
            LastActiveHours = WriterManager.active_session;
        }

        public void QueueFile(string filepath)
        {
            uploadQueue.Enqueue(filepath);
        }

        public async Task ClearQueue()
        {
            Debug.WriteLine("Clearing upload queue");
            while (uploadQueue.Count > 0)
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

                        // Unlock reward after first successful upload
                        RewardManager.Instance.UnlockReward();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error uploading {filepath}: {ex}");
                    }
                }
            }
        }

        public async Task UploadWorkerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
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