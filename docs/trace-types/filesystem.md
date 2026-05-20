# Filesystem Trace (`FILESYSTEM`)

Captures detailed file system I/O operations via Windows ETW kernel tracing.

**CSV Header:**
`Ts,Op,Pid,Comm,Filename,TraceSize,CreateOptions,ShareAccess,CreateDisposition,Offset,ViewSize,FileInfoClass,FsctlCode,ThreadId,Irp,FileKey,FileAttributes,IoFlags,CommandLine`

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
| `FileInfoClass`     | `FILE_INFORMATION_CLASS` for file metadata queries/sets | See [FileInfoClass](#fileinfoclass-values). _`query_info`, `set_info` only_        |
| `FsctlCode`         | FSCTL control code for file-system control requests      | See [FsctlCode](#fsctlcode-values). _`fs_control` only_                            |
| `ThreadId`          | Thread ID of the operation               |                                                                                   |
| `Irp`               | I/O Request Packet pointer               | Hex format: `0x...`. Useful for correlating request/completion pairs              |
| `FileKey`           | Kernel file-object identifier            |                                                                                   |
| `FileAttributes`    | File attribute flags                     | See [FileAttributes](#fileattributes-values). _`create` only_                     |
| `IoFlags`           | I/O flags                                | See [IoFlags](#ioflags-values). _`read`, `write` only_                            |
| `CommandLine`       | Full command line of the process         |                                                                                   |

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

### FileInfoClass Values

Populated for `query_info` and `set_info` operations. Contains the decoded `FILE_INFORMATION_CLASS` integer from the ETW `FileIOInfo` event (decoded via `FileIOFlags.FormatInfoClass()`).

#### FILE_INFORMATION_CLASS values

Common values:

- `FileBasicInformation`, `FileStandardInformation`, `FileNameInformation`
- `FileRenameInformation`, `FileDispositionInformation`, `FileAllocationInformation`
- `FileEndOfFileInformation`, `FileStreamInformation`, `FileCompressionInformation`
- `FileIdBothDirectoryInformation`, `FileIdFullDirectoryInformation`
- `FileNetworkOpenInformation`, `FileAttributeTagInformation`
- `FileRemoteProtocolInformation`, `FileStatInformation`
- … and other `File*Information` values (full list in `FileIOFlags.FileInfoClassValue` / `FormatInfoClass()`).

### FsctlCode Values

Populated for `fs_control` operations. Contains the decoded FSCTL control code from the ETW `FileIOInfo` event (decoded via `FileIOFlags.FormatFsctlCode()`). The raw value is produced by the `CTL_CODE` macro and encodes device type, access, function number, and transfer method — e.g. `0x000900EB`.

Single value. Common codes from `winioctl.h`:

| FSCTL name | Value | Notes |
| :--- | :--- | :--- |
| `FSCTL_REQUEST_OPLOCK_LEVEL_1` | `0x00090000` | |
| `FSCTL_REQUEST_OPLOCK_LEVEL_2` | `0x00090004` | |
| `FSCTL_REQUEST_BATCH_OPLOCK` | `0x00090008` | |
| `FSCTL_REQUEST_FILTER_OPLOCK` | `0x0009005C` | |
| `FSCTL_REQUEST_OPLOCK` | `0x00090240` | Windows 7+ oplock v2 |
| `FSCTL_LOCK_VOLUME` | `0x00090018` | |
| `FSCTL_UNLOCK_VOLUME` | `0x0009001C` | |
| `FSCTL_DISMOUNT_VOLUME` | `0x00090020` | |
| `FSCTL_IS_VOLUME_MOUNTED` | `0x00090028` | |
| `FSCTL_IS_VOLUME_DIRTY` | `0x00090078` | |
| `FSCTL_GET_COMPRESSION` | `0x0009003C` | |
| `FSCTL_SET_COMPRESSION` | `0x0009C040` | |
| `FSCTL_SET_SPARSE` | `0x000900C4` | |
| `FSCTL_SET_ZERO_DATA` | `0x000980C8` | |
| `FSCTL_QUERY_ALLOCATED_RANGES` | `0x000940CF` | |
| `FSCTL_FILESYSTEM_GET_STATISTICS` | `0x00090060` | |
| `FSCTL_GET_NTFS_VOLUME_DATA` | `0x00090064` | |
| `FSCTL_GET_VOLUME_BITMAP` | `0x0009006F` | |
| `FSCTL_GET_RETRIEVAL_POINTERS` | `0x00090073` | |
| `FSCTL_MOVE_FILE` | `0x00090074` | |
| `FSCTL_FIND_FILES_BY_SID` | `0x0009008F` | |
| `FSCTL_SET_REPARSE_POINT` | `0x000900A4` | |
| `FSCTL_GET_REPARSE_POINT` | `0x000900A8` | |
| `FSCTL_DELETE_REPARSE_POINT` | `0x000900AC` | |
| `FSCTL_SET_OBJECT_ID` | `0x00090098` | |
| `FSCTL_GET_OBJECT_ID` | `0x0009009C` | Retrieves NTFS object ID for a file |
| `FSCTL_DELETE_OBJECT_ID` | `0x000900A0` | |
| `FSCTL_CREATE_OR_GET_OBJECT_ID` | `0x000900C0` | |
| `FSCTL_SET_OBJECT_ID_EXTENDED` | `0x000900BC` | |
| `FSCTL_CREATE_USN_JOURNAL` | `0x000900E7` | |
| `FSCTL_READ_USN_JOURNAL` | `0x000900BB` | |
| `FSCTL_READ_FILE_USN_DATA` | `0x000900EB` | Reads USN change-journal record for a file |
| `FSCTL_WRITE_USN_CLOSE_RECORD` | `0x000900EF` | |
| `FSCTL_QUERY_USN_JOURNAL` | `0x000900F4` | |
| `FSCTL_DELETE_USN_JOURNAL` | `0x000900F8` | |
| `FSCTL_ENUM_USN_DATA` | `0x000900B3` | |
| `FSCTL_MARK_HANDLE` | `0x000900FC` | |
| `FSCTL_SET_ENCRYPTION` | `0x000900D7` | |
| `FSCTL_READ_RAW_ENCRYPTED` | `0x000900E3` | |
| `FSCTL_WRITE_RAW_ENCRYPTED` | `0x000900DF` | |
| `FSCTL_GET_INTEGRITY_INFORMATION` | `0x0009027C` | ReFS/NTFS integrity streams |
| `FSCTL_SET_INTEGRITY_INFORMATION` | `0x0009C280` | ReFS/NTFS integrity streams |
| `FSCTL_DUPLICATE_EXTENTS_TO_FILE` | `0x00098344` | Block-clone / copy-on-write |

Any code not in the above list is emitted as `0xXXXXXXXX`. The full set of recognised codes is defined in `FileIOFlags.FsctlCode` / `FormatFsctlCode()`.

---

## Example

```csv
Ts,Op,Pid,Comm,Filename,TraceSize,CreateOptions,ShareAccess,CreateDisposition,Offset,ViewSize,FileInfoClass,FsctlCode,ThreadId,Irp,FileKey,FileAttributes,IoFlags,CommandLine
2026-02-08 23:23:45.123456,create,1234,notepad.exe,C:\Users\User\Documents\test.txt,0,FILE_NON_DIRECTORY_FILE|FILE_SYNCHRONOUS_IO_NONALERT,FILE_SHARE_READ,FILE_OPEN_IF,,,,,5678,0x24CBEEB5A40,18446744071562067968,FILE_ATTRIBUTE_NORMAL,,notepad.exe C:\Users\User\Documents\test.txt
2026-02-08 23:23:45.125789,read,1234,notepad.exe,C:\Users\User\Documents\test.txt,4096,,,,0,,,,5678,0x24CBEEB5A40,18446744071562067968,,IRP_PAGING_IO|IRP_NOCACHE,notepad.exe C:\Users\User\Documents\test.txt
2026-02-08 23:23:45.130012,write,1234,notepad.exe,C:\Users\User\Documents\test.txt,512,,,,4096,,,,5678,0x24CBEEB5A40,18446744071562067968,,IRP_SYNCHRONOUS_API,notepad.exe C:\Users\User\Documents\test.txt
2026-02-08 23:23:45.140000,close,1234,notepad.exe,C:\Users\User\Documents\test.txt,0,,,,,,,,,0x24CBEEB5A40,18446744071562067968,,,notepad.exe C:\Users\User\Documents\test.txt
```

---

## Cache-Based Filename Resolution

Many ETW filesystem events (e.g., `read`, `write`, `flush`, `close`, `dir_notify`, `query_info`, `set_info`, `fs_control`) do **not** reliably include the filename. To work around this, the tracer maintains an in-memory cache (`nameByObj`) keyed by kernel file-object pointer and uses a multi-step resolution strategy via the `Resolve()` method.

### Background: ETW FileKey vs FileObject

Windows ETW uses two different pointer-sized identifiers across event types for what is conceptually the same file:

| Event type | Field used as name-lookup key |
| :--- | :--- |
| `FileIOCreate` | `FileObject` (NT file object pointer) |
| `FileIOName` / `FileIOFileRundown` | `FileKey` (ETW internal file key, exposed by TraceEvent as `FileKey`) |
| `FileIOReadWrite` (read, write) | `FileKey` |
| `FileIOSimpleOp` (close, flush, cleanup) | Either field depending on kernel version / event source |
| `FileIOInfo` (query_info, set_info, etc.) | Either field depending on kernel version / event source |

Because the exact field varies, `Resolve()` always tries **both** `FileKey` and `FileObject` before giving up.

### Resolution Strategy

For every event that carries file-object identifiers, the tracer resolves the filename as follows:

1. **Event-supplied name first** — if the ETW event itself provides a non-empty `FileName`, that value is used directly.
2. **Cache lookup by `FileKey`** — if the event name is empty, look up `FileKey` in the cache.
3. **Cache lookup by `FileObject`** — if that also misses, try `FileObject` as a fallback.
4. **Empty result** — if no source yields a name, the filename is recorded as an empty string.

### Cache Lifecycle

| Phase        | Trigger                                          | Action                                                                                  |
| :----------- | :----------------------------------------------- | :-------------------------------------------------------------------------------------- |
| **Populate** | `create` event (`OnCreate`)                      | Maps `FileObject → FileName` when the filename is non-empty.                            |
| **Populate** | `name` / `file_rundown` events (`OnName`, `OnFileRundown`) | Maps `FileKey → FileName` for files that were already open when the trace started. |
| **Update**   | `rename` event (`OnRename`)                      | Overwrites both `FileKey` and `FileObject` entries with the new filename.               |
| **Consume**  | Any I/O event (e.g., `OnRead`)                   | Calls `Resolve()` which tries `FileKey` then `FileObject` when the event name is empty. |
| **Evict**    | `close` event (`OnClose`)                        | Removes both `FileKey` and `FileObject` entries after the handle is closed.             |

### Complete Filename Resolution (Deferred Emit Queue)

To capture every resolvable filename, the tracer implements a three-layer resolution strategy:

#### Layer 1: Decouple Cache Population from Process Filtering

The cache is now populated **regardless of process filter**. When `OnCreate`, `OnName`, `OnFileRundown`, or `OnRename` fires, the filename is stored in `nameByObj` before the `ProcessFilter.ShouldTrace` check. This ensures that:

- Files opened by non-traced processes (e.g., `svchost`, `System`) are still indexed
- Traced processes can resolve reads/writes on those files via the cache
- Only the emission is filtered; the cache is global

**Impact:** Eliminates empty filenames on cross-process file operations (e.g., traced app reading a file opened by a filtered background service).

#### Layer 2: Cross-Populate FileKey ↔ FileObject

The `Resolve(ulong key1, ulong key2, string eventName)` method now writes back to both keys when a match is found:

```csharp
if (nameByObj.TryGetValue(key1, out var cached))
{
    if (key2 != 0 && key2 != key1) nameByObj.TryAdd(key2, cached);  // ← writes back
    return MarkDirectoryIfNoExtension(cached);
}
```

Once a filename is found via `FileObject` (populated by `create`), it is immediately indexed under `FileKey` for future lookups. Subsequent reads using only `FileKey` will hit on the first try.

**Impact:** Eliminates double-lookups and stale entries from kernel file-object reuse. Reduces lookup cost on hot paths.

#### Layer 3: Deferred Emit Queue with Timer Flush

For events that still resolve to empty after Layers 1 and 2:

1. **Enqueue:** The event is not emitted immediately. Instead, a lambda closure capturing all its fields is queued in `_pending`, indexed by `FileKey`.

2. **Drain on Name:** When `OnName` or `OnFileRundown` fires and populates the cache, `DrainPending(fileKey, resolvedName)` immediately dequeues and invokes all pending lambdas with the resolved filename.

3. **Timer Flush:** A background `System.Threading.Timer` (50ms interval) scans `_pending` for stale entries (>100ms old) and flushes them with best-effort resolution (cached name or empty string).

**Impact:** Handles transient timing races where `read` fires between `create` and `name` events (typically <100μs apart, but can be delayed under high load). Events are emitted with correct filenames instead of empty strings.

**Example flow:**
```
OnCreate fires: FileObject=0xABCD, FileName="C:\file.txt"
  → nameByObj[0xABCD] = "C:\file.txt"

OnRead fires: FileKey=0x1234, FileObject=0xABCD, FileName=""
  → Resolve(0x1234, 0xABCD, "") tries:
    1. nameByObj[0x1234] → miss (OnName hasn't fired yet)
    2. nameByObj[0xABCD] → HIT! Returns "C:\file.txt"
    3. Also stores: nameByObj[0x1234] = "C:\file.txt" (cross-populate)
  → Emits immediately (not deferred)

OnName fires (shortly after): FileKey=0x1234, FileName="C:\file.txt"
  → nameByObj[0x1234] = "C:\file.txt" (already there from cross-populate)
  → DrainPending(0x1234, "C:\file.txt") → queue is empty (already emitted)
```

In a race scenario where `OnName` is delayed:
```
OnCreate fires: FileObject=0xABCD, FileName="C:\file.txt"
  → nameByObj[0xABCD] = "C:\file.txt"

OnRead fires BEFORE OnName: FileKey=0x1234, FileObject=0xYYYY, FileName=""
  → (FileObject differs! Perhaps reused pointer or cross-process)
  → Resolve(0x1234, 0xYYYY, "") → both misses
  → EnqueuePending(0x1234, 0xYYYY, lambda(...))

OnName fires: FileKey=0x1234, FileName="C:\file.txt"
  → nameByObj[0x1234] = "C:\file.txt"
  → DrainPending(0x1234, "C:\file.txt") → invokes lambda with name
  → Emits: read,... with correct filename
```

Timer fires after 100ms: any remaining deferred events are flushed with whatever resolution was achieved.

### Which Operations Use the Cache

| Uses cache (`Resolve()`)                                                                                                           | Does **not** use cache (name taken directly from event)                                   |
| :--------------------------------------------------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------- |
| `read`, `write`, `flush`, `close`, `cleanup`, `delete`, `rename`, `dir_enum`, `dir_notify`, `query_info`, `set_info`, `fs_control` | `create`, `file_create`, `file_delete`, `file_rundown`, `map_file*`, `unmap_file`, `name` |

> **Note:** `create` and `rename` are special — they both **populate** the cache _and_ call `Resolve()`, so they benefit from the cache if their own `FileName` happens to be empty.

### Why the Cache Can Still Miss

With the three-layer resolution strategy (decoupled cache, cross-population, and deferred emit), most timing and filtering issues are eliminated. However, some edge cases remain:

- **Pre-existing handles with no rundown** — files that were open when the trace started are normally covered by `file_rundown` / `name` events that fire at session startup. However, if a file object was not enumerated in those rundown events, it will never enter the cache. The deferred queue cannot help (no `name` event will arrive).
- **Kernel-internal objects with no filesystem path** — kernel paging, anonymous sections, and unnamed device objects have no resolvable filename in ETW. These are unfixable at user mode without a kernel minifilter driver.
- **`dir_notify` handle mismatch** — the directory handle used for `ReadDirectoryChangesW` notifications typically differs from handles seen in `create` or rundown events, so both cache lookups return nothing (see [Known Limitations](#empty-filename-on-dir_notify-events)).

> **Note:** For `dir_enum` and `dir_notify`, the ETW `FileName` field contains the **search pattern** passed to `NtQueryDirectoryFile` (e.g., `*.txt`, `Get-WmiObject*`), not the directory path. The tracer ignores this field and resolves the directory name exclusively from the cache.

> **Improvement:** The deferred emit queue now handles **`FileIOName` timing races**. If a read fires before the corresponding `name` event (rare but possible under high load), the read is queued and emitted with the correct filename when `name` fires. Previously these would always result in empty filenames.

---

## Known Limitations

> **Note on Implementation Status:** The three-layer filename resolution (decoupled cache population, cross-population, and deferred emit queue) has significantly reduced empty filenames. Most timing races and process-filter issues are now resolved. The limitations below apply to edge cases where ETW itself does not expose a filename or handle.

### Empty `Filename` on `dir_notify` events

Windows ETW `DirNotify` events — fired when a process watches a directory for changes via `ReadDirectoryChangesW` — frequently have an empty `Filename`. This is an ETW kernel-provider limitation.

**Root cause:** `DirNotify` is an asynchronous change notification on a directory handle. Unlike `create`, `read`, or `write` events, the ETW kernel provider does not reliably populate the file or directory name. The `FileName`, `DirectoryName`, `FileObject`, and `FileKey` fields are all either empty or do not match entries from other event types (`FileIOName`, `FileIOFileRundown`, `FileIOFileCreate`).

**Why cache-based resolution fails:**

- `FileName` / `DirectoryName` on the `FileIODirEnumTraceData` are empty for `DirNotify`.
- The `FileObject`-based cache (populated by `create` events) does not match because the directory handle used for notifications differs from standard file handles.
- The `FileKey`-based cache (populated by `Name` / `FileRundown` events) is unavailable under the standard `FileIO | FileIOInit` kernel flags.
- The `DirEnum` directory cache uses a different `FileObject` than `DirNotify` for the same directory.

### Empty `Filename` on `fs_control` events

`IRP_MJ_FILE_SYSTEM_CONTROL` events may lack a `Filename`. The tracer attempts resolution via the two-key cache (`FileKey`, then `FileObject`), but this fails when:

- The file handle was opened **before** tracing started and was not covered by a `file_rundown` or `name` event.
- The operation targets a **volume or device** rather than a specific file (e.g., `FSCTL_QUERY_USN_JOURNAL`, `FSCTL_GET_REPARSE_POINT`).
- Neither `FileKey` nor `FileObject` from this event matches anything in the cache.

### Empty `Filename` on other I/O events

`read`, `write`, `flush`, `close`, `query_info`, and `set_info` events may also have an empty `Filename`. The ETW event types for these operations (`FileIOReadWriteTraceData`, `FileIOSimpleOpTraceData`, `FileIOInfoTraceData`) do not include the filename.

The tracer resolves names via the cache and deferred emit queue. Most cases are now handled:

- ✅ **Cache population from all processes** — Cache entries are now populated even for non-traced processes. Traced apps reading files opened by background services now resolve correctly.
- ✅ **Cross-population** — Once a filename is found via `FileKey` or `FileObject`, it is stored under both keys. Subsequent lookups are O(1) and avoid transient races.
- ✅ **Deferred queue** — If resolution still returns empty, the event is queued. When the `FileIOName` event fires, the pending event is immediately emitted with the resolved filename. This handles timing races where `read` fires before `name` (rare but possible under extreme load).

Resolution still **cannot** resolve when:

- The file was open **before** tracing started and no `file_rundown` / `name` event enumerated it. The deferred queue cannot help since no future `name` event will arrive.
- The operation targets a kernel-internal object with no filesystem path (paging file, unnamed sections, volume/device operations).

At **session startup**, until `file_rundown` and initial `name` events populate the cache, some reads from pre-existing files may be empty. This window closes within milliseconds as the cache is seeded.

### Why `query_info` instead of `query`

The ETW kernel event for file information queries is named `FileIOQueryInfo`, which maps to `IRP_MJ_QUERY_INFORMATION`. We use `query_info` as the operation name (rather than a shorter `query`) for two reasons:

1. **ETW alignment** — `query_info` directly mirrors the ETW event name `FileIOQueryInfo`, making it straightforward to trace an operation back to its source event.
2. **Consistent pairing** — `query_info` pairs naturally with `set_info` (`IRP_MJ_SET_INFORMATION` / `FileIOSetInfo`), keeping the naming symmetrical.
