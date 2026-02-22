using CsvHelper;
using CsvHelper.Configuration;
using IOTracesCORE.utils;
using System;
using System.Globalization;
using System.IO;

namespace IOTracesCORE.trace
{
    class FilesystemTrace
    {
        public DateTime Ts { get; set; }
        public string Op { get; set; }
        public int Pid { get; set; }
        public string Comm { get; set; }
        public string Filename { get; set; }
        public int TraceSize { get; set; }

        // New fields for enhanced IO tracking
        public string? CreateOptions { get; set; }      // For Create operations - human readable flags
        public string? ShareAccess { get; set; }        // For Create operations - sharing flags
        public string? CreateDisposition { get; set; }  // For Create operations - open/create behavior
        public long? Offset { get; set; }               // For Read/Write operations
        public long? ViewSize { get; set; }             // For MapFile operations
        public string? InfoClass { get; set; }          // For Query/SetInfo operations

        // Additional fields for better IO event differentiation
        public int ThreadId { get; set; }               // Thread ID for concurrency analysis
        public ulong? IrpPtr { get; set; }              // IRP pointer for correlation
        public ulong? FileKey { get; set; }             // Unique file identifier
        // NOTE: DesiredAccess is NOT available from Windows ETW FileIO events.
        // Neither the NT Kernel Logger (FileIOCreateTraceData) nor Microsoft-Windows-Kernel-File
        // include DesiredAccess in their event schemas. To capture this field, a minifilter
        // driver would be required (similar to how Process Monitor captures it).
        public string? FileAttributes { get; set; }     // For Create - file attributes
        public string? IoFlags { get; set; }            // For Read/Write - IO operation flags

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public string FormatAsCsv(bool is_anonymous)
        {
            buffer.GetStringBuilder().Clear();
            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
            csv.WriteField(Op);
            csv.WriteField(Pid);
            csv.WriteField(Comm);
            if (is_anonymous)
            {
                try
                {
                    string root = Path.GetPathRoot(Filename) ?? "/";
                    csv.WriteField(PathHasher.HashFilePath(Filename, root, true, 16));
                }
                catch (Exception)
                {
                    csv.WriteField(Filename);
                }
            }
            else
            {
                csv.WriteField(Filename);
            }
            csv.WriteField(TraceSize);

            // Write extended fields (empty string if null)
            csv.WriteField(CreateOptions ?? "");
            csv.WriteField(ShareAccess ?? "");
            csv.WriteField(CreateDisposition ?? "");
            csv.WriteField(Offset?.ToString() ?? "");
            csv.WriteField(ViewSize?.ToString() ?? "");
            csv.WriteField(InfoClass ?? "");

            // Write additional IO differentiation fields
            csv.WriteField(ThreadId);
            csv.WriteField(IrpPtr?.ToString() ?? "");
            csv.WriteField(FileKey?.ToString() ?? "");
            csv.WriteField(FileAttributes ?? "");
            csv.WriteField(IoFlags ?? "");

            csv.NextRecord();
            return buffer.ToString();
        }

        // Original constructor for backward compatibility
        public FilesystemTrace(
                DateTime ts,
                string op,
                int pid,
                int threadId,
                string comm,
                string filename,
                int size
            ) : this(ts, op, pid, threadId, comm, filename, size, null, null, null, null, null, null, null, null, null, null)
        {
        }

        // Extended constructor with all fields
        public FilesystemTrace(
                DateTime ts,
                string op,
                int pid,
                int threadId,
                string comm,
                string filename,
                int size,
                string? createOptions,
                string? shareAccess,
                string? createDisposition,
                long? offset,
                long? viewSize,
                string? infoClass,
                ulong? irpPtr,
                ulong? fileKey,
                string? fileAttributes,
                string? ioFlags
            )
        {
            Ts = ts;
            Op = op;
            Pid = pid;
            ThreadId = threadId;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Filename = string.IsNullOrEmpty(filename) ? "" : filename;
            TraceSize = size;

            CreateOptions = createOptions;
            ShareAccess = shareAccess;
            CreateDisposition = createDisposition;
            Offset = offset;
            ViewSize = viewSize;
            InfoClass = infoClass;

            IrpPtr = irpPtr;
            FileKey = fileKey;
            FileAttributes = fileAttributes;
            IoFlags = ioFlags;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n"
            };

            this.csv = new CsvWriter(buffer, config);
        }
    }
}
