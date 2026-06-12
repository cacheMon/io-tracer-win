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
        public void ToString_Read_ReturnsName()
        {
            Assert.Equal("Read", IrpFlagsHelper.ToString(256));
        }

        [Fact]
        public void ToString_ReadAndWrite_JoinsInDeclaredOrder()
        {
            // 256 (Read) | 512 (Write)
            Assert.Equal("Read|Write", IrpFlagsHelper.ToString(768));
        }

        [Fact]
        public void ToString_NormalPriority_DecodesPriorityBits()
        {
            // Priority value 2 shifted into bits 17-19 => 2 << 17 = 0x40000
            Assert.Equal("Priority:Normal", IrpFlagsHelper.ToString(0x40000));
        }
    }
}
