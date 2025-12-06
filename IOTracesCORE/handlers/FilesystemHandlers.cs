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

        private static void TrackStart(
        ulong irpPtr,
        double timeStamp,
        string type,
        string fileName,
        ulong fileObject = 0,
        long size = 0)
        {
            if (irpPtr == 0) return;
            lock (_activeRequests)
            {
                if (!_activeRequests.ContainsKey(irpPtr))
                    {
                        _activeRequests[irpPtr] = timeStamp;
                        _requestTypes[irpPtr] = type;
                        _requestFileObjects[irpPtr] = fileObject;
                        _requestNames[irpPtr] = string.IsNullOrEmpty(fileName) ? "" : fileName.Trim();
                        _requestSizes[irpPtr] = size;
                    }
                }
        }

        public void OnUniversalEnd(TraceEvent data)
        {
            ulong irpPtr = Converter.SafeToUInt64(data.PayloadByName("IrpPtr"));
            long sizeFromEvent = Converter.SafeToInt64(data.PayloadByName("IoSize"));
            ulong fileObjectFromEvent = Converter.SafeToUInt64(data.PayloadByName("FileObject"));
            string fileNameFromEvent = Clean(Converter.SafeToString(data.PayloadByName("FileName")));

            double startTime;
            string opType = "UNKNOWN";
            ulong storedFileObject = 0;
            string storedName = "";
            long storedSize = 0;

            lock (_activeRequests)
            {
                if (!_activeRequests.TryGetValue(irpPtr, out startTime))
                    return;

                if (_requestTypes.TryGetValue(irpPtr, out var t)) opType = t;
                if (_requestFileObjects.TryGetValue(irpPtr, out var fo)) storedFileObject = fo;
                if (_requestNames.TryGetValue(irpPtr, out var rn)) storedName = rn;
                if (_requestSizes.TryGetValue(irpPtr, out var rs)) storedSize = rs;
            }

            double latency = data.TimeStampRelativeMSec - startTime;
            if (latency <= 0.1) return; 

            string fileNameResolved = fileNameFromEvent;
            if (string.IsNullOrEmpty(fileNameResolved))
            {
                if (!string.IsNullOrEmpty(storedName))
                    fileNameResolved = storedName;
                else if (storedFileObject != 0 && nameByObj.TryGetValue(storedFileObject, out var cached1))
                    fileNameResolved = cached1;
                else if (fileObjectFromEvent != 0 && nameByObj.TryGetValue(fileObjectFromEvent, out var cached2))
                    fileNameResolved = cached2;
                else
                    fileNameResolved = "";
            }

            long finalSizeLong = sizeFromEvent != 0 ? sizeFromEvent : storedSize;
            int size = finalSizeLong > int.MaxValue ? int.MaxValue : (int)finalSizeLong;



            lock (_activeRequests)
            {
                _activeRequests.Remove(irpPtr);
                _requestTypes.Remove(irpPtr);
                _requestFileObjects.Remove(irpPtr);
                _requestNames.Remove(irpPtr);
                _requestSizes.Remove(irpPtr);
            }

            Emit(data.TimeStamp, $"{opType.ToLower()}", data.ProcessID, data.ProcessName, fileNameResolved, size, latency);
        }



        private string Resolve(ulong fileObject, string eventName)
        {
            var n = Clean(eventName);
            if (!string.IsNullOrEmpty(n)) return n;
            return nameByObj.TryGetValue(fileObject, out var cached) ? cached : "";
        }

        private void Emit(DateTime ts, string op, int pid, string proc, string name, int size, double latency = 0) =>
            wm.Write(new FilesystemTrace(ts, op, pid, proc, name, size,latency));


        public void OnRead(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            //Emit(d.TimeStamp, "read", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.IoSize);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "read", d.FileName, d.FileObject, d.IoSize);
        }

        public void OnWrite(FileIOReadWriteTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            //Emit(d.TimeStamp, "write", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), d.IoSize);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "write", d.FileName, d.FileObject, d.IoSize);
        }

        public void OnFlush(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            //Emit(d.TimeStamp, "flush", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "flush", d.FileName, d.FileObject);
        }

        public void OnQuery(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            //Emit(d.TimeStamp, "query", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "query", d.FileName, d.FileObject);
        }

        public void OnDirEnum(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            //Emit(d.TimeStamp, "dir_enum", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "dir_enum", d.FileName, d.FileObject);
        }

        public void OnCreate(FileIOCreateTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileObject] = name;
            //Emit(d.TimeStamp, "create", d.ProcessID, d.ProcessName, name, 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "create", d.FileName, d.FileObject);
        }

        public void OnFileCreate(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "file_create", d.ProcessID, d.ProcessName, name, 0);
            //TrackStart((ulong)d.PayloadByName("IrpPtr"), d.TimeStampRelativeMSec, "FILE_CREATE", d.FileName);
        }

        public void OnDelete(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileObject, d.FileName);
            //Emit(d.TimeStamp, "delete", d.ProcessID, d.ProcessName, name, 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "delete", d.FileName, d.FileObject);
        }

        public void OnFileDelete(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "file_delete", d.ProcessID, d.ProcessName, name, 0);
            //TrackStart((ulong)d.PayloadByName("IrpPtr"), d.TimeStampRelativeMSec, "FILE_DELETE", d.FileName);
        }

        public void OnClose(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Resolve(d.FileObject, d.FileName);
            //Emit(d.TimeStamp, "close", d.ProcessID, d.ProcessName, name, 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "close", d.FileName, d.FileObject);
            nameByObj.TryRemove(d.FileObject, out _); // drop mapping after close
        }

        public void OnRename(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            if (!string.IsNullOrEmpty(name)) nameByObj[d.FileObject] = name;
            //Emit(d.TimeStamp, "rename", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "rename", d.FileName, d.FileObject);
        }

        public void OnCleanup(FileIOSimpleOpTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            //Emit(d.TimeStamp, "cleanup", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "cleanup", d.FileName, d.FileObject);
        }

        public void OnDirNotify(FileIODirEnumTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            //Emit(d.TimeStamp, "dir_notify", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "dir_notify", d.FileName, d.FileObject);
        }

        public void OnFileRundown(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "file_rundown", d.ProcessID, d.ProcessName, name, 0);
            //TrackStart((ulong)d.PayloadByName("IrpPtr"), d.TimeStampRelativeMSec, "FILE_RUNDOWN", d.FileName);
        }

        public void OnFSControl(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            //Emit(d.TimeStamp, "fs_control", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "fs_control", d.FileName, d.FileObject);
        }

        public void OnMapFile(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "map_file", d.ProcessID, d.ProcessName, name, 0);
            //TrackStart((ulong)d.PayloadByName("IrpPtr"), d.TimeStampRelativeMSec, "map_file", d.FileName);
        }

        public void OnMapFileDCStart(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "map_file_dc_start", d.ProcessID, d.ProcessName, name, 0);
            //TrackStart((ulong)d.PayloadByName("IrpPtr"), d.TimeStampRelativeMSec, "map_file_dc_start", d.FileName);
        }

        public void OnMapFileDCStop(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "map_file_dc_stop", d.ProcessID, d.ProcessName, name, 0);
            //TrackStart((ulong)d.PayloadByName("IrpPtr"), d.TimeStampRelativeMSec, "map_file_dc_stop", d.FileName);
        }

        public void OnName(FileIONameTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "name", d.ProcessID, d.ProcessName, name, 0);
            //TrackStart((ulong)d.PayloadByName("IrpPtr"), d.TimeStampRelativeMSec, "name", d.FileName);
        }

        public void OnOperationEnd(FileIOOpEndTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = "";
            //Emit(d.TimeStamp, "operation_end", d.ProcessID, d.ProcessName, name, 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "operation_end", name);
        }

        public void OnQueryInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            //Emit(d.TimeStamp, "query_info", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "query_info", d.FileName, d.FileObject);
        }

        public void OnSetInfo(FileIOInfoTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            //Emit(d.TimeStamp, "set_info", d.ProcessID, d.ProcessName, Resolve(d.FileObject, d.FileName), 0);
            TrackStart(d.IrpPtr, d.TimeStampRelativeMSec, "set_info", d.FileName, d.FileObject);
        }

        public void OnUnmapFile(MapFileTraceData d)
        {
            if (!ProcessFilter.ShouldTrace(d.ProcessID, d.ProcessName)) return;
            var name = Clean(d.FileName);
            Emit(d.TimeStamp, "unmap_file", d.ProcessID, d.ProcessName, name, 0);
            //TrackStart((ulong)d.PayloadByName("IrpPtr"), d.TimeStampRelativeMSec, "unmap_file", d.FileName);
        }
    }
}
