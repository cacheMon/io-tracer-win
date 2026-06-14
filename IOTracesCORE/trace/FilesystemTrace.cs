using CsvHelper;
using CsvHelper.Configuration;
using IOTracesCORE.utils;
using System;
using System.Diagnostics;
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
        public string? FileInfoClass { get; set; }       // For query_info/set_info - FILE_INFORMATION_CLASS value
        public string? FsctlCode { get; set; }           // For fs_control - FSCTL control code

        // Additional fields for better IO event differentiation
        public int ThreadId { get; set; }               // Thread ID for concurrency analysis
        public ulong? Irp { get; set; }                 // IRP pointer for correlation
        public ulong? FileKey { get; set; }             // Unique file identifier
        // NOTE: DesiredAccess is NOT available from Windows ETW FileIO events.
        // Neither the NT Kernel Logger (FileIOCreateTraceData) nor Microsoft-Windows-Kernel-File
        // include DesiredAccess in their event schemas. To capture this field, a minifilter
        // driver would be required (similar to how Process Monitor captures it).
        public string? FileAttributes { get; set; }     // For Create - file attributes
        public string? IoFlags { get; set; }            // For Read/Write - IO operation flags
        public string CommandLine { get; set; }

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
            csv.WriteField(FileInfoClass ?? "");
            csv.WriteField(FsctlCode ?? "");

            // Write additional IO differentiation fields
            csv.WriteField(ThreadId);
            csv.WriteField(Irp.HasValue ? string.Format("0x{0:X}", Irp.Value) : "");
            // Kernel pointer — emit as hex (matching Irp) instead of a raw 2^64 decimal,
            // and empty when absent rather than "0".
            csv.WriteField(FileKey.HasValue && FileKey.Value != 0 ? string.Format("0x{0:X}", FileKey.Value) : "");
            csv.WriteField(FileAttributes ?? "");
            csv.WriteField(IoFlags ?? "");
            csv.WriteField(CommandLine);

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
            ) : this(ts, op, pid, threadId, comm, filename, size, null, null, null, null, null, null, null, null, null, null, null, null)
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
                string? fileInfoClass,
                string? fsctlCode,
                ulong? irp,
                ulong? fileKey,
                string? fileAttributes,
                string? ioFlags,
                string? commandLine
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
            FileInfoClass = fileInfoClass;
            FsctlCode = fsctlCode;

            Irp = irp;
            FileKey = fileKey;
            FileAttributes = fileAttributes;
            IoFlags = ioFlags;
            CommandLine = string.IsNullOrEmpty(commandLine) ? "" : commandLine;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n"
            };

            this.csv = new CsvWriter(buffer, config);
        }
    }
}
