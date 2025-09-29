using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.trace
{
    class FilesystemTrace
    {
        public DateTime Ts { get; set; }
        public string Op { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public string Filename { get; set; }
        public int TraceSize { get; set; }


        public FilesystemTrace(
                DateTime ts, 
                string op, 
                int pid, 
                string comm, 
                string filename, 
                int size
            )
        {
            Ts = ts;
            Op = op;
            Pid = pid;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Filename = string.IsNullOrEmpty(filename) ? "" : filename;
            TraceSize = size;
        }
    }
}
