# Filesystem Snapshot (`FILESYSTEM_SNAPSHOT`)

Snapshot of file system state (metadata).

## Multi-Part Files

To optimize memory usage during large filesystem scans, snapshots are automatically split into multiple compressed parts:

- **File Naming:** `filesystem_snapshot_part####_TIMESTAMP_DEVICEID.csv.zst`
  - Part numbers are zero-padded (e.g., `part0001`, `part0002`, ...)
  - Each part is compressed with Zstandard immediately after writing

- **Completion Marker:** The final part is renamed to indicate completion:
  - **Format:** `filesystem_snapshot_part####_TIMESTAMP_DEVICEID_complete_partsN.csv.zst`
  - The `_complete_partsN` suffix indicates this is the last part, where N is the total number of parts
  - Example: `filesystem_snapshot_part0003_20260214_120000_ABC123_complete_parts3.csv.zst` means 3 parts total and this is the final one

**CSV Header:**
`timestamp,path,size,CreationDate,modificationDate,LastAccessTime,Attributes,Extension,IsReadOnly`

**Fields:**

| Field              | Description                      | Notes                                  |
| :----------------- | :------------------------------- | :------------------------------------- |
| `timestamp`        | Snapshot timestamp               | format: `yyyy-MM-dd HH:mm:ss.fff`      |
| `path`             | Full path to the file            |                                        |
| `size`             | File size in bytes               |                                        |
| `CreationDate`     | File creation timestamp          | format: `yyyy-MM-dd HH:mm:ss.fff`      |
| `modificationDate` | File last modification timestamp | format: `yyyy-MM-dd HH:mm:ss.fff`      |
| `LastAccessTime`   | File last access timestamp       | format: `yyyy-MM-dd HH:mm:ss.fff`      |
| `Attributes`       | File attributes                  | e.g., `Archive`, `Directory`, `Hidden` |
| `Extension`        | File extension                   | including the dot (e.g., `.txt`)       |
| `IsReadOnly`       | Whether the file is read-only    | `True` or `False`                      |

**Example:**

```csv
2026-02-09 23:00:00.000,C:/Users/User/Documents/test.txt,1024,2026-02-08 23:00:00.000,2026-02-08 23:23:45.123,2026-02-09 10:00:00.000,Archive,.txt,False
```

## Known Limitations

### File Count Lower Than System Total

The snapshot file count will typically be **lower** than the total reported by tools like Windows Explorer, `os.walk` (Python), or `Get-ChildItem` (PowerShell). This is intentional, not a bug.

**Root cause: Reparse points are not traversed.**

The snapper explicitly skips directories that have the `ReparsePoint` attribute set (see `FilesystemSnapper.cs`):

```csharp
if ((fsi.Attributes & FileAttributes.ReparsePoint) != FileAttributes.ReparsePoint)
{
    dirs.Push(fsi.FullName); // Only recurse into real directories
}
```

NTFS uses reparse points to implement **junctions**, **symbolic links**, and **volume mount points**. Common examples on Windows:

| Path                         | Points To              |
| :--------------------------- | :--------------------- |
| `C:\Documents and Settings`  | `C:\Users`             |
| `C:\Users\All Users`         | `C:\ProgramData`       |
| `C:\Users\Default User`      | `C:\Users\Default`     |
| `C:\Program Files (x86)\...` | Various SysWOW64 paths |

Tools that follow these reparse points (e.g., Python's `os.walk` on Windows) will **traverse the same physical files multiple times** under different path prefixes, inflating their counts significantly.

**The tracer's count reflects unique, physical file paths only.** This avoids double-counting and provides a more accurate representation of the actual files on disk.

> [!NOTE]
> The discrepancy between the tracer and naive enumeration tools can be hundreds of thousands of files on a typical Windows installation, depending on how many junctions exist on the system.
