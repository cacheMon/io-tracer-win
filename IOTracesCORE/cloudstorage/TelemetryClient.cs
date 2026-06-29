using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IOTracesCORE.cloudstorage
{
    internal class TelemetryClient
    {
        private static readonly HttpClient http =
            new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        private const string EndpointUrl =
            "https://io-tracer-worker.1a1a11a.workers.dev";

        private static readonly TelemetryClient _instance = new();
        public static TelemetryClient Instance => _instance;

        private static readonly System.Threading.ReaderWriterLockSlim _stateIoLock =
            new System.Threading.ReaderWriterLockSlim();

        // ─────────────────────────────────────────────────────────────

        public async Task SendDeltaTelemetryAsync(
            string computerId,
            DateTime timestamp)
        {
            try
            {
                // Calculate delta from current counters vs last sent state
                var delta = CalculateDelta();
                if (delta == null)
                    return;  // No change, skip send

                Debug.WriteLine(
                    $"[Telemetry] Sending delta: " +
                    $"active={delta.ActiveDurationSeconds}s, " +
                    $"filesystem={delta.FileOperations}, " +
                    $"disk={delta.DiskOperations}, " +
                    $"memory={delta.MemoryOperations}, " +
                    $"network={delta.NetworkOperations}, " +
                    $"process={delta.ProcessOperations}, " +
                    $"fs_snapshot={delta.FilesystemSnapshotOperations}");

                // Serialize payload
                var payload = new
                {
                    computer_id = computerId,
                    operating_system = GetOsVersion(),
                    duration_s = delta.ActiveDurationSeconds,
                    events = new
                    {
                        filesystem = delta.FileOperations,
                        disk = delta.DiskOperations,
                        memory = delta.MemoryOperations,
                        network = delta.NetworkOperations,
                        process = delta.ProcessOperations,
                        filesystem_snapshot = delta.FilesystemSnapshotOperations
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json");

                // POST to worker (non-blocking)
                var response = await http.PostAsync(
                    $"{EndpointUrl}/telemetry",
                    content);

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[Telemetry] Sent successfully");

                    // Persist new state only on success
                    await PersistStateAsync(
                        delta.CurrentActiveSec,
                        delta.CurrentFileOps,
                        delta.CurrentDiskOps,
                        delta.CurrentMemoryOps,
                        delta.CurrentNetworkOps,
                        delta.CurrentProcessOps,
                        delta.CurrentFsSnapshotOps);
                }
                else
                {
                    Debug.WriteLine(
                        $"[Telemetry] Server returned {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                // Never throw; telemetry is optional
                Debug.WriteLine($"[Telemetry] Error: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────

        private DeltaTelemetry? CalculateDelta()
        {
            try
            {
                // Load last sent state
                var lastState = LoadState();

                // Read current counters atomically
                long currentActiveSec =
                    (long)IOTracesCORE.WriterManager.GetActiveDuration().TotalSeconds;
                long currentFileOps =
                    IOTracesCORE.WriterManager.GetFileEventCount();
                long currentDiskOps =
                    IOTracesCORE.WriterManager.GetDiskEventCount();
                long currentMemoryOps =
                    IOTracesCORE.WriterManager.GetMemoryEventCount();
                long currentNetworkOps =
                    IOTracesCORE.WriterManager.GetNetworkEventCount();
                long currentProcessOps =
                    IOTracesCORE.WriterManager.GetProcessSnapshotCount();
                long currentFsSnapshotOps =
                    IOTracesCORE.WriterManager.GetFilesystemSnapshotCount();

                // Calculate deltas
                long activeDelta = currentActiveSec - lastState.LastActiveDurationSentSeconds;
                long fileOpsDelta = currentFileOps - lastState.LastFileEventsSent;
                long diskOpsDelta = currentDiskOps - lastState.LastDiskEventsSent;
                long memoryOpsDelta = currentMemoryOps - lastState.LastMemoryEventsSent;
                long networkOpsDelta = currentNetworkOps - lastState.LastNetworkEventsSent;
                long processOpsDelta = currentProcessOps - lastState.LastProcessEventsSent;
                long fsSnapshotOpsDelta = currentFsSnapshotOps - lastState.LastFilesystemSnapshotEventsSent;

                // Detect session restart (counter reset)
                if (activeDelta < 0 || fileOpsDelta < 0 || diskOpsDelta < 0 || memoryOpsDelta < 0 ||
                    networkOpsDelta < 0 || processOpsDelta < 0 || fsSnapshotOpsDelta < 0)
                {
                    Debug.WriteLine("[Telemetry] Detected counter reset, skipping delta");
                    return null;
                }

                // Skip if no change
                if (activeDelta == 0 && fileOpsDelta == 0 && diskOpsDelta == 0 && memoryOpsDelta == 0 &&
                    networkOpsDelta == 0 && processOpsDelta == 0 && fsSnapshotOpsDelta == 0)
                    return null;

                return new DeltaTelemetry
                {
                    ActiveDurationSeconds = (int)Math.Min(int.MaxValue, activeDelta),
                    FileOperations = (int)Math.Min(int.MaxValue, fileOpsDelta),
                    DiskOperations = (int)Math.Min(int.MaxValue, diskOpsDelta),
                    MemoryOperations = (int)Math.Min(int.MaxValue, memoryOpsDelta),
                    NetworkOperations = (int)Math.Min(int.MaxValue, networkOpsDelta),
                    ProcessOperations = (int)Math.Min(int.MaxValue, processOpsDelta),
                    FilesystemSnapshotOperations = (int)Math.Min(int.MaxValue, fsSnapshotOpsDelta),
                    CurrentActiveSec = currentActiveSec,
                    CurrentFileOps = currentFileOps,
                    CurrentDiskOps = currentDiskOps,
                    CurrentMemoryOps = currentMemoryOps,
                    CurrentNetworkOps = currentNetworkOps,
                    CurrentProcessOps = currentProcessOps,
                    CurrentFsSnapshotOps = currentFsSnapshotOps
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] Delta calculation error: {ex.Message}");
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────

        private TelemetryState LoadState()
        {
            _stateIoLock.EnterReadLock();
            try
            {
                string statePath = GetStateFilePath();
                if (!File.Exists(statePath))
                    return new TelemetryState();

                try
                {
                    string encrypted = File.ReadAllText(statePath);
                    byte[] decrypted = System.Security.Cryptography.ProtectedData
                        .Unprotect(
                            Convert.FromBase64String(encrypted),
                            null,
                            System.Security.Cryptography.DataProtectionScope.CurrentUser);

                    string json = Encoding.UTF8.GetString(decrypted);
                    var state = JsonSerializer
                        .Deserialize<TelemetryState>(json);

                    return state ?? new TelemetryState();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Telemetry] Failed to load state: {ex.Message}");
                    return new TelemetryState();  // Fallback to zeros
                }
            }
            finally
            {
                _stateIoLock.ExitReadLock();
            }
        }

        private async Task PersistStateAsync(
            long activeSec,
            long fileOps,
            long diskOps,
            long memoryOps,
            long networkOps,
            long processOps,
            long fsSnapshotOps)
        {
            _stateIoLock.EnterWriteLock();
            try
            {
                var newState = new TelemetryState
                {
                    LastActiveDurationSentSeconds = activeSec,
                    LastFileEventsSent = fileOps,
                    LastDiskEventsSent = diskOps,
                    LastMemoryEventsSent = memoryOps,
                    LastNetworkEventsSent = networkOps,
                    LastProcessEventsSent = processOps,
                    LastFilesystemSnapshotEventsSent = fsSnapshotOps,
                    LastSendUtc = DateTime.UtcNow
                };

                string json = JsonSerializer.Serialize(newState);
                byte[] plaintext = Encoding.UTF8.GetBytes(json);
                byte[] encrypted = System.Security.Cryptography.ProtectedData
                    .Protect(
                        plaintext,
                        null,
                        System.Security.Cryptography.DataProtectionScope.CurrentUser);

                string statePath = GetStateFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
                File.WriteAllText(statePath, Convert.ToBase64String(encrypted));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] Failed to persist state: {ex.Message}");
                // Non-critical failure; will retry on next upload
            }
            finally
            {
                _stateIoLock.ExitWriteLock();
            }
        }

        private string GetStateFilePath()
        {
            string appDataPath = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appDataPath, "IOTracer", "telemetry_state.json.enc");
        }

        /// <summary>Clear telemetry state file at session start.</summary>
        public void ClearSessionState()
        {
            _stateIoLock.EnterWriteLock();
            try
            {
                string statePath = GetStateFilePath();
                if (File.Exists(statePath))
                {
                    try { File.Delete(statePath); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Telemetry] Failed to clear state file: {ex.Message}");
                    }
                }
            }
            finally
            {
                _stateIoLock.ExitWriteLock();
            }
        }

        private string GetOsVersion()
        {
            var os = Environment.OSVersion;
            return $"Windows";
        }
    }

    // ───────────────────────────────────────────────────────────────

    internal class TelemetryState
    {
        public long LastActiveDurationSentSeconds { get; set; } = 0;
        public long LastFileEventsSent { get; set; } = 0;
        public long LastDiskEventsSent { get; set; } = 0;
        public long LastMemoryEventsSent { get; set; } = 0;
        public long LastNetworkEventsSent { get; set; } = 0;
        public long LastProcessEventsSent { get; set; } = 0;
        public long LastFilesystemSnapshotEventsSent { get; set; } = 0;
        public DateTime LastSendUtc { get; set; } = DateTime.UtcNow;
    }

    internal class DeltaTelemetry
    {
        public int ActiveDurationSeconds { get; set; }
        public int FileOperations { get; set; }
        public int DiskOperations { get; set; }
        public int MemoryOperations { get; set; }
        public int NetworkOperations { get; set; }
        public int ProcessOperations { get; set; }
        public int FilesystemSnapshotOperations { get; set; }
        public long CurrentActiveSec { get; set; }
        public long CurrentFileOps { get; set; }
        public long CurrentDiskOps { get; set; }
        public long CurrentMemoryOps { get; set; }
        public long CurrentNetworkOps { get; set; }
        public long CurrentProcessOps { get; set; }
        public long CurrentFsSnapshotOps { get; set; }
    }
}
