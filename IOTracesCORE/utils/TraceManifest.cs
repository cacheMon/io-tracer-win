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
        public const string SchemaVersion = "4";

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
                    Col("Ts", "timestamp"), Col("Op", "string"), Col("Pid", "int"),
                    Col("Comm", "string"), Col("Filename", "string"), Col("TraceSize", "int", "bytes"),
                    Col("CreateOptions", "flags"), Col("ShareAccess", "flags"), Col("CreateDisposition", "enum"),
                    Col("Offset", "int", "bytes"), Col("ViewSize", "int", "bytes"), Col("FileInfoClass", "string"),
                    Col("FsctlCode", "string"), Col("ThreadId", "int"), Col("Irp", "hex_pointer"),
                    Col("FileKey", "hex_pointer"), Col("FileAttributes", "flags"), Col("IoFlags", "flags"),
                    Col("CommandLine", "string"),
                }
            },
            ["ds"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "ds/*.csv.zst",
                ["columns"] = new object[]
                {
                    Col("Ts", "timestamp"), Col("Pid", "int"), Col("ThreadId", "int"), Col("Comm", "string"),
                    Col("Sector", "int", "512B_sectors"), Col("Operation", "string"), Col("TraceSize", "int", "bytes"),
                    Col("Latency", "float", "ms"), Col("DiskNumber", "int"), Col("Irp", "hex_pointer"),
                    Col("IrpFlags", "flags"),
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
            ["driver"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "driver/*.csv.zst",
                ["columns"] = new object[]
                {
                    Col("Ts", "timestamp"), Col("Pid", "int"), Col("ThreadId", "int"), Col("Comm", "string"),
                    Col("Operation", "string"), Col("Irp", "hex_pointer"), Col("RequestId", "int"),
                    Col("MajorFunction", "int"), Col("MinorFunction", "int"), Col("RoutineAddr", "hex_pointer"),
                    Col("FileObject", "hex_pointer"), Col("DeviceObject", "hex_pointer"),
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
        /// Per-stream cumulative event counts and the streams that produced zero
        /// events (likely a dead probe). Network uses raw packet count rather than
        /// emitted rows, since rows are only per-minute summaries.
        /// </summary>
        private static (Dictionary<string, object?> counters, List<string> dead) CountersAndDeadProbes()
        {
            var streamCounts = new Dictionary<string, long>
            {
                ["filesystem"] = TraceStats.FilesystemEvents,
                ["ds"] = TraceStats.DiskEvents,
                ["mr"] = TraceStats.MemoryEvents,
                ["driver"] = TraceStats.DriverEvents,
                ["nw"] = TraceStats.NetworkPackets,
                ["process"] = TraceStats.ProcessSnapshotRows,
                ["filesystem_snapshot"] = TraceStats.FilesystemSnapshotRows,
            };

            var dead = new List<string>();
            foreach (var kv in streamCounts)
                if (kv.Value == 0) dead.Add(kv.Key);

            var counters = new Dictionary<string, object?>
            {
                ["filesystem_events"] = TraceStats.FilesystemEvents,
                ["disk_events"] = TraceStats.DiskEvents,
                ["memory_events"] = TraceStats.MemoryEvents,
                ["driver_events"] = TraceStats.DriverEvents,
                ["network_packets"] = TraceStats.NetworkPackets,
                ["network_rows"] = TraceStats.NetworkRows,
                ["process_snapshot_rows"] = TraceStats.ProcessSnapshotRows,
                ["filesystem_snapshot_rows"] = TraceStats.FilesystemSnapshotRows,
                ["etw_events_lost"] = TraceStats.EtwEventsLost,
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
