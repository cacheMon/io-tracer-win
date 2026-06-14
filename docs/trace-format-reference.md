# Trace Format Reference

Complete reference for all trace formats captured by IOTracer, including CSV headers, JSON schemas, and cloud storage locations.

## Cloud Storage Structure

All traces are uploaded to cloud storage with the following prefix structure:

```
windows_trace_v4_test/{deviceId}/{timestamp}/
```

Where:
- `{deviceId}` - Unique device identifier (hashed)
- `{timestamp}` - Trace session start time in format `yyyyMMdd_HHmmss`

Each trace type is stored in its own subdirectory under this prefix:
- `filesystem/` - Filesystem operation traces
- `ds/` - Disk I/O traces  
- `mr/` - Memory traces
- `nw/` - Network traces
- `driver/` - Driver operation traces
- `process/` - Process snapshot data
- `filesystem_snapshot/` - Filesystem metadata snapshots
- `system_spec/` - System hardware/software specifications (JSON)

---

## CSV Trace Formats

### Filesystem Trace (`filesystem/`)

Captures detailed file system operations.

> CSV files do **not** contain a header row. The column order below is fixed for
> every row regardless of `Op`; fields that do not apply to an operation are left
> empty (no `-1`/`0` placeholders). The path is **always** column 5 (`Filename`).

**CSV Header (column order):**
```
Ts,Op,Pid,Comm,Filename,TraceSize,CreateOptions,ShareAccess,CreateDisposition,Offset,ViewSize,FileInfoClass,FsctlCode,ThreadId,Irp,FileKey,FileAttributes,IoFlags,CommandLine
```

**Fields:**

| # | Field | Type | Description | Notes |
|---|-------|------|-------------|-------|
| 1 | `Ts` | timestamp | Timestamp (UTC) of the event | Format: `yyyy-MM-dd HH:mm:ss.ffffff` |
| 2 | `Op` | string | Operation name | `create`, `read`, `write`, `flush`, `close`, `cleanup`, `delete`, `rename`, `set_info`, `query_info`, `dir_enum`, `dir_notify`, `fs_control`, `map_file`, etc. |
| 3 | `Pid` | integer | Process ID initiating the operation | `-1` when the kernel did not attribute the event to a process (e.g. cache-manager / system-context I/O) |
| 4 | `Comm` | string | Command/Process name | Empty when the kernel did not supply a name; quoted if it contains special characters |
| 5 | `Filename` | string | Full path of the file involved | Hashed if anonymous mode enabled |
| 6 | `TraceSize` | integer | Size of the data transfer (bytes) | |
| 7 | `CreateOptions` | flags | Flags specified during file creation | Pipe-separated. Only for `create` ops |
| 8 | `ShareAccess` | flags | File sharing mode flags | Pipe-separated. Only for `create` ops |
| 9 | `CreateDisposition` | enum | Action to take on file creation | Only for `create` ops |
| 10 | `Offset` | integer | Byte offset where operation occurred | Only for `read`, `write` ops; empty otherwise |
| 11 | `ViewSize` | integer | Size of the view | Only for `map_file` family ops |
| 12 | `FileInfoClass` | string | `FILE_INFORMATION_CLASS` value | Only for `query_info` / `set_info` |
| 13 | `FsctlCode` | string | FSCTL control code | Only for `fs_control` |
| 14 | `ThreadId` | integer | Thread ID of the operation | |
| 15 | `Irp` | pointer | Pointer to I/O Request Packet | Hex format `0x...`; empty when absent |
| 16 | `FileKey` | pointer | Kernel file-object identifier | Hex format `0x...`; empty when absent |
| 17 | `FileAttributes` | flags | File attributes | Pipe-separated. Only for `create` ops |
| 18 | `IoFlags` | flags | I/O specific flags | Pipe-separated. Only for `read`, `write` ops |
| 19 | `CommandLine` | string | Full command line of the process | Empty if unavailable |

**Example:**
```csv
2026-02-08 23:23:45.123456,create,1234,notepad.exe,C:\Users\User\Documents\test.txt,0,FILE_FLAG_OVERLAPPED,FILE_SHARE_READ,OPEN_EXISTING,,,,,5678,0xFFFFAB12,0xFFFFCD34,FILE_ATTRIBUTE_NORMAL,,"C:\Windows\System32\notepad.exe"
2026-02-08 23:23:45.125789,read,1234,notepad.exe,C:\Users\User\Documents\test.txt,4096,,,,0,,,,5678,0xFFFFAB12,0xFFFFCD34,,IRP_PAGING_IO|IRP_NOCACHE,"C:\Windows\System32\notepad.exe"
```

---

### Disk Trace (`ds/`)

Captures low-level disk I/O operations.

**CSV Header:**
```
Ts,Pid,ThreadId,Comm,Sector,Operation,TraceSize,Latency,DiskNumber,Irp,IrpFlags
```

**Fields:**

| Field | Type | Description | Notes |
|-------|------|-------------|-------|
| `Ts` | timestamp | Timestamp (UTC) | Format: `yyyy-MM-dd HH:mm:ss.fff` |
| `Pid` | integer | Process ID | |
| `ThreadId` | integer | Thread ID | |
| `Comm` | string | Command/Process name | |
| `Sector` | integer | Logical sector number on the disk | |
| `Operation` | string | Operation type | `read`, `write`, `flush` |
| `TraceSize` | integer | Size of the I/O request (bytes) | |
| `Latency` | float | Duration in milliseconds | **Empty** when unknown — i.e. no matching `DiskIOInit` was seen for the IRP. The read/write/flush row is still emitted so no operation is lost. |
| `DiskNumber` | integer | Disk number | |
| `Irp` | pointer | I/O Request Packet pointer | Hex format: `0x...` |
| `IrpFlags` | flags | IRP Flags | Pipe-separated: `Nocache`, `PagingIo`, `SynchronousApi`, `Priority:*`, etc. |

**Example:**
```csv
2026-02-08 23:23:45.123,1234,5678,notepad.exe,1024345,read,4096,0.5,0,0xFFFF800012345678,Nocache|PagingIo|Priority:Normal
```

---

### Memory Trace (`mr/`)

Captures memory management, cache coherency, and working set events.

**CSV Header:**
```
Ts,Pid,ThreadId,Comm,Type,VirtualAddress,ByteCount
```

**Fields:**

| Field | Type | Description | Notes |
|-------|------|-------------|-------|
| `Ts` | timestamp | Timestamp (UTC) | Format: `yyyy-MM-dd HH:mm:ss.fff` |
| `Pid` | integer | Process ID | |
| `ThreadId` | integer | Thread ID | |
| `Comm` | string | Command/Process name | Quoted if contains spaces |
| `Type` | string | Event Type | See event types below |
| `VirtualAddress` | pointer | Virtual Address | Hex format (e.g., `0x7FF...`). 0 if N/A |
| `ByteCount` | integer | Size or auxiliary data | Bytes involved, or specific value (e.g., priority) |

**Event Types:**
- **Cache**: `HIT`, `MISS`, `DIRTY`, `WRITEBACK_START`, `WRITEBACK_END`, `EVICT`, `INVALIDATE`, `DROP`, `READAHEAD`, `RECLAIM`
- **File Cache**: `CACHE_READ`, `CACHE_WRITE`, `CACHE_FLUSH_START`, `CACHE_FLUSH_END`, `CACHE_LAZY_WRITE`, `CACHE_READ_AHEAD`, `CACHE_MAP`, `CACHE_UNMAP`, `CACHE_PURGE`
- **Working Set**: `WS_TRIM`, `WS_EXPANSION`, `WS_FAULT_IN`, `WS_FAULT_OUT`, `WS_AGING`, `WS_LOCK`, `WS_UNLOCK`
- **Modified Page Writer**: `MPW_WRITE_START`, `MPW_WRITE_END`, `MPW_THROTTLE`, `MPW_QUEUE`, `MPW_DEQUEUE`
- **Standby List**: `STANDBY_INSERT`, `STANDBY_REMOVE`, `STANDBY_REPURPOSE`
- **Memory Pressure**: `LOW_MEMORY`, `HIGH_MEMORY`, `OUT_OF_MEMORY`, `PRIORITY_TRIM`
- **Prefetch**: `PREFETCH_START`, `PREFETCH_END`, `SUPERFETCH_QUERY`, `SUPERFETCH_DECISION`
- **TLB/NUMA**: `TLB_FLUSH`, `TLB_MISS`, `NUMA_MIGRATION`, `NUMA_FAULT`
- **Special**: `GUARD`, `ALLOC`

**Example:**
```csv
2026-02-09 10:15:30.123,4560,9812,"notepad.exe",HIT,0x7FF7A2B0000,4096
2026-02-09 10:15:30.125,4560,9812,"notepad.exe",MISS,0x0,4096
2026-02-09 10:15:30.140,4560,9812,"notepad.exe",CACHE_READ,0xFFFF8B012345678,1024
```

---

### Network Trace (`nw/`)

Captures network traffic **aggregated per connection, per minute**. Rather than
one row per packet, the tracer accumulates bytes for each connection (keyed by
`proto` + 5-tuple) and emits a single summary row per active connection every
minute, carrying the bytes sent and received during that window. Connections with
no traffic for several windows are evicted. Local-only conversations (both
endpoints private/loopback) are excluded. Connection lifecycle events
(connect/accept/disconnect/retransmit/handshake) are no longer emitted as rows;
they only seed connection identity.

**CSV Header (column order):**
```
Ts,Pid,Comm,Proto,Saddr,Daddr,Sport,Dport,ConnId,BytesSent,BytesReceived
```

**Fields:**

| # | Field | Type | Description | Notes |
|---|-------|------|-------------|-------|
| 1 | `Ts` | timestamp | Flush time (UTC) marking the end of the 1-minute window | Format: `yyyy-MM-dd HH:mm:ss.ffffff` |
| 2 | `Pid` | integer | Process ID owning the connection | |
| 3 | `Comm` | string | Command/Process name | |
| 4 | `Proto` | integer | IP protocol number | `6` = TCP, `17` = UDP |
| 5 | `Saddr` | string | Local (this host) IP address | |
| 6 | `Daddr` | string | Remote peer IP address | |
| 7 | `Sport` | integer | Local port | |
| 8 | `Dport` | integer | Remote port | |
| 9 | `ConnId` | integer | Connection ID | Kernel connection identifier when available, else `0` |
| 10 | `BytesSent` | integer | Bytes sent on this connection during the window | |
| 11 | `BytesReceived` | integer | Bytes received on this connection during the window | |

**Example:**
```csv
2026-02-08 23:24:00.000000,1234,chrome.exe,6,192.168.1.100,8.8.8.8,54321,443,12345678,8421,153002
2026-02-08 23:25:00.000000,1234,chrome.exe,6,192.168.1.100,8.8.8.8,54321,443,12345678,512,2048
2026-02-08 23:25:00.000000,4321,svchost.exe,17,192.168.1.100,1.1.1.1,53124,53,0,88,264
```

---

### Driver Trace (`driver/`)

Captures lower-level driver interactions.

**CSV Header (column order):**
```
Ts,Pid,ThreadId,Comm,Operation,Irp,RequestId,MajorFunction,MinorFunction,RoutineAddr,FileObject,DeviceObject
```

**Fields:**

| # | Field | Type | Description | Notes |
|---|-------|------|-------------|-------|
| 1 | `Ts` | timestamp | Timestamp (UTC) | Format: `yyyy-MM-dd HH:mm:ss.ffffff` |
| 2 | `Pid` | integer | Process ID | |
| 3 | `ThreadId` | integer | Thread ID | |
| 4 | `Comm` | string | Command/Process name | |
| 5 | `Operation` | string | Driver operation type | `driver_call`, `driver_return`, `driver_completion`, `driver_complete_req`, `driver_complete_req_ret` |
| 6 | `Irp` | pointer | I/O Request Packet pointer | Hex format `0x...`. **Reused** by the kernel across requests — do not use alone to pair events. |
| 7 | `RequestId` | integer | Session-unique request id | Assigned at `driver_call` and retired at completion. Use this (not `Irp`) to correlate call/return/completion of one request. |
| 8 | `MajorFunction` | integer | Major function code | Empty when not reported by the event |
| 9 | `MinorFunction` | integer | Minor function code | Empty when not reported by the event |
| 10 | `RoutineAddr` | pointer | Address of routine | Hex format: `0x...` |
| 11 | `FileObject` | pointer | File object address | Hex format: `0x...` |
| 12 | `DeviceObject` | pointer | Device object address | Hex format: `0x...` |

> **Throughput note:** driver events do not carry a byte count or offset, so
> block-device throughput cannot be derived from this stream — use the `ds/`
> (disk) trace, which has `TraceSize`, `Sector`, and `Latency`.

**Example:**
```csv
2026-02-09 10:15:30.123456,4568,9102,explorer.exe,driver_call,0xFFFFFA80036F5010,42,0,0,0xFFFFF80002E71000,0xFFFFFA80036F5050,0xFFFFFA80036F6000
2026-02-09 10:15:30.123789,4568,9102,explorer.exe,driver_complete_req_ret,0xFFFFFA80036F5010,42,,,0x0,0xFFFFFA80036F5050,0xFFFFFA80036F6000
```

---

### Process Snapshot (`process/`)

Periodic snapshot of running processes.

**CSV Header:**
```
Ts,ProcessId,Name,CommandLine,VirtualSize,WorkingSetSize,CreationDate,CpuUsage_5s,CpuUsage_2m,CpuUsage_1h
```

**Fields:**

| Field | Type | Description | Notes |
|-------|------|-------------|-------|
| `Ts` | timestamp | Timestamp (UTC) of the snapshot | Format: `yyyy-MM-dd HH:mm:ss.fff` |
| `ProcessId` | integer | Process ID | |
| `Name` | string | Process name | |
| `CommandLine` | string | Process command line arguments | |
| `VirtualSize` | integer | Virtual memory size (bytes) | |
| `WorkingSetSize` | integer | Working set (physical memory) size (bytes) | |
| `CreationDate` | timestamp | Process creation time | Format: `yyyy-MM-dd HH:mm:ss.fff` |
| `CpuUsage_5s` | float | CPU usage over last 5 seconds | Percentage |
| `CpuUsage_2m` | float | CPU usage over last 2 minutes | Percentage |
| `CpuUsage_1h` | float | CPU usage over last 1 hour | Percentage |

**Example:**
```csv
2026-02-08 23:23:45.123,1234,notepad.exe,"C:\Windows\System32\notepad.exe",104857600,20971520,2026-02-08 23:00:00.000,0.5,0.2,0.1
```

---

### Filesystem Snapshot (`filesystem_snapshot/`)

Snapshot of file system state (metadata).

**CSV Header:**
```
timestamp,path,size,CreationDate,modificationDate,LastAccessTime,Attributes,Extension,IsReadOnly
```

**Fields:**

| Field | Type | Description | Notes |
|-------|------|-------------|-------|
| `timestamp` | timestamp | Snapshot timestamp | Format: `yyyy-MM-dd HH:mm:ss.fff` |
| `path` | string | Full path to the file | |
| `size` | integer | File size in bytes | |
| `CreationDate` | timestamp | File creation timestamp | Format: `yyyy-MM-dd HH:mm:ss.fff` |
| `modificationDate` | timestamp | File last modification timestamp | Format: `yyyy-MM-dd HH:mm:ss.fff` |
| `LastAccessTime` | timestamp | File last access timestamp | Format: `yyyy-MM-dd HH:mm:ss.fff` |
| `Attributes` | string | File attributes | e.g., `Archive`, `Directory`, `Hidden` |
| `Extension` | string | File extension | Including the dot (e.g., `.txt`) |
| `IsReadOnly` | boolean | Whether the file is read-only | `True` or `False` |

**Example:**
```csv
2026-02-09 23:00:00.000,C:/Users/User/Documents/test.txt,1024,2026-02-08 23:00:00.000,2026-02-08 23:23:45.123,2026-02-09 10:00:00.000,Archive,.txt,False
```

---

## JSON Trace Formats

### System Snapshot (`system_spec/`)

Hardware and software specifications captured at trace start. Stored as separate JSON files.

**Cloud Storage Location:**
```
windows_trace_v4_test/{deviceId}/{timestamp}/system_spec/
```

#### cpu_info.json

CPU hardware specifications.

**Schema:**
```json
{
  "brand": "string (nullable)",
  "cores_logical": "integer",
  "cores_physical": "integer",
  "frequency_mhz": "float (nullable)",
  "frequency_min_mhz": "float (nullable)",
  "frequency_max_mhz": "float (nullable)"
}
```

**Example:**
```json
{
  "brand": "Intel(R) Core(TM) i7-10700 CPU @ 2.90GHz",
  "cores_logical": 16,
  "cores_physical": 8,
  "frequency_mhz": 2900.0,
  "frequency_min_mhz": null,
  "frequency_max_mhz": 2900.0
}
```

---

#### memory_info.json

System memory statistics.

**Schema:**
```json
{
  "total_bytes": "integer",
  "available_bytes": "integer",
  "used_bytes": "integer",
  "percent_used": "float",
  "total_gb": "float",
  "available_gb": "float",
  "swap_total_bytes": "integer",
  "swap_used_bytes": "integer",
  "swap_free_bytes": "integer"
}
```

**Example:**
```json
{
  "total_bytes": 17062027264,
  "available_bytes": 9073254400,
  "used_bytes": 7988772864,
  "percent_used": 46.8,
  "total_gb": 15.89,
  "available_gb": 8.45,
  "swap_total_bytes": 2147483648,
  "swap_used_bytes": 0,
  "swap_free_bytes": 2147483648
}
```

---

#### disk_info.json

Storage devices, partitions, and GPU information.

**Schema:**
```json
{
  "storage_devices": ["string"],
  "partitions": [
    {
      "device": "string",
      "mountpoint": "string",
      "fstype": "string",
      "opts": "string",
      "total_bytes": "integer",
      "used_bytes": "integer",
      "free_bytes": "integer",
      "percent_used": "float"
    }
  ],
  "gpus": ["string"]
}
```

**Example:**
```json
{
  "storage_devices": [
    "Samsung SSD 980 PRO 1TB  931.51 GB",
    "WDC WD10EZEX-00W  931.51 GB"
  ],
  "partitions": [
    {
      "device": "C:\\",
      "mountpoint": "C:\\",
      "fstype": "NTFS",
      "opts": "Fixed",
      "total_bytes": 500107862016,
      "used_bytes": 125829120000,
      "free_bytes": 348827648000,
      "percent_used": 26.5
    }
  ],
  "gpus": [
    "NVIDIA GeForce RTX 3080"
  ]
}
```

---

#### network_info.json

Network interface information.

**Schema:**
```json
{
  "interfaces": {
    "interface_name": {
      "addresses": [
        {
          "family": "string",
          "address": "string",
          "netmask": "string (nullable)",
          "broadcast": "string (nullable)"
        }
      ],
      "is_up": "boolean",
      "speed_mbps": "integer (nullable)",
      "mtu": "integer"
    }
  },
  "hostname": "string"
}
```

**Example:**
```json
{
  "interfaces": {
    "Ethernet": {
      "addresses": [
        {
          "family": "AF_INET",
          "address": "192.168.1.100",
          "netmask": "255.255.255.0",
          "broadcast": null
        },
        {
          "family": "AF_PACKET",
          "address": "00:1a:2b:3c:4d:5e",
          "netmask": null,
          "broadcast": null
        }
      ],
      "is_up": true,
      "speed_mbps": 1000,
      "mtu": 1500
    }
  },
  "hostname": "DESKTOP-PC"
}
```

---

#### os_info.json

Operating system information.

**Schema:**
```json
{
  "system": "string",
  "release": "string",
  "version": "string",
  "machine": "string",
  "hostname": "string",
  "country": "string"
}
```

**Example:**
```json
{
  "system": "Windows",
  "release": "10.0.22000",
  "version": "Microsoft Windows 11 Pro (Build 22000)",
  "machine": "x64",
  "hostname": "DESKTOP-PC",
  "country": "US"
}
```

---

## File Naming Convention

CSV files follow this naming pattern:
```
{type}_{timestamp}_{deviceId}.csv.zst
```

Where:
- `{type}` - Trace type: `fs`, `ds`, `mr`, `nw`, `driver`, `process`, `filesystem_snapshot`
- `{timestamp}` - File creation time in format `yyyyMMdd_HHmmss`
- `{deviceId}` - Unique device identifier (hashed)
- `.zst` - Zstandard compression extension

**Example:**
```
filesystem/fs_20260212_143022_a1b2c3d4e5f6.csv.zst
ds/ds_20260212_143022_a1b2c3d4e5f6.csv.zst
```

---

## Data Collection Notes

### Timestamps
All timestamps are in UTC and use the format `yyyy-MM-dd HH:mm:ss.fff` (millisecond precision).

### Anonymous Mode
When anonymous mode is enabled:
- File paths in `Filename` field are SHA256 hashed
- Maintains privacy while preserving trace structure

### Compression
All CSV files are compressed using Zstandard (.zst) compression before upload to reduce storage and bandwidth requirements.

### Flush & Upload Frequency
In-memory trace buffers are flushed to local compressed chunks based on:
- Memory pressure (adaptive sizing)
- Minimum 10-second flush interval
- 256 MB maximum in-memory buffer size

Network traffic is the exception: it is summarized **per connection, once per
minute** (see the Network Trace section).

Compressed chunks are then batched into a local per-trace-type buffer and
uploaded to cloud storage as a single object once the buffer reaches **100 MB**
or is **20 minutes** old (whichever comes first).

### Device Identification
The `{deviceId}` is a persistent unique identifier generated for each device, used to correlate traces from the same machine across multiple trace sessions.
