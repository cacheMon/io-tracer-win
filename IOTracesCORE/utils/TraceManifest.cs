using System;
using System.Collections.Generic;
using System.Text.Json;

namespace IOTracesCORE.utils
{
    /// <summary>
    /// Builds the per-session <c>manifest.json</c>: the authoritative, machine-readable
    /// description of a trace session — schema version, per-stream column definitions
    /// (name/type/unit), clock source, tracer/version metadata, start/stop times, and
    /// the end-of-session counters (events per stream, ETW lost events, dead probes).
    /// This replaces relying on the human-maintained format doc, which drifts.
    /// </summary>
    internal static class TraceManifest
    {
        // Bump when any stream's column set or semantics change.
        // v5: cross-OS aligned fs/ds layout — a fixed shared column prefix
        //     (identical names/order to the Linux tracer), lowercase canonical
        //     operation names, and a CSV header row on every file.
        public const string SchemaVersion = "5";

        private static Dictionary<string, object?> Col(string name, string type, string? unit = null)
        {
            var d = new Dictionary<string, object?> { ["name"] = name, ["type"] = type };
            if (unit != null) d["unit"] = unit;
            return d;
        }

        // Column order MUST match each trace type's FormatAsCsv emission order.
        private static Dictionary<string, object?> Streams() => new()
        {
            ["filesystem"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "fs/*.csv.zst",
                ["columns"] = new object[]
                {
                    // --- shared cross-OS prefix (columns 1-12; identical on Linux) --- #
                    Col("timestamp", "timestamp"), Col("operation", "string"), Col("pid", "int"),
                    Col("tid", "int"), Col("command", "string"), Col("filename", "string"),
                    Col("size", "int", "bytes"), Col("offset", "int", "bytes"),
                    Col("bytes_completed", "int", "bytes"), Col("inode", "int"),
                    Col("device", "string"), Col("flags", "flags"),
                    // --- Windows-only extras (columns 13+) --- #
                    Col("create_options", "flags"), Col("share_access", "flags"),
                    Col("create_disposition", "enum"), Col("view_size", "int", "bytes"),
                    Col("file_info_class", "string"), Col("fsctl_code", "string"),
                    Col("irp", "hex_pointer"), Col("file_key", "hex_pointer"),
                    Col("file_attributes", "flags"), Col("command_line", "string"),
                }
            },
            ["ds"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "ds/*.csv.zst",
                ["columns"] = new object[]
                {
                    // --- shared cross-OS prefix (columns 1-10; identical on Linux) --- #
                    Col("timestamp", "timestamp"), Col("operation", "string"), Col("pid", "int"),
                    Col("tid", "int"), Col("command", "string"), Col("sector", "int", "512B_sectors"),
                    Col("size", "int", "bytes"), Col("latency_ms", "float", "ms"),
                    Col("device", "string"), Col("flags", "flags"),
                    // --- Windows-only extras (columns 11+) --- #
                    Col("irp", "hex_pointer"),
                }
            },
            ["mr"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "mr/*.csv.zst",
                ["columns"] = new object[]
                {
                    Col("Ts", "timestamp"), Col("Pid", "int"), Col("ThreadId", "int"), Col("Comm", "string"),
                    Col("Type", "string"), Col("VirtualAddress", "hex_pointer"), Col("ByteCount", "int", "bytes"),
                }
            },
            ["nw"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "nw/*.csv.zst",
                ["aggregation"] = "per-connection, per-minute byte totals",
                ["columns"] = new object[]
                {
                    Col("Ts", "timestamp"), Col("Pid", "int"), Col("Comm", "string"), Col("Proto", "int"),
                    Col("Saddr", "string"), Col("Daddr", "string"), Col("Sport", "int"), Col("Dport", "int"),
                    Col("ConnId", "int"), Col("BytesSent", "int", "bytes"), Col("BytesReceived", "int", "bytes"),
                }
            },
            ["process"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "process/*.csv.zst",
                ["columns"] = new object[]
                {
                    Col("Ts", "timestamp"), Col("ProcessId", "int"), Col("Name", "string"), Col("CommandLine", "string"),
                    Col("VirtualSize", "int", "bytes"), Col("WorkingSetSize", "int", "bytes"), Col("CreationDate", "timestamp"),
                    Col("CpuUsage_5s", "float", "percent"), Col("CpuUsage_2m", "float", "percent"),
                    Col("CpuUsage_1h", "float", "percent"),
                }
            },
            ["filesystem_snapshot"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "filesystem_snapshot/*.csv.zst",
                ["columns"] = new object[]
                {
                    Col("timestamp", "timestamp"), Col("path", "string"), Col("size", "int", "bytes"),
                    Col("CreationDate", "timestamp"), Col("modificationDate", "timestamp"), Col("LastAccessTime", "timestamp"),
                    Col("Attributes", "string"), Col("Extension", "string"), Col("IsReadOnly", "bool"),
                }
            },
            ["system_spec"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "system_spec/*.json",
                ["format"] = "json",
            },
        };

        /// <summary>
        /// CSV header row (comma-joined column names) for a WriterManager trace
        /// type, so every emitted CSV file is self-describing. Returns "" for
        /// types without a defined column schema. The names come from the same
        /// <see cref="Streams"/> table that backs the manifest, so the header can
        /// never drift from the manifest schema.
        /// </summary>
        public static string HeaderLine(string tracetype)
        {
            string key = tracetype switch
            {
                "disk" => "ds",
                "memory" => "mr",
                "network" => "nw",
                _ => tracetype, // filesystem, process, filesystem_snapshot
            };

            if (Streams().TryGetValue(key, out var sObj)
                && sObj is Dictionary<string, object?> s
                && s.TryGetValue("columns", out var colsObj)
                && colsObj is object[] cols)
            {
                var names = new List<string>(cols.Length);
                foreach (var c in cols)
                    if (c is Dictionary<string, object?> cd
                        && cd.TryGetValue("name", out var n) && n is string ns)
                        names.Add(ns);
                return string.Join(",", names);
            }
            return "";
        }

        /// <summary>
        /// Per-stream cumulative event counts and the streams that produced zero
        /// events (likely a dead probe). Network uses raw packet count rather than
        /// emitted rows, since rows are only per-minute summaries.
        /// </summary>
        private static (Dictionary<string, object?> counters, List<string> dead) CountersAndDeadProbes()
        {
            // Snapshot each counter once via an atomic read. Interlocked.Read keeps the
            // read atomic on 32-bit runtimes (64-bit reads aren't atomic there), pairing
            // with the Interlocked writes in TraceStats; reading once also keeps the
            // counters and dead-probe list mutually consistent.
            var snapshot = TraceStats.Snapshot();

            var streamCounts = new Dictionary<string, long>
            {
                ["filesystem"] = snapshot.FilesystemEvents,
                ["ds"] = snapshot.DiskEvents,
                ["mr"] = snapshot.MemoryEvents,
                ["nw"] = snapshot.NetworkPackets,
                ["process"] = snapshot.ProcessSnapshotRows,
                ["filesystem_snapshot"] = snapshot.FilesystemSnapshotRows,
            };

            var dead = new List<string>();
            foreach (var kv in streamCounts)
                if (kv.Value == 0) dead.Add(kv.Key);

            var counters = new Dictionary<string, object?>
            {
                ["filesystem_events"] = snapshot.FilesystemEvents,
                ["disk_events"] = snapshot.DiskEvents,
                ["memory_events"] = snapshot.MemoryEvents,
                ["network_packets"] = snapshot.NetworkPackets,
                ["network_rows"] = snapshot.NetworkRows,
                ["process_snapshot_rows"] = snapshot.ProcessSnapshotRows,
                ["filesystem_snapshot_rows"] = snapshot.FilesystemSnapshotRows,
                ["etw_events_lost"] = snapshot.EtwEventsLost,
            };

            return (counters, dead);
        }

        public static string Build(bool final, string deviceId, bool anonymous, bool uploadEnabled,
            DateTime startUtc, DateTime? stopUtc)
        {
            var (counters, dead) = CountersAndDeadProbes();
            // Dead-probe detection is only meaningful once the session has run; an
            // initial (non-final) manifest hasn't seen any events yet.
            if (!final) dead = new List<string>();

            var manifest = new Dictionary<string, object?>
            {
                ["schema_version"] = SchemaVersion,
                ["tracer_version"] = VersionManager.Instance.GetVersionString(),
                ["platform"] = "windows",
                ["finalized"] = final,
                ["device_id"] = deviceId,
                ["anonymous"] = anonymous,
                ["upload_enabled"] = uploadEnabled,
                ["clock_source"] = new Dictionary<string, object?>
                {
                    // ETW DateTime timestamps are machine-local wall-clock (QPC-derived);
                    // dead-probe note: nw counts raw packets, so 0 may simply mean no external traffic.
                    ["timestamps"] = "local_wall_clock",
                    ["format"] = "yyyy-MM-dd HH:mm:ss.ffffff",
                    ["derived_from"] = "windows_qpc",
                },
                ["start_utc"] = startUtc.ToString("yyyy-MM-dd HH:mm:ss.ffffff"),
                ["stop_utc"] = stopUtc?.ToString("yyyy-MM-dd HH:mm:ss.ffffff"),
                ["streams"] = Streams(),
                ["counters"] = counters,
                ["dead_probes"] = dead,
            };

            return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
