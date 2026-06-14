using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Globalization;
using System.IO;

namespace IOTracesCORE.trace
{
    /// <summary>
    /// One per-connection, per-minute network summary row. Instead of emitting a
    /// row for every packet, <see cref="IOTracesCORE.handlers.NetworkHandlers"/>
    /// accumulates bytes per connection and flushes a single row per connection
    /// each minute, carrying the bytes sent/received during that window.
    /// </summary>
    internal class NetworkTrace
    {
        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public int Proto { get; set; }
        public string Saddr { get; set; }
        public string Daddr { get; set; }
        public int Sport { get; set; }
        public int Dport { get; set; }
        public ulong ConnId { get; set; }
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public NetworkTrace(DateTime ts, int pid, string comm, int proto,
            string saddr, string daddr, int sport, int dport, ulong connId,
            long bytesSent, long bytesReceived)
        {
            Ts = ts;
            Pid = pid;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Proto = proto;
            Saddr = saddr;
            Daddr = daddr;
            Sport = sport;
            Dport = dport;
            ConnId = connId;
            BytesSent = bytesSent;
            BytesReceived = bytesReceived;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n"
            };

            this.csv = new CsvWriter(buffer, config);
        }

        public string FormatAsCsv()
        {
            buffer.GetStringBuilder().Clear();
            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
            csv.WriteField(Pid);
            csv.WriteField(Comm);
            csv.WriteField(Proto);
            csv.WriteField(Saddr);
            csv.WriteField(Daddr);
            csv.WriteField(Sport);
            csv.WriteField(Dport);
            csv.WriteField(ConnId);
            csv.WriteField(BytesSent);
            csv.WriteField(BytesReceived);
            csv.NextRecord();
            return buffer.ToString();
        }
    }
}
