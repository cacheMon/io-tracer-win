using IOTracesCORE;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace IOTracesCORE.handlers
{
    class FilesystemHandlers
    {
        private readonly WriterManager wm;
            
        private readonly ConcurrentDictionary<ulong, string> nameByObj = new();

        public FilesystemHandlers(WriterManager old_wm) => wm = old_wm;

        private static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Trim();

        private static Dictionary<ulong, double> _activeRequests = new Dictionary<ulong, double>();
        private static Dictionary<ulong, string> _requestTypes = new Dictionary<ulong, string>();
        private static Dictionary<ulong, ulong> _requestFileObjects = new Dictionary<ulong, ulong>(); 
        private static Dictionary<ulong, string> _requestNames = new Dictionary<ulong, string>();   
        private static Dictionary<ulong, long> _requestSizes = new Dictionary<ulong, long>();

        private string Resolve(ulong fileObject, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return n;
            return nameByObj.TryGetValue(fileObject, out var cached) ? cached : "";
        }

        private void Emit(DateTime ts, string op, int pid, string proc, string name, int size) =>
            wm.Write(new FilesystemTrace(ts, op, pid, proc, name, size));


        public void OnRead(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "read", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.IoSize);
        }

        public void OnWrite(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "write", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.IoSize);
        }

        public void OnFlush(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "flush", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnQuery(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "query", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnDirEnum(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "dir_enum", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnCreate(FileIOCreateTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileObject] = name;
            Emit(d.TimeStamp, "create", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnFileCreate(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "file_create", d.ProcessID, d.ProcessName, name, 0);
        }

        public void OnDelete(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileObject, d.FileName);
            Emit(d.TimeStamp, "delete", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnFileDelete(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "file_delete", d.ProcessID, d.ProcessName, name, 0);
        }

        public void OnClose(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileObject, d.FileName);
            Emit(d.TimeStamp, "close", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
            nameByObj.TryRemove(d.FileObject, out _); // drop mapping after close
        }

        public void OnRename(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileObject] = name;
            Emit(d.TimeStamp, "rename", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnCleanup(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "cleanup", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnDirNotify(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "dir_notify", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnFileRundown(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "file_rundown", d.ProcessID, d.ProcessName, name, 0);
        }

        public void OnFSControl(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "fs_control", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnMapFile(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "map_file", d.ProcessID, d.ProcessName, name, 0);
        }

        public void OnMapFileDCStart(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "map_file_dc_start", d.ProcessID, d.ProcessName, name, 0);
        }

        public void OnMapFileDCStop(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "map_file_dc_stop", d.ProcessID, d.ProcessName, name, 0);
        }

        public void OnName(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "name", d.ProcessID, d.ProcessName, name, 0);
        }

        public void OnQueryInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "query_info", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnSetInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "set_info", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnUnmapFile(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "unmap_file", d.ProcessID, d.ProcessName, name, 0);
        }
    }
}
