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
        // Schema version stamped into manifest.json. Reset to "1" by request; the
        // cross-OS aligned fs/block column layout (shared prefix, lowercase canonical
        // operation names, header row on every file) is unchanged.
        public const string SchemaVersion = "1";

        // Consumer lag (ms) at session stop above which the fs stream is considered to have a
        // silently-dropped tail (the consumer never drained the kernel-buffered backlog before
        // stop). 5s is well clear of normal end-of-run drain (sub-second) yet flags the
        // multi-minute freezes seen under sustained FileIO overload. See fs_consumer_lag_ms.
        private const long TailIncompleteLagMs = 5000;

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
                    // --- cross-OS prefix --- #
                    // Omits the Linux prefix's `inode` and `device`: Windows ETW FileIO events
                    // carry neither (kernel file identity is `file_key`, volume is in `filename`),
                    // so they were always empty here. This diverges the Windows fs layout from
                    // Linux by design — read columns by NAME, not position.
                    Col("timestamp", "timestamp"), Col("operation", "string"), Col("pid", "int"),
                    Col("tid", "int"), Col("command", "string"), Col("filename", "string"),
                    Col("size", "int", "bytes"), Col("offset", "int", "bytes"),
                    Col("bytes_completed", "int", "bytes"), Col("flags", "flags"),
                    // --- Windows-only extras --- #
                    Col("create_options", "flags"), Col("share_access", "flags"),
                    Col("create_disposition", "enum"), Col("view_size", "int", "bytes"),
                    Col("file_info_class", "string"), Col("fsctl_code", "string"),
                    Col("irp", "hex_pointer"), Col("file_key", "hex_pointer"),
                    Col("file_attributes", "flags"), Col("command_line", "string"),
                    Col("nt_status", "hex"),
                }
            },
            ["block"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "block/*.csv.zst",
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
            // Lossless aggregation of metadata events SHED from fs/ under sustained load:
            // when the consumer falls behind (see LoadShedder), low-value ops (getattr/
            // close/op_end/dir_enum/...) are folded into per-(process,file,op) counts here
            // instead of being emitted (or kernel-dropped). read/write/create keep full
            // per-event detail in fs/. A file's metadata may therefore appear in fs/ (when
            // not overloaded) and/or here (when overloaded) — merge both for full coverage.
            ["fs_summary"] = new Dictionary<string, object?>
            {
                ["path_glob"] = "fs_summary/*.csv.zst",
                ["aggregation"] = "per-(process,file,operation), per-minute event counts (shed under load)",
                ["columns"] = new object[]
                {
                    Col("Ts", "timestamp"), Col("Pid", "int"), Col("Comm", "string"),
                    Col("Filename", "string"), Col("Operation", "string"), Col("Count", "int"),
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
                "disk" => "block",
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
                ["block"] = snapshot.DiskEvents,
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
                // Diagnostic for a sparse fs/ stream. filesystem_events_received = raw FileIO
                // events the kernel delivered to a handler (before ProcessFilter / filename
                // resolution); filesystem_events_filtered_by_process = those dropped for an
                // excluded process. received >> filesystem_events => we discard most of what
                // the kernel delivers; received ~= filesystem_events ~= low (with
                // session_events_lost 0) => the kernel genuinely delivers little (low-I/O box,
                // not under-capture).
                ["filesystem_events_received"] = snapshot.FilesystemEventsReceived,
                ["filesystem_events_filtered_by_process"] = snapshot.FilesystemEventsFilteredByProcess,
                ["disk_events"] = snapshot.DiskEvents,
                ["memory_events"] = snapshot.MemoryEvents,
                ["network_packets"] = snapshot.NetworkPackets,
                ["network_rows"] = snapshot.NetworkRows,
                ["process_snapshot_rows"] = snapshot.ProcessSnapshotRows,
                ["filesystem_snapshot_rows"] = snapshot.FilesystemSnapshotRows,
                // Snapshot coverage: directories fully walked vs. skipped (access
                // denied / not found / error). A non-zero inaccessible count means the
                // filesystem snapshot is partial and by roughly how much.
                ["filesystem_snapshot_dirs_scanned"] = snapshot.FilesystemSnapshotDirsScanned,
                ["filesystem_snapshot_dirs_inaccessible"] = snapshot.FilesystemSnapshotDirsInaccessible,
                ["etw_events_lost"] = snapshot.EtwEventsLost,
                // AUTHORITATIVE OS-queried kernel-side lost count (session.EventsLost). When
                // session_events_lost > etw_events_lost, the kernel dropped events that the
                // consumer-side counter could not see — the actual fs-truncation signal.
                ["session_events_lost"] = snapshot.SessionEventsLost,
                // The kernel buffer pool (MB) actually granted this session, after Tracer's
                // step-down. Read WITH session_events_lost: a small pool here alongside heavy
                // authoritative loss points at insufficient burst headroom (the OS refused the
                // requested pool) rather than a slow consumer.
                ["etw_buffer_size_mb"] = snapshot.BufferSizeMb,
                // Loss/back-pressure DOWNSTREAM of the ETW consumer: filesystem events the
                // off-thread formatter could not keep up with (bounded queue rejected them),
                // plus the queue's last/peak depth. etw_events_lost covers kernel->consumer
                // loss; these cover consumer->formatter loss that the kernel counter cannot
                // see. fs_format_dropped>0 or a high-water near the cap => formatting is the
                // bottleneck; ~0 high-water with a frozen fs stream => the freeze is upstream.
                ["fs_format_dropped"] = snapshot.FsFormatDropped,
                ["fs_format_queue_depth"] = snapshot.FsFormatQueueDepth,
                ["fs_format_queue_high_water"] = snapshot.FsFormatQueueHighWater,
                // Load-shedding (LoadShedder): under sustained backpressure the consumer sheds
                // low-value metadata ops into the fs_summary stream (lossless per-(process,file,op)
                // counts) instead of emitting full rows. fs_summary_events = raw events shed+
                // aggregated; fs_summary_rows = summary rows written; fs_shed_peak_lag_ms = peak
                // consumer event-time lag (the shed trigger).
                ["fs_summary_events"] = snapshot.FsSummaryEvents,
                ["fs_summary_rows"] = snapshot.FsSummaryRows,
                ["fs_shed_peak_lag_ms"] = snapshot.FsShedPeakLagMs,
                // CONSUMER LAG / SILENT TAIL LOSS. session_events_lost only counts kernel buffer
                // OVERFLOW; it does NOT count events the kernel buffered fine but the single FileIO
                // consumer never drained before the session stopped. When the consumer can't keep
                // up (issue #68), it replays the backlog at a fraction of real time and the fs
                // stream FREEZES at its drain point — everything generated after that is abandoned
                // at stop, uncounted. fs_consumer_lag_ms is the lag at finalize = the span of fs
                // events that were buffered but never consumed (~the amount of tail missing).
                // fs_capture_tail_incomplete flags this so session_events_lost == 0 is not misread
                // as "complete": a 0 here with a large fs_consumer_lag_ms means the tail is gone.
                // Do NOT read "high fs_summary_events + low session_events_lost" as "absorbed
                // losslessly" — under a freeze it instead means the tail was silently dropped.
                ["fs_consumer_lag_ms"] = snapshot.FsConsumerLagMs,
                ["fs_capture_tail_incomplete"] = snapshot.FsConsumerLagMs >= TailIncompleteLagMs,
            };

            return (counters, dead);
        }

        public static string Build(bool final, string deviceId, bool anonymous, bool uploadEnabled,
            DateTime startUtc, DateTime? stopUtc, bool lowOverheadLogging = false)
        {
            var (counters, dead) = CountersAndDeadProbes();
            // Dead-probe detection is only meaningful once the session has run; an
            // initial (non-final) manifest hasn't seen any events yet.
            if (!final) dead = new List<string>();
            // In lightweight mode the memory stream is intentionally disabled (its keywords
            // are never enabled), so an empty `mr` stream is expected — not a dead probe.
            if (lowOverheadLogging) dead.Remove("mr");

            // Per-row timestamps are machine-local wall-clock with no embedded offset
            // (see clock_source below). Record the capture machine's UTC offset and
            // timezone so consumers can convert local timestamps to UTC without having
            // to guess the machine's region. The offset is taken at session start; a
            // mid-session DST transition would change it, but the timezone id resolves
            // the exact per-instant offset for the rare long capture that spans one.
            TimeSpan startOffset = TimeZoneInfo.Local.GetUtcOffset(startUtc);
            string utcOffset = (startOffset < TimeSpan.Zero ? "-" : "+")
                + startOffset.Duration().ToString("hh\\:mm");

            var manifest = new Dictionary<string, object?>
            {
                ["schema_version"] = SchemaVersion,
                ["tracer_version"] = VersionManager.Instance.GetVersionString(),
                ["tracer_commit"] = VersionManager.Instance.GetCommitId(),
                ["platform"] = "windows",
                ["finalized"] = final,
                ["device_id"] = deviceId,
                ["anonymous"] = anonymous,
                ["upload_enabled"] = uploadEnabled,
                // "full" logs everything via the legacy NT-Kernel-Logger FileIO. "lightweight"
                // suppresses op_end + the memory keywords AND switches fs capture to the modern
                // Microsoft-Windows-Kernel-File provider with a reduced keyword set, so the kernel
                // never generates the metadata-op mass (getattr/close/cleanup/dir_enum/fsctl) or
                // map_file — ~3x fewer FileIO events (read/write/create + filenames kept). The mode
                // is chosen by machine spec unless overridden. Recorded so a consumer knows missing
                // op_end / memory / metadata-op / map_file data is intentional (the mode), not inactivity.
                ["logging_mode"] = lowOverheadLogging ? "lightweight" : "full",
                ["clock_source"] = new Dictionary<string, object?>
                {
                    // ETW DateTime timestamps are machine-local wall-clock (QPC-derived).
                    // The per-row format carries no offset, so utc_offset/timezone below make
                    // the local timestamps unambiguous and convertible to UTC. Note that
                    // folder & shard names and start_utc/stop_utc are already in UTC.
                    ["timestamps"] = "local_wall_clock",
                    ["format"] = "yyyy-MM-dd HH:mm:ss.ffffff",
                    ["derived_from"] = "windows_qpc",
                    ["utc_offset"] = utcOffset,
                    ["timezone"] = TimeZoneInfo.Local.Id,
                    ["timezone_display_name"] = TimeZoneInfo.Local.DisplayName,
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
