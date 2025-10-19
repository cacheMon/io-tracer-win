using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.utils
{
    internal class PathHasher
    {
        public static string HashDirectoryPath(string fullPath, string rootBase, int keepLevels = 2, int hashLen = 16)
        {
            fullPath = Path.GetFullPath(fullPath);
            rootBase = Path.GetFullPath(rootBase);

            if (!fullPath.StartsWith(rootBase, StringComparison.OrdinalIgnoreCase))
                return fullPath;

            string relative = Path.GetRelativePath(rootBase, fullPath);

            var segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries
            );

            var transformed = segments
                .Select((seg, i) => i < keepLevels ? seg : Hash(seg, hashLen))
                .ToArray();

            return transformed.Length == 0
                ? rootBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : Path.Combine(rootBase, Path.Combine(transformed));
        }


        public static string HashFilePath(string fileFullPath, string rootBase, bool anonymous, int hashLen = 16)
        {
            string dir = Path.GetDirectoryName(fileFullPath) ?? "";

            if (anonymous)
            {
                dir = HashDirectoryPath(
                    fullPath: dir,
                    rootBase: rootBase,
                    keepLevels: 2,
                    hashLen: hashLen
                );
            }

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
