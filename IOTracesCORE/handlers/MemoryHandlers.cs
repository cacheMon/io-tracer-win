using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;

namespace IOTracesCORE.handlers
{
    /// <summary>
    /// Handles memory and cache-related ETW events, mapping them to standardized
    /// cache event types for workload analysis.
    /// </summary>
    class MemoryHandlers
    {
        private WriterManager wm;

        /// <summary>
        /// Standardized cache event types matching Linux page cache events
        /// for cross-platform analysis compatibility.
        /// </summary>
        public enum CacheEventType
        {
            HIT = 0,              // Page found in memory (soft fault)
            MISS = 1,             // Page not in memory (hard fault, requires I/O)
            DIRTY = 2,            // Page marked as modified
            WRITEBACK_START = 3,  // Modified page write initiated
            WRITEBACK_END = 4,    // Modified page write completed
            EVICT = 5,            // Page removed from working set
            INVALIDATE = 6,       // Page invalidated (access violation)
            DROP = 7,             // Page dropped from cache
            READAHEAD = 8,        // Sequential read optimization
            RECLAIM = 9           // Memory pressure reclaim
        }

        public MemoryHandlers(WriterManager old_wm)
        {
            wm = old_wm;
        }

        /// <summary>
        /// HIT: Soft page fault - page found in standby or modified list.
        /// No disk I/O required, just transitions page back to active.
        /// </summary>
        public void OnMemoryTransitionFault(MemoryPageFaultTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "HIT",
                eventType: (int)CacheEventType.HIT,
                virtualAddress: data.VirtualAddress,
                byteCount: 4096,  // Standard page size on Windows
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// HIT: Demand zero fault - new page allocated and zero-filled.
        /// Common for heap allocations and BSS segment.
        /// </summary>
        public void OnMemoryDemandZeroFault(MemoryPageFaultTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "HIT",
                eventType: (int)CacheEventType.HIT,
                virtualAddress: data.VirtualAddress,
                byteCount: 4096,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// MISS: Hard page fault - page not in memory, requires disk I/O.
        /// This is the most critical event for I/O workload characterization.
        /// </summary>
        public void OnMemoryHardFault(MemoryHardFaultTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "MISS",
                eventType: (int)CacheEventType.MISS,
                virtualAddress: 0,  // Not available in HardFaultTraceData
                byteCount: data.ByteCount,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// DIRTY: Copy-on-write fault - page is being modified.
        /// Indicates transition from read-only shared to private writable.
        /// </summary>
        public void OnMemoryCopyOnWrite(MemoryPageFaultTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "DIRTY",
                eventType: (int)CacheEventType.DIRTY,
                virtualAddress: data.VirtualAddress,
                byteCount: 4096,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// INVALIDATE: Access violation - attempt to access protected/unmapped memory.
        /// Can indicate bugs or security violations.
        /// </summary>
        public void OnMemoryAccessViolation(MemoryPageFaultTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "INVALIDATE",
                eventType: (int)CacheEventType.INVALIDATE,
                virtualAddress: data.VirtualAddress,
                byteCount: 0,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// Guard page fault - used for stack growth detection.
        /// </summary>
        public void OnMemoryGuardMemory(MemoryPageFaultTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "GUARD",
                eventType: -1,  // Special type, not in standard cache events
                virtualAddress: data.VirtualAddress,
                byteCount: 4096,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// Virtual memory allocation - can indicate working set expansion.
        /// </summary>
        public void OnVirtualMemAlloc(VirtualAllocTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "ALLOC",
                eventType: -1,
                virtualAddress: (ulong)data.BaseAddr,
                byteCount: (long)data.Length,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }

        /// <summary>
        /// EVICT: Virtual memory free - pages removed from working set.
        /// Indicates memory pressure or explicit deallocation.
        /// </summary>
        public void OnVirtualMemFree(VirtualAllocTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
                return;

            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "EVICT",
                eventType: (int)CacheEventType.EVICT,
                virtualAddress: (ulong)data.BaseAddr,
                byteCount: (long)data.Length,
                threadId: data.ThreadID
            );

            wm.Write(mt);
        }
    }
}
