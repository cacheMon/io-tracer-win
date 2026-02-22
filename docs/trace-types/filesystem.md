# Filesystem Trace (`FILESYSTEM`)

Captures detailed file system I/O operations via Windows ETW kernel tracing.

**CSV Header:**
`Ts,Op,Pid,Comm,Filename,TraceSize,CreateOptions,ShareAccess,CreateDisposition,Offset,ViewSize,InfoClass,ThreadId,IrpPtr,FileKey,FileAttributes,IoFlags`

**Fields:**

| Field               | Description                              | Notes                                                                             |
| :------------------ | :--------------------------------------- | :-------------------------------------------------------------------------------- |
| `Ts`                | Timestamp (UTC) of the event             | Format: `yyyy-MM-dd HH:mm:ss.ffffff`                                              |
| `Op`                | Operation name                           | See [Operation Values](#operation-values-op) below                                |
| `Pid`               | Process ID initiating the operation      |                                                                                   |
| `Comm`              | Command / process name                   |                                                                                   |
| `Filename`          | Full path of the file involved           | Hashed if anonymous mode is enabled                                               |
| `TraceSize`         | Data transfer size (bytes)               |                                                                                   |
| `CreateOptions`     | Flags specified during file creation     | See [CreateOptions](#createoptions-values). _`create` only_                       |
| `ShareAccess`       | File sharing mode flags                  | See [ShareAccess](#shareaccess-values). _`create` only_                           |
| `CreateDisposition` | Action to take on file creation          | See [CreateDisposition](#createdisposition-values). _`create` only_               |
| `Offset`            | Byte offset of the operation             | _`read`, `write` only_                                                            |
| `ViewSize`          | Size of the mapped view                  | _`map_file` family only_                                                          |
| `InfoClass`         | Type of information being queried or set | See [InfoClass](#infoclass-values). _`query_info`, `set_info`, `fs_control` only_ |
| `ThreadId`          | Thread ID of the operation               |                                                                                   |
| `IrpPtr`            | I/O Request Packet pointer               | Useful for correlating request/completion pairs                                   |
| `FileKey`           | Kernel file-object identifier            |                                                                                   |
| `FileAttributes`    | File attribute flags                     | See [FileAttributes](#fileattributes-values). _`create` only_                     |
| `IoFlags`           | I/O flags                                | See [IoFlags](#ioflags-values). _`read`, `write` only_                            |

> **Note:** `DesiredAccess` is **not** available in Windows ETW FileIO events. Capturing it would require a minifilter driver (e.g., Process Monitor).

---

## Operation Values (`Op`)

Operations are grouped by category below.

| Category            | Operation           | Description                                                |
| :------------------ | :------------------ | :--------------------------------------------------------- |
| **Lifecycle**       | `create`            | Open or create a file (`IRP_MJ_CREATE`)                    |
|                     | `file_create`       | File creation notification (name-only event)               |
|                     | `close`             | Close the file handle (`IRP_MJ_CLOSE`)                     |
|                     | `cleanup`           | Last handle to a file object closed (`IRP_MJ_CLEANUP`)     |
| **Read / Write**    | `read`              | Read data from a file                                      |
|                     | `write`             | Write data to a file                                       |
|                     | `flush`             | Flush file buffers to disk                                 |
| **Delete / Rename** | `delete`            | File deletion (by handle)                                  |
|                     | `file_delete`       | File deletion notification (name-only event)               |
|                     | `rename`            | Rename or move a file                                      |
| **Metadata**        | `query_info`        | Query file metadata (`IRP_MJ_QUERY_INFORMATION`)           |
|                     | `set_info`          | Set file metadata (`IRP_MJ_SET_INFORMATION`)               |
| **Directory**       | `dir_enum`          | Enumerate directory contents                               |
|                     | `dir_notify`        | Directory change notification (`ReadDirectoryChangesW`)    |
| **Memory Mapping**  | `map_file`          | Map a file section into memory                             |
|                     | `map_file_dc_start` | Data-collection start for existing mapped files            |
|                     | `map_file_dc_stop`  | Data-collection stop for existing mapped files             |
|                     | `unmap_file`        | Unmap a file section from memory                           |
| **Other**           | `fs_control`        | File-system control request (`IRP_MJ_FILE_SYSTEM_CONTROL`) |
|                     | `file_rundown`      | Rundown event enumerating open files at trace start        |
|                     | `name`              | File name event (internal kernel name association)         |

---

## Field Values

### CreateOptions Values

Pipe-separated flags. Common values:

- `FILE_DIRECTORY_FILE`, `FILE_NON_DIRECTORY_FILE`
- `FILE_WRITE_THROUGH`, `FILE_SEQUENTIAL_ONLY`, `FILE_RANDOM_ACCESS`
- `FILE_NO_INTERMEDIATE_BUFFERING` — disables caching
- `FILE_SYNCHRONOUS_IO_ALERT`, `FILE_SYNCHRONOUS_IO_NONALERT`
- `FILE_DELETE_ON_CLOSE`, `FILE_OPEN_BY_FILE_ID`
- `FILE_OPEN_FOR_BACKUP_INTENT`, `FILE_NO_COMPRESSION`
- `FILE_OPEN_REQUIRING_OPLOCK`, `FILE_DISALLOW_EXCLUSIVE`
- `FILE_SESSION_AWARE`, `FILE_RESERVE_OPFILTER`
- `FILE_OPEN_REPARSE_POINT`, `FILE_OPEN_NO_RECALL`
- `FILE_OPEN_FOR_FREE_SPACE_QUERY`

### ShareAccess Values

Pipe-separated flags:

- `FILE_SHARE_NONE` — exclusive access
- `FILE_SHARE_READ`
- `FILE_SHARE_WRITE`
- `FILE_SHARE_DELETE`

### CreateDisposition Values

Single value indicating the create/open intent:

| Value | Name                | Behaviour                             |
| :---- | :------------------ | :------------------------------------ |
| 0     | `FILE_SUPERSEDE`    | Replace if exists, create if not      |
| 1     | `FILE_OPEN`         | Open existing; fail if not found      |
| 2     | `FILE_CREATE`       | Create new; fail if already exists    |
| 3     | `FILE_OPEN_IF`      | Open if exists, otherwise create      |
| 4     | `FILE_OVERWRITE`    | Open and overwrite; fail if not found |
| 5     | `FILE_OVERWRITE_IF` | Open and overwrite, otherwise create  |

### FileAttributes Values

Pipe-separated flags:

- `FILE_ATTRIBUTE_NORMAL`, `FILE_ATTRIBUTE_READONLY`, `FILE_ATTRIBUTE_HIDDEN`
- `FILE_ATTRIBUTE_SYSTEM`, `FILE_ATTRIBUTE_DIRECTORY`, `FILE_ATTRIBUTE_ARCHIVE`
- `FILE_ATTRIBUTE_DEVICE`, `FILE_ATTRIBUTE_TEMPORARY`, `FILE_ATTRIBUTE_SPARSE_FILE`
- `FILE_ATTRIBUTE_REPARSE_POINT`, `FILE_ATTRIBUTE_COMPRESSED`, `FILE_ATTRIBUTE_OFFLINE`
- `FILE_ATTRIBUTE_NOT_CONTENT_INDEXED`, `FILE_ATTRIBUTE_ENCRYPTED`
- `FILE_ATTRIBUTE_INTEGRITY_STREAM`, `FILE_ATTRIBUTE_VIRTUAL`, `FILE_ATTRIBUTE_NO_SCRUB_DATA`

### IoFlags Values

Pipe-separated flags (applicable to `read` / `write`):

- `IRP_NOCACHE`, `IRP_PAGING_IO`, `IRP_SYNCHRONOUS_API`
- `IRP_ASSOCIATED_IRP`, `IRP_BUFFERED_IO`, `IRP_DEALLOCATE_BUFFER`
- `IRP_INPUT_OPERATION`, `IRP_SYNCHRONOUS_PAGING_IO`
- `IRP_DEFER_IO_COMPLETION`, `IRP_OB_QUERY_NAME`, `IRP_HOLD_DEVICE_QUEUE`, `IRP_UM_DRIVER_INITIATED_IO`

### InfoClass Values

Information type for `query_info` / `set_info` / `fs_control` operations:

- `FileBasicInformation`, `FileStandardInformation`, `FileNameInformation`
- `FileRenameInformation`, `FileDispositionInformation`, `FileAllocationInformation`
- `FileEndOfFileInformation`, `FileStreamInformation`, `FileCompressionInformation`
- `FileIdBothDirectoryInformation`, `FileIdFullDirectoryInformation`
- `FileNetworkOpenInformation`, `FileAttributeTagInformation`
- … and other `File*Information` values.

---

## Example

```csv
Ts,Op,Pid,Comm,Filename,TraceSize,CreateOptions,ShareAccess,CreateDisposition,Offset,ViewSize,InfoClass,ThreadId,IrpPtr,FileKey,FileAttributes,IoFlags
2026-02-08 23:23:45.123456,create,1234,notepad.exe,C:\Users\User\Documents\test.txt,0,FILE_NON_DIRECTORY_FILE|FILE_SYNCHRONOUS_IO_NONALERT,FILE_SHARE_READ,FILE_OPEN_IF,,,,5678,9876543210,18446744071562067968,FILE_ATTRIBUTE_NORMAL,
2026-02-08 23:23:45.125789,read,1234,notepad.exe,C:\Users\User\Documents\test.txt,4096,,,,0,,,5678,9876543210,18446744071562067968,,IRP_PAGING_IO|IRP_NOCACHE
2026-02-08 23:23:45.130012,write,1234,notepad.exe,C:\Users\User\Documents\test.txt,512,,,,4096,,,5678,9876543210,18446744071562067968,,IRP_SYNCHRONOUS_API
2026-02-08 23:23:45.140000,close,1234,notepad.exe,C:\Users\User\Documents\test.txt,0,,,,,,,,9876543210,18446744071562067968,,
```

---

## Cache-Based Filename Resolution

Many ETW filesystem events (e.g., `read`, `write`, `flush`, `close`, `dir_notify`, `query_info`, `set_info`, `fs_control`) do **not** reliably include the filename. To work around this, the tracer maintains an in-memory `FileObject → Filename` cache (`nameByObj`) and uses a two-step resolution strategy via the `Resolve()` method:

### Resolution Strategy

For every event that carries a `FileObject` handle, the tracer resolves the filename as follows:

1. **Event-supplied name first** — if the ETW event itself provides a non-empty `FileName`, that value is used directly.
2. **Cache fallback** — if the event's `FileName` is empty, the tracer looks up the `FileObject` in the cache. If a mapping exists, the cached name is returned.
3. **Empty result** — if neither source yields a name, the filename is recorded as an empty string.

### Cache Lifecycle

| Phase        | Trigger                        | Action                                                                       |
| :----------- | :----------------------------- | :--------------------------------------------------------------------------- |
| **Populate** | `create` event (`OnCreate`)    | Maps `FileObject → FileName` when the filename is non-empty.                 |
| **Update**   | `rename` event (`OnRename`)    | Overwrites the mapping with the new filename after a rename.                 |
| **Consume**  | Any I/O event (e.g., `OnRead`) | Calls `Resolve()` which reads from the cache when the event name is missing. |
| **Evict**    | `close` event (`OnClose`)      | Removes the `FileObject` entry after the handle is closed.                   |

### Which Operations Use the Cache

| Uses cache (`Resolve()`)                                                                                                           | Does **not** use cache (name taken directly from event)                                   |
| :--------------------------------------------------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------- |
| `read`, `write`, `flush`, `close`, `cleanup`, `delete`, `rename`, `dir_enum`, `dir_notify`, `query_info`, `set_info`, `fs_control` | `create`, `file_create`, `file_delete`, `file_rundown`, `map_file*`, `unmap_file`, `name` |

> **Note:** `create` and `rename` are special — they both **populate** the cache _and_ call `Resolve()`, so they benefit from the cache if their own `FileName` happens to be empty.

### Why the Cache Can Miss

- **Pre-existing handles** — files opened _before_ tracing started have no corresponding `create` event, so they never enter the cache.
- **`FileObject` reuse** — after a `close` evicts an entry, the kernel may reassign the same `FileObject` value to a new file. The cache only reflects the most recent mapping.
- **`dir_notify` mismatch** — the directory handle used for `ReadDirectoryChangesW` notifications typically differs from the handle seen in `create`, so the cache lookup returns nothing (see [Known Limitations](#empty-filename-on-dir_notify-events)).

---

## Known Limitations

### Empty `Filename` on `dir_notify` events

Windows ETW `DirNotify` events — fired when a process watches a directory for changes via `ReadDirectoryChangesW` — frequently have an empty `Filename`. This is an ETW kernel-provider limitation.

**Root cause:** `DirNotify` is an asynchronous change notification on a directory handle. Unlike `create`, `read`, or `write` events, the ETW kernel provider does not reliably populate the file or directory name. The `FileName`, `DirectoryName`, `FileObject`, and `FileKey` fields are all either empty or do not match entries from other event types (`FileIOName`, `FileIOFileRundown`, `FileIOFileCreate`).

**Why cache-based resolution fails:**

- `FileName` / `DirectoryName` on the `FileIODirEnumTraceData` are empty for `DirNotify`.
- The `FileObject`-based cache (populated by `create` events) does not match because the directory handle used for notifications differs from standard file handles.
- The `FileKey`-based cache (populated by `Name` / `FileRundown` events) is unavailable under the standard `FileIO | FileIOInit` kernel flags.
- The `DirEnum` directory cache uses a different `FileObject` than `DirNotify` for the same directory.

### Empty `Filename` on `fs_control` events

`IRP_MJ_FILE_SYSTEM_CONTROL` events may lack a `Filename`. The tracer attempts resolution via the `FileObject`-based cache, but this fails when:

- The file handle was opened **before** tracing started (no prior `create` event).
- The operation targets a **volume or device** rather than a specific file (e.g., `FSCTL_QUERY_USN_JOURNAL`, `FSCTL_GET_REPARSE_POINT`).
- The `FileObject` was not seen in any prior `create` or `rename` event.

### Empty `Filename` on other I/O events

`read`, `write`, `flush`, `close`, `query`, `query_info`, and `set_info` events may also have an empty `Filename`. These operations use ETW event types (`FileIOReadWriteTraceData`, `FileIOSimpleOpTraceData`, `FileIOInfoTraceData`) that do not always include the filename.

The tracer resolves names via the `FileObject`-based cache (populated by `create` and `rename` events), but resolution fails when:

- The file handle was opened **before** tracing started.
- The `FileObject` was not seen in any prior `create` or `rename` event.

This is most common at the **beginning of a trace session** and becomes less frequent as the cache is populated over time.

### Why `query_info` instead of `query`

The ETW kernel event for file information queries is named `FileIOQueryInfo`, which maps to `IRP_MJ_QUERY_INFORMATION`. We use `query_info` as the operation name (rather than a shorter `query`) for two reasons:

1. **ETW alignment** — `query_info` directly mirrors the ETW event name `FileIOQueryInfo`, making it straightforward to trace an operation back to its source event.
2. **Consistent pairing** — `query_info` pairs naturally with `set_info` (`IRP_MJ_SET_INFORMATION` / `FileIOSetInfo`), keeping the naming symmetrical.
