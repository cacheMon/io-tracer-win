using IOTracesCORE.utils;
using Xunit;

namespace IOTracesCORE.Tests
{
    public class PathHasherTests
    {
        [Fact]
        public void Hash_KnownInput_MatchesTruncatedSha256()
        {
            // SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
            Assert.Equal("ba7816bf8f01cfea", PathHasher.Hash("abc", 16));
        }

        [Fact]
        public void Hash_EmptyString_MatchesTruncatedSha256()
        {
            // SHA-256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
            Assert.Equal("e3b0c44298fc1c14", PathHasher.Hash("", 16));
        }

        [Fact]
        public void Hash_NonPositiveLength_ReturnsFullDigest()
        {
            Assert.Equal(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                PathHasher.Hash("abc", 0));
        }

        [Fact]
        public void Hash_LengthLargerThanDigest_ReturnsFullDigest()
        {
            string result = PathHasher.Hash("abc", 1000);
            Assert.Equal(64, result.Length);
        }

        [Fact]
        public void Hash_IsDeterministic()
        {
            Assert.Equal(PathHasher.Hash("hello", 16), PathHasher.Hash("hello", 16));
        }

        [Fact]
        public void HashFileName_PreservesExtension_AndHashesStem()
        {
            string result = PathHasher.HashFileName("report.txt", 16);

            Assert.EndsWith(".txt", result);
            Assert.Equal(16 + ".txt".Length, result.Length);
            // The original stem must not survive in the output.
            Assert.DoesNotContain("report", result);
        }

        [Fact]
        public void HashFileName_DifferentNames_ProduceDifferentHashes()
        {
            Assert.NotEqual(
                PathHasher.HashFileName("a.txt", 16),
                PathHasher.HashFileName("b.txt", 16));
        }

        [Fact]
        public void HashFilePath_EmptyInput_ReturnsInput()
        {
            Assert.Equal("", PathHasher.HashFilePath("", "C:\\", anonymous: false));
        }

        [Fact]
        public void HashFilePath_NonAnonymous_KeepsDirectoryButHashesFileName()
        {
            string result = PathHasher.HashFilePath(
                @"C:\Users\bob\report.txt", @"C:\", anonymous: false);

            Assert.StartsWith(@"C:\Users\bob\", result);
            Assert.EndsWith(".txt", result);
            Assert.DoesNotContain("report", result);
        }

        [Fact]
        public void HashFilePath_JustFileName_DoesNotAppendToItself()
        {
            string result = PathHasher.HashFilePath("report.txt", @"C:\", anonymous: false);
            Assert.DoesNotContain("report.txt", result);
        }

        [Fact]
        public void HashDirectoryPath_SamePathAsRoot_ReturnsRootBaseWithoutDot()
        {
            string result = PathHasher.HashDirectoryPath(@"C:\Users", @"C:\Users", keepLevels: 2);
            Assert.Equal(@"C:\Users", result);
        }

        [Fact]
        public void HashDirectoryPath_WithKeepLevels_HashesCorrectSegments()
        {
            string result = PathHasher.HashDirectoryPath(
                @"C:\Users\bob\Documents\Project", @"C:\Users", keepLevels: 1);

            Assert.StartsWith(@"C:\Users\bob\", result);
            Assert.DoesNotContain("Documents", result);
            Assert.DoesNotContain("Project", result);
        }

        [Fact]
        public void HashFilePath_Anonymous_KeepLevels1_DoesNotLeakUsername()
        {
            // Regression: the fs stream anonymized paths with keepLevels:2, which kept
            // BOTH "Users" and the <username> segment of C:\Users\<name>\..., leaking the
            // Windows username. The fix passes keepLevels:1 — keep only "Users", hash the
            // username and everything below.
            string result = PathHasher.HashFilePath(
                @"C:\Users\alice\Secret\report.txt", @"C:\",
                anonymous: true, hashLen: 16, keepLevels: 1);

            Assert.StartsWith(@"C:\Users\", result);
            Assert.DoesNotContain("alice", result);
            Assert.DoesNotContain("Secret", result);
            Assert.DoesNotContain("report", result);
        }
    }
}
