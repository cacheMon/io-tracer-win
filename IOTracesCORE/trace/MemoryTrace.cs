using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.trace
{
    class MemoryTrace
    {
        public MemoryTrace(DateTime ts, int pid, string comm, string type)
        {
            Ts = ts;
            Pid = pid;
            Comm = $"\"{comm}\"";
            Type = type;
        }

        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public string Type { get; set; }
    }
}
