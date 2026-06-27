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
                         int diskNumber = -1, ulong? irp = null, ulong irpFlags = 0)
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

            IrpFlags = irpFlags;

            // CsvWriter is no longer built per row here — formatting uses the per-thread
            // CsvRowBuffer (see FormatAsCsv). Per-row writer construction was the dominant cost.
        }

        public string FormatAsCsv()
        {
            var csv = CsvRowBuffer.Begin(out var buffer);

            // --- shared cross-OS prefix (columns 1-10; identical on Linux) --- #
            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
            csv.WriteField(Operation);          // already a base op (read/write/flush)
            csv.WriteField(Pid);
            csv.WriteField(ThreadId);
            csv.WriteField(Comm);
            csv.WriteField(Sector);
            csv.WriteField(TraceSize);
            // A negative latency is the "unknown" sentinel (no matching DiskIOInit was
            // seen for this IRP); emit an empty field rather than a misleading 0.
            csv.WriteField(Latency < 0 ? "" : Latency.ToString(CultureInfo.InvariantCulture));
            csv.WriteField(DiskNumber);         // device — disk index (Linux uses major:minor)
            csv.WriteField(IrpFlagsHelper.ToString(IrpFlags));

            // --- Windows-only extras (columns 11+) --- #
            csv.WriteField(Irp.HasValue ? string.Format("0x{0:X}", Irp.Value) : "");

            csv.NextRecord();
            return buffer.ToString();
        }


        // Row buffer/writer are provided per-thread by CsvRowBuffer (no per-row allocation).

        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public int ThreadId { get; set; }
        public string Comm { get; set; }
        public long Sector { get; set; }
        public string Operation { get; set; }
        public int TraceSize { get; set; }
        public double Latency { get; set; }
        public int DiskNumber { get; set; }
        public ulong? Irp { get; set; }

        public ulong IrpFlags { get; set; }

    }
}
