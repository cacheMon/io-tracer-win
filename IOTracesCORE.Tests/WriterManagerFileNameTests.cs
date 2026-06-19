using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using IOTracesCORE;
using Xunit;

namespace IOTracesCORE.Tests
{
    /// <summary>
    /// Covers rotation-filename generation. Filenames must be unique even when many
    /// rotations land in the same millisecond, because compression is now deferred to a
    /// background thread that reads + deletes each rotated raw file asynchronously — a
    /// reused path could otherwise be appended-to while it is being compressed.
    /// </summary>
    public class WriterManagerFileNameTests
    {
        private static readonly DateTime FixedUtc =
            new DateTime(2026, 6, 18, 13, 9, 13, 850, DateTimeKind.Utc);

        [Fact]
        public void BuildTraceFileName_NoPart_HasExpectedFormat()
        {
            string name = WriterManager.BuildTraceFileName("fs", 5, "ABC", FixedUtc);
            Assert.Equal(@".\fs\fs_20260618_130913850_5_ABC.csv", name);
        }

        [Fact]
        public void BuildTraceFileName_WithPart_HasExpectedFormat()
        {
            string name = WriterManager.BuildTraceFileName(
                "filesystem_snapshot", 7, "ABC", FixedUtc, partNumber: 2);
            Assert.Equal(
                @".\filesystem_snapshot\filesystem_snapshot_part0002_20260618_130913850_7_ABC.csv",
                name);
        }

        [Fact]
        public void BuildTraceFileName_WithPart_MatchesSnapshotGlobPrefix()
        {
            // FinalizeFilesystemSnapshot deletes via "filesystem_snapshot_part*.csv*".
            string name = WriterManager.BuildTraceFileName(
                "filesystem_snapshot", 1, "DEV", FixedUtc, partNumber: 12);
            Assert.Contains(@"\filesystem_snapshot_part0012_", name);
            Assert.EndsWith(".csv", name);
        }

        [Fact]
        public void BuildTraceFileName_SameTimestamp_DistinctSeq_ProducesDistinctNames()
        {
            // Same millisecond, only the seq differs — names must still be unique.
            var seen = new HashSet<string>();
            for (int seq = 1; seq <= 10_000; seq++)
            {
                string name = WriterManager.BuildTraceFileName("fs", seq, "DEV", FixedUtc);
                Assert.True(seen.Add(name), $"duplicate filename for seq={seq}: {name}");
            }
        }

        [Fact]
        public void NextFilePathSeq_ConcurrentCallers_AllValuesUnique()
        {
            // The production seq source must hand out a unique value to every rotation,
            // even under concurrent flushes from the ETW, snapshot, and flush-timer threads.
            const int total = 50_000;
            var values = new ConcurrentBag<int>();
            Parallel.For(0, total, _ => values.Add(WriterManager.NextFilePathSeq()));

            Assert.Equal(total, values.Count);
            Assert.Equal(total, new HashSet<int>(values).Count);
        }
    }
}
