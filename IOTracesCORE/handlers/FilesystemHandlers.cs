using IOTracesCORE;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace IOTracesCORE.handlers
{
    class FilesystemHandlers
    {
        private readonly WriterManager wm;
        private readonly ProcessCommandLineCache processCache;

        private readonly ConcurrentDictionary<ulong, string> nameByObj = new();

        public FilesystemHandlers(WriterManager old_wm, ProcessCommandLineCache processCache)
        {
            wm = old_wm;
            this.processCache = processCache;
        }

        private static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Trim();

        private static bool IsIgnored(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Contains("IOTracer", StringComparison.OrdinalIgnoreCase)) return true;
            // Wildcard characters are never valid in Windows file paths — these are leaked
            // directory enumeration search patterns (e.g. "Get-WmiObject*" from PowerShell dir_enum events).
            if (s.IndexOfAny(new[] { '*', '?' }) >= 0) return true;
            return false;
        }

        private static string MarkDirectoryIfNoExtension(string s)
        {
            var n = Clean(s);
            if (string.IsNullOrEmpty(n)) return n;
            if (n.EndsWith("\\", StringComparison.Ordinal) || n.EndsWith("/", StringComparison.Ordinal)) return n;

            // Heuristic requested: if there's no extension, treat as directory and mark with a trailing '\'.
            // Avoid changing drive letters and alternate data streams (":" after the last path separator).
            if (Path.GetExtension(n).Length != 0) return n;
            var lastSep = Math.Max(n.LastIndexOf('\\'), n.LastIndexOf('/'));
            var lastColon = n.LastIndexOf(':');
            if (lastColon > lastSep) return n;

            return n + "\\";
        }

        private string Resolve(ulong key, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return MarkDirectoryIfNoExtension(n);
            return nameByObj.TryGetValue(key, out var cached) ? MarkDirectoryIfNoExtension(cached) : "";
        }

        // Tries key1 first, then key2 as fallback — handles events where ETW FileKey vs
        // FileObject meaning differs across event types (create vs read/write/close).
        private string Resolve(ulong key1, ulong key2, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return MarkDirectoryIfNoExtension(n);
            if (nameByObj.TryGetValue(key1, out var cached)) return MarkDirectoryIfNoExtension(cached);
            if (nameByObj.TryGetValue(key2, out cached)) return MarkDirectoryIfNoExtension(cached);
            return "";
        }

        // Emit for simple operations with basic enhanced fields
        private void Emit(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            ulong? irpPtr = null, ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                null, null, null, null, null, null, irpPtr, fileKey, null, null, processCache.Get(pid)));
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
                null, processCache.Get(pid)));
        }

        // Extended emit for Read/Write operations with offset and IoFlags
        private void EmitReadWrite(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            long offset, ulong irpPtr, ulong fileKey, int ioFlags)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                null, null, null, offset, null, null, irpPtr, fileKey, null,
                FileIOFlags.FormatIoFlags(ioFlags), processCache.Get(pid)));
        }

        // Extended emit for MapFile operations with view size
        private void EmitMapFile(DateTime ts, string op, int pid, int tid, string proc, string name, ulong viewSize,
            ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, 0,
                null, null, null, null, (long)viewSize, null, null, fileKey, null, null, processCache.Get(pid)));
        }

        // Extended emit for Query/SetInfo operations with info class
        private void EmitWithInfoClass(DateTime ts, string op, int pid, int tid, string proc, string name,
            int infoClass, ulong? irpPtr = null, ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, 0,
                null, null, null, null, null, FileIOFlags.FormatInfoClass(infoClass),
                irpPtr, fileKey, null, null, processCache.Get(pid)));
        }


        public void OnRead(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitReadWrite(d.TimeStamp, "read", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileKey, d.FileObject, d.FileName), d.IoSize, d.Offset,
                d.IrpPtr, d.FileKey, d.IoFlags);
        }

        public void OnWrite(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitReadWrite(d.TimeStamp, "write", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileKey, d.FileObject, d.FileName), d.IoSize, d.Offset,
                d.IrpPtr, d.FileKey, d.IoFlags);
        }

        public void OnFlush(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "flush", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileKey, d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
        }

        public void OnDirEnum(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            // d.FileName is the search pattern (e.g. "*.txt", "Get-WmiObject*"), not the directory path.
            // Resolve the directory name from the cache instead.
            Emit(d.TimeStamp, "dir_enum", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileKey, d.FileObject, ""), 0, d.IrpPtr, d.FileKey);
        }

        public void OnCreate(FileIOCreateTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            // Store under FileObject (the only key available in create events).
            // FileIOName fires shortly after and stores under FileKey for read/write lookups.
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
                Resolve(d.FileKey, d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
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
                Resolve(d.FileKey, d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
            nameByObj.TryRemove(d.FileKey, out _);
            nameByObj.TryRemove(d.FileObject, out _);
        }

        public void OnRename(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                nameByObj[d.FileKey] = name;
                nameByObj[d.FileObject] = name;
            }
            Emit(d.TimeStamp, "rename", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileKey, d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
        }

        public void OnCleanup(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "cleanup", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileKey, d.FileObject, d.FileName), 0, d.IrpPtr, d.FileKey);
        }

        public void OnDirNotify(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            // Same as dir_enum: d.FileName is a filter pattern, not the directory path.
            Emit(d.TimeStamp, "dir_notify", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileKey, d.FileObject, ""), 0, d.IrpPtr, d.FileKey);
        }

        public void OnFileRundown(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileKey] = name;
            Emit(d.TimeStamp, "file_rundown", d.ProcessID, d.ThreadID, d.ProcessName, name, 0);
        }

        public void OnFSControl(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "fs_control", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileKey, d.FileObject, d.FileName), d.InfoClass, d.IrpPtr, d.FileKey);
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
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileKey] = name;
            Emit(d.TimeStamp, "name", d.ProcessID, d.ThreadID, d.ProcessName, name, 0);
        }

        public void OnQueryInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "query_info", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileKey, d.FileObject, d.FileName), d.InfoClass, d.IrpPtr, d.FileKey);
        }

        public void OnSetInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitWithInfoClass(d.TimeStamp, "set_info", d.ProcessID, d.ThreadID, d.ProcessName,
                Resolve(d.FileKey, d.FileObject, d.FileName), d.InfoClass, d.IrpPtr, d.FileKey);
        }

        public void OnUnmapFile(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "unmap_file", d.ProcessID, d.ThreadID, d.ProcessName,
                Clean(d.FileName), d.ViewSize, d.FileKey);
        }
    }
}
