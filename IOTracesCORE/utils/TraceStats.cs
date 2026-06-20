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
        // ETW events the kernel dropped because consumer buffers could not keep up.
        public static long EtwEventsLost;
        // Filesystem events the off-thread formatter could not keep up with: the bounded
        // _fsFormatQueue rejected them (TryAdd failed). Distinct from EtwEventsLost (which
        // is kernel->consumer loss): this is loss DOWNSTREAM of the consumer, invisible to
        // the kernel counter. FsFormatQueueDepth is the last observed queue depth and
        // FsFormatQueueHighWater its peak — together they show whether formatting kept up.
        public static long FsFormatDropped;
        public static long FsFormatQueueDepth;
        public static long FsFormatQueueHighWater;

        public static void Reset()
        {
            Interlocked.Exchange(ref FilesystemEvents, 0);
            Interlocked.Exchange(ref DiskEvents, 0);
            Interlocked.Exchange(ref MemoryEvents, 0);
            Interlocked.Exchange(ref NetworkPackets, 0);
            Interlocked.Exchange(ref NetworkRows, 0);
            Interlocked.Exchange(ref ProcessSnapshotRows, 0);
            Interlocked.Exchange(ref FilesystemSnapshotRows, 0);
            Interlocked.Exchange(ref FilesystemSnapshotDirsScanned, 0);
            Interlocked.Exchange(ref FilesystemSnapshotDirsInaccessible, 0);
            Interlocked.Exchange(ref EtwEventsLost, 0);
            Interlocked.Exchange(ref FsFormatDropped, 0);
            Interlocked.Exchange(ref FsFormatQueueDepth, 0);
            Interlocked.Exchange(ref FsFormatQueueHighWater, 0);
        }

        /// <summary>Immutable snapshot of all counters, each read atomically.</summary>
        public readonly struct Counts
        {
            public long FilesystemEvents { get; init; }
            public long DiskEvents { get; init; }
            public long MemoryEvents { get; init; }
            public long NetworkPackets { get; init; }
            public long NetworkRows { get; init; }
            public long ProcessSnapshotRows { get; init; }
            public long FilesystemSnapshotRows { get; init; }
            public long FilesystemSnapshotDirsScanned { get; init; }
            public long FilesystemSnapshotDirsInaccessible { get; init; }
            public long EtwEventsLost { get; init; }
            public long FsFormatDropped { get; init; }
            public long FsFormatQueueDepth { get; init; }
            public long FsFormatQueueHighWater { get; init; }
        }

        /// <summary>
        /// Reads every counter with <see cref="Interlocked.Read"/> so 64-bit reads
        /// stay atomic on 32-bit runtimes (pairing with the atomic increments).
        /// </summary>
        public static Counts Snapshot() => new Counts
        {
            FilesystemEvents = Interlocked.Read(ref FilesystemEvents),
            DiskEvents = Interlocked.Read(ref DiskEvents),
            MemoryEvents = Interlocked.Read(ref MemoryEvents),
            NetworkPackets = Interlocked.Read(ref NetworkPackets),
            NetworkRows = Interlocked.Read(ref NetworkRows),
            ProcessSnapshotRows = Interlocked.Read(ref ProcessSnapshotRows),
            FilesystemSnapshotRows = Interlocked.Read(ref FilesystemSnapshotRows),
            FilesystemSnapshotDirsScanned = Interlocked.Read(ref FilesystemSnapshotDirsScanned),
            FilesystemSnapshotDirsInaccessible = Interlocked.Read(ref FilesystemSnapshotDirsInaccessible),
            EtwEventsLost = Interlocked.Read(ref EtwEventsLost),
            FsFormatDropped = Interlocked.Read(ref FsFormatDropped),
            FsFormatQueueDepth = Interlocked.Read(ref FsFormatQueueDepth),
            FsFormatQueueHighWater = Interlocked.Read(ref FsFormatQueueHighWater),
        };

        public static void IncFilesystem() => Interlocked.Increment(ref FilesystemEvents);
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
    }
}
