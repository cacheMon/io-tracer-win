using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.utils
{
    public static class IrpFlagsHelper
    {
        [Flags]
        public enum IrpFlags
        {
            None = 0,
            Nocache = 1,
            PagingIo = 2,
            MountCompletion = 2,
            SynchronousApi = 4,
            AssociatedIrp = 8,
            BufferedIO = 16,
            DeallocateBuffer = 32,
            InputOperation = 64,
            SynchronousPagingIO = 64,
            Create = 128,
            Read = 256,
            Write = 512,
            Close = 1024,
            DeferIOCompletion = 2048,
            ObQueryName = 4096,
            HoldDeviceQueue = 8192,
            PriorityMask = 917504
        }

        public static string ToString(ulong flagsVal)
        {
            if (flagsVal == 0) return "";

            // Cast ulong to int for the enum, assuming flags fit in int range.
            var flags = (IrpFlags)(int)flagsVal;
            var result = new List<string>();
            ulong matched = 0;

            if ((flags & IrpFlags.Nocache) != 0) { result.Add("Nocache"); matched |= (ulong)IrpFlags.Nocache; }

            // 0x2 can be PagingIo or MountCompletion
            // For disk traces, PagingIo is the standard meaning.
            if ((flags & IrpFlags.PagingIo) != 0) { result.Add("PagingIo"); matched |= (ulong)IrpFlags.PagingIo; }

            if ((flags & IrpFlags.SynchronousApi) != 0) { result.Add("SynchronousApi"); matched |= (ulong)IrpFlags.SynchronousApi; }
            if ((flags & IrpFlags.AssociatedIrp) != 0) { result.Add("AssociatedIrp"); matched |= (ulong)IrpFlags.AssociatedIrp; }
            if ((flags & IrpFlags.BufferedIO) != 0) { result.Add("BufferedIO"); matched |= (ulong)IrpFlags.BufferedIO; }
            if ((flags & IrpFlags.DeallocateBuffer) != 0) { result.Add("DeallocateBuffer"); matched |= (ulong)IrpFlags.DeallocateBuffer; }

            // 0x40 can be InputOperation or SynchronousPagingIO
            // SynchronousPagingIO is very common in file system filters and disk I/O.
            if ((flags & IrpFlags.SynchronousPagingIO) != 0) { result.Add("SynchronousPagingIO"); matched |= (ulong)IrpFlags.SynchronousPagingIO; }

            if ((flags & IrpFlags.Create) != 0) { result.Add("Create"); matched |= (ulong)IrpFlags.Create; }
            if ((flags & IrpFlags.Read) != 0) { result.Add("Read"); matched |= (ulong)IrpFlags.Read; }
            if ((flags & IrpFlags.Write) != 0) { result.Add("Write"); matched |= (ulong)IrpFlags.Write; }
            if ((flags & IrpFlags.Close) != 0) { result.Add("Close"); matched |= (ulong)IrpFlags.Close; }
            if ((flags & IrpFlags.DeferIOCompletion) != 0) { result.Add("DeferIOCompletion"); matched |= (ulong)IrpFlags.DeferIOCompletion; }
            if ((flags & IrpFlags.ObQueryName) != 0) { result.Add("ObQueryName"); matched |= (ulong)IrpFlags.ObQueryName; }
            if ((flags & IrpFlags.HoldDeviceQueue) != 0) { result.Add("HoldDeviceQueue"); matched |= (ulong)IrpFlags.HoldDeviceQueue; }


            if ((flags & IrpFlags.PriorityMask) != 0)
            {
                // Extract priority bits (17-19)
                int priority = ((int)flags & (int)IrpFlags.PriorityMask) >> 17;
                switch (priority)
                {
                    case 1: result.Add("Priority:Low"); break;
                    case 2: result.Add("Priority:Normal"); break;
                    case 3: result.Add("Priority:High"); break;
                    case 4: result.Add("Priority:Critical"); break;
                    default: result.Add($"Priority:{priority}"); break;
                }
                matched |= (ulong)IrpFlags.PriorityMask;
            }

            // Preserve any bits we did not map so trace data is never silently lost.
            ulong remaining = flagsVal & ~matched;
            if (remaining != 0)
                result.Add($"0x{remaining:X}");

            return string.Join("|", result);
        }
    }
}
