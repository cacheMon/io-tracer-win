using CsvHelper;
using CsvHelper.Configuration;
using IOTracesCORE.utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public string FormatAsCsv(bool is_anonymous)
        {
            buffer.GetStringBuilder().Clear();
            csv.WriteField(Ts.ToString("yyyy-MM-dd HH:mm:ss.fff"));
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

            // Write new fields (empty string if null)
            csv.WriteField(CreateOptions ?? "");
            csv.WriteField(ShareAccess ?? "");
            csv.WriteField(CreateDisposition ?? "");
            csv.WriteField(Offset?.ToString() ?? "");
            csv.WriteField(ViewSize?.ToString() ?? "");
            csv.WriteField(InfoClass ?? "");

            csv.NextRecord();
            return buffer.ToString();
        }

        // Original constructor for backward compatibility
        public FilesystemTrace(
                DateTime ts,
                string op,
                int pid,
                string comm,
                string filename,
                int size
            ) : this(ts, op, pid, comm, filename, size, null, null, null, null, null, null)
        {
        }

        // Extended constructor with all new fields
        public FilesystemTrace(
                DateTime ts,
                string op,
                int pid,
                string comm,
                string filename,
                int size,
                string? createOptions,
                string? shareAccess,
                string? createDisposition,
                long? offset,
                long? viewSize,
                string? infoClass
            )
        {
            Ts = ts;
            Op = op;
            Pid = pid;
            Comm = string.IsNullOrEmpty(comm) ? "" : comm;
            Filename = string.IsNullOrEmpty(filename) ? "" : filename;
            TraceSize = size;

            CreateOptions = createOptions;
            ShareAccess = shareAccess;
            CreateDisposition = createDisposition;
            Offset = offset;
            ViewSize = viewSize;
            InfoClass = infoClass;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n"
            };

            this.csv = new CsvWriter(buffer, config);
        }
    }

    /// <summary>
    /// Helper class to convert Windows file IO flags to human-readable strings
    /// </summary>
    static class FileIOFlags
    {
        // CreateOptions (NtCreateFile CreateOptions parameter)
        [Flags]
        public enum CreateOptionsFlags : uint
        {
            FILE_DIRECTORY_FILE = 0x00000001,
            FILE_WRITE_THROUGH = 0x00000002,
            FILE_SEQUENTIAL_ONLY = 0x00000004,
            FILE_NO_INTERMEDIATE_BUFFERING = 0x00000008,
            FILE_SYNCHRONOUS_IO_ALERT = 0x00000010,
            FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020,
            FILE_NON_DIRECTORY_FILE = 0x00000040,
            FILE_CREATE_TREE_CONNECTION = 0x00000080,
            FILE_COMPLETE_IF_OPLOCKED = 0x00000100,
            FILE_NO_EA_KNOWLEDGE = 0x00000200,
            FILE_OPEN_REMOTE_INSTANCE = 0x00000400,
            FILE_RANDOM_ACCESS = 0x00000800,
            FILE_DELETE_ON_CLOSE = 0x00001000,
            FILE_OPEN_BY_FILE_ID = 0x00002000,
            FILE_OPEN_FOR_BACKUP_INTENT = 0x00004000,
            FILE_NO_COMPRESSION = 0x00008000,
            FILE_OPEN_REQUIRING_OPLOCK = 0x00010000,
            FILE_DISALLOW_EXCLUSIVE = 0x00020000,
            FILE_SESSION_AWARE = 0x00040000,
            FILE_RESERVE_OPFILTER = 0x00100000,
            FILE_OPEN_REPARSE_POINT = 0x00200000,
            FILE_OPEN_NO_RECALL = 0x00400000,
            FILE_OPEN_FOR_FREE_SPACE_QUERY = 0x00800000
        }

        // ShareAccess flags
        [Flags]
        public enum ShareAccessFlags : uint
        {
            FILE_SHARE_NONE = 0x00000000,
            FILE_SHARE_READ = 0x00000001,
            FILE_SHARE_WRITE = 0x00000002,
            FILE_SHARE_DELETE = 0x00000004
        }

        // CreateDisposition values
        public enum CreateDispositionValue : uint
        {
            FILE_SUPERSEDE = 0,
            FILE_OPEN = 1,
            FILE_CREATE = 2,
            FILE_OPEN_IF = 3,
            FILE_OVERWRITE = 4,
            FILE_OVERWRITE_IF = 5
        }

        // FileInfoClass values (common ones for Query/SetInfo)
        public enum FileInfoClassValue : uint
        {
            FileDirectoryInformation = 1,
            FileFullDirectoryInformation = 2,
            FileBothDirectoryInformation = 3,
            FileBasicInformation = 4,
            FileStandardInformation = 5,
            FileInternalInformation = 6,
            FileEaInformation = 7,
            FileAccessInformation = 8,
            FileNameInformation = 9,
            FileRenameInformation = 10,
            FileLinkInformation = 11,
            FileNamesInformation = 12,
            FileDispositionInformation = 13,
            FilePositionInformation = 14,
            FileFullEaInformation = 15,
            FileModeInformation = 16,
            FileAlignmentInformation = 17,
            FileAllInformation = 18,
            FileAllocationInformation = 19,
            FileEndOfFileInformation = 20,
            FileAlternateNameInformation = 21,
            FileStreamInformation = 22,
            FilePipeInformation = 23,
            FilePipeLocalInformation = 24,
            FilePipeRemoteInformation = 25,
            FileMailslotQueryInformation = 26,
            FileMailslotSetInformation = 27,
            FileCompressionInformation = 28,
            FileObjectIdInformation = 29,
            FileCompletionInformation = 30,
            FileMoveClusterInformation = 31,
            FileQuotaInformation = 32,
            FileReparsePointInformation = 33,
            FileNetworkOpenInformation = 34,
            FileAttributeTagInformation = 35,
            FileTrackingInformation = 36,
            FileIdBothDirectoryInformation = 37,
            FileIdFullDirectoryInformation = 38,
            FileValidDataLengthInformation = 39,
            FileShortNameInformation = 40
        }

        public static string FormatCreateOptions(int flags)
        {
            if (flags == 0) return "NONE";

            var result = new List<string>();
            var enumFlags = (CreateOptionsFlags)flags;

            foreach (CreateOptionsFlags flag in Enum.GetValues(typeof(CreateOptionsFlags)))
            {
                if (enumFlags.HasFlag(flag))
                {
                    result.Add(flag.ToString());
                }
            }

            return result.Count > 0 ? string.Join("|", result) : $"0x{flags:X8}";
        }

        public static string FormatShareAccess(int flags)
        {
            if (flags == 0) return "FILE_SHARE_NONE";

            var result = new List<string>();
            var enumFlags = (ShareAccessFlags)flags;

            foreach (ShareAccessFlags flag in Enum.GetValues(typeof(ShareAccessFlags)))
            {
                if (flag != ShareAccessFlags.FILE_SHARE_NONE && enumFlags.HasFlag(flag))
                {
                    result.Add(flag.ToString());
                }
            }

            return result.Count > 0 ? string.Join("|", result) : $"0x{flags:X8}";
        }

        public static string FormatCreateDisposition(int value)
        {
            if (Enum.IsDefined(typeof(CreateDispositionValue), (uint)value))
            {
                return ((CreateDispositionValue)value).ToString();
            }
            return $"0x{value:X8}";
        }

        public static string FormatInfoClass(int value)
        {
            if (Enum.IsDefined(typeof(FileInfoClassValue), (uint)value))
            {
                return ((FileInfoClassValue)value).ToString();
            }
            return $"0x{value:X8}";
        }
    }
}
