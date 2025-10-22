using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.trace
{
    internal class NetworkTrace
    {
        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public string Saddr { get; set; }
        public string Daddr { get; set; }
        public int Sport { get; set; }
        public int Dport { get; set; }
        public int Bytes { get; set; }
        public string Type { get; set; }

        public NetworkTrace(DateTime ts, int pid, string comm, string saddr, string daddr, int sport, int dport, int bytes, string type)
        {
            Ts = ts;
            Pid = pid;
            Comm = comm;
            this.Saddr = saddr;
            this.Daddr = daddr;
            this.Sport = sport;
            this.Dport = dport;
            this.Bytes = bytes;
            this.Type = type;
        }

        public override string ToString()
        {
            return $"{Ts.ToString("o")},{Pid},{Comm},{Saddr},{Daddr},{Sport},{Dport},{Bytes},{Type}";
        }
    }
}
