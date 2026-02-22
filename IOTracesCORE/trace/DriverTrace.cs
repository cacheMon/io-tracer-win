using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace IOTracesCORE.trace
{
    class DriverTrace
    {
        public DriverTrace(DateTime ts, int pid, int threadId, string comm, string operation,
                         ulong irp = 0, int majorFunction = -1, int minorFunction = -1,
                         ulong routineAddr = 0, ulong fileObject = 0, ulong deviceObject = 0)
        {
            Ts = ts;
            Pid = pid;
            ThreadId = threadId;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Operation = operation;
            Irp = irp;
            MajorFunction = majorFunction;
            MinorFunction = minorFunction;
            RoutineAddr = routineAddr;
            FileObject = fileObject;
            DeviceObject = deviceObject;

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
            csv.WriteField(ThreadId);
            csv.WriteField(Comm);
            csv.WriteField(Operation);
            csv.WriteField(string.Format("0x{0:X}", Irp));
            csv.WriteField(MajorFunction);
            csv.WriteField(MinorFunction);
            csv.WriteField(string.Format("0x{0:X}", RoutineAddr));
            csv.WriteField(string.Format("0x{0:X}", FileObject));
            csv.WriteField(string.Format("0x{0:X}", DeviceObject));

            csv.NextRecord();
            return buffer.ToString();
        }

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public int ThreadId { get; set; }
        public string Comm { get; set; }
        public string Operation { get; set; }
        public ulong Irp { get; set; }
        public int MajorFunction { get; set; }
        public int MinorFunction { get; set; }
        public ulong RoutineAddr { get; set; }
        public ulong FileObject { get; set; }
        public ulong DeviceObject { get; set; }
    }
}
