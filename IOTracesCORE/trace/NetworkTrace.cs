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
        public ulong ConnId { get; set; }
        public int SeqNum { get; set; }
        public int Proto { get; set; }
        public int Mss { get; set; }
        public int SndWinScale { get; set; }
        public int RcvWinScale { get; set; }
        public int RcvWin { get; set; }
        public int WsOpt { get; set; }
        public int TsOpt { get; set; }
        public int SackOpt { get; set; }
        public ulong Context { get; set; }
        public int DSize { get; set; }

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public NetworkTrace(DateTime ts, int pid, string comm, string saddr, string daddr, int sport, int dport, int bytes, string type, int status = 0, ulong connId = 0, int seqNum = 0, int proto = 0, int mss = 0, int sndWinScale = 0, int rcvWinScale = 0, int rcvWin = 0, int wsOpt = 0, int tsOpt = 0, int sackOpt = 0, ulong context = 0, int dSize = 0)
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
            this.ConnId = connId;
            this.SeqNum = seqNum;
            this.Proto = proto;
            this.Mss = mss;
            this.SndWinScale = sndWinScale;
            this.RcvWinScale = rcvWinScale;
            this.RcvWin = rcvWin;
            this.WsOpt = wsOpt;
            this.TsOpt = tsOpt;
            this.SackOpt = sackOpt;
            this.Context = context;
            this.DSize = dSize;

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
            csv.WriteField(ConnId);
            csv.WriteField(SeqNum);
            csv.WriteField(Proto);
            csv.WriteField(Mss);
            csv.WriteField(SndWinScale);
            csv.WriteField(RcvWinScale);
            csv.WriteField(RcvWin);
            csv.WriteField(WsOpt);
            csv.WriteField(TsOpt);
            csv.WriteField(SackOpt);
            csv.WriteField(Context);
            csv.WriteField(DSize);
            csv.NextRecord();
            return buffer.ToString();
        }

        public override string ToString()
        {
            return $"{Ts.ToString("o")},{Pid},{Comm},{Saddr},{Daddr},{Sport},{Dport},{Bytes},{Type},{Status},{ConnId},{SeqNum},{Proto},{Mss},{SndWinScale},{RcvWinScale},{RcvWin},{WsOpt},{TsOpt},{SackOpt},{Context},{DSize}";
        }
    }
}
