using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Globalization;
using System.IO;

namespace IOTracesCORE.trace
{
    /// <summary>
    /// Standardized memory and cache event types.
    /// Combines Linux page cache events and Windows-specific extended events.
    /// </summary>
    public enum MemoryEventType
    {
        // --- Basic Cache Events (0-9) ---
        HIT = 0,              // Page found in memory (soft fault)
        MISS = 1,             // Page not in memory (hard fault, requires I/O)
        DIRTY = 2,            // Page marked as modified
        WRITEBACK_START = 3,  // Modified page write initiated
        WRITEBACK_END = 4,    // Modified page write completed
        EVICT = 5,            // Page removed from working set
        INVALIDATE = 6,       // Page invalidated (access violation)
        DROP = 7,             // Page dropped from cache
        READAHEAD = 8,        // Sequential read optimization
        RECLAIM = 9,          // Memory pressure reclaim

        // --- File System Cache Events (10-19) ---
        CACHE_READ = 10,           // File data read from cache
        CACHE_WRITE = 11,          // File data written to cache
        CACHE_FLUSH_START = 12,    // Cache flush initiated
        CACHE_FLUSH_END = 13,      // Cache flush completed
        CACHE_LAZY_WRITE = 14,     // Lazy writer background flush
        CACHE_READ_AHEAD = 15,     // Predictive read-ahead
        CACHE_MISS_PARTIAL = 16,   // Partial cache hit
        CACHE_MAP = 17,            // File section mapped to cache
        CACHE_UNMAP = 18,          // File section unmapped
        CACHE_PURGE = 19,          // Cache purged for file

        // --- Working Set Events (20-29) ---
        WS_TRIM = 20,              // Working set trimmed
        WS_EXPANSION = 21,         // Working set expanded
        WS_FAULT_IN = 22,          // Page faulted into working set
        WS_FAULT_OUT = 23,         // Page faulted out of working set
        WS_AGING = 24,             // Working set page aged
        WS_LOCK = 25,              // Page locked in working set
        WS_UNLOCK = 26,            // Page unlocked from working set

        // --- Modified Page Writer Events (30-39) ---
        MPW_WRITE_START = 30,      // Modified page write initiated
        MPW_WRITE_END = 31,        // Modified page write completed
        MPW_THROTTLE = 32,         // Modified page writer throttled
        MPW_QUEUE = 33,            // Page queued to modified list
        MPW_DEQUEUE = 34,          // Page dequeued from modified list

        // --- Standby List Events (40-49) ---
        STANDBY_INSERT = 40,       // Page inserted to standby list
        STANDBY_REMOVE = 41,       // Page removed from standby list
        STANDBY_REPURPOSE = 42,    // Standby page repurposed

        // --- Memory Pressure Events (50-59) ---
        LOW_MEMORY = 50,           // Low memory condition
        HIGH_MEMORY = 51,          // High memory condition
        OUT_OF_MEMORY = 52,        // Out of memory
        PRIORITY_TRIM = 53,        // Priority-based trim

        // --- Prefetch/Superfetch Events (60-69) ---
        PREFETCH_START = 60,       // Prefetch operation started
        PREFETCH_END = 61,         // Prefetch operation completed
        SUPERFETCH_QUERY = 62,     // Superfetch query
        SUPERFETCH_DECISION = 63,  // Superfetch decision made

        // --- TLB Events (70-79) ---
        TLB_FLUSH = 70,            // TLB flushed
        TLB_MISS = 71,             // TLB miss

        // --- NUMA Events (80-89) ---
        NUMA_MIGRATION = 80,       // Page migrated across NUMA nodes
        NUMA_FAULT = 81,           // NUMA fault

        // --- Special Events ---
        GUARD = 100,
        ALLOC = 101
    }

    /// <summary>
    /// Represents a memory/cache event for tracking page faults, cache hits/misses,
    /// and memory pressure events.
    /// </summary>
    class MemoryTrace
    {
        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public string Type { get; set; }           // Human-readable event type
        public int EventType { get; set; }         // Numeric code for analysis
        public ulong VirtualAddress { get; set; }  // Virtual address of the page
        public long ByteCount { get; set; }        // Bytes involved
        public int ThreadId { get; set; }          // Thread that caused the fault

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public MemoryTrace(
            DateTime ts,
            int pid,
            string comm,
            MemoryEventType type,
            ulong virtualAddress = 0,
            long byteCount = 0,
            int threadId = 0)
        {
            Ts = ts;
            Pid = pid;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Type = type.ToString();
            EventType = (int)type;
            VirtualAddress = virtualAddress;
            ByteCount = byteCount;
            ThreadId = threadId;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n"
            };
            this.csv = new CsvWriter(buffer, config);
        }

        public string FormatAsCsv()
        {
            buffer.GetStringBuilder().Clear();

            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            csv.WriteField(Pid);
            csv.WriteField(ThreadId);
            csv.WriteField(Comm);
            csv.WriteField(Type);
            csv.WriteField($"0x{VirtualAddress:X}");
            csv.WriteField(ByteCount);

            csv.NextRecord();
            return buffer.ToString();
        }
    }
}
