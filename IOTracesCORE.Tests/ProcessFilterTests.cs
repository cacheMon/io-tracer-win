using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class ProcessFilterTests
    {
        [Fact]
        public void ShouldTrace_RegularProcess_ReturnsTrue()
        {
            Assert.True(ProcessFilter.ShouldTrace(1234, "notepad.exe"));
        }

        [Theory]
        [InlineData("IOTracesCORE.exe")]
        [InlineData("RuntimeBroker")]
        [InlineData("WmiPrvSE.exe")]
        [InlineData("System Idle Process")]
        public void ShouldTrace_ExcludedProcess_ReturnsFalse(string processName)
        {
            Assert.False(ProcessFilter.ShouldTrace(1234, processName));
        }

        [Fact]
        public void ShouldTrace_ExclusionIsCaseInsensitive()
        {
            Assert.False(ProcessFilter.ShouldTrace(1234, "wmiprvse"));
        }

        [Fact]
        public void ShouldTrace_ExclusionMatchesSubstring()
        {
            // "iotrace" is a substring of the process name.
            Assert.False(ProcessFilter.ShouldTrace(1234, "my-iotrace-helper"));
        }

        [Fact]
        public void ShouldTrace_NullOrEmptyProcessName_DoesNotThrow()
        {
            // A missing process name must not crash the tracer; it is treated as traceable.
            Assert.True(ProcessFilter.ShouldTrace(1234, null!));
            Assert.True(ProcessFilter.ShouldTrace(1234, ""));
        }
    }
}
