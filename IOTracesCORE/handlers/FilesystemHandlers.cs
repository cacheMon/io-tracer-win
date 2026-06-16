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
    class FilesystemHandlers : IDisposable
    {
        private readonly WriterManager wm;
        private readonly ProcessCommandLineCache processCache;

        private readonly ConcurrentDictionary<ulong, string> nameByObj = new();
        private readonly ConcurrentDictionary<ulong, ConcurrentQueue<(DateTime addedAt, Action<string> emit)>>
            _pending = new();
        // Track alternate keys (FileObject, etc) for each FileKey so we can drain all related pending queues.
        // The inner set is a ConcurrentDictionary (used as a set) so it can be safely mutated by the ETW
        // callback thread while the flush-timer thread enumerates and removes entries during cleanup.
        private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, byte>> _keyAliases = new();
        private readonly System.Threading.Timer _flushTimer;
        private static readonly TimeSpan MaxPendingAge = TimeSpan.FromMilliseconds(100);

        public FilesystemHandlers(WriterManager wm, ProcessCommandLineCache processCache)
        {
            this.wm = wm;
            this.processCache = processCache;
            _flushTimer = new System.Threading.Timer(FlushStalePending, null,
                TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        }

        public void Dispose() => _flushTimer?.Dispose();

        /// <summary>
        /// Drops all cross-event correlation state (filename cache, pending emit
        /// queue, and key aliases). Call on ETW session restart so kernel
        /// FileObject / FileKey pointers reused by the new session are never
        /// matched against stale entries from the previous session, which would
        /// otherwise emit wrong filenames. Pending events from the old session can
        /// never be drained (their FileIOName events won't arrive again), so they
        /// are discarded rather than later flushed with empty names.
        /// </summary>
        public void Reset()
        {
            nameByObj.Clear();
            _pending.Clear();
            _keyAliases.Clear();
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

        private void LogEmptyFilenameIfNeeded(string name, DateTime ts, string op, int pid, int tid, string proc)
        {
            if (string.IsNullOrEmpty(name))
            {
                wm.LogEmptyFilename(ts, op, pid, tid, proc);
            }
        }

        private string Resolve(ulong key, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return MarkDirectoryIfNoExtension(n);
            return nameByObj.TryGetValue(key, out var cached) ? MarkDirectoryIfNoExtension(cached) : "";
        }

        // Tries key1 first, then key2 as fallback — handles events where ETW FileKey vs
        // FileObject meaning differs across event types (create vs read/write/close).
        // Cross-populates the other key when found, so future lookups find the name via either key.
        private string Resolve(ulong key1, ulong key2, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return MarkDirectoryIfNoExtension(n);

            if (nameByObj.TryGetValue(key1, out var cached))
            {
                if (key2 != 0 && key2 != key1) nameByObj.TryAdd(key2, cached);
                return MarkDirectoryIfNoExtension(cached);
            }

            if (nameByObj.TryGetValue(key2, out cached))
            {
                if (key1 != 0 && key1 != key2) nameByObj.TryAdd(key1, cached);
                return MarkDirectoryIfNoExtension(cached);
            }

            return "";
        }

        private void EnqueuePending(ulong fileKey, ulong fileObject, Action<string> emit)
        {
            ulong key = fileKey != 0 ? fileKey : fileObject;
            if (key == 0) { emit(""); return; }

            // Track both keys as aliases so DrainPending can find them via either key
            if (fileKey != 0 && fileObject != 0 && fileKey != fileObject)
            {
                _keyAliases.GetOrAdd(fileKey, _ => new ConcurrentDictionary<ulong, byte>()).TryAdd(fileObject, 0);
                _keyAliases.GetOrAdd(fileObject, _ => new ConcurrentDictionary<ulong, byte>()).TryAdd(fileKey, 0);
            }

            _pending.GetOrAdd(key, _ => new ConcurrentQueue<(DateTime, Action<string>)>())
                    .Enqueue((DateTime.UtcNow, emit));
        }

        private void DrainPending(ulong fileKey, string resolvedName)
        {
            if (fileKey == 0) return;

            // Cache the cleaned name once
            var cleanedName = MarkDirectoryIfNoExtension(resolvedName);

            // Primary drain: the exact key from the name event
            if (_pending.TryRemove(fileKey, out var queue))
            {
                while (queue.TryDequeue(out var item))
                {
                    item.emit(cleanedName);
                }
            }

            // Secondary drain: the alternate keys recorded for THIS same file — i.e. the
            // FileKey/FileObject pair captured together in a single EnqueuePending call. Those keys
            // are safely correlated with fileKey, so we drain their queues and cache the name under
            // them for future lookups.
            //
            // We deliberately do NOT scan the rest of _pending. That dictionary is global and holds
            // pending events for every file; a key we have not explicitly correlated with this file
            // may belong to a completely different file, so assigning this resolvedName to it would
            // corrupt the filename in the output trace.
            if (_keyAliases.TryRemove(fileKey, out var aliases))
            {
                foreach (var altKey in aliases.Keys)
                {
                    // Drop the reverse mapping (altKey -> fileKey) so _keyAliases does not grow unbounded.
                    _keyAliases.TryRemove(altKey, out _);

                    if (altKey != 0 && !string.IsNullOrEmpty(resolvedName))
                        nameByObj.TryAdd(altKey, resolvedName);

                    if (_pending.TryRemove(altKey, out queue))
                    {
                        while (queue.TryDequeue(out var item))
                        {
                            item.emit(cleanedName);
                        }
                    }
                }
            }
        }

        // Removes a key's alias set along with the reverse mappings that point back at it,
        // so _keyAliases does not accumulate stale entries after a pending event is resolved or flushed.
        private void RemoveAliases(ulong key)
        {
            if (_keyAliases.TryRemove(key, out var aliases))
            {
                foreach (var altKey in aliases.Keys)
                    _keyAliases.TryRemove(altKey, out _);
            }
        }

        private void FlushStalePending(object? state)
        {
            var cutoff = DateTime.UtcNow - MaxPendingAge;
            foreach (var kvp in _pending)
            {
                if (!kvp.Value.TryPeek(out var oldest) || oldest.addedAt > cutoff) continue;
                if (!_pending.TryRemove(kvp.Key, out var queue)) continue;
                RemoveAliases(kvp.Key);
                nameByObj.TryGetValue(kvp.Key, out var cachedName);
                var finalName = string.IsNullOrEmpty(cachedName) ? "" : MarkDirectoryIfNoExtension(cachedName);
                while (queue.TryDequeue(out var item))
                {
                    item.emit(finalName);
                }
            }
        }

        // Emit for simple operations with basic enhanced fields
        private void Emit(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            ulong? irp = null, ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                null, null, null, null, null, null, null, irp, fileKey, null, null, processCache.Get(pid)));
        }

        // Extended emit for Create operations with all flags
        // NOTE: DesiredAccess is NOT captured here because it is not available in Windows ETW FileIO events.
        // Both the NT Kernel Logger (FileIOCreateTraceData) and Microsoft-Windows-Kernel-File provider
        // do not include DesiredAccess in their event schemas. The available fields are:
        // CreateOptions, ShareAccess, CreateDisposition, FileAttributes, FileName.
        // To capture DesiredAccess, a minifilter driver would be required (like Process Monitor uses).
        private void EmitCreate(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            int createOptions, int shareAccess, int createDisposition, ulong irp, ulong fileKey,
            int fileAttributes)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                FileIOFlags.FormatCreateOptions(createOptions),
                FileIOFlags.FormatShareAccess(shareAccess),
                FileIOFlags.FormatCreateDisposition(createDisposition),
                null, null, null, null, irp, fileKey,
                FileIOFlags.FormatFileAttributes(fileAttributes),
                null, processCache.Get(pid)));
        }

        // Extended emit for Read/Write operations with offset and IoFlags
        private void EmitReadWrite(DateTime ts, string op, int pid, int tid, string proc, string name, int size,
            long offset, ulong irp, ulong fileKey, int ioFlags)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, size,
                null, null, null, offset, null, null, null, irp, fileKey, null,
                FileIOFlags.FormatIoFlags(ioFlags), processCache.Get(pid)));
        }

        // Extended emit for MapFile operations with view size
        private void EmitMapFile(DateTime ts, string op, int pid, int tid, string proc, string name, ulong viewSize,
            ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, 0,
                null, null, null, null, (long)viewSize, null, null, null, fileKey, null, null, processCache.Get(pid)));
        }

        // Extended emit for Query/SetInfo operations with info class
        private void EmitWithInfoClass(DateTime ts, string op, int pid, int tid, string proc, string name,
            int infoClass, ulong? irp = null, ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, 0,
                null, null, null, null, null, FileIOFlags.FormatInfoClass(infoClass), null,
                irp, fileKey, null, null, processCache.Get(pid)));
        }

        // fs_control events carry an FSCTL code in InfoClass, not a FILE_INFORMATION_CLASS value
        private void EmitFsControl(DateTime ts, string op, int pid, int tid, string proc, string name,
            int fsctlCode, ulong? irp = null, ulong? fileKey = null)
        {
            if (IsIgnored(name)) return;
            wm.Write(new FilesystemTrace(ts, op, pid, tid, proc, name, 0,
                null, null, null, null, null, null, FileIOFlags.FormatFsctlCode(fsctlCode),
                irp, fileKey, null, null, processCache.Get(pid)));
        }


        public void OnRead(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                EmitReadWrite(d.TimeStamp, "read", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, d.IoSize, d.Offset, d.IrpPtr, d.FileKey, d.IoFlags);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var size = d.IoSize; var offset = d.Offset; var irp = d.IrpPtr; var fk = d.FileKey; var flags = d.IoFlags;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "read", pid, tid, proc);
                EmitReadWrite(ts, "read", pid, tid, proc, resolvedName, size, offset, irp, fk, flags);
            });
        }

        public void OnWrite(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                EmitReadWrite(d.TimeStamp, "write", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, d.IoSize, d.Offset, d.IrpPtr, d.FileKey, d.IoFlags);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var size = d.IoSize; var offset = d.Offset; var irp = d.IrpPtr; var fk = d.FileKey; var flags = d.IoFlags;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "write", pid, tid, proc);
                EmitReadWrite(ts, "write", pid, tid, proc, resolvedName, size, offset, irp, fk, flags);
            });
        }

        public void OnFlush(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "flush", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "flush", pid, tid, proc);
                Emit(ts, "flush", pid, tid, proc, resolvedName, 0, irp, fk);
            });
        }

        public void OnDirEnum(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            // d.FileName is the search pattern (e.g. "*.txt", "Get-WmiObject*"), not the directory path.
            // Resolve the directory name from the cache instead.
            var name = Resolve(d.FileKey, d.FileObject, "");
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "dir_enum", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "dir_enum", pid, tid, proc);
                Emit(ts, "dir_enum", pid, tid, proc, resolvedName, 0, irp, fk);
            });
        }

        public void OnCreate(FileIOCreateTraceData d)
        {
            var name = Clean(d.FileName);
            // Store under FileObject (the only key available in create events).
            // FileIOName fires shortly after and stores under FileKey for read/write lookups.
            // Cache population happens regardless of process filter so all files are indexed.
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileObject] = name;

            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;

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
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "delete", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "delete", pid, tid, proc);
                Emit(ts, "delete", pid, tid, proc, resolvedName, 0, irp, fk);
            });
        }

        public void OnFileDelete(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "file_delete", d.ProcessID, d.ThreadID, d.ProcessName, Clean(d.FileName), 0);
        }

        public void OnClose(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "close", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, 0, d.IrpPtr, d.FileKey);
            }
            else
            {
                // Defer if name is empty—will be resolved by OnName or flushed by timer
                var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
                var irp = d.IrpPtr; var fk = d.FileKey;
                EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
                {
                    LogEmptyFilenameIfNeeded(resolvedName, ts, "close", pid, tid, proc);
                    Emit(ts, "close", pid, tid, proc, resolvedName, 0, irp, fk);
                });
            }

            // Evict AFTER enqueuing so OnName can drain pending before keys are removed
            nameByObj.TryRemove(d.FileKey, out _);
            nameByObj.TryRemove(d.FileObject, out _);
        }

        public void OnRename(FileIOInfoTraceData d)
        {
            var name = Clean(d.FileName);
            // Cache population happens regardless of process filter so all files are indexed.
            if (!string.IsNullOrEmpty(name))
            {
                nameByObj[d.FileKey] = name;
                nameByObj[d.FileObject] = name;
            }

            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var resolvedName = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(resolvedName))
            {
                Emit(d.TimeStamp, "rename", d.ProcessID, d.ThreadID, d.ProcessName,
                    resolvedName, 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, finalName =>
            {
                LogEmptyFilenameIfNeeded(finalName, ts, "rename", pid, tid, proc);
                Emit(ts, "rename", pid, tid, proc, finalName, 0, irp, fk);
            });
        }

        public void OnCleanup(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "cleanup", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "cleanup", pid, tid, proc);
                Emit(ts, "cleanup", pid, tid, proc, resolvedName, 0, irp, fk);
            });
        }

        public void OnDirNotify(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            // Same as dir_enum: d.FileName is a filter pattern, not the directory path.
            var name = Resolve(d.FileKey, d.FileObject, "");
            if (!string.IsNullOrEmpty(name))
            {
                Emit(d.TimeStamp, "dir_notify", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, 0, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "dir_notify", pid, tid, proc);
                Emit(ts, "dir_notify", pid, tid, proc, resolvedName, 0, irp, fk);
            });
        }

        public void OnFileRundown(FileIONameTraceData d)
        {
            var name = Clean(d.FileName);
            // Cache population happens regardless of process filter so all files are indexed.
            if (!string.IsNullOrEmpty(name))
            {
                nameByObj[d.FileKey] = name;
                DrainPending(d.FileKey, MarkDirectoryIfNoExtension(name));
            }

            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "file_rundown", d.ProcessID, d.ThreadID, d.ProcessName, name, 0);
        }

        public void OnFSControl(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                EmitFsControl(d.TimeStamp, "fs_control", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, d.InfoClass, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var fsctlCode = d.InfoClass; var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "fs_control", pid, tid, proc);
                EmitFsControl(ts, "fs_control", pid, tid, proc, resolvedName, fsctlCode, irp, fk);
            });
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
            var name = Clean(d.FileName);
            // Cache population happens regardless of process filter so all files are indexed.
            if (!string.IsNullOrEmpty(name))
            {
                nameByObj[d.FileKey] = name;
                DrainPending(d.FileKey, MarkDirectoryIfNoExtension(name));
            }

            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            Emit(d.TimeStamp, "name", d.ProcessID, d.ThreadID, d.ProcessName, name, 0);
        }

        public void OnQueryInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                EmitWithInfoClass(d.TimeStamp, "query_info", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, d.InfoClass, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var infoClass = d.InfoClass; var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "query_info", pid, tid, proc);
                EmitWithInfoClass(ts, "query_info", pid, tid, proc, resolvedName, infoClass, irp, fk);
            });
        }

        public void OnSetInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileKey, d.FileObject, d.FileName);
            if (!string.IsNullOrEmpty(name))
            {
                EmitWithInfoClass(d.TimeStamp, "set_info", d.ProcessID, d.ThreadID, d.ProcessName,
                    name, d.InfoClass, d.IrpPtr, d.FileKey);
                return;
            }

            // Defer if name is empty—will be resolved by OnName or flushed by timer
            var ts = d.TimeStamp; var pid = d.ProcessID; var tid = d.ThreadID; var proc = d.ProcessName;
            var infoClass = d.InfoClass; var irp = d.IrpPtr; var fk = d.FileKey;
            EnqueuePending(d.FileKey, d.FileObject, resolvedName =>
            {
                LogEmptyFilenameIfNeeded(resolvedName, ts, "set_info", pid, tid, proc);
                EmitWithInfoClass(ts, "set_info", pid, tid, proc, resolvedName, infoClass, irp, fk);
            });
        }

        public void OnUnmapFile(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            EmitMapFile(d.TimeStamp, "unmap_file", d.ProcessID, d.ThreadID, d.ProcessName,
                Clean(d.FileName), d.ViewSize, d.FileKey);
        }

        // The FileIO OperationEnd event reports the final NTSTATUS of a completed IRP.
        // It carries only the IRP pointer, status, and completing thread — no FileKey or
        // FileName — so we cannot resolve a path here. We emit a lightweight "op_end" row
        // that consumers join back to the originating read/write/create by the `irp`
        // column; the result code is in `nt_status`.
        //
        // Volume gate: the start events already record every *attempted* operation, so an
        // op_end per completion would ~double the fs stream. We therefore emit op_end only
        // for *failed* operations (NTSTATUS error severity, 0xC0000000+) — the one thing
        // the start events can't tell you (success vs failure). Capturing op_end for every
        // completion (to also get success-path latency) is intentionally left as a future
        // opt-in flag rather than paying 2x volume by default.
        private const uint NtStatusErrorSeverity = 0xC0000000;

        public void OnOperationEnd(FileIOOpEndTraceData d)
        {
            if ((uint)d.NtStatus < NtStatusErrorSeverity) return;   // success / info / warning — skip
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            wm.Write(new FilesystemTrace(
                d.TimeStamp, "op_end", d.ProcessID, d.ThreadID, d.ProcessName, "", 0,
                null, null, null, null, null, null, null, d.IrpPtr, null, null, null,
                processCache.Get(d.ProcessID), d.NtStatus));
        }
    }
}
