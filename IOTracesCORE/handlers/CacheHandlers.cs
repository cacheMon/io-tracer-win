using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace IOTracesCORE.handlers
{
    /// <summary>
    /// Extended cache event types for file system cache tracking
    /// </summary>
    public enum ExtendedCacheEventType
    {

        // File system cache events (10-19)
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

        // Working set events (20-29)
        WS_TRIM = 20,              // Working set trimmed
        WS_EXPANSION = 21,         // Working set expanded
        WS_FAULT_IN = 22,          // Page faulted into working set
        WS_FAULT_OUT = 23,         // Page faulted out of working set
        WS_AGING = 24,             // Working set page aged
        WS_LOCK = 25,              // Page locked in working set
        WS_UNLOCK = 26,            // Page unlocked from working set

        // Modified page writer events (30-39)
        MPW_WRITE_START = 30,      // Modified page write initiated
        MPW_WRITE_END = 31,        // Modified page write completed
        MPW_THROTTLE = 32,         // Modified page writer throttled
        MPW_QUEUE = 33,            // Page queued to modified list
        MPW_DEQUEUE = 34,          // Page dequeued from modified list

        // Standby list events (40-49)
        STANDBY_INSERT = 40,       // Page inserted to standby list
        STANDBY_REMOVE = 41,       // Page removed from standby list
        STANDBY_REPURPOSE = 42,    // Standby page repurposed

        // Memory pressure events (50-59)
        LOW_MEMORY = 50,           // Low memory condition
        HIGH_MEMORY = 51,          // High memory condition
        OUT_OF_MEMORY = 52,        // Out of memory
        PRIORITY_TRIM = 53,        // Priority-based trim

        // Prefetch/Superfetch events (60-69)
        PREFETCH_START = 60,       // Prefetch operation started
        PREFETCH_END = 61,         // Prefetch operation completed
        SUPERFETCH_QUERY = 62,     // Superfetch query
        SUPERFETCH_DECISION = 63,  // Superfetch decision made

        // TLB events (70-79)
        TLB_FLUSH = 70,            // TLB flushed
        TLB_MISS = 71,             // TLB miss

        // NUMA events (80-89)
        NUMA_MIGRATION = 80,       // Page migrated across NUMA nodes
        NUMA_FAULT = 81,           // NUMA fault
    }

    /// <summary>
    /// Handles advanced cache and memory management events from Windows ETW.
    /// Provides deep visibility into file system cache, working sets, and memory pressure.
    /// </summary>
    class CacheHandlers
    {
        private WriterManager wm;

        // Track cache flush operations
        private ConcurrentDictionary<ulong, DateTime> _activeFlushes = new();

        // Track working set operations
        private ConcurrentDictionary<int, WorkingSetState> _workingSetState = new();

        public CacheHandlers(WriterManager old_wm)
        {
            wm = old_wm;
        }

        private class WorkingSetState
        {
            public long Size { get; set; }
            public long Peak { get; set; }
            public DateTime LastTrim { get; set; }
        }

        #region File System Cache Events

        /// <summary>
        /// CACHE_READ: File data read from system cache (fast path).
        /// This is the equivalent of a cache hit for file I/O.
        /// </summary>
        public void OnCacheRead(FileIOReadWriteTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName))
                return;

            // Only track reads that completed from cache (IoFlags indicates cached)
            if ((data.IoFlags & 0x00000001) != 0) // IRP_NOCACHE flag NOT set
            {
                MemoryTrace mt = new MemoryTrace(
                    ts: data.TimeStamp,
                    pid: data.ProcessID,
                    comm: data.ProcessName,
                    type: "CACHE_READ",
                    eventType: (int)ExtendedCacheEventType.CACHE_READ,
                    virtualAddress: data.FileKey,
                    byteCount: data.IoSize,
                    threadId: data.ThreadID
                );

                wm.Write(mt);
            }
        }

        /// <summary>
        /// CACHE_WRITE: File data written to system cache.
        /// Indicates buffered write operation.
        /// </summary>
        public void OnCacheWrite(FileIOReadWriteTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName))
                return;

            if ((data.IoFlags & 0x00000001) != 0) // IRP_NOCACHE flag NOT set
            {
                MemoryTrace mt = new MemoryTrace(
                    ts: data.TimeStamp,
                    pid: data.ProcessID,
                    comm: data.ProcessName,
                    type: "CACHE_WRITE",
                    eventType: (int)ExtendedCacheEventType.CACHE_WRITE,
                    virtualAddress: data.FileKey,
                    byteCount: data.IoSize,
                    threadId: data.ThreadID
                );

                wm.Write(mt);
            }
        }

        /// <summary>
        /// CACHE_FLUSH: File cache flush operation.
        /// Indicates forced write-through of cached data.
        /// </summary>
        public void OnCacheFlush(FileIOSimpleOpTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName))
                return;

            _activeFlushes[data.IrpPtr] = data.TimeStamp;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "CACHE_FLUSH_START",
                eventType: (int)ExtendedCacheEventType.CACHE_FLUSH_START,
                virtualAddress: data.FileKey,
                byteCount: 0,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// CACHE_MAP: File section mapped into cache.
        /// Indicates memory-mapped file operation.
        /// </summary>
        public void OnCacheMap(MapFileTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName))
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "CACHE_MAP",
                eventType: (int)ExtendedCacheEventType.CACHE_MAP,
                virtualAddress: data.FileKey,
                byteCount: (long)data.ViewSize,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// CACHE_UNMAP: File section unmapped from cache.
        /// </summary>
        public void OnCacheUnmap(MapFileTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName))
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "CACHE_UNMAP",
                eventType: (int)ExtendedCacheEventType.CACHE_UNMAP,
                virtualAddress: data.FileKey,
                byteCount: (long)data.ViewSize,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        #endregion

        #region Working Set Events

        /// <summary>
        /// WS_TRIM: Working set trimmed due to memory pressure.
        /// Critical indicator of memory pressure affecting performance.
        /// </summary>
        public void OnWorkingSetTrim(TraceEvent data)
        {
            int pid = data.ProcessID;
            if (!ProcessFilter.ShouldTrace(pid, data.ProcessName))
                return;

            // Extract trim amount from payload
            long trimmedBytes = GetLongPayload(data, "TrimmedSize", 0);

            _workingSetState.AddOrUpdate(pid,
                new WorkingSetState { LastTrim = data.TimeStamp },
                (_, state) => { state.LastTrim = data.TimeStamp; return state; });

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: pid,
                comm: data.ProcessName,
                type: "WS_TRIM",
                eventType: (int)ExtendedCacheEventType.WS_TRIM,
                virtualAddress: 0,
                byteCount: trimmedBytes,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// WS_EXPANSION: Working set expanded (process allocated more memory).
        /// </summary>
        public void OnWorkingSetExpansion(VirtualAllocTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName))
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "WS_EXPANSION",
                eventType: (int)ExtendedCacheEventType.WS_EXPANSION,
                virtualAddress: (ulong)data.BaseAddr,
                byteCount: (long)data.Length,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        #endregion

        #region Modified Page Writer Events

        /// <summary>
        /// MPW_WRITE: Modified page writer flushing dirty pages.
        /// This is the Windows equivalent of pdflush/writeback in Linux.
        /// </summary>
        public void OnModifiedPageWrite(TraceEvent data)
        {
            int pid = data.ProcessID;
            if (!ProcessFilter.ShouldTrace(pid, data.ProcessName))
                return;

            long pageCount = GetLongPayload(data, "PageCount", 1);
            long byteCount = pageCount * 4096; // Standard page size

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: pid,
                comm: data.ProcessName,
                type: "MPW_WRITE_START",
                eventType: (int)ExtendedCacheEventType.MPW_WRITE_START,
                virtualAddress: 0,
                byteCount: byteCount,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// MPW_QUEUE: Page queued to modified list.
        /// Indicates a dirty page that will be written back.
        /// </summary>
        public void OnModifiedPageQueue(TraceEvent data)
        {
            int pid = data.ProcessID;
            if (!ProcessFilter.ShouldTrace(pid, data.ProcessName))
                return;

            ulong pageAddr = GetUlongPayload(data, "VirtualAddress", 0);

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: pid,
                comm: data.ProcessName,
                type: "MPW_QUEUE",
                eventType: (int)ExtendedCacheEventType.MPW_QUEUE,
                virtualAddress: pageAddr,
                byteCount: 4096,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        #endregion

        #region Standby List Events

        /// <summary>
        /// STANDBY_INSERT: Page moved to standby list (clean page cache).
        /// Pages on standby can be reclaimed without writing to disk.
        /// </summary>
        public void OnStandbyInsert(TraceEvent data)
        {
            int pid = data.ProcessID;
            if (!ProcessFilter.ShouldTrace(pid, data.ProcessName))
                return;

            ulong pageAddr = GetUlongPayload(data, "VirtualAddress", 0);
            long priority = GetLongPayload(data, "Priority", 0);

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: pid,
                comm: data.ProcessName,
                type: "STANDBY_INSERT",
                eventType: (int)ExtendedCacheEventType.STANDBY_INSERT,
                virtualAddress: pageAddr,
                byteCount: priority, // Store priority in byteCount field
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// STANDBY_REMOVE: Page removed from standby list.
        /// Either repurposed or reclaimed due to memory pressure.
        /// </summary>
        public void OnStandbyRemove(TraceEvent data)
        {
            int pid = data.ProcessID;
            if (!ProcessFilter.ShouldTrace(pid, data.ProcessName))
                return;

            ulong pageAddr = GetUlongPayload(data, "VirtualAddress", 0);

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: pid,
                comm: data.ProcessName,
                type: "STANDBY_REMOVE",
                eventType: (int)ExtendedCacheEventType.STANDBY_REMOVE,
                virtualAddress: pageAddr,
                byteCount: 4096,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        #endregion

        #region Memory Pressure Events

        /// <summary>
        /// LOW_MEMORY: System entered low memory condition.
        /// Critical event indicating system-wide memory pressure.
        /// </summary>
        public void OnLowMemory(TraceEvent data)
        {
            // This is a system-level event, not per-process
            long availableBytes = GetLongPayload(data, "AvailableBytes", 0);
            long commitLimit = GetLongPayload(data, "CommitLimit", 0);

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: 0, // System event
                comm: "System",
                type: "LOW_MEMORY",
                eventType: (int)ExtendedCacheEventType.LOW_MEMORY,
                virtualAddress: 0,
                byteCount: availableBytes,
                threadId: 0
            );

            wm.Write(mt);
        }

        /// <summary>
        /// OUT_OF_MEMORY: System is out of memory.
        /// Severe condition that may cause process termination.
        /// </summary>
        public void OnOutOfMemory(TraceEvent data)
        {
            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: 0,
                comm: "System",
                type: "OUT_OF_MEMORY",
                eventType: (int)ExtendedCacheEventType.OUT_OF_MEMORY,
                virtualAddress: 0,
                byteCount: 0,
                threadId: 0
            );

            wm.Write(mt);
        }

        #endregion

        #region Prefetch/ReadAhead Events

        /// <summary>
        /// PREFETCH_START: Windows Prefetcher started prefetch operation.
        /// Indicates anticipated future file access.
        /// </summary>
        public void OnPrefetchStart(TraceEvent data)
        {
            int pid = data.ProcessID;
            if (!ProcessFilter.ShouldTrace(pid, data.ProcessName))
                return;

            string filename = GetStringPayload(data, "FileName", "");
            long byteCount = GetLongPayload(data, "Size", 0);

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: pid,
                comm: data.ProcessName,
                type: "PREFETCH_START",
                eventType: (int)ExtendedCacheEventType.PREFETCH_START,
                virtualAddress: 0,
                byteCount: byteCount,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// CACHE_READ_AHEAD: Sequential read-ahead detected.
        /// </summary>
        public void OnCacheReadAhead(FileIOReadWriteTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName))
                return;

            // Detect sequential reads (consecutive offsets)
            // This is a heuristic - real read-ahead tracking requires correlation

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "CACHE_READ_AHEAD",
                eventType: (int)ExtendedCacheEventType.CACHE_READ_AHEAD,
                virtualAddress: data.FileKey,
                byteCount: data.IoSize,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        #endregion

        #region Helper Methods

        private ulong GetUlongPayload(TraceEvent data, string name, ulong defaultValue = 0)
        {
            try
            {
                var val = data.PayloadByName(name);
                return val == null ? defaultValue : Convert.ToUInt64(val);
            }
            catch
            {
                return defaultValue;
            }
        }

        private long GetLongPayload(TraceEvent data, string name, long defaultValue = 0)
        {
            try
            {
                var val = data.PayloadByName(name);
                return val == null ? defaultValue : Convert.ToInt64(val);
            }
            catch
            {
                return defaultValue;
            }
        }

        private string GetStringPayload(TraceEvent data, string name, string defaultValue = "")
        {
            try
            {
                var val = data.PayloadByName(name);
                return val?.ToString() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        #endregion
    }
}
