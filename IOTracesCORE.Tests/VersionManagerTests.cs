using IOTracesCORE;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class VersionManagerTests
    {
        [Fact]
        public void ParseCommitId_ExtractsShaAfterPlus()
        {
            Assert.Equal("ab12cd34ef56", VersionManager.ParseCommitId("1.2.3+ab12cd34ef56"));
        }

        [Fact]
        public void ParseCommitId_HandlesSemverPrereleaseBeforePlus()
        {
            Assert.Equal("deadbeef", VersionManager.ParseCommitId("1.2.3-beta.1+deadbeef"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1.2.3")]        // no metadata
        [InlineData("1.2.3+")]       // trailing '+' with nothing after it
        public void ParseCommitId_ReturnsUnknown_WhenNoShaPresent(string? input)
        {
            Assert.Equal("unknown", VersionManager.ParseCommitId(input));
        }
    }
}
