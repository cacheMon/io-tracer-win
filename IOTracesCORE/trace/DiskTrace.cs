using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
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

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n"
            };

            this.csv = new CsvWriter(buffer, config);
        }

        public string FormatAsCsv()
        {
            buffer.GetStringBuilder().Clear();

            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            csv.WriteField(Pid);
            csv.WriteField(Comm);
            csv.WriteField(Sector);
            csv.WriteField(Operation);
            csv.WriteField(TraceSize);
            csv.NextRecord();
            return buffer.ToString();
        }


        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public long Sector { get; set; }
        public string Operation { get; set; }
        public int TraceSize { get; set; }

    }
}
