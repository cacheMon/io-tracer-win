# IO Tracer - Trace Types Documentation

## 1. Filesystem Trace (`FILESYSTEM`)

Captures detailed file system operations.

**CSV Header:**
`Ts,Op,Pid,Comm,Filename,TraceSize,CreateOptions,ShareAccess,CreateDisposition,Offset,ViewSize,InfoClass,ThreadId,IrpPtr,FileKey,FileAttributes,IoFlags`

**Fields:**

| Field               | Description                                   | Notes                                                                                |
| :------------------ | :-------------------------------------------- | :----------------------------------------------------------------------------------- |
| `Ts`                | Timestamp (UTC) of the event                  | Format: `yyyy-MM-dd HH:mm:ss.fff`                                                    |
| `Op`                | Operation name                                | e.g., `Create`, `Read`, `Write`, `Close`, `SetInfo`, `QueryInfo`, `MapFile`, `Flush` |
| `Pid`               | Process ID initiating the operation           |                                                                                      |
| `Comm`              | Command/Process name                          |                                                                                      |
| `Filename`          | Full path of the file involved                | Hashed if anonymous mode is enabled                                                  |
| `TraceSize`         | Size of the data transfer (bytes)             |                                                                                      |
| `CreateOptions`     | Flags specified during file creation          | e.g., `FILE_FLAG_OVERLAPPED`, `FILE_FLAG_DELETE_ON_CLOSE`. _Only for Create ops_     |
| `ShareAccess`       | File sharing mode flags                       | e.g., `FILE_SHARE_READ`, `FILE_SHARE_WRITE`. _Only for Create ops_                   |
| `CreateDisposition` | Action to take on file creation               | e.g., `OPEN_EXISTING`, `CREATE_ALWAYS`. _Only for Create ops_                        |
| `Offset`            | Byte offset where the operation occurred      | _Only for Read/Write ops_                                                            |
| `ViewSize`          | Size of the view                              | _Only for MapFile ops_                                                               |
| `InfoClass`         | Type of information being queried or set      | _Only for Query/SetInfo ops_                                                         |
| `ThreadId`          | Thread ID of the operation                    |                                                                                      |
| `IrpPtr`            | Pointer to the I/O Request Packet (IRP)       | _Useful for correlating events_                                                      |
| `FileKey`           | Unique file object identifier from the kernel |                                                                                      |
| `FileAttributes`    | File attributes                               | e.g., `FILE_ATTRIBUTE_NORMAL`, `FILE_ATTRIBUTE_HIDDEN`. _Only for Create ops_        |
| `IoFlags`           | I/O specific flags                            | e.g., `PAGING_IO`, `SYNCHRONOUS_PAGING_IO`. _Only for Read/Write ops_                |

**Example:**

```csv
2026-02-08 23:23:45.123,Create,1234,notepad.exe,C:\Users\User\Documents\test.txt,0,FILE_FLAG_OVERLAPPED,FILE_SHARE_READ,OPEN_EXISTING,,,,"",5678,18446744071562067968,305419896,FILE_ATTRIBUTE_NORMAL,
2026-02-08 23:23:45.125,Read,1234,notepad.exe,C:\Users\User\Documents\test.txt,4096,,,,0,,,,5678,18446744071562067968,305419896,,PAGING_IO
```

## 2. Disk Trace (`DISK`)

Captures low-level disk I/O operations.

**CSV Header:**
`Ts,Pid,Comm,Sector,Operation,TraceSize,Latency`

**Fields:**

| Field       | Description                               | Notes                          |
| :---------- | :---------------------------------------- | :----------------------------- |
| `Ts`        | Timestamp (UTC)                           |                                |
| `Pid`       | Process ID                                |                                |
| `Comm`      | Command/Process name                      |                                |
| `Sector`    | Logical sector number on the disk         |                                |
| `Operation` | Operation type                            | e.g., `Read`, `Write`, `Flush` |
| `TraceSize` | Size of the I/O request (bytes)           |                                |
| `Latency`   | Duration of the operation in milliseconds |                                |

**Example:**

```csv
2026-02-08 23:23:45.123,1234,notepad.exe,1024345,Read,4096,0.5
```

## 3. Network Trace (`NETWORK`)

Captures network traffic summary.

**CSV Header:**
`Ts,Pid,Comm,Saddr,Daddr,Sport,Dport,Bytes,Type`

**Fields:**

| Field   | Description                 | Notes              |
| :------ | :-------------------------- | :----------------- |
| `Ts`    | Timestamp (UTC)             |                    |
| `Pid`   | Process ID                  |                    |
| `Comm`  | Command/Process name        |                    |
| `Saddr` | Source IP address           |                    |
| `Daddr` | Destination IP address      |                    |
| `Sport` | Source Port                 |                    |
| `Dport` | Destination Port            |                    |
| `Bytes` | Number of bytes transferred |                    |
| `Type`  | Protocol type               | e.g., `TCP`, `UDP` |

**Example:**

```csv
2026-02-08 23:23:45.123,1234,chrome.exe,192.168.1.100,8.8.8.8,54321,443,1500,TCP
```

## 4. Memory Trace (`MEMORY`)

Captures memory-related events.

**CSV Header:**
`Ts,Pid,Comm,Type`

**Fields:**

| Field  | Description                   | Notes             |
| :----- | :---------------------------- | :---------------- |
| `Ts`   | Timestamp (UTC)               |                   |
| `Pid`  | Process ID                    |                   |
| `Comm` | Command/Process name (quoted) |                   |
| `Type` | Type of memory event          | e.g., `PageFault` |

**Example:**

```csv
2026-02-08 23:23:45.123,1234,"notepad.exe",PageFaultHard
```

## 5. Process Snapshot (`PROCESS`)

Periodic snapshot of running processes.

**CSV Header:**
`Ts,ProcessId,Name,CommandLine,VirtualSize,WorkingSetSize,CreationDate,CpuUsage_5s,CpuUsage_2m,CpuUsage_1h`

**Fields:**

| Field            | Description                                | Notes      |
| :--------------- | :----------------------------------------- | :--------- |
| `Ts`             | Timestamp (UTC) of the snapshot            |            |
| `ProcessId`      | Process ID                                 |            |
| `Name`           | Process name                               |            |
| `CommandLine`    | Process command line arguments             |            |
| `VirtualSize`    | Virtual memory size (bytes)                |            |
| `WorkingSetSize` | Working set (physical memory) size (bytes) |            |
| `CreationDate`   | Process creation time                      |            |
| `CpuUsage_5s`    | CPU usage over the last 5 seconds          | Percentage |
| `CpuUsage_2m`    | CPU usage over the last 2 minutes          | Percentage |
| `CpuUsage_1h`    | CPU usage over the last 1 hour             | Percentage |

**Example:**

```csv
2026-02-08 23:23:45.123,1234,notepad.exe,"C:\Windows\System32\notepad.exe",104857600,20971520,2026-02-08 23:00:00.000,0.5,0.2,0.1
```

## 6. Filesystem Snapshot (`FILESYSTEM_SNAPSHOT`)

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
