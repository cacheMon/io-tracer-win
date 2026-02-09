# Filesystem Trace (`FILESYSTEM`)

Captures detailed file system operations.

**CSV Header:**
`Ts,Op,Pid,Comm,Filename,TraceSize,CreateOptions,ShareAccess,CreateDisposition,Offset,ViewSize,InfoClass,ThreadId,IrpPtr,FileKey,FileAttributes,IoFlags`

**Fields:**

| Field               | Description                                   | Notes                                                                                |
| :------------------ | :-------------------------------------------- | :----------------------------------------------------------------------------------- |
| `Ts`                | Timestamp (UTC) of the event                  | Format: `yyyy-MM-dd HH:mm:ss.fff`                                                    |
| `Op`                | Operation name                                | See [Operation Values](#operation-values-op) below                                   |
| `Pid`               | Process ID initiating the operation           |                                                                                      |
| `Comm`              | Command/Process name                          |                                                                                      |
| `Filename`          | Full path of the file involved                | Hashed if anonymous mode is enabled                                                  |
| `TraceSize`         | Size of the data transfer (bytes)             |                                                                                      |
| `CreateOptions`     | Flags specified during file creation          | See [CreateOptions Values](#createoptions-values) below. _Only for `create` ops_     |
| `ShareAccess`       | File sharing mode flags                       | See [ShareAccess Values](#shareaccess-values) below. _Only for `create` ops_         |
| `CreateDisposition` | Action to take on file creation               | See [CreateDisposition Values](#createdisposition-values) below. _Only for `create`_ |
| `Offset`            | Byte offset where the operation occurred      | _Only for `read`, `write` ops_                                                       |
| `ViewSize`          | Size of the view                              | _Only for `map_file` family of ops_                                                  |
| `InfoClass`         | Type of information being queried or set      | See [InfoClass Values](#infoclass-values) below. _Only for Info ops_                 |
| `ThreadId`          | Thread ID of the operation                    |                                                                                      |
| `IrpPtr`            | Pointer to the I/O Request Packet (IRP)       | _Useful for correlating events_                                                      |
| `FileKey`           | Unique file object identifier from the kernel |                                                                                      |
| `FileAttributes`    | File attributes                               | See [FileAttributes Values](#fileattributes-values) below. _Only for `create` ops_   |
| `IoFlags`           | I/O specific flags                            | See [IoFlags Values](#ioflags-values) below. _Only for `read`, `write` ops_          |

> **Note:** `DesiredAccess` is **not** available in Windows ETW FileIO events and is excluded.

## Field Values

### Operation Values (`Op`)

`create`, `file_create`, `read`, `write`, `flush`, `close`, `cleanup`, `delete`, `file_delete`, `rename`, `set_info`, `query_info`, `query`, `dir_enum`, `dir_notify`, `file_rundown`, `fs_control`, `map_file`, `map_file_dc_start`, `map_file_dc_stop`, `unmap_file`, `name`.

### CreateOptions Values

Pipe-separated flags. Common values:

- `FILE_DIRECTORY_FILE`, `FILE_NON_DIRECTORY_FILE`
- `FILE_WRITE_THROUGH`, `FILE_SEQUENTIAL_ONLY`, `FILE_RANDOM_ACCESS`
- `FILE_NO_INTERMEDIATE_BUFFERING` (No Cache)
- `FILE_SYNCHRONOUS_IO_ALERT`, `FILE_SYNCHRONOUS_IO_NONALERT`
- `FILE_DELETE_ON_CLOSE`, `FILE_OPEN_BY_FILE_ID`
- `FILE_OPEN_FOR_BACKUP_INTENT`, `FILE_NO_COMPRESSION`
- `FILE_OPEN_REQUIRING_OPLOCK`, `FILE_DISALLOW_EXCLUSIVE`
- `FILE_SESSION_AWARE`, `FILE_RESERVE_OPFILTER`
- `FILE_OPEN_REPARSE_POINT`, `FILE_OPEN_NO_RECALL`
- `FILE_OPEN_FOR_FREE_SPACE_QUERY`

### ShareAccess Values

Pipe-separated flags:

- `FILE_SHARE_NONE` (Exclusive)
- `FILE_SHARE_READ`
- `FILE_SHARE_WRITE`
- `FILE_SHARE_DELETE`

### CreateDisposition Values

Single value indicating intent:

- `FILE_SUPERSEDE` (0): Replace if exists, create if not.
- `FILE_OPEN` (1): Open existing, fail if not exists.
- `FILE_CREATE` (2): Create new, fail if exists.
- `FILE_OPEN_IF` (3): Open if exists, create if not.
- `FILE_OVERWRITE` (4): Open and overwrite, fail if not exists.
- `FILE_OVERWRITE_IF` (5): Open and overwrite, create if not.

### FileAttributes Values

Pipe-separated flags:

- `FILE_ATTRIBUTE_NORMAL`, `FILE_ATTRIBUTE_READONLY`, `FILE_ATTRIBUTE_HIDDEN`
- `FILE_ATTRIBUTE_SYSTEM`, `FILE_ATTRIBUTE_DIRECTORY`, `FILE_ATTRIBUTE_ARCHIVE`
- `FILE_ATTRIBUTE_DEVICE`, `FILE_ATTRIBUTE_TEMPORARY`, `FILE_ATTRIBUTE_SPARSE_FILE`
- `FILE_ATTRIBUTE_REPARSE_POINT`, `FILE_ATTRIBUTE_COMPRESSED`, `FILE_ATTRIBUTE_OFFLINE`
- `FILE_ATTRIBUTE_NOT_CONTENT_INDEXED`, `FILE_ATTRIBUTE_ENCRYPTED`
- `FILE_ATTRIBUTE_INTEGRITY_STREAM`, `FILE_ATTRIBUTE_VIRTUAL`, `FILE_ATTRIBUTE_NO_SCRUB_DATA`

### IoFlags Values

Pipe-separated flags (for Read/Write):

- `IRP_NOCACHE`, `IRP_PAGING_IO`, `IRP_SYNCHRONOUS_API`
- `IRP_ASSOCIATED_IRP`, `IRP_BUFFERED_IO`, `IRP_DEALLOCATE_BUFFER`
- `IRP_INPUT_OPERATION`, `IRP_SYNCHRONOUS_PAGING_IO`
- `IRP_DEFER_IO_COMPLETION`, `IRP_OB_QUERY_NAME`, `IRP_HOLD_DEVICE_QUEUE`, `IRP_UM_DRIVER_INITIATED_IO`

### InfoClass Values

Type of information for `Query`/`Set` operations:

- `FileBasicInformation`, `FileStandardInformation`, `FileNameInformation`
- `FileRenameInformation`, `FileDispositionInformation`, `FileAllocationInformation`
- `FileEndOfFileInformation`, `FileStreamInformation`, `FileCompressionInformation`
- `FileIdBothDirectoryInformation`, `FileIdFullDirectoryInformation`
- `FileNetworkOpenInformation`, `FileAttributeTagInformation`
- ...and other `File*Information` values.

**Example:**

```csv
2026-02-08 23:23:45.123,create,1234,notepad.exe,C:\Users\User\Documents\test.txt,0,FILE_FLAG_OVERLAPPED,FILE_SHARE_READ,OPEN_EXISTING,,,,"",5678,18446744071562067968,305419896,FILE_ATTRIBUTE_NORMAL,
2026-02-08 23:23:45.125,read,1234,notepad.exe,C:\Users\User\Documents\test.txt,4096,,,,0,,,,5678,18446744071562067968,305419896,,IRP_PAGING_IO|IRP_NOCACHE
```
