using CsvHelper;
using CsvHelper.Configuration;
using IOTracesCORE.utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        public int Status { get; set; }

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public NetworkTrace(DateTime ts, int pid, string comm, string saddr, string daddr, int sport, int dport, int bytes, string type, int status = 0)
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
            this.Status = status;

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
            csv.WriteField(Saddr);
            csv.WriteField(Daddr);
            csv.WriteField(Sport);
            csv.WriteField(Dport);
            csv.WriteField(Bytes);
            csv.WriteField(Type);
            csv.WriteField(Status);
            csv.NextRecord();
            return buffer.ToString();
        }

        public override string ToString()
        {
            return $"{Ts.ToString("o")},{Pid},{Comm},{Saddr},{Daddr},{Sport},{Dport},{Bytes},{Type},{Status}";
        }
    }
}
