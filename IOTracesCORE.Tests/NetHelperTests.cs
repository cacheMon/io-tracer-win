using System.Net;
using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class NetHelperTests
    {
        [Theory]
        [InlineData("10.0.0.1", "10.255.255.254")]      // 10.0.0.0/8
        [InlineData("172.16.0.1", "172.31.255.254")]    // 172.16.0.0/12 bounds
        [InlineData("192.168.1.1", "192.168.0.2")]      // 192.168.0.0/16
        [InlineData("169.254.1.1", "169.254.9.9")]      // link-local
        [InlineData("127.0.0.1", "127.0.0.1")]          // loopback
        public void IsLocalConversation_BothPrivate_ReturnsTrue(string src, string dst)
        {
            Assert.True(NetHelper.IsLocalConversation(IPAddress.Parse(src), IPAddress.Parse(dst)));
        }

        [Theory]
        [InlineData("8.8.8.8", "10.0.0.1")]             // public src
        [InlineData("10.0.0.1", "8.8.8.8")]             // public dst
        [InlineData("172.15.0.1", "192.168.0.1")]       // 172.15 is below the private range
        [InlineData("172.32.0.1", "192.168.0.1")]       // 172.32 is above the private range
        public void IsLocalConversation_AnyPublic_ReturnsFalse(string src, string dst)
        {
            Assert.False(NetHelper.IsLocalConversation(IPAddress.Parse(src), IPAddress.Parse(dst)));
        }

        [Fact]
        public void IsLocalConversation_IPv6LinkLocal_ReturnsTrue()
        {
            Assert.True(NetHelper.IsLocalConversation(
                IPAddress.Parse("fe80::1"), IPAddress.Parse("fe80::2")));
        }

        [Fact]
        public void IsLocalConversation_IPv6UniqueLocal_ReturnsTrue()
        {
            Assert.True(NetHelper.IsLocalConversation(
                IPAddress.Parse("fd00::1"), IPAddress.Parse("fc00::1")));
        }

        [Fact]
        public void IsLocalConversation_IPv6GlobalUnicast_ReturnsFalse()
        {
            Assert.False(NetHelper.IsLocalConversation(
                IPAddress.Parse("2001:4860:4860::8888"), IPAddress.Parse("fe80::1")));
        }
    }
}
