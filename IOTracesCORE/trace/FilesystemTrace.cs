using CsvHelper;
using CsvHelper.Configuration;
using IOTracesCORE.utils;
using System;
using System.Collections.Generic;
using System.Globalization;
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

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public string FormatAsCsv(bool is_anonymous)
        {
            buffer.GetStringBuilder().Clear(); 
            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            csv.WriteField(Op);
            csv.WriteField(Pid);
            csv.WriteField(Comm);
            csv.WriteField(is_anonymous ? PathHasher.HashFileName(Filename) : Filename);
            csv.WriteField(TraceSize);
            csv.NextRecord();
            return buffer.ToString();
        }

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

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n"
            };

            this.csv = new CsvWriter(buffer, config);
        }
    }
}
