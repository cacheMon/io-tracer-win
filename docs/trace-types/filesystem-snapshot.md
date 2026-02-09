# Filesystem Snapshot (`FILESYSTEM_SNAPSHOT`)

Snapshot of file system state (metadata).

**CSV Header:**
`path,size,CreationDate,modificationDate`

**Fields:**

| Field              | Description                      | Notes |
| :----------------- | :------------------------------- | :---- |
| `path`             | Full path to the file            |       |
| `size`             | File size in bytes               |       |
| `CreationDate`     | File creation timestamp          |       |
| `modificationDate` | File last modification timestamp |       |

**Example:**

```csv
C:\Users\User\Documents\test.txt,1024,2026-02-08 23:00:00.000,2026-02-08 23:23:45.123
```
