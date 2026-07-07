using IOTracesCORE;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.handlers
{
    class DiskHandlers
    {
        private WriterManager wm;
        // Instance (not static) so a session restart starts with a clean map; a
        // stale Init timestamp from a previous session must not be matched against
        // a reused IRP pointer (TimeStampRelativeMSec resets per session).
        private readonly Dictionary<ulong, double> _activeRequests = new Dictionary<ulong, double>();
        // A DiskIOInit whose matching read/write completion is dropped or never arrives is
        // never removed from _activeRequests, so over a long capture the map grows without
        // bound (a slow leak). When it crosses this cap, prune entries older than the age
        // window — any real in-flight request completes in well under a second, so an Init
        // still around after this long has lost its completion and will never be matched.
        private const int ActiveRequestsCap = 100_000;
        private const double ActiveRequestMaxAgeMs = 60_000.0;

        public DiskHandlers(WriterManager old_wm)
        {
            wm = old_wm;
        }

        /// <summary>Drops in-flight IRP start-times. Call when a new ETW session starts.</summary>
        public void Reset()
        {
            lock (_activeRequests) _activeRequests.Clear();
        }

        public void OnDiskInit(DiskIOInitTraceData data)
        {
            ulong irp = (ulong)data.Irp;

            lock (_activeRequests)
            {
                if (!_activeRequests.ContainsKey(irp))
                {
                    _activeRequests[irp] = data.TimeStampRelativeMSec;
                }

                // Bound the map: if it has grown past the cap, drop start-times old enough
                // that their completion was clearly lost. O(n) but only on the rare overflow.
                if (_activeRequests.Count > ActiveRequestsCap)
                {
                    double cutoff = data.TimeStampRelativeMSec - ActiveRequestMaxAgeMs;
                    var stale = _activeRequests.Where(kv => kv.Value < cutoff)
                                               .Select(kv => kv.Key).ToList();
                    foreach (var k in stale) _activeRequests.Remove(k);
                }
            }
        }


        public void OnDiskRead(DiskIOTraceData data) => EmitDiskIO(data, "read");

        public void OnDiskWrite(DiskIOTraceData data) => EmitDiskIO(data, "write");

        // Emits a disk read/write row. The matching DiskIOInit gives the request
        // latency; if it was missed/dropped we still emit the row (with an unknown
        // latency) rather than silently discarding the operation — otherwise every
        // read/write whose Init we didn't catch would vanish from the block stream.
        private void EmitDiskIO(DiskIOTraceData data, string operation)
        {
            ulong irp = (ulong)data.Irp;
            double startTime;
            bool found;

            lock (_activeRequests)
            {
                found = _activeRequests.TryGetValue(irp, out startTime);
                if (found) _activeRequests.Remove(irp); // Clean up
            }

            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }

            // -1 = unknown (no matching Init). When found, clamp tiny negative values
            // from event reordering / same-tick resolution to 0 so a real measurement
            // is never mistaken for the "unknown" sentinel.
            double latency = found ? Math.Max(0, data.TimeStampRelativeMSec - startTime) : -1;

            DiskTrace dt = new DiskTrace(
                    ts: data.TimeStamp,
                    pid: data.ProcessID,
                    threadId: data.ThreadID,
                    comm: data.ProcessName,
                    sector: data.ByteOffset / 512,
                    operation: operation,
                    traceSize: data.TransferSize,
                    latency: latency,
                    diskNumber: data.DiskNumber,
                    irp: irp,
                    // Carry the IRP flags on read/write rows (the flush path already
                    // does). These decode to the request's origin/class — most
                    // importantly PagingIo (memory-mapped / section / pagefile page
                    // faults) and Nocache, which distinguish demand-paged and
                    // non-cached I/O from ordinary buffered reads. This is the only
                    // request-origin signal Windows exposes at the block layer, and
                    // the trace-format docs already document the flags column as
                    // populated for read/write.
                    irpFlags: (ulong)data.IrpFlags
                );

            wm.Write(dt);
        }

        public void OnDiskFlush(DiskIOFlushBuffersTraceData data)
        {
            // Flush doesn't always have a start time tracked by DiskIOInit in the same way, 
            // or we might miss Init if we didn't subscribe to FlushInit specifically (which we will).
            // For now, we report latency if we find it, else 0.
            ulong irp = (ulong)data.Irp;
            double startTime = 0;
            bool found = false;

            lock (_activeRequests)
            {
                found = _activeRequests.TryGetValue(irp, out startTime);
                if (found) _activeRequests.Remove(irp);
            }

            double latency = found ? Math.Max(0, data.TimeStampRelativeMSec - startTime) : -1; // -1 = unknown

            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }

            DiskTrace dt = new DiskTrace(
                    ts: data.TimeStamp,
                    pid: data.ProcessID,
                    threadId: data.ThreadID,
                    comm: data.ProcessName,
                    sector: 0, // Flush doesn't essentially have a sector/offset?
                    operation: "flush",
                    traceSize: 0,
                    latency: latency,
                    diskNumber: data.DiskNumber,
                    irp: irp,
                    irpFlags: (ulong)data.IrpFlags
                );

            wm.Write(dt);
        }



    }
}
