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
