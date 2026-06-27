using CsvHelper;
using CsvHelper.Configuration;
using IOTracesCORE.utils;
using System;
using System.Collections.Generic;
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
        public int? NtStatus { get; set; }              // For op_end - final NTSTATUS of the completed IRP

        // Row buffer/writer are provided per-thread by CsvRowBuffer (no per-row allocation).
        // Per-row CsvWriter construction was the dominant fs-consumer cost: ~24 KB allocated PER
        // row (~96% of per-event cost, ~23% of wall-clock in GC) — the fs-truncation root cause.

        // Maps the native ETW filesystem op names onto the cross-OS canonical
        // vocabulary shared with the Linux tracer. Ops not listed (read, write,
        // close, cleanup, delete, rename, dir_notify, ...) are already canonical.
        private static readonly Dictionary<string, string> CanonicalOps = new()
        {
            ["create"] = "open",
            ["flush"] = "fsync",
            ["query_info"] = "getattr",
            ["set_info"] = "setattr",
            ["dir_enum"] = "readdir",
            ["map_file"] = "mmap",
            ["fs_control"] = "fsctl",
        };

        public string FormatAsCsv(bool is_anonymous)
        {
            var csv = CsvRowBuffer.Begin(out var buffer);

            string op = Op != null && CanonicalOps.TryGetValue(Op, out var canon) ? canon : (Op ?? "");
            // bytes_completed: Windows reports only the requested transfer size,
            // so for read/write we mirror it here to line up with the Linux
            // bytes_completed column; empty for non-I/O ops.
            string bytesCompleted = (op == "read" || op == "write") ? TraceSize.ToString() : "";

            // --- cross-OS prefix --- #
            // NOTE: the Linux tracer's shared prefix has `inode` and `device` after
            // bytes_completed; they are omitted here because Windows ETW FileIO events carry
            // neither (the kernel file identity is emitted as `file_key`, and the volume is in
            // `filename`), so on Windows they were always empty. This intentionally diverges
            // the Windows fs column layout from Linux — consumers must read columns by name
            // (the manifest is the source of truth), not by fixed position.
            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
            csv.WriteField(op);
            csv.WriteField(Pid);
            csv.WriteField(ThreadId);
            csv.WriteField(Comm);
            if (is_anonymous)
            {
                try
                {
                    string root = Path.GetPathRoot(Filename) ?? "/";
                    // keepLevels:1 — keep only the first segment below the volume root
                    // (e.g. "Users", "Windows"). keepLevels:2 leaked the *username*
                    // segment for C:\Users\<name>\... in anonymous mode.
                    csv.WriteField(PathHasher.HashFilePath(Filename, root, true, 16, 1));
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
            csv.WriteField(Offset?.ToString() ?? "");
            csv.WriteField(bytesCompleted);
            csv.WriteField(IoFlags ?? "");

            // --- Windows-only extras (columns 13+) --- #
            csv.WriteField(CreateOptions ?? "");
            csv.WriteField(ShareAccess ?? "");
            csv.WriteField(CreateDisposition ?? "");
            csv.WriteField(ViewSize?.ToString() ?? "");
            csv.WriteField(FileInfoClass ?? "");
            csv.WriteField(FsctlCode ?? "");
            csv.WriteField(Irp.HasValue ? string.Format("0x{0:X}", Irp.Value) : "");
            // Kernel pointer — emit as hex (matching Irp) instead of a raw 2^64 decimal,
            // and empty when absent rather than "0".
            csv.WriteField(FileKey.HasValue && FileKey.Value != 0 ? string.Format("0x{0:X}", FileKey.Value) : "");
            csv.WriteField(FileAttributes ?? "");
            // Command lines routinely embed usernames, paths, URLs and secrets. In
            // anonymous mode the filename is hashed above, so the raw command line must
            // not leak through this column. Hash it stably (empty stays empty) so
            // identical command lines still group while the content is non-reversible.
            csv.WriteField(is_anonymous && !string.IsNullOrEmpty(CommandLine)
                ? PathHasher.Hash(CommandLine, 16)
                : CommandLine);
            // NTSTATUS of the completed IRP (op_end rows only; empty otherwise).
            // Hex so it lines up with the documented NTSTATUS constants (e.g. 0xC0000034).
            csv.WriteField(NtStatus.HasValue ? string.Format("0x{0:X8}", (uint)NtStatus.Value) : "");

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
                string? commandLine,
                int? ntStatus = null
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
            NtStatus = ntStatus;
            // No per-row CsvWriter/StringWriter allocation here anymore — formatting reuses a
            // thread-local writer in FormatAsCsv. The row object now holds only its data fields.
        }
    }
}
