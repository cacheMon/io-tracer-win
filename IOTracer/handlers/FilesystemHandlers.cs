using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracer
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
