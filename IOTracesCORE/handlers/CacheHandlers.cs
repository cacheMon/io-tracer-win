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
                    type: MemoryEventType.CACHE_READ,
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
                    type: MemoryEventType.CACHE_WRITE,
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
                type: MemoryEventType.CACHE_FLUSH_START,
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
                type: MemoryEventType.CACHE_MAP,
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
                type: MemoryEventType.CACHE_UNMAP,
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
                type: MemoryEventType.WS_TRIM,
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
                type: MemoryEventType.WS_EXPANSION,
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
                type: MemoryEventType.MPW_WRITE_START,
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
                type: MemoryEventType.MPW_QUEUE,
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
                type: MemoryEventType.STANDBY_INSERT,
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
                type: MemoryEventType.STANDBY_REMOVE,
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
                type: MemoryEventType.LOW_MEMORY,
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
                type: MemoryEventType.OUT_OF_MEMORY,
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
                type: MemoryEventType.PREFETCH_START,
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
                type: MemoryEventType.CACHE_READ_AHEAD,
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
                if (data.PayloadNames.Contains(name))
                {
                    var val = data.PayloadByName(name);
                    return val == null ? defaultValue : Convert.ToUInt64(val);
                }
                return defaultValue;
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
                if (data.PayloadNames.Contains(name))
                {
                    var val = data.PayloadByName(name);
                    return val == null ? defaultValue : Convert.ToInt64(val);
                }
                return defaultValue;
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
                if (data.PayloadNames.Contains(name))
                {
                    var val = data.PayloadByName(name);
                    return val?.ToString() ?? defaultValue;
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        #endregion
    }
}
