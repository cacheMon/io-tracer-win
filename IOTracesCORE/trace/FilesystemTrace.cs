using CsvHelper;
using CsvHelper.Configuration;
using IOTracesCORE.utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        public double Latency { get; set; }

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public string FormatAsCsv(bool is_anonymous)
        {
            buffer.GetStringBuilder().Clear(); 
            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            csv.WriteField(Op);
            csv.WriteField(Pid);
            csv.WriteField(Comm);
            if (is_anonymous)
            {
                try
                {
                    string root = Path.GetPathRoot(Filename) ?? "/";
                    csv.WriteField(PathHasher.HashFilePath(Filename, root, true, 16));
                }
                catch (Exception)
                {
                    csv.WriteField(Filename);
                }
            }
            else
            {
                csv.WriteField(Filename);
            }
            csv.WriteField(TraceSize);
            csv.WriteField(Latency);
            csv.NextRecord();
            return buffer.ToString();
        }

        public FilesystemTrace(
                DateTime ts, 
                string op, 
                int pid, 
                string comm, 
                string filename, 
                int size,
                double latency
            )
        {
            Ts = ts;
            Op = op;
            Pid = pid;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Filename = string.IsNullOrEmpty(filename) ? "" : filename;
            TraceSize = size;
            Latency = latency;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n"
            };

            this.csv = new CsvWriter(buffer, config);
        }
    }
}
