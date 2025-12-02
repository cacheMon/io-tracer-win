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
                virtualAddress: data.VirtualAddress,
                type: "transition_fault"
            );
            wm.Write(mt);
        }

        public void OnMemoryDemandZeroFault(MemoryPageFaultTraceData data)
        {
        }

        public void OnMemoryCopyOnWrite(MemoryPageFaultTraceData data)
        {
        }

        public void OnMemoryGuardMemory(MemoryPageFaultTraceData data)
        {
        }

        public void OnMemoryAccessViolation(MemoryPageFaultTraceData data)
        {
        }

        public void OnMemoryHardFault(MemoryHardFaultTraceData data)
        {
            MemoryTrace mt = new MemoryTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                comm: data.ProcessName,
                virtualAddress: data.VirtualAddress,
                type: "hard_fault"
            );
            wm.Write(mt);
        }

        public void OnVirtualMemAlloc(VirtualAllocTraceData data)
        {
        }

        public void OnVirtualMemFree(VirtualAllocTraceData data)
        {
        }
    }
}
