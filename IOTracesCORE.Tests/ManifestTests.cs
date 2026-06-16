using System;
using System.Text.Json;
using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    // Covers manifest fields added for trace-consumer clarity:
    //  - clock_source.utc_offset / timezone, so local row timestamps are convertible to UTC
    //  - counters.filesystem_snapshot_dirs_* , so snapshot coverage/completeness is known
    public class ManifestTests
    {
        private static JsonElement BuildFinalRoot()
        {
            string json = TraceManifest.Build(
                final: true, deviceId: "DEV", anonymous: false, uploadEnabled: false,
                startUtc: new DateTime(2026, 6, 14, 1, 2, 3, DateTimeKind.Utc),
                stopUtc: new DateTime(2026, 6, 14, 2, 2, 3, DateTimeKind.Utc));
            return JsonDocument.Parse(json).RootElement;
        }

        [Fact]
        public void ClockSource_RecordsUtcOffsetAndTimezone()
        {
            var clock = BuildFinalRoot().GetProperty("clock_source");

            Assert.Equal("local_wall_clock", clock.GetProperty("timestamps").GetString());
            // e.g. "+07:00" / "-05:00" / "+00:00" — always signed HH:mm so it is unambiguous.
            Assert.Matches(@"^[+-]\d{2}:\d{2}$", clock.GetProperty("utc_offset").GetString());
            Assert.False(string.IsNullOrEmpty(clock.GetProperty("timezone").GetString()));
        }

        [Fact]
        public void Counters_ExposeSnapshotDirectoryCoverage()
        {
            var counters = BuildFinalRoot().GetProperty("counters");

            Assert.True(counters.TryGetProperty("filesystem_snapshot_dirs_scanned", out _));
            Assert.True(counters.TryGetProperty("filesystem_snapshot_dirs_inaccessible", out _));
        }
    }
}
