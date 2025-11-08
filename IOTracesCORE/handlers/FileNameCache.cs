using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.handlers
{
    internal readonly struct FileObjKey : IEquatable<FileObjKey>
    {
        public readonly int Pid;
        public readonly ulong FileObject;
        public FileObjKey(int pid, ulong fileObject) { Pid = pid; FileObject = fileObject; }
        public bool Equals(FileObjKey other) => Pid == other.Pid && FileObject == other.FileObject;
        public override bool Equals(object obj) => obj is FileObjKey k && Equals(k);
        public override int GetHashCode() => HashCode.Combine(Pid, FileObject);
    }

    internal sealed class FileNameCache
    {
        private readonly ConcurrentDictionary<FileObjKey, string> _map = new();

        public void Upsert(int pid, ulong fileObject, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _map[new FileObjKey(pid, fileObject)] = name;
        }

        public bool TryGet(int pid, ulong fileObject, out string name)
            => _map.TryGetValue(new FileObjKey(pid, fileObject), out name);

        public void Remove(int pid, ulong fileObject)
            => _map.TryRemove(new FileObjKey(pid, fileObject), out _);
    }
}
