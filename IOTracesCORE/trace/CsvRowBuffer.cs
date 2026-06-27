using CsvHelper;
using CsvHelper.Configuration;
using System;
using System.Globalization;
using System.IO;

namespace IOTracesCORE.trace
{
    /// <summary>
    /// One reusable CsvHelper writer per THREAD, shared by every trace row type.
    ///
    /// Constructing a CsvWriter + StringWriter + CsvConfiguration PER ROW was the dominant tracer
    /// cost: measured ~24 KB allocated per filesystem row (~96% of per-event consumer cost), with
    /// the resulting GC consuming ~23% of wall-clock and starving the single ETW consumer thread
    /// (the fs-truncation root cause). Reusing one writer per thread and resetting it between rows
    /// cut GC pause ~86-93% and consumer lag ~80-90% with no change to output.
    ///
    /// Usage per row:  var csv = CsvRowBuffer.Begin(out var buffer); ...WriteField...; csv.NextRecord();
    ///                 return buffer.ToString();
    /// FormatAsCsv is a leaf operation (no re-entrancy), so a single thread-local writer is
    /// race-free without locks. All row types share the same config (InvariantCulture, NewLine "\n").
    /// </summary>
    internal static class CsvRowBuffer
    {
        [ThreadStatic] private static StringWriter? _buffer;
        [ThreadStatic] private static CsvWriter? _csv;

        /// <summary>Returns this thread's reusable CsvWriter, cleared and ready for one row, along
        /// with the StringWriter backing it (call <c>buffer.ToString()</c> after NextRecord()).</summary>
        internal static CsvWriter Begin(out StringWriter buffer)
        {
            buffer = _buffer ??= new StringWriter();
            var csv = _csv ??= new CsvWriter(buffer,
                new CsvConfiguration(CultureInfo.InvariantCulture) { NewLine = "\n" });
            buffer.GetStringBuilder().Clear();
            return csv;
        }
    }
}
