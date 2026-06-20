using System;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    // Locks the column schema: the manifest HeaderLine() must match the FormatAsCsv
    // field order. The block stream keeps the Linux-aligned prefix; the fs stream
    // intentionally OMITS the Linux prefix's inode/device (Windows ETW carries neither —
    // file identity is file_key, the volume is in filename), so the fs prefix below
    // diverges from Linux by design.
    public class AlignedSchemaTests
    {
        // Windows fs prefix — Linux's inode/device dropped (always empty on Windows).
        private static readonly string[] FsPrefix =
        {
            "timestamp", "operation", "pid", "tid", "command", "filename",
            "size", "offset", "bytes_completed", "flags",
        };
        private static readonly string[] BlockPrefix =
        {
            "timestamp", "operation", "pid", "tid", "command", "sector",
            "size", "latency_ms", "device", "flags",
        };

        private static string[] FirstRow(string csv) =>
            csv.Replace("\r", "").TrimEnd('\n').Split('\n')[0].Split(',');

        [Fact]
        public void FsHeader_StartsWithSharedPrefix()
        {
            var header = TraceManifest.HeaderLine("filesystem").Split(',');
            for (int i = 0; i < FsPrefix.Length; i++)
                Assert.Equal(FsPrefix[i], header[i]);
        }

        [Fact]
        public void BlockHeader_MatchesSharedPrefixThenIrp()
        {
            var header = TraceManifest.HeaderLine("disk").Split(',');
            for (int i = 0; i < BlockPrefix.Length; i++)
                Assert.Equal(BlockPrefix[i], header[i]);
            Assert.Equal("irp", header[BlockPrefix.Length]); // only Windows extra
        }

        [Fact]
        public void DiskHeaderLine_ResolvesTracetypeAlias()
        {
            // WriterManager passes "disk"; it must resolve to the block stream.
            Assert.StartsWith("timestamp,operation,pid,tid,command,sector",
                              TraceManifest.HeaderLine("disk"));
        }

        [Fact]
        public void FsRow_ReadOp_AlignsWithHeader()
        {
            var trace = new FilesystemTrace(
                new DateTime(2026, 6, 14, 1, 2, 3), "read", 1234, 5678,
                "notepad.exe", "C:\\a.txt", 4096);
            var f = FirstRow(trace.FormatAsCsv(false));

            Assert.Equal("read", f[1]);   // operation
            Assert.Equal("1234", f[2]);   // pid
            Assert.Equal("5678", f[3]);   // tid (moved up into the shared prefix)
            Assert.Equal("notepad.exe", f[4]);
            Assert.Equal("4096", f[6]);   // size
            Assert.Equal("4096", f[8]);   // bytes_completed mirrors size for read/write
        }

        [Fact]
        public void FsRow_CreateOp_IsCanonicalizedToOpen()
        {
            var trace = new FilesystemTrace(
                new DateTime(2026, 6, 14), "create", 1, 2, "x", "C:\\b", 0);
            var f = FirstRow(trace.FormatAsCsv(false));

            Assert.Equal("open", f[1]);   // create -> open
            Assert.Equal("", f[8]);       // bytes_completed empty for non-I/O
        }

        [Fact]
        public void BlockRow_AlignsWithHeader()
        {
            var trace = new DiskTrace(
                new DateTime(2026, 6, 14), 1234, 5678, "notepad.exe",
                1024345, "read", 4096, 0.5, diskNumber: 0);
            var f = FirstRow(trace.FormatAsCsv());

            Assert.Equal("read", f[1]);      // operation
            Assert.Equal("1234", f[2]);      // pid
            Assert.Equal("5678", f[3]);      // tid
            Assert.Equal("notepad.exe", f[4]);
            Assert.Equal("1024345", f[5]);   // sector
            Assert.Equal("4096", f[6]);      // size
        }
    }
}
