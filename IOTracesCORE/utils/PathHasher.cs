using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.utils
{
    internal class PathHasher
    {
        public static string HashDirectoryPath(string fullPath, string rootBase, int hashLen = 16)
        {
            string root = Path.GetPathRoot(fullPath) ?? "";
            string relative = Path.GetRelativePath(rootBase, fullPath);

            var hashedSegments = relative
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .Select(seg => Hash(seg, hashLen));

            return Path.Combine(root, Path.Combine(hashedSegments.ToArray()));
        }

        public static string HashFilePath(string fileFullPath, string rootBase, bool anonymous, int hashLen = 16)
        {
            string dir = Path.GetDirectoryName(fileFullPath) ?? "";
            dir = anonymous ? HashDirectoryPath(dir, rootBase, hashLen) : dir;
            string file = Path.GetFileName(fileFullPath);

            string name = Path.GetFileNameWithoutExtension(file);
            string ext = Path.GetExtension(file);

            string hashedName = Hash(name, hashLen) + ext;
            return Path.Combine(dir, hashedName);
        }

        private static string Hash(string s, int len)
        {
            using var sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            byte[] hash = sha.ComputeHash(bytes);

            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));

            return (len > 0 && len < sb.Length) ? sb.ToString(0, len) : sb.ToString();
        }
    }
}
