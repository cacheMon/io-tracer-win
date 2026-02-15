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

        private static bool IsIgnored(string s) => !string.IsNullOrEmpty(s) && s.Contains("IOTracer", StringComparison.OrdinalIgnoreCase);

        private string Resolve(ulong fileObject, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return n;
            return nameByObj.TryGetValue(fileObject, out var cached) ? cached : "";
        }

        // Emit for simple operations with basic enhanced fields
        private void Emit(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            ulong? irpPtr = null, ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                null, null, null, null, null, null, irpPtr, fileKey, null, null));
        }

        // Extended emit for Create operations with all flags
        // NOTE: DesiredAccess is NOT captured here because it is not available in Windows ETW FileIO events.
        // Both the NT Kernel Logger (FileIOCreateTraceData) and Microsoft-Windows-Kernel-File provider
        // do not include DesiredAccess in their event schemas. The available fields are:
        // CreateOptions, ShareAccess, CreateDisposition, FileAttributes, FileName.
        // To capture DesiredAccess, a minifilter driver would be required (like Process Monitor uses).
        private void EmitCreate(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            int createOptions, int shareAccess, int createDisposition, ulong irpPtr, ulong fileKey,
            int fileAttributes)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                FileIOFlags.FormatCreateOptions(createOptions),
                FileIOFlags.FormatShareAccess(shareAccess),
                FileIOFlags.FormatCreateDisposition(createDisposition),
                null, null, null, irpPtr, fileKey,
                FileIOFlags.FormatFileAttributes(fileAttributes),
                null));
        }

        // Extended emit for Read/Write operations with offset and IoFlags
        private void EmitReadWrite(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            long offset, ulong irpPtr, ulong fileKey, int ioFlags)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                null, null, null, offset, null, null, irpPtr, fileKey, null,
                FileIOFlags.FormatIoFlags(ioFlags)));
        }

        // Extended emit for MapFile operations with view size
        private void EmitMapFile(DateTime ts, string op, int pid, int tid, string proc, string name, ulong viewSize,
            ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, 0,
                null, null, null, null, (long)viewSize, null, null, fileKey, null, null));
        }

        // Extended emit for Query/SetInfo operations with info class
        private void EmitWithInfoClass(DateTime ts, string op, int pid, int tid, string proc, string name,
            int infoClass, ulong? irpPtr = null, ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, 0,
                null, null, null, null, null, FileIOFlags.FormatInfoClass(infoClass),
                irpPtr, fileKey, null, null));
        }


        public void OnRead(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitReadWrite(d.TimeStamp, "read", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), d.IoSize, d.Offset,
                d.IrpPtr, d.FileKey, d.IoFlags);
        }

        public void OnWrite(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitReadWrite(d.TimeStamp, "write", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), d.IoSize, d.Offset,
                d.IrpPtr, d.FileKey, d.IoFlags);
        }

        public void OnFlush(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "flush", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
        }

        public void OnQuery(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "query", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), d.InfoClass, d.IrpPtr, d.FileKey);
        }

        public void OnDirEnum(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "dir_enum", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
        }

        public void OnCreate(FileIOCreateTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileObject] = name;

            // Capturing CreateOptions, ShareAccess, CreateDisposition, and FileAttributes
            // Note: FileIOCreateTraceData uses FileObject as file identifier
            EmitCreate(d.TimeStamp, "create", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), 0,
                (int)d.CreateOptions, (int)d.ShareAccess, (int)d.CreateDisposition,
                d.IrpPtr, d.FileObject, (int)d.FileAttributes);
        }

        public void OnFileCreate(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "file_create", d.ProcessID, d.ThreadID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnDelete(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "delete", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
        }

        public void OnFileDelete(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "file_delete", d.ProcessID, d.ThreadID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnClose(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "close", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
            nameByObj.TryRemove(d.FileObject, out _); // drop mapping after close
        }

        public void OnRename(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileObject] = name;
            Emit(d.TimeStamp, "rename", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
        }

        public void OnCleanup(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "cleanup", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
        }

        public void OnDirNotify(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "dir_notify", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
        }

        public void OnFileRundown(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "file_rundown", d.ProcessID, d.ThreadID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnFSControl(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "fs_control", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), d.InfoClass, d.IrpPtr, d.FileKey);
        }

        public void OnMapFile(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "map_file", d.ProcessID, d.ThreadID, d.ProcessName,
                Clean(d.FileName), d.ViewSize, d.FileKey);
        }

        public void OnMapFileDCStart(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "map_file_dc_start", d.ProcessID, d.ThreadID, d.ProcessName,
                Clean(d.FileName), d.ViewSize, d.FileKey);
        }

        public void OnMapFileDCStop(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "map_file_dc_stop", d.ProcessID, d.ThreadID, d.ProcessName,
                Clean(d.FileName), d.ViewSize, d.FileKey);
        }

        public void OnName(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "name", d.ProcessID, d.ThreadID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnQueryInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "query_info", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), d.InfoClass, d.IrpPtr, d.FileKey);
        }

        public void OnSetInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "set_info", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileObject, d.FileName), d.InfoClass, d.IrpPtr, d.FileKey);
        }

        public void OnUnmapFile(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "unmap_file", d.ProcessID, d.ThreadID, d.ProcessName,
                Clean(d.FileName), d.ViewSize, d.FileKey);
        }
    }
}
