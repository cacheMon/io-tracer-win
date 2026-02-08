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

        private string Resolve(ulong fileObject, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return n;
            return nameByObj.TryGetValue(fileObject, out var cached) ? cached : "";
        }

        // Original emit for simple operations (backward compatible)
        private void Emit(DateTime ts, string op, int pid, string proc, string name, int size) =>
            wm.Write(new FilesystemTrace(ts, op, pid, proc, name, size));

        // Extended emit for Create operations with flags
        private void EmitCreate(DateTime ts, string op, int pid, string proc, string name, int size,
            int createOptions, int shareAccess, int createDisposition) =>
            wm.Write(new FilesystemTrace(ts, op, pid, proc, name, size,
                FileIOFlags.FormatCreateOptions(createOptions),
                FileIOFlags.FormatShareAccess(shareAccess),
                FileIOFlags.FormatCreateDisposition(createDisposition),
                null, null, null));

        // Extended emit for Read/Write operations with offset
        private void EmitWithOffset(DateTime ts, string op, int pid, string proc, string name, int size, long offset) =>
            wm.Write(new FilesystemTrace(ts, op, pid, proc, name, size,
                null, null, null, offset, null, null));

        // Extended emit for MapFile operations with view size
        private void EmitMapFile(DateTime ts, string op, int pid, string proc, string name, ulong viewSize) =>
            wm.Write(new FilesystemTrace(ts, op, pid, proc, name, 0,
                null, null, null, null, (long)viewSize, null));

        // Extended emit for Query/SetInfo operations with info class
        private void EmitWithInfoClass(DateTime ts, string op, int pid, string proc, string name, int infoClass) =>
            wm.Write(new FilesystemTrace(ts, op, pid, proc, name, 0,
                null, null, null, null, null, FileIOFlags.FormatInfoClass(infoClass)));


        public void OnRead(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithOffset(d.TimeStamp, "read", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.IoSize, d.Offset);
        }

        public void OnWrite(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithOffset(d.TimeStamp, "write", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.IoSize, d.Offset);
        }

        public void OnFlush(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "flush", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnQuery(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "query", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.InfoClass);
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

            // Now capturing CreateOptions, ShareAccess, and CreateDisposition flags
            EmitCreate(d.TimeStamp, "create", d.ProcessID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), 0,
                (int)d.CreateOptions, (int)d.ShareAccess, (int)d.CreateDisposition);
        }

        public void OnFileCreate(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "file_create", d.ProcessID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnDelete(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "delete", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
        }

        public void OnFileDelete(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "file_delete", d.ProcessID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnClose(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
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
            Emit(d.TimeStamp, "file_rundown", d.ProcessID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnFSControl(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "fs_control", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.InfoClass);
        }

        public void OnMapFile(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            // Now capturing ViewSize for memory-mapped file operations
            EmitMapFile(d.TimeStamp, "map_file", d.ProcessID, d.ProcessName, Clean(d.FileName), d.ViewSize);
        }

        public void OnMapFileDCStart(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "map_file_dc_start", d.ProcessID, d.ProcessName, Clean(d.FileName), d.ViewSize);
        }

        public void OnMapFileDCStop(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "map_file_dc_stop", d.ProcessID, d.ProcessName, Clean(d.FileName), d.ViewSize);
        }

        public void OnName(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "name", d.ProcessID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnQueryInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "query_info", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.InfoClass);
        }

        public void OnSetInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "set_info", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.InfoClass);
        }

        public void OnUnmapFile(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "unmap_file", d.ProcessID, d.ProcessName, Clean(d.FileName), d.ViewSize);
        }
    }
}
