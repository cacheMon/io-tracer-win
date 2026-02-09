using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Globalization;
using System.IO;

namespace IOTracesCORE.trace
{
    /// <summary>
    /// Represents a memory/cache event for tracking page faults, cache hits/misses,
    /// and memory pressure events.
    /// </summary>
    class MemoryTrace
    {
        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public string Type { get; set; }           // Human-readable event type (HIT, MISS, etc.)
        public int EventType { get; set; }         // Numeric code (0-9) for analysis
        public ulong VirtualAddress { get; set; }  // Virtual address of the page
        public long ByteCount { get; set; }        // Bytes involved (for hard faults)
        public int ThreadId { get; set; }          // Thread that caused the fault

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public MemoryTrace(
            DateTime ts,
            int pid,
            string comm,
            string type,
            int eventType,
            ulong virtualAddress = 0,
            long byteCount = 0,
            int threadId = 0)
        {
            Ts = ts;
            Pid = pid;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Type = type;
            EventType = eventType;
            VirtualAddress = virtualAddress;
            ByteCount = byteCount;
            ThreadId = threadId;

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
            csv.WriteField(Type);
            csv.WriteField(EventType);
            csv.WriteField($"0x{VirtualAddress:X}");
            csv.WriteField(ByteCount);

            csv.NextRecord();
            return buffer.ToString();
        }
    }
}
