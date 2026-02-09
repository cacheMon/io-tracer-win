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
        private static Dictionary<ulong, double> _activeRequests = new Dictionary<ulong, double>();

        public DiskHandlers(WriterManager old_wm)
        {
            wm = old_wm;
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
            }
        }


        public void OnDiskRead(DiskIOTraceData data)
        {
            ulong irp = (ulong)data.Irp;
            double startTime;
            bool found;

            lock (_activeRequests)
            {
                found = _activeRequests.TryGetValue(irp, out startTime);
                if (found) _activeRequests.Remove(irp); // Clean up
            }

            if (found)
            {
                double latency = data.TimeStampRelativeMSec - startTime;
                if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
                {
                    return;
                }

                DiskTrace dt = new DiskTrace(
                        ts: data.TimeStamp,
                        pid: data.ProcessID,
                        threadId: data.ThreadID,
                        comm: data.ProcessName,
                        sector: data.ByteOffset / 512,
                        operation: "read",
                        traceSize: data.TransferSize,
                        latency: latency,
                        diskNumber: data.DiskNumber,
                        irp: irp

                    );

                wm.Write(dt);
            }

        }

        public void OnDiskWrite(DiskIOTraceData data)
        {
            ulong irp = (ulong)data.Irp;
            double startTime;
            bool found;

            lock (_activeRequests)
            {
                found = _activeRequests.TryGetValue(irp, out startTime);
                if (found) _activeRequests.Remove(irp); // Clean up
            }

            if (found)
            {
                double latency = data.TimeStampRelativeMSec - startTime;
                if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
                {
                    return;
                }

                DiskTrace dt = new DiskTrace(
                        ts: data.TimeStamp,
                        pid: data.ProcessID,
                        threadId: data.ThreadID,
                        comm: data.ProcessName,
                        sector: data.ByteOffset / 512,
                        operation: "write",
                        traceSize: data.TransferSize,
                        latency: latency,
                        diskNumber: data.DiskNumber,
                        irp: irp

                    );

                wm.Write(dt);
            }
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

            double latency = found ? data.TimeStampRelativeMSec - startTime : 0;

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
