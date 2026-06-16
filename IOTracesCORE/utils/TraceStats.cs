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
        // ETW events the kernel dropped because consumer buffers could not keep up.
        public static long EtwEventsLost;

        public static void Reset()
        {
            Interlocked.Exchange(ref FilesystemEvents, 0);
            Interlocked.Exchange(ref DiskEvents, 0);
            Interlocked.Exchange(ref MemoryEvents, 0);
            Interlocked.Exchange(ref NetworkPackets, 0);
            Interlocked.Exchange(ref NetworkRows, 0);
            Interlocked.Exchange(ref ProcessSnapshotRows, 0);
            Interlocked.Exchange(ref FilesystemSnapshotRows, 0);
            Interlocked.Exchange(ref EtwEventsLost, 0);
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
            public long EtwEventsLost { get; init; }
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
            EtwEventsLost = Interlocked.Read(ref EtwEventsLost),
        };

        public static void IncFilesystem() => Interlocked.Increment(ref FilesystemEvents);
        public static void IncDisk() => Interlocked.Increment(ref DiskEvents);
        public static void IncMemory() => Interlocked.Increment(ref MemoryEvents);
        public static void IncNetworkPacket() => Interlocked.Increment(ref NetworkPackets);
        public static void IncNetworkRow() => Interlocked.Increment(ref NetworkRows);
        public static void IncFilesystemSnapshot() => Interlocked.Increment(ref FilesystemSnapshotRows);
        public static void AddProcessSnapshotRows(long n) => Interlocked.Add(ref ProcessSnapshotRows, n);
        public static void AddEtwEventsLost(long n) => Interlocked.Add(ref EtwEventsLost, n);
    }
}
