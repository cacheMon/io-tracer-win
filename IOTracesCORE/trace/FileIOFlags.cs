using System;
using System.Collections.Generic;

namespace IOTracesCORE.trace
{
    /// <summary>
    /// Helper class to convert Windows file IO flags to human-readable strings
    /// </summary>
    static class FileIOFlags
    {
        /// <summary>
        /// CreateOptions flags for NtCreateFile/ZwCreateFile.
        /// Source: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntcreatefile
        /// Header: ntifs.h, wdm.h (Windows Driver Kit)
        /// </summary>
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

        /// <summary>
        /// ShareAccess flags for NtCreateFile/ZwCreateFile.
        /// These are bitmask flags that can be combined with OR (|).
        /// <para>
        /// Primary Source: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntcreatefile
        /// </para>
        /// <para>
        /// Win32 API equivalent: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilea (dwShareMode parameter)
        /// </para>
        /// <para>
        /// WDK Header: wdm.h - C:\Program Files (x86)\Windows Kits\10\Include\{version}\km\wdm.h
        /// </para>
        /// </summary>
        [Flags]
        public enum ShareAccessFlags : uint
        {
            FILE_SHARE_NONE = 0x00000000,   // Exclusive access, no sharing allowed
            FILE_SHARE_READ = 0x00000001,   // Allow other openers to read
            FILE_SHARE_WRITE = 0x00000002,  // Allow other openers to write
            FILE_SHARE_DELETE = 0x00000004  // Allow other openers to delete/rename
            // Combined: FILE_SHARE_READ | FILE_SHARE_WRITE = 0x03
            // Combined: FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE = 0x07
        }

        /// <summary>
        /// CreateDisposition values for NtCreateFile/ZwCreateFile.
        /// These are discrete enumerated values (0-5), NOT bitmask flags.
        /// <para>
        /// Primary Source: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntcreatefile
        /// </para>
        /// <para>
        /// Undocumented NtCreateFile: https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile
        /// </para>
        /// <para>
        /// WDK Header: wdm.h - C:\Program Files (x86)\Windows Kits\10\Include\{version}\km\wdm.h
        /// </para>
        /// </summary>
        /// <remarks>
        /// Established in Windows NT 3.1 (1993), values unchanged for backward compatibility.
        /// </remarks>
        public enum CreateDispositionValue : uint
        {
            FILE_SUPERSEDE = 0,     // 0x00 - If exists, delete and create new. If not exists, create.
            FILE_OPEN = 1,          // 0x01 - Open existing file only. Fail if not exists.
            FILE_CREATE = 2,        // 0x02 - Create new file only. Fail if already exists.
            FILE_OPEN_IF = 3,       // 0x03 - Open if exists, else create new.
            FILE_OVERWRITE = 4,     // 0x04 - Open existing and truncate. Fail if not exists.
            FILE_OVERWRITE_IF = 5   // 0x05 - Open and truncate if exists, else create new.
        }

        /// <summary>
        /// FILE_INFORMATION_CLASS values for ZwQueryInformationFile/ZwSetInformationFile.
        /// Source: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/ne-wdm-_file_information_class
        /// Header: wdm.h (Windows Driver Kit)
        /// </summary>
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
            FileShortNameInformation = 40,
            FileIoCompletionNotificationInformation = 41,
            FileIoStatusBlockRangeInformation = 42,
            FileIoPriorityHintInformation = 43,
            FileSfioReserveInformation = 44,
            FileSfioVolumeInformation = 45,
            FileHardLinkInformation = 46,
            FileProcessIdsUsingFileInformation = 47,
            FileNormalizedNameInformation = 48,
            FileNetworkPhysicalNameInformation = 49,
            FileIdGlobalTxDirectoryInformation = 50,
            FileIsRemoteDeviceInformation = 51,
            FileUnusedInformation = 52,
            FileNumaNodeInformation = 53,
            FileStandardLinkInformation = 54,
            FileRemoteProtocolInformation = 55,
            FileRenameInformationBypassAccessCheck = 56,
            FileLinkInformationBypassAccessCheck = 57,
            FileVolumeNameInformation = 58,
            FileIdInformation = 59,
            FileIdExtdDirectoryInformation = 60,
            FileReplaceCompletionInformation = 61,
            FileHardLinkFullIdInformation = 62,
            FileIdExtdBothDirectoryInformation = 63,
            FileDispositionInformationEx = 64,
            FileRenameInformationEx = 65,
            FileRenameInformationExBypassAccessCheck = 66,
            FileDesiredStorageClassInformation = 67,
            FileStatInformation = 68,
            FileMemoryPartitionInformation = 69,
            FileStatLxInformation = 70,
            FileCaseSensitiveInformation = 71,
            FileLinkInformationEx = 72,
            FileLinkInformationExBypassAccessCheck = 73,
            FileStorageReserveIdInformation = 74,
            FileCaseSensitiveInformationForceAccessCheck = 75,
            FileKnownFolderInformation = 76,
            FileStatBasicInformation = 77,
            FileId64ExtdDirectoryInformation = 78,
            FileId64ExtdBothDirectoryInformation = 79,
            FileIdAllExtdDirectoryInformation = 80,
            FileIdAllExtdBothDirectoryInformation = 81,
            FileStreamReservationInformation,
            FileMupProviderInfo,
            FileMaximumInformation
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

        // NOTE: DesiredAccess (ACCESS_MASK) is NOT available from Windows ETW FileIO events.
        // Neither the NT Kernel Logger (FileIOCreateTraceData) nor the Microsoft-Windows-Kernel-File
        // provider include DesiredAccess in their event schemas. The ETW FileIO/Create events only
        // provide: CreateOptions, ShareAccess, CreateDisposition, FileAttributes, FileName.
        //
        // The DesiredAccessFlags enum and FormatDesiredAccess method have been intentionally removed.
        // To capture DesiredAccess, a minifilter driver would be required (similar to how Process
        // Monitor captures it). See: https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/access-mask

        /// <summary>
        /// File Attribute constants.
        /// Source: https://learn.microsoft.com/en-us/windows/win32/fileio/file-attribute-constants
        /// Header: winnt.h
        /// </summary>
        [Flags]
        public enum FileAttributeFlags : uint
        {
            FILE_ATTRIBUTE_READONLY = 0x00000001,
            FILE_ATTRIBUTE_HIDDEN = 0x00000002,
            FILE_ATTRIBUTE_SYSTEM = 0x00000004,
            FILE_ATTRIBUTE_DIRECTORY = 0x00000010,
            FILE_ATTRIBUTE_ARCHIVE = 0x00000020,
            FILE_ATTRIBUTE_DEVICE = 0x00000040,
            FILE_ATTRIBUTE_NORMAL = 0x00000080,
            FILE_ATTRIBUTE_TEMPORARY = 0x00000100,
            FILE_ATTRIBUTE_SPARSE_FILE = 0x00000200,
            FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400,
            FILE_ATTRIBUTE_COMPRESSED = 0x00000800,
            FILE_ATTRIBUTE_OFFLINE = 0x00001000,
            FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = 0x00002000,
            FILE_ATTRIBUTE_ENCRYPTED = 0x00004000,
            FILE_ATTRIBUTE_INTEGRITY_STREAM = 0x00008000,
            FILE_ATTRIBUTE_VIRTUAL = 0x00010000,
            FILE_ATTRIBUTE_NO_SCRUB_DATA = 0x00020000,
            // Note: FILE_ATTRIBUTE_EA and FILE_ATTRIBUTE_RECALL_ON_OPEN share the same value (0x00040000).
            // FILE_ATTRIBUTE_EA is for internal use only (extended attributes present).
            // FILE_ATTRIBUTE_RECALL_ON_OPEN appears in directory enumeration (virtual/remote file).
            FILE_ATTRIBUTE_EA = 0x00040000,
            FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000,
            FILE_ATTRIBUTE_PINNED = 0x00080000,           // HSM: keep fully present locally
            FILE_ATTRIBUTE_UNPINNED = 0x00100000,         // HSM: do not keep fully present locally
            FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000  // Not fully present locally (sparse/virtualized)
        }

        public static string FormatFileAttributes(int flags)
        {
            if (flags == 0) return "NONE";

            var result = new List<string>();
            var enumFlags = (FileAttributeFlags)flags;

            foreach (FileAttributeFlags flag in Enum.GetValues(typeof(FileAttributeFlags)))
            {
                if (enumFlags.HasFlag(flag))
                {
                    result.Add(flag.ToString());
                }
            }

            return result.Count > 0 ? string.Join("|", result) : $"0x{flags:X8}";
        }

        /// <summary>
        /// IRP Flags for Read/Write operations from IRP.Flags field.
        /// <para>
        /// Flag names reference: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/ns-wdm-_irp
        /// (Note: The online documentation only lists flag names without hex values)
        /// </para>
        /// <para>
        /// Hex values source: WDK Header wdm.h (Windows Driver Kit).
        /// Online mirror: https://github.com/tpn/winsdk-10/blob/master/Include/10.0.10240.0/km/wdm.h
        /// These values are stable across Windows versions and match the kernel definitions.
        /// </para>
        /// <para>
        /// Note: IRP_PAGING_IO (0x02) and IRP_MOUNT_COMPLETION (0x02) share the same value (context-dependent).
        /// Similarly, IRP_INPUT_OPERATION (0x40) and IRP_SYNCHRONOUS_PAGING_IO (0x40) share the same value.
        /// </para>
        /// </summary>
        [Flags]
        public enum IoOperationFlags : uint
        {
            IRP_NOCACHE = 0x00000001,
            IRP_PAGING_IO = 0x00000002,
            IRP_MOUNT_COMPLETION = 0x00000002,
            IRP_SYNCHRONOUS_API = 0x00000004,
            IRP_ASSOCIATED_IRP = 0x00000008,
            IRP_BUFFERED_IO = 0x00000010,
            IRP_DEALLOCATE_BUFFER = 0x00000020,
            IRP_INPUT_OPERATION = 0x00000040,
            IRP_SYNCHRONOUS_PAGING_IO = 0x00000040,
            IRP_CREATE_OPERATION = 0x00000080,
            IRP_READ_OPERATION = 0x00000100,
            IRP_WRITE_OPERATION = 0x00000200,
            IRP_CLOSE_OPERATION = 0x00000400,
            IRP_DEFER_IO_COMPLETION = 0x00000800,
            IRP_OB_QUERY_NAME = 0x00001000,
            IRP_HOLD_DEVICE_QUEUE = 0x00002000,
            IRP_UM_DRIVER_INITIATED_IO = 0x00400000
        }

        public static string FormatIoFlags(int flags)
        {
            if (flags == 0) return "NONE";

            var result = new List<string>();
            var enumFlags = (IoOperationFlags)flags;

            foreach (IoOperationFlags flag in Enum.GetValues(typeof(IoOperationFlags)))
            {
                if (enumFlags.HasFlag(flag))
                {
                    result.Add(flag.ToString());
                }
            }

            return result.Count > 0 ? string.Join("|", result) : $"0x{flags:X8}";
        }
    }
}
