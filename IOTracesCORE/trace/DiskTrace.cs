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
    class DiskTrace
    {
        public DiskTrace(DateTime ts, int pid, int threadId, string comm, long sector, string operation, int traceSize, double latency,
                         int diskNumber = -1, ulong irp = 0, long byteOffset = 0, ulong irpFlags = 0,
                         int majorFunction = -1, int minorFunction = -1, ulong routineAddr = 0, ulong fileObject = 0, ulong deviceObject = 0)
        {
            Ts = ts;
            Pid = pid;
            ThreadId = threadId;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Sector = sector;
            Operation = operation;
            TraceSize = traceSize;
            Latency = latency;
            DiskNumber = diskNumber;
            Irp = irp;
            ByteOffset = byteOffset;
            IrpFlags = irpFlags;
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

            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            csv.WriteField(Pid);
            csv.WriteField(ThreadId);
            csv.WriteField(Comm);
            csv.WriteField(Sector);
            csv.WriteField(Operation);
            csv.WriteField(TraceSize);
            csv.WriteField(Latency);
            csv.WriteField(DiskNumber);
            csv.WriteField(string.Format("0x{0:X}", Irp));
            csv.WriteField(ByteOffset);
            csv.WriteField(IrpFlagsHelper.ToString(IrpFlags));
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
        public long Sector { get; set; }
        public string Operation { get; set; }
        public int TraceSize { get; set; }
        public double Latency { get; set; }
        public int DiskNumber { get; set; }
        public ulong Irp { get; set; }
        public long ByteOffset { get; set; }
        public ulong IrpFlags { get; set; }
        public int MajorFunction { get; set; }
        public int MinorFunction { get; set; }
        public ulong RoutineAddr { get; set; }
        public ulong FileObject { get; set; }
        public ulong DeviceObject { get; set; }

    }
}
