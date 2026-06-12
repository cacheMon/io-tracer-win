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

        /// <summary>
        /// FSCTL (File System Control) codes passed in the InfoClass field of fs_control events.
        /// Source: winioctl.h (Windows SDK).
        /// Formula: CTL_CODE(FILE_DEVICE_FILE_SYSTEM=0x9, Function, Method, Access)
        ///          = (0x9 &lt;&lt; 16) | (Access &lt;&lt; 14) | (Function &lt;&lt; 2) | Method
        /// Method:  METHOD_BUFFERED=0, METHOD_IN_DIRECT=1, METHOD_OUT_DIRECT=2, METHOD_NEITHER=3
        /// Access:  FILE_ANY_ACCESS=0, FILE_READ_ACCESS=1, FILE_WRITE_ACCESS=2
        /// </summary>
        public enum FsctlCode : uint
        {
            FSCTL_REQUEST_OPLOCK_LEVEL_1    = 0x00090000,
            FSCTL_REQUEST_OPLOCK_LEVEL_2    = 0x00090004,
            FSCTL_REQUEST_BATCH_OPLOCK      = 0x00090008,
            FSCTL_OPLOCK_BREAK_ACKNOWLEDGE  = 0x0009000C,
            FSCTL_OPBATCH_ACK_CLOSE_PENDING = 0x00090010,
            FSCTL_OPLOCK_BREAK_NOTIFY       = 0x00090014,
            FSCTL_LOCK_VOLUME               = 0x00090018,
            FSCTL_UNLOCK_VOLUME             = 0x0009001C,
            FSCTL_DISMOUNT_VOLUME           = 0x00090020,
            FSCTL_IS_VOLUME_MOUNTED         = 0x00090028,
            FSCTL_IS_PATHNAME_VALID         = 0x0009002C,
            FSCTL_MARK_VOLUME_DIRTY         = 0x00090030,
            FSCTL_QUERY_RETRIEVAL_POINTERS  = 0x0009003B, // METHOD_NEITHER
            FSCTL_GET_COMPRESSION           = 0x0009003C,
            FSCTL_SET_COMPRESSION           = 0x0009C040, // READ|WRITE access
            FSCTL_OPLOCK_BREAK_ACK_NO_2     = 0x00090050,
            FSCTL_INVALIDATE_VOLUMES        = 0x00090054,
            FSCTL_QUERY_FAT_BPB             = 0x00090058,
            FSCTL_REQUEST_FILTER_OPLOCK     = 0x0009005C,
            FSCTL_FILESYSTEM_GET_STATISTICS = 0x00090060,
            FSCTL_GET_NTFS_VOLUME_DATA      = 0x00090064,
            FSCTL_GET_NTFS_FILE_RECORD      = 0x00090068,
            FSCTL_GET_VOLUME_BITMAP         = 0x0009006F, // METHOD_NEITHER
            FSCTL_GET_RETRIEVAL_POINTERS    = 0x00090073, // METHOD_NEITHER
            FSCTL_MOVE_FILE                 = 0x00090074,
            FSCTL_IS_VOLUME_DIRTY           = 0x00090078,
            FSCTL_ALLOW_EXTENDED_DASD_IO    = 0x00090083, // METHOD_NEITHER
            FSCTL_FIND_FILES_BY_SID         = 0x0009008F, // METHOD_NEITHER
            FSCTL_SET_OBJECT_ID             = 0x00090098,
            FSCTL_GET_OBJECT_ID             = 0x0009009C,
            FSCTL_DELETE_OBJECT_ID          = 0x000900A0,
            FSCTL_SET_REPARSE_POINT         = 0x000900A4,
            FSCTL_GET_REPARSE_POINT         = 0x000900A8,
            FSCTL_DELETE_REPARSE_POINT      = 0x000900AC,
            FSCTL_ENUM_USN_DATA             = 0x000900B3, // METHOD_NEITHER
            FSCTL_SECURITY_ID_CHECK         = 0x000940B7, // METHOD_NEITHER, READ_ACCESS
            FSCTL_READ_USN_JOURNAL          = 0x000900BB, // METHOD_NEITHER
            FSCTL_SET_OBJECT_ID_EXTENDED    = 0x000900BC,
            FSCTL_CREATE_OR_GET_OBJECT_ID   = 0x000900C0,
            FSCTL_SET_SPARSE                = 0x000900C4,
            FSCTL_SET_ZERO_DATA             = 0x000980C8, // WRITE_ACCESS
            FSCTL_QUERY_ALLOCATED_RANGES    = 0x000940CF, // METHOD_NEITHER, READ_ACCESS
            FSCTL_ENABLE_UPGRADE            = 0x000900D0,
            FSCTL_SET_ENCRYPTION            = 0x000900D7, // METHOD_NEITHER
            FSCTL_ENCRYPTION_FSCTL_IO       = 0x000900DB, // METHOD_NEITHER
            FSCTL_WRITE_RAW_ENCRYPTED       = 0x000900DF, // METHOD_NEITHER
            FSCTL_READ_RAW_ENCRYPTED        = 0x000900E3, // METHOD_NEITHER
            FSCTL_CREATE_USN_JOURNAL        = 0x000900E7, // METHOD_NEITHER
            FSCTL_READ_FILE_USN_DATA        = 0x000900EB, // METHOD_NEITHER
            FSCTL_WRITE_USN_CLOSE_RECORD    = 0x000900EF, // METHOD_NEITHER
            FSCTL_EXTEND_VOLUME             = 0x000900F0,
            FSCTL_QUERY_USN_JOURNAL         = 0x000900F4,
            FSCTL_DELETE_USN_JOURNAL        = 0x000900F8,
            FSCTL_MARK_HANDLE               = 0x000900FC,
            FSCTL_SIS_COPYFILE              = 0x00090100,
            FSCTL_REQUEST_OPLOCK            = 0x00090240,
            FSCTL_CSV_TUNNEL_REQUEST        = 0x00090244,
            FSCTL_IS_CSV_FILE               = 0x00090248,
            FSCTL_QUERY_FILE_SYSTEM_RECOGNITION = 0x00090250,
            FSCTL_GET_INTEGRITY_INFORMATION = 0x0009027C,
            FSCTL_SET_INTEGRITY_INFORMATION = 0x0009C280, // READ|WRITE access
            FSCTL_DUPLICATE_EXTENTS_TO_FILE = 0x00098344, // WRITE_ACCESS
        }

        public static string FormatFsctlCode(int code)
        {
            if (Enum.IsDefined(typeof(FsctlCode), (uint)code))
                return ((FsctlCode)(uint)code).ToString();
            return $"0x{code:X8}";
        }

        public static string FormatCreateOptions(int flags)
        {
            if (flags == 0) return "";

            var result = new List<string>();
            var enumFlags = (CreateOptionsFlags)flags;
            uint matched = 0;

            foreach (CreateOptionsFlags flag in Enum.GetValues(typeof(CreateOptionsFlags)))
            {
                if (flag != 0 && enumFlags.HasFlag(flag))
                {
                    result.Add(flag.ToString());
                    matched |= (uint)flag;
                }
            }

            uint remaining = (uint)flags & ~matched;
            if (remaining != 0)
                result.Add($"0x{remaining:X8}");

            return string.Join("|", result);
        }

        public static string FormatShareAccess(int flags)
        {
            if (flags == 0) return "";

            var result = new List<string>();
            var enumFlags = (ShareAccessFlags)flags;
            uint matched = 0;

            foreach (ShareAccessFlags flag in Enum.GetValues(typeof(ShareAccessFlags)))
            {
                if (flag != ShareAccessFlags.FILE_SHARE_NONE && enumFlags.HasFlag(flag))
                {
                    result.Add(flag.ToString());
                    matched |= (uint)flag;
                }
            }

            uint remaining = (uint)flags & ~matched;
            if (remaining != 0)
                result.Add($"0x{remaining:X8}");

            return string.Join("|", result);
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
            if (flags == 0) return "";

            var result = new List<string>();
            var enumFlags = (FileAttributeFlags)flags;
            uint matched = 0;

            foreach (FileAttributeFlags flag in Enum.GetValues(typeof(FileAttributeFlags)))
            {
                if (flag != 0 && enumFlags.HasFlag(flag))
                {
                    result.Add(flag.ToString());
                    matched |= (uint)flag;
                }
            }

            uint remaining = (uint)flags & ~matched;
            if (remaining != 0)
                result.Add($"0x{remaining:X8}");

            return string.Join("|", result);
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
            if (flags == 0) return "";

            var result = new List<string>();
            var enumFlags = (IoOperationFlags)flags;
            uint matched = 0;

            foreach (IoOperationFlags flag in Enum.GetValues(typeof(IoOperationFlags)))
            {
                if (flag != 0 && enumFlags.HasFlag(flag))
                {
                    result.Add(flag.ToString());
                    matched |= (uint)flag;
                }
            }

            uint remaining = (uint)flags & ~matched;
            if (remaining != 0)
                result.Add($"0x{remaining:X8}");

            return string.Join("|", result);
        }
    }
}
