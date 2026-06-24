using System.Threading;

namespace IOTracesCORE.utils
{
    /// <summary>
    /// Process-wide cumulative counters for the current trace session. Used to
    /// populate the per-session manifest (event volume per stream, ETW lost-event
    /// count) and to flag streams whose probe produced zero events (a dead probe).
    /// All increments are atomic so they are safe from the ETW thread, snapper
    /// threads, and the network flush timer.
    /// </summary>
    internal static class TraceStats
    {
        public static long FilesystemEvents;
        // Diagnostic: raw FileIO events the kernel DELIVERED to a filesystem handler,
        // counted at the per-handler gate BEFORE ProcessFilter / filename resolution.
        // FilesystemEventsReceived >> FilesystemEvents means we discard most of what the
        // kernel delivers (in our own code); Received ~= Events ~= low means the kernel
        // genuinely delivers little (a low-file-I/O box, not under-capture). FilteredByProcess
        // is the subset rejected by ProcessFilter (excluded process names). Together with the
        // authoritative session_events_lost (kernel-side drop), these separate the three
        // possible causes of a sparse fs stream.
        public static long FilesystemEventsReceived;
        public static long FilesystemEventsFilteredByProcess;
        public static long DiskEvents;
        public static long MemoryEvents;
        // Raw send/receive packets observed (pre-aggregation) — the network probe
        // liveness signal, since emitted rows are only per-minute summaries.
        public static long NetworkPackets;
        public static long NetworkRows;
        public static long ProcessSnapshotRows;
        public static long FilesystemSnapshotRows;
        // Filesystem-snapshot directory coverage: directories fully enumerated vs.
        // those skipped because they could not be read (access denied / not found /
        // error). A non-zero inaccessible count means the snapshot is partial; both
        // are surfaced in the manifest so consumers can judge snapshot completeness.
        public static long FilesystemSnapshotDirsScanned;
        public static long FilesystemSnapshotDirsInaccessible;
        // ETW events the kernel dropped, CONSUMER-side count (source.EventsLost). For a
        // real-time kernel session this often reads 0 even when the kernel did drop events,
        // so it is NOT authoritative on its own — pair it with SessionEventsLost below.
        public static long EtwEventsLost;
        // Authoritative session-level lost-event count, queried from the OS via
        // session.EventsLost (ControlTrace -> EVENT_TRACE_PROPERTIES). This is the count
        // that actually reveals fs truncation when EtwEventsLost (the consumer-side view)
        // reads a false 0. A gap (SessionEventsLost > EtwEventsLost) means the kernel
        // dropped events that the consumer-side counter never saw.
        public static long SessionEventsLost;
        // The ETW kernel buffer pool size (MB) actually granted for this session, after the
        // step-down in Tracer. A small value here (e.g. 64) next to a large SessionEventsLost
        // means the OS refused the requested pool and the capture ran with little burst
        // headroom — i.e. insufficient buffering, not a slow consumer.
        public static long BufferSizeMb;
        // Filesystem events the off-thread formatter could not keep up with: the bounded
        // _fsFormatQueue rejected them (TryAdd failed). Distinct from EtwEventsLost (which
        // is kernel->consumer loss): this is loss DOWNSTREAM of the consumer, invisible to
        // the kernel counter. FsFormatQueueDepth is the last observed queue depth and
        // FsFormatQueueHighWater its peak — together they show whether formatting kept up.
        public static long FsFormatDropped;
        public static long FsFormatQueueDepth;
        public static long FsFormatQueueHighWater;
        // Filesystem events SHED under sustained backpressure and aggregated into the
        // fs_summary stream instead of emitted as full rows. FsSummaryEvents = raw shed
        // events folded in; FsSummaryRows = summary rows emitted; FsShedPeakLagMs = peak
        // consumer event-time lag observed (the shed trigger). See LoadShedder.
        public static long FsSummaryEvents;
        public static long FsSummaryRows;
        public static long FsShedPeakLagMs;

        public static void Reset()
        {
            Interlocked.Exchange(ref FilesystemEvents, 0);
            Interlocked.Exchange(ref FilesystemEventsReceived, 0);
            Interlocked.Exchange(ref FilesystemEventsFilteredByProcess, 0);
            Interlocked.Exchange(ref DiskEvents, 0);
            Interlocked.Exchange(ref MemoryEvents, 0);
            Interlocked.Exchange(ref NetworkPackets, 0);
            Interlocked.Exchange(ref NetworkRows, 0);
            Interlocked.Exchange(ref ProcessSnapshotRows, 0);
            Interlocked.Exchange(ref FilesystemSnapshotRows, 0);
            Interlocked.Exchange(ref FilesystemSnapshotDirsScanned, 0);
            Interlocked.Exchange(ref FilesystemSnapshotDirsInaccessible, 0);
            Interlocked.Exchange(ref EtwEventsLost, 0);
            Interlocked.Exchange(ref SessionEventsLost, 0);
            Interlocked.Exchange(ref BufferSizeMb, 0);
            Interlocked.Exchange(ref FsFormatDropped, 0);
            Interlocked.Exchange(ref FsFormatQueueDepth, 0);
            Interlocked.Exchange(ref FsFormatQueueHighWater, 0);
            Interlocked.Exchange(ref FsSummaryEvents, 0);
            Interlocked.Exchange(ref FsSummaryRows, 0);
            Interlocked.Exchange(ref FsShedPeakLagMs, 0);
        }

        /// <summary>
        /// Zeroes the filesystem-snapshot counters. Called when an INCOMPLETE snapshot's
        /// part files are discarded on shutdown, so the manifest never reports rows/dirs
        /// for a snapshot that shipped zero files (which would mismatch the on-disk stream).
        /// </summary>
        public static void ResetFilesystemSnapshotCounters()
        {
            Interlocked.Exchange(ref FilesystemSnapshotRows, 0);
            Interlocked.Exchange(ref FilesystemSnapshotDirsScanned, 0);
            Interlocked.Exchange(ref FilesystemSnapshotDirsInaccessible, 0);
        }

        /// <summary>
        /// Zeroes the process-snapshot row counter. Called when an incomplete process
        /// snapshot's files are discarded, for the same manifest==disk consistency reason.
        /// </summary>
        public static void ResetProcessSnapshotCounter()
        {
            Interlocked.Exchange(ref ProcessSnapshotRows, 0);
        }

        /// <summary>Immutable snapshot of all counters, each read atomically.</summary>
        public readonly struct Counts
        {
            public long FilesystemEvents { get; init; }
            public long FilesystemEventsReceived { get; init; }
            public long FilesystemEventsFilteredByProcess { get; init; }
            public long DiskEvents { get; init; }
            public long MemoryEvents { get; init; }
            public long NetworkPackets { get; init; }
            public long NetworkRows { get; init; }
            public long ProcessSnapshotRows { get; init; }
            public long FilesystemSnapshotRows { get; init; }
            public long FilesystemSnapshotDirsScanned { get; init; }
            public long FilesystemSnapshotDirsInaccessible { get; init; }
            public long EtwEventsLost { get; init; }
            public long SessionEventsLost { get; init; }
            public long BufferSizeMb { get; init; }
            public long FsFormatDropped { get; init; }
            public long FsFormatQueueDepth { get; init; }
            public long FsFormatQueueHighWater { get; init; }
            public long FsSummaryEvents { get; init; }
            public long FsSummaryRows { get; init; }
            public long FsShedPeakLagMs { get; init; }
            // Consumer event-time lag at the moment of the snapshot (computed, not stored): in the
            // FINAL manifest this is the lag at session stop = the span of fs events the kernel
            // buffered but the consumer never drained (silent tail loss, invisible to
            // session_events_lost). See LoadShedder.ConsumerLagMsNow.
            public long FsConsumerLagMs { get; init; }
        }

        /// <summary>
        /// Reads every counter with <see cref="Interlocked.Read"/> so 64-bit reads
        /// stay atomic on 32-bit runtimes (pairing with the atomic increments).
        /// </summary>
        public static Counts Snapshot() => new Counts
        {
            FilesystemEvents = Interlocked.Read(ref FilesystemEvents),
            FilesystemEventsReceived = Interlocked.Read(ref FilesystemEventsReceived),
            FilesystemEventsFilteredByProcess = Interlocked.Read(ref FilesystemEventsFilteredByProcess),
            DiskEvents = Interlocked.Read(ref DiskEvents),
            MemoryEvents = Interlocked.Read(ref MemoryEvents),
            NetworkPackets = Interlocked.Read(ref NetworkPackets),
            NetworkRows = Interlocked.Read(ref NetworkRows),
            ProcessSnapshotRows = Interlocked.Read(ref ProcessSnapshotRows),
            FilesystemSnapshotRows = Interlocked.Read(ref FilesystemSnapshotRows),
            FilesystemSnapshotDirsScanned = Interlocked.Read(ref FilesystemSnapshotDirsScanned),
            FilesystemSnapshotDirsInaccessible = Interlocked.Read(ref FilesystemSnapshotDirsInaccessible),
            EtwEventsLost = Interlocked.Read(ref EtwEventsLost),
            SessionEventsLost = Interlocked.Read(ref SessionEventsLost),
            BufferSizeMb = Interlocked.Read(ref BufferSizeMb),
            FsFormatDropped = Interlocked.Read(ref FsFormatDropped),
            FsFormatQueueDepth = Interlocked.Read(ref FsFormatQueueDepth),
            FsFormatQueueHighWater = Interlocked.Read(ref FsFormatQueueHighWater),
            FsSummaryEvents = Interlocked.Read(ref FsSummaryEvents),
            FsSummaryRows = Interlocked.Read(ref FsSummaryRows),
            FsShedPeakLagMs = Interlocked.Read(ref FsShedPeakLagMs),
            FsConsumerLagMs = LoadShedder.ConsumerLagMsNow(),
        };

        public static void IncFilesystem() => Interlocked.Increment(ref FilesystemEvents);
        public static void IncFilesystemReceived() => Interlocked.Increment(ref FilesystemEventsReceived);
        public static void IncFilesystemFilteredByProcess() => Interlocked.Increment(ref FilesystemEventsFilteredByProcess);
        public static void IncDisk() => Interlocked.Increment(ref DiskEvents);
        public static void IncMemory() => Interlocked.Increment(ref MemoryEvents);
        public static void IncNetworkPacket() => Interlocked.Increment(ref NetworkPackets);
        public static void IncNetworkRow() => Interlocked.Increment(ref NetworkRows);
        public static void IncFilesystemSnapshot() => Interlocked.Increment(ref FilesystemSnapshotRows);
        public static void IncFilesystemSnapshotDirScanned() => Interlocked.Increment(ref FilesystemSnapshotDirsScanned);
        public static void IncFilesystemSnapshotDirInaccessible() => Interlocked.Increment(ref FilesystemSnapshotDirsInaccessible);
        public static void AddProcessSnapshotRows(long n) => Interlocked.Add(ref ProcessSnapshotRows, n);
        public static void AddEtwEventsLost(long n) => Interlocked.Add(ref EtwEventsLost, n);
        // Overwrite with an authoritative running total. Used to publish the live
        // ETW lost-event count mid-session (polled from source.EventsLost) so the
        // periodically-refreshed manifest reports drops even when the session is
        // killed before a clean shutdown.
        public static void SetEtwEventsLost(long n) => Interlocked.Exchange(ref EtwEventsLost, n);
        // Authoritative OS-queried session-level lost count (session.EventsLost). Set on the
        // same poll as SetEtwEventsLost so the manifest carries both the consumer-side and
        // the authoritative view, and their gap exposes the consumer-side false negative.
        public static void SetSessionEventsLost(long n) => Interlocked.Exchange(ref SessionEventsLost, n);
        // The kernel buffer pool size (MB) granted for the live session (see Tracer's step-down).
        public static void SetBufferSizeMb(long n) => Interlocked.Exchange(ref BufferSizeMb, n);

        public static void IncFsFormatDropped() => Interlocked.Increment(ref FsFormatDropped);
        // Publish the current fs-format queue depth and track its peak. Called on each
        // enqueue from the ETW consumer thread; the high-water update is a lock-free CAS
        // max so concurrent enqueues never lose a peak.
        public static void NoteFsFormatQueueDepth(long depth)
        {
            Interlocked.Exchange(ref FsFormatQueueDepth, depth);
            long hw;
            while (depth > (hw = Interlocked.Read(ref FsFormatQueueHighWater)))
            {
                if (Interlocked.CompareExchange(ref FsFormatQueueHighWater, depth, hw) == hw) break;
            }
        }

        public static void IncFsSummaryEvent() => Interlocked.Increment(ref FsSummaryEvents);
        public static void IncFsSummaryRow() => Interlocked.Increment(ref FsSummaryRows);
        // Peak consumer event-time lag (ms) seen by the load-shedder; lock-free CAS-max.
        public static void NoteFsShedPeakLagMs(long ms)
        {
            long hw;
            while (ms > (hw = Interlocked.Read(ref FsShedPeakLagMs)))
            {
                if (Interlocked.CompareExchange(ref FsShedPeakLagMs, ms, hw) == hw) break;
            }
        }
    }
}
