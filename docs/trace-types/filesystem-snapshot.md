# Filesystem Snapshot (`FILESYSTEM_SNAPSHOT`)

Snapshot of file system state (metadata).

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
