using IOTracesCORE.trace;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class FileIOFlagsTests
    {
        // ---- FormatFsctlCode ----

        [Fact]
        public void FormatFsctlCode_KnownCode_ReturnsName()
        {
            Assert.Equal("FSCTL_GET_COMPRESSION", FileIOFlags.FormatFsctlCode(0x0009003C));
        }

        [Fact]
        public void FormatFsctlCode_UnknownCode_ReturnsHex()
        {
            Assert.Equal("0x12345678", FileIOFlags.FormatFsctlCode(0x12345678));
        }

        // ---- FormatCreateOptions ----

        [Fact]
        public void FormatCreateOptions_Zero_ReturnsEmpty()
        {
            Assert.Equal("", FileIOFlags.FormatCreateOptions(0));
        }

        [Fact]
        public void FormatCreateOptions_SingleFlag_ReturnsName()
        {
            Assert.Equal("FILE_DIRECTORY_FILE", FileIOFlags.FormatCreateOptions(0x1));
        }

        [Fact]
        public void FormatCreateOptions_CombinedFlags_JoinedWithPipe()
        {
            // FILE_DIRECTORY_FILE (0x1) | FILE_WRITE_THROUGH (0x2)
            Assert.Equal("FILE_DIRECTORY_FILE|FILE_WRITE_THROUGH", FileIOFlags.FormatCreateOptions(0x3));
        }

        [Fact]
        public void FormatCreateOptions_UnknownBits_ReturnsHex()
        {
            // 0x01000000 maps to no defined CreateOptions flag.
            Assert.Equal("0x01000000", FileIOFlags.FormatCreateOptions(0x01000000));
        }

        [Fact]
        public void FormatCreateOptions_MixedKnownAndUnknownBits_ReturnsBoth()
        {
            // FILE_DIRECTORY_FILE (0x1) | unknown 0x01000000: the unknown bit must be
            // preserved rather than silently dropped.
            Assert.Equal("FILE_DIRECTORY_FILE|0x01000000", FileIOFlags.FormatCreateOptions(0x01000001));
        }

        [Fact]
        public void FormatIoFlags_MixedKnownAndUnknownBits_ReturnsBoth()
        {
            // IRP_NOCACHE (0x1) | unknown 0x00010000.
            Assert.Equal("IRP_NOCACHE|0x00010000", FileIOFlags.FormatIoFlags(0x00010001));
        }

        // ---- FormatShareAccess ----

        [Fact]
        public void FormatShareAccess_Zero_ReturnsEmpty()
        {
            Assert.Equal("", FileIOFlags.FormatShareAccess(0));
        }

        [Fact]
        public void FormatShareAccess_Read_ReturnsName()
        {
            Assert.Equal("FILE_SHARE_READ", FileIOFlags.FormatShareAccess(0x1));
        }

        [Fact]
        public void FormatShareAccess_ReadWriteDelete_JoinedWithPipe()
        {
            Assert.Equal("FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE",
                FileIOFlags.FormatShareAccess(0x7));
        }

        // ---- FormatCreateDisposition ----

        [Theory]
        [InlineData(0, "FILE_SUPERSEDE")]
        [InlineData(1, "FILE_OPEN")]
        [InlineData(2, "FILE_CREATE")]
        [InlineData(3, "FILE_OPEN_IF")]
        [InlineData(4, "FILE_OVERWRITE")]
        [InlineData(5, "FILE_OVERWRITE_IF")]
        public void FormatCreateDisposition_KnownValues_ReturnNames(int value, string expected)
        {
            Assert.Equal(expected, FileIOFlags.FormatCreateDisposition(value));
        }

        [Fact]
        public void FormatCreateDisposition_UnknownValue_ReturnsHex()
        {
            Assert.Equal("0x00000063", FileIOFlags.FormatCreateDisposition(99));
        }

        // ---- FormatInfoClass ----

        [Fact]
        public void FormatInfoClass_KnownValue_ReturnsName()
        {
            Assert.Equal("FileStandardInformation", FileIOFlags.FormatInfoClass(5));
        }

        [Fact]
        public void FormatInfoClass_Zero_IsUndefined_ReturnsHex()
        {
            // The enum starts at 1, so 0 is undefined.
            Assert.Equal("0x00000000", FileIOFlags.FormatInfoClass(0));
        }

        // ---- FormatFileAttributes ----

        [Fact]
        public void FormatFileAttributes_Zero_ReturnsEmpty()
        {
            Assert.Equal("", FileIOFlags.FormatFileAttributes(0));
        }

        [Fact]
        public void FormatFileAttributes_SingleFlag_ReturnsName()
        {
            Assert.Equal("FILE_ATTRIBUTE_ARCHIVE", FileIOFlags.FormatFileAttributes(0x20));
        }

        [Fact]
        public void FormatFileAttributes_ReadOnlyHidden_JoinedWithPipe()
        {
            Assert.Equal("FILE_ATTRIBUTE_READONLY|FILE_ATTRIBUTE_HIDDEN",
                FileIOFlags.FormatFileAttributes(0x3));
        }

        // ---- FormatIoFlags ----

        [Fact]
        public void FormatIoFlags_Zero_ReturnsEmpty()
        {
            Assert.Equal("", FileIOFlags.FormatIoFlags(0));
        }

        [Fact]
        public void FormatIoFlags_ReadOperation_ReturnsName()
        {
            // IRP_READ_OPERATION (0x100) is a unique bit value.
            Assert.Equal("IRP_READ_OPERATION", FileIOFlags.FormatIoFlags(0x100));
        }

        [Fact]
        public void FormatIoFlags_NocacheAndReadWrite_JoinedWithPipe()
        {
            // IRP_NOCACHE (0x1) | IRP_READ_OPERATION (0x100) | IRP_WRITE_OPERATION (0x200)
            Assert.Equal("IRP_NOCACHE|IRP_READ_OPERATION|IRP_WRITE_OPERATION",
                FileIOFlags.FormatIoFlags(0x301));
        }
    }
}
