using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.trace
{
    internal class ProcessInfo
    {
        public DateTime Ts { get; set; }
        public uint ProcessId { get; set; }
        public string Name { get; set; }
        public string CommandLine { get; set; }
        public ulong VirtualSize { get; set; }       // bytes
        public ulong WorkingSetSize { get; set; }    // bytes
        public DateTime? CreationDate { get; set; }

        public double CpuUsage_5s { get; set; }
        public double CpuUsage_2m { get; set; }
        public double CpuUsage_1h { get; set; }

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public ProcessInfo(
            DateTime ts,
            uint processId,
            string name,
            string commandLine,
            ulong virtualSize,
            ulong workingSetSize,
            DateTime? creationDate,
            double cpuUsage_5s,
            double cpuUsage_2m,
            double cpuUsage_1h
        )
            {
                Ts = ts;
                ProcessId = processId;
                Name = name;
                CommandLine = commandLine;
                VirtualSize = virtualSize;
                WorkingSetSize = workingSetSize;
                CreationDate = creationDate;
                CpuUsage_2m = cpuUsage_2m;
                CpuUsage_5s = cpuUsage_5s;
                CpuUsage_1h = cpuUsage_1h;

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
            csv.WriteField(ProcessId);
            csv.WriteField(Name);
            csv.WriteField(CommandLine);
            csv.WriteField(VirtualSize);
            csv.WriteField(WorkingSetSize);
            csv.WriteField(CreationDate?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "");
            csv.WriteField(CpuUsage_5s);
            csv.WriteField(CpuUsage_2m);
            csv.WriteField(CpuUsage_1h);
            csv.NextRecord();
            return buffer.ToString();
        }

        public override string ToString()
        {
            return $"{ProcessId}: {Name} (WS: {WorkingSetSize / 1024 / 1024} MB)";
        }


    }
}
