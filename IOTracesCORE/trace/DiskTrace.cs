using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.trace
{
    class DiskTrace
    {
        public DiskTrace(DateTime ts, int pid, string comm, long sector, string operation, int traceSize)
        {
            Ts = ts;
            Pid = pid;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Sector = sector;
            Operation = operation;
            TraceSize = traceSize;
        }

        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public long Sector { get; set; }
        public string Operation { get; set; }
        public int TraceSize { get; set; }

    }
}
