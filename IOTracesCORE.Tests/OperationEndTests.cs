using System;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    // Covers the op_end / nt_status addition (FileIO OperationEnd -> completion status).
    // nt_status is a trailing Windows-only column, so the shared cross-OS prefix
    // (asserted in AlignedSchemaTests) is unaffected.
    public class OperationEndTests
    {
        private static string[] FirstRow(string csv) =>
            csv.Replace("\r", "").TrimEnd('\n').Split('\n')[0].Split(',');

        [Fact]
        public void FsHeader_EndsWithNtStatus()
        {
            var header = TraceManifest.HeaderLine("filesystem").Split(',');
            Assert.Equal("nt_status", header[^1]);
        }

        [Fact]
        public void OpEndRow_EmitsHexStatusInLastColumn()
        {
            // 0xC0000034 = STATUS_OBJECT_NAME_NOT_FOUND (negative as a signed int).
            var trace = new FilesystemTrace(
                new DateTime(2026, 6, 14, 1, 2, 3), "op_end", 1234, 5678, "notepad.exe",
                "", 0, null, null, null, null, null, null, null,
                irp: 0xFFFFAB12UL, fileKey: null, fileAttributes: null, ioFlags: null,
                commandLine: "", ntStatus: unchecked((int)0xC0000034));
            var f = FirstRow(trace.FormatAsCsv(false));

            Assert.Equal("op_end", f[1]);
            Assert.Equal("0xFFFFAB12", f[16]);   // irp column (shifted -2 after dropping inode/device)
            Assert.Equal("0xC0000034", f[^1]);   // nt_status column
        }

        [Fact]
        public void NonOpEndRow_LeavesNtStatusEmpty()
        {
            var trace = new FilesystemTrace(
                new DateTime(2026, 6, 14, 1, 2, 3), "read", 1234, 5678,
                "notepad.exe", "C:\\a.txt", 4096);
            var f = FirstRow(trace.FormatAsCsv(false));

            Assert.Equal("", f[^1]);
        }
    }
}
