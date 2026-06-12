using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class IrpFlagsHelperTests
    {
        [Fact]
        public void ToString_Zero_ReturnsEmpty()
        {
            Assert.Equal("", IrpFlagsHelper.ToString(0));
        }

        [Fact]
        public void ToString_Nocache_ReturnsName()
        {
            Assert.Equal("Nocache", IrpFlagsHelper.ToString(1));
        }

        [Fact]
        public void ToString_ReadAndWrite_PreservesOrder()
        {
            // Read (256) | Write (512); Read is emitted before Write.
            Assert.Equal("Read|Write", IrpFlagsHelper.ToString(256 | 512));
        }

        [Fact]
        public void ToString_NormalPriority_Decoded()
        {
            // Priority is stored in bits 17-19. Normal == 2, so 2 << 17.
            Assert.Equal("Priority:Normal", IrpFlagsHelper.ToString(2UL << 17));
        }

        [Fact]
        public void ToString_CriticalPriority_Decoded()
        {
            Assert.Equal("Priority:Critical", IrpFlagsHelper.ToString(4UL << 17));
        }

        [Fact]
        public void ToString_UnmappedBit_ReturnsHex()
        {
            // Bit 16 (0x10000) is not part of any named flag or the priority mask.
            Assert.Equal("0x10000", IrpFlagsHelper.ToString(0x10000));
        }

        [Fact]
        public void ToString_MixedKnownAndUnmappedBits_ReturnsBoth()
        {
            // Nocache (0x1) | unmapped 0x10000: the unmapped bit must be preserved.
            Assert.Equal("Nocache|0x10000", IrpFlagsHelper.ToString(0x10001));
        }
    }
}
