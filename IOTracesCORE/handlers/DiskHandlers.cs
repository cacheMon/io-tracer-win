using IOTracesCORE;
using IOTracesCORE.trace;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.handlers
{
    class DiskHandlers
    {
        private WriterManager wm;

        public DiskHandlers(WriterManager old_wm)
        {
            wm = old_wm;
        }

        public void OnDiskRead(DiskIOTraceData data)
        {
            DiskTrace dt = new DiskTrace(
                    ts: data.TimeStamp,
                    pid: data.ProcessID,
                    comm: data.ProcessName,
                    sector: data.ByteOffset / 512,
                    operation: "read",
                    traceSize: data.TransferSize
                );

            wm.Write(dt);
        }

        public void OnDiskWrite(DiskIOTraceData data)
        {
            DiskTrace dt = new DiskTrace(
                    ts: data.TimeStamp,
                    pid: data.ProcessID,
                    comm: data.ProcessName,
                    sector: data.ByteOffset / 512,
                    operation: "write",
                    traceSize: data.TransferSize
                );

            wm.Write(dt);
        }

    }
}
