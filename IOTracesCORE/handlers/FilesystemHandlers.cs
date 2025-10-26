using IOTracesCORE;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.handlers
{
    class FilesystemHandlers
    {
        private WriterManager wm;

        public FilesystemHandlers(WriterManager old_wm)
        {
            wm = old_wm;
        }

        public void OnFileRead(FileIOReadWriteTraceData data)
        {


            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }


            DateTime ts = data.TimeStamp;
            string operation_type = "read";
            int pid = data.ProcessID;
            string process_name = data.ProcessName;
            string filename = data.FileName;
            int size = data.IoSize;

            FilesystemTrace fs_trace = new FilesystemTrace(ts, operation_type, pid, process_name, filename, size);

            wm.Write(fs_trace);
        }
        
        public void OnFileWrite(FileIOReadWriteTraceData data)
        {


            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }


            DateTime ts = data.TimeStamp;
            string operation_type = "write";
            int pid = data.ProcessID;
            string process_name = data.ProcessName;
            string filename = data.FileName;
            int size = data.IoSize;

            FilesystemTrace fs_trace = new FilesystemTrace(ts, operation_type, pid, process_name, filename, size);

            wm.Write(fs_trace);
        }

        public void OnFileClose(FileIOSimpleOpTraceData data)
        {


            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }


            DateTime ts = data.TimeStamp;
            string operation_type = "write";
            int pid = data.ProcessID;
            string process_name = data.ProcessName;
            string filename = data.FileName;
            int size = 0;

            FilesystemTrace fs_trace = new FilesystemTrace(ts, operation_type, pid, process_name, filename, size);

            wm.Write(fs_trace);
        }

        public void OnFileCreate(FileIOCreateTraceData data)
        {


            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }


            DateTime ts = data.TimeStamp;
            string operation_type = "write";
            int pid = data.ProcessID;
            string process_name = data.ProcessName;
            string filename = data.FileName;
            int size = 0;

            FilesystemTrace fs_trace = new FilesystemTrace(ts, operation_type, pid, process_name, filename, size);

            wm.Write(fs_trace);
        }

        public void OnFileDelete(FileIOInfoTraceData data)
        {


            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }


            DateTime ts = data.TimeStamp;
            string operation_type = "write";
            int pid = data.ProcessID;
            string process_name = data.ProcessName;
            string filename = data.FileName;
            int size = 0;

            FilesystemTrace fs_trace = new FilesystemTrace(ts, operation_type, pid, process_name, filename, size);

            wm.Write(fs_trace);
        }

        public void OnFileFlush(FileIOSimpleOpTraceData data)
        {


            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }


            DateTime ts = data.TimeStamp;
            string operation_type = "write";
            int pid = data.ProcessID;
            string process_name = data.ProcessName;
            string filename = data.FileName;
            int size = 0;

            FilesystemTrace fs_trace = new FilesystemTrace(ts, operation_type, pid, process_name, filename, size);

            wm.Write(fs_trace);
        }
    }
}
