using IOTracesCORE;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace IOTracesCORE.handlers
{
    class FilesystemHandlers
    {
        private readonly WriterManager wm;

        private readonly ConcurrentDictionary<ulong, string> nameByObj = new();

        public FilesystemHandlers(WriterManager old_wm) => wm = old_wm;

        private static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Trim();

        private string Resolve(ulong fileObject, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return n;
            return nameByObj.TryGetValue(fileObject, out var cached) ? cached : "";
        }

        private void Emit(DateTime ts, string op, int pid, string proc, string name, int size) =>
            wm.Write(new FilesystemTrace(ts, op, pid, proc, name, size));

        public void OnFileRead(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "read", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.IoSize);
        }

        public void OnFileWrite(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "write", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.IoSize);
        }

        public void OnFileFlush(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "flush", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnFileIoQuery(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "stat", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnFileDirEnum(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "getdirentry", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnFileCreate(FileIOCreateTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileObject] = name;
            Emit(d.TimeStamp, "create", d.ProcessID, d.ProcessName, name, 0);
        }

        public void OnFileDelete(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileObject, d.FileName);
            Emit(d.TimeStamp, "delete", d.ProcessID, d.ProcessName, name, 0);
        }

        public void OnFileClose(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileObject, d.FileName);
            Emit(d.TimeStamp, "close", d.ProcessID, d.ProcessName, name, 0);
            nameByObj.TryRemove(d.FileObject, out _); // drop mapping after close
        }

        public void OnFileRename(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileObject] = name;
            Emit(d.TimeStamp, "rename", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }
    }
}
