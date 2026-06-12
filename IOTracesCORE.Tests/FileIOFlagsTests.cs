using IOTracesCORE.trace;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class FileIOFlagsTests
    {
        [Fact]
        public void FormatShareAccess_Zero_ReturnsEmpty()
        {
            Assert.Equal("", FileIOFlags.FormatShareAccess(0));
        }

        [Fact]
        public void FormatShareAccess_Read_ReturnsReadName()
        {
            Assert.Equal("FILE_SHARE_READ", FileIOFlags.FormatShareAccess(0x1));
        }

        [Fact]
        public void FormatShareAccess_ReadWrite_JoinsInDefinitionOrder()
        {
            Assert.Equal("FILE_SHARE_READ|FILE_SHARE_WRITE", FileIOFlags.FormatShareAccess(0x3));
        }

        [Fact]
        public void FormatCreateDisposition_Known_ReturnsName()
        {
            Assert.Equal("FILE_OPEN", FileIOFlags.FormatCreateDisposition(1));
        }

        [Fact]
        public void FormatCreateDisposition_Unknown_ReturnsHex()
        {
            Assert.Equal("0x00000063", FileIOFlags.FormatCreateDisposition(99));
        }

        [Fact]
        public void FormatCreateOptions_Zero_ReturnsEmpty()
        {
            Assert.Equal("", FileIOFlags.FormatCreateOptions(0));
        }

        [Fact]
        public void FormatCreateOptions_DirectoryFile_ReturnsName()
        {
            Assert.Equal("FILE_DIRECTORY_FILE", FileIOFlags.FormatCreateOptions(0x1));
        }

        [Fact]
        public void FormatFileAttributes_Zero_ReturnsEmpty()
        {
            Assert.Equal("", FileIOFlags.FormatFileAttributes(0));
        }

        [Fact]
        public void FormatFileAttributes_ReadOnly_ReturnsName()
        {
            Assert.Equal("FILE_ATTRIBUTE_READONLY", FileIOFlags.FormatFileAttributes(0x1));
        }

        [Fact]
        public void FormatInfoClass_Known_ReturnsName()
        {
            Assert.Equal("FileStandardInformation", FileIOFlags.FormatInfoClass(5));
        }

        [Fact]
        public void FormatFsctlCode_Known_ReturnsName()
        {
            Assert.Equal("FSCTL_LOCK_VOLUME", FileIOFlags.FormatFsctlCode(0x00090018));
        }

        [Fact]
        public void FormatFsctlCode_Unknown_ReturnsHex()
        {
            Assert.Equal("0x12345678", FileIOFlags.FormatFsctlCode(0x12345678));
        }
    }
}
