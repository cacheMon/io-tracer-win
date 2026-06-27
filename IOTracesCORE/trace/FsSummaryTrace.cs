using CsvHelper;
using CsvHelper.Configuration;
using IOTracesCORE.utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace IOTracesCORE.trace
{
    /// <summary>
    /// One per-(process, file, operation), per-minute SUMMARY row. Under sustained load
    /// <see cref="IOTracesCORE.handlers.FilesystemHandlers"/> sheds low-value metadata ops
    /// (getattr/close/op_end/dir_enum/...) out of the full fs/ stream and accumulates a
    /// count here instead, flushed once a minute — so the information (how many of each op
    /// per file) is retained at a fraction of the volume while read/write keep full
    /// per-event fidelity in fs/. Mirrors the nw stream's per-minute aggregation shape.
    /// </summary>
    internal class FsSummaryTrace
    {
        public DateTime Ts { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public string Filename { get; set; }
        public string Op { get; set; }
        public long Count { get; set; }

        // Mirror FilesystemTrace's canonical op vocabulary so fs_summary operations match
        // the fs/ stream (getattr/readdir/setattr/...) for consistent analysis.
        private static readonly Dictionary<string, string> CanonicalOps = new()
        {
            ["create"] = "open", ["flush"] = "fsync", ["query_info"] = "getattr",
            ["set_info"] = "setattr", ["dir_enum"] = "readdir", ["map_file"] = "mmap",
            ["fs_control"] = "fsctl",
        };

        // Row buffer/writer are provided per-thread by CsvRowBuffer (no per-row allocation).

        public FsSummaryTrace(DateTime ts, int pid, string comm, string filename, string op, long count)
        {
            Ts = ts;
            Pid = pid;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Filename = string.IsNullOrEmpty(filename) ? "" : filename;
            Op = op ?? "";
            Count = count;

            // CsvWriter is no longer built per row here — formatting uses the per-thread
            // CsvRowBuffer (see FormatAsCsv). Per-row writer construction was the dominant cost.
        }

        public string FormatAsCsv(bool is_anonymous)
        {
            var csv = CsvRowBuffer.Begin(out var buffer);
            string op = CanonicalOps.TryGetValue(Op, out var canon) ? canon : Op;

            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
            csv.WriteField(Pid);
            csv.WriteField(Comm);
            if (is_anonymous && !string.IsNullOrEmpty(Filename))
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
            csv.WriteField(op);
            csv.WriteField(Count);
            csv.NextRecord();
            return buffer.ToString();
        }
    }
}
