using IOTracesCORE.handlers;
using Xunit;

namespace IOTracesCORE.Tests
{
    // MarkDirectoryIfNoExtension is applied ONLY to dir_enum / dir_notify (their target is a
    // directory). These tests pin the helper's contract; the call-site scoping (file ops do NOT
    // call it, so extensionless files keep their real names) is enforced in FilesystemHandlers.
    public class MarkDirectoryTests
    {
        [Theory]
        // Extensionless path => marked as a directory.
        [InlineData(@"C:\Users\foo\Documents", @"C:\Users\foo\Documents\")]
        [InlineData(@"C:\Windows\System32", @"C:\Windows\System32\")]
        // Has an extension => left alone (a file).
        [InlineData(@"C:\x\README.md", @"C:\x\README.md")]
        [InlineData(@"C:\x\archive.tar.gz", @"C:\x\archive.tar.gz")]
        // Already directory-terminated => unchanged (no double slash).
        [InlineData(@"C:\x\dir\", @"C:\x\dir\")]
        [InlineData(@"C:\x\dir/", @"C:\x\dir/")]
        // Drive root / alternate data stream (':' after the last separator) => unchanged.
        [InlineData(@"C:", @"C:")]
        [InlineData(@"C:\x\file:stream", @"C:\x\file:stream")]
        // Empty stays empty.
        [InlineData("", "")]
        public void MarkDirectoryIfNoExtension_Contract(string input, string expected)
        {
            Assert.Equal(expected, FilesystemHandlers.MarkDirectoryIfNoExtension(input));
        }

        [Theory]
        // The regression case: an extensionless FILE (README, LICENSE, Makefile) IS still marked
        // by the helper itself — which is exactly why it must NOT be called on file ops. Pinning
        // the helper behavior documents why the call sites are scoped to directory operations.
        [InlineData(@"C:\proj\README")]
        [InlineData(@"C:\proj\LICENSE")]
        [InlineData(@"C:\proj\Makefile")]
        public void MarkDirectoryIfNoExtension_WouldMislabelExtensionlessFiles(string file)
        {
            // The helper cannot tell an extensionless file from a directory — confirming the bug
            // would reappear if file ops resumed calling it.
            Assert.EndsWith("\\", FilesystemHandlers.MarkDirectoryIfNoExtension(file));
        }
    }
}
