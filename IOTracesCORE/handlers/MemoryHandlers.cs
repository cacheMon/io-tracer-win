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
    class MemoryHandlers
    {
        private WriterManager wm;

        public MemoryHandlers(WriterManager old_wm)
        {
            wm = old_wm;
        }

        public void OnMemoryTransitionFault(MemoryPageFaultTraceData data)
        {
            MemoryTrace mt = new MemoryTrace(
                    ts: data.TimeStamp,
                    pid: data.ProcessID,
                    comm: data.ProcessName,
                    type: "MemoryTransitionFault"
                );

            wm.Write(mt);
        }

        public void OnMemoryDemandZeroFault(MemoryPageFaultTraceData data)
        {
            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "MemoryDemandZeroFault"
            );

            wm.Write(mt);
        }

        public void OnMemoryCopyOnWrite(MemoryPageFaultTraceData data)
        {
            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "MemoryCopyOnWrite"
            );

            wm.Write(mt);
        }

        public void OnMemoryGuardMemory(MemoryPageFaultTraceData data)
        {
            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "MemoryGuardMemory"
            );

            wm.Write(mt);
        }

        public void OnMemoryAccessViolation(MemoryPageFaultTraceData data)
        {
            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "MemoryAccessViolation"
            );

            wm.Write(mt);
        }

        public void OnMemoryHardFault(MemoryHardFaultTraceData data)
        {
            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "MemoryHardFault"
            );

            wm.Write(mt);
        }

        public void OnVirtualMemAlloc(VirtualAllocTraceData data)
        {
            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "VirtualMemAlloc"
            );

            wm.Write(mt);
        }

        public void OnVirtualMemFree(VirtualAllocTraceData data)
        {
            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                type: "VirtualMemFree"
            );

            wm.Write(mt);
        }
    }
}
