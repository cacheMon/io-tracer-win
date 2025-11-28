using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.utils
{
    internal class PathHasher
    {
        public static string machineGuid = Microsoft.Win32.Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", ""
        )?.ToString() ?? "unknown";
        public static string deviceId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(machineGuid))).Substring(0,16);
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

        public static string HashFileName(string filename, int hashLen = 16)
        {
            string name = Path.GetFileNameWithoutExtension(filename);
            string ext = Path.GetExtension(filename);
            string hashedName = Hash(name, hashLen) + ext;
            return hashedName;
        }

        public static string HashFilePath(string fileFullPath, string rootBase, bool anonymous, int hashLen = 16, int keepLevels = 2)
        {
            if (string.IsNullOrEmpty(fileFullPath))
                return fileFullPath;
            string dir = Path.GetDirectoryName(fileFullPath) ?? fileFullPath;

            if (anonymous)
            {
                dir = HashDirectoryPath(
                    fullPath: dir,
                    rootBase: rootBase,
                    keepLevels: keepLevels,
                    hashLen: hashLen
                );
            }

            string file = Path.GetFileName(fileFullPath);
            string name = Path.GetFileNameWithoutExtension(file);
            string ext = Path.GetExtension(file);


            string hashedName = Hash(name, hashLen) + ext;
            return Path.Combine(dir, hashedName);
        }


        public static string Hash(string s, int len)
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
