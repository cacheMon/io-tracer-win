using IOTracesCORE.cloudstorage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IOTracesCORE.utils
{
    internal class ConfigClasses
    {
        public class PersistedConfig
        {
            public string OutputPath { get; set; } = "";
            public bool Anonymous { get; set; }
            public bool UploadEnabled { get; set; }
            public bool DevMode { get; set; }
        }

        public class TraceMetadata
        {
            public long TraceDuration { get; set; }
            public long FileEventsCount { get; set; }
        }

        public static string AppConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "IOTracer", "config.json.enc");

        public static string ConfigTraceMetadataPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "IOTracer", "trace_metadata.json.enc");

        public static void SaveTracemetaConfiguration(TimeSpan trace_active, long file_events)
        {
            try
            {
                Debug.WriteLine($"Saving a session of {trace_active.TotalSeconds} seconds");
                var cfg = new TraceMetadata
                {
                    TraceDuration = (long)trace_active.TotalSeconds,
                    FileEventsCount = file_events
                };

                string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                byte[] encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigTraceMetadataPath)!);
                File.WriteAllBytes(ConfigTraceMetadataPath, encrypted);
            }
            catch (Exception ex) { Debug.WriteLine("Save config failed: " + ex.Message); }
        }

        public static void LoadTracemetaConfiguration()
        {
            try
            {
                Debug.Write($"Receiving config from: {ConfigTraceMetadataPath}");
                if (!File.Exists(ConfigTraceMetadataPath)) return;

                byte[] encrypted = File.ReadAllBytes(ConfigTraceMetadataPath);
                byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(decrypted);
                var cfg = JsonSerializer.Deserialize<TraceMetadata>(json);

                if (cfg == null) return;

                WriterManager.trace_duration = TimeSpan.FromSeconds(cfg.TraceDuration);
                WriterManager.file_event_counter += cfg.FileEventsCount;
                ObjectStorageHandler.LastFileEvent = cfg.FileEventsCount;
                Debug.WriteLine($"Loaded a session of {cfg.TraceDuration} seconds");
                Debug.WriteLine($"Loaded {cfg.FileEventsCount} file events");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Load config failed: " + ex.Message);
                
            }
        }
    }
}
