using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.trace
{
    internal class ProcessInfo
    {
        public uint ProcessId { get; set; }
        public string Name { get; set; }
        public string CommandLine { get; set; }
        public ulong VirtualSize { get; set; }       // bytes
        public ulong WorkingSetSize { get; set; }    // bytes
        public DateTime? CreationDate { get; set; }

        public double CpuUsage { get; set; } // percentage

        public ProcessInfo(
            uint processId,
            string name,
            string commandLine,
            ulong virtualSize,
            ulong workingSetSize,
            DateTime? creationDate,
            double cpuUsage
        )
            {
                ProcessId = processId;
                Name = name;
                CommandLine = commandLine;
                VirtualSize = virtualSize;
                WorkingSetSize = workingSetSize;
                CreationDate = creationDate;
                CpuUsage = cpuUsage;
        }

        public override string ToString()
        {
            return $"{ProcessId}: {Name} (WS: {WorkingSetSize / 1024 / 1024} MB)";
        }

    }
}
