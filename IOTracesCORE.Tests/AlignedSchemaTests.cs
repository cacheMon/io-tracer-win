using System;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    // Locks the cross-OS aligned schema (v5): the shared column prefix and the
    // FormatAsCsv field order must match the Linux tracer's fs/block streams.
    public class AlignedSchemaTests
    {
        // Shared prefixes — identical names/order to the Linux tracer.
        private static readonly string[] FsPrefix =
        {
            "timestamp", "operation", "pid", "tid", "command", "filename",
            "size", "offset", "bytes_completed", "inode", "device", "flags",
        };
        private static readonly string[] DsPrefix =
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
        public void DsHeader_MatchesSharedPrefixThenIrp()
        {
            var header = TraceManifest.HeaderLine("disk").Split(',');
            for (int i = 0; i < DsPrefix.Length; i++)
                Assert.Equal(DsPrefix[i], header[i]);
            Assert.Equal("irp", header[DsPrefix.Length]); // only Windows extra
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
        public void DsRow_AlignsWithHeader()
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
