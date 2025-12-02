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
    class MemoryTrace
    {
        public MemoryTrace(DateTime ts, int pid, string comm, ulong virtualAddress, string type)
        {
            Ts = ts;
            Pid = pid;
            Comm = comm;
            VirtualAddress = virtualAddress;
            Type = type;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n"
            };

            this.csv = new CsvWriter(buffer, config);
        }

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public string FormatAsCsv()
        {
            buffer.GetStringBuilder().Clear();
            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            csv.WriteField(Pid);
            csv.WriteField(Comm);
            csv.WriteField($"0x{VirtualAddress:X16}");
            csv.WriteField(Type);
            csv.NextRecord();
            return buffer.ToString();
        }

        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public ulong VirtualAddress { get; set; }
        public string Type { get; set; }
    }
}
