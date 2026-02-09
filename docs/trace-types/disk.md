# Disk Trace (`DISK`)

Captures low-level disk I/O operations.

**CSV Header:**
`Ts,Pid,ThreadId,Comm,Sector,Operation,TraceSize,Latency,DiskNumber,Irp,IrpFlags`

**Fields:**

| Field        | Description                               | Notes                             |
| :----------- | :---------------------------------------- | :-------------------------------- |
| `Ts`         | Timestamp (UTC)                           | Format: `yyyy-MM-dd HH:mm:ss.fff` |
| `Pid`        | Process ID                                |                                   |
| `ThreadId`   | Thread ID                                 |                                   |
| `Comm`       | Command/Process name                      |                                   |
| `Sector`     | Logical sector number on the disk         |                                   |
| `Operation`  | Operation type                            | Values: `read`, `write`, `flush`  |
| `TraceSize`  | Size of the I/O request (bytes)           |                                   |
| `Latency`    | Duration of the operation in milliseconds |                                   |
| `DiskNumber` | Disk number                               |                                   |
| `Irp`        | I/O Request Packet pointer                | Hex format: `0x...`               |

| `IrpFlags` | IRP Flags | Piped string. Possible values: `Nocache`, `PagingIo`, `SynchronousApi`, `AssociatedIrp`, `BufferedIO`, `DeallocateBuffer`, `SynchronousPagingIO`, `Create`, `Read`, `Write`, `Close`, `DeferIOCompletion`, `ObQueryName`, `HoldDeviceQueue`, `Priority:Low`, `Priority:Normal`, `Priority:High`, `Priority:Critical`, `Priority:<val>` |

**Example:**

```csv
2026-02-08 23:23:45.123,1234,5678,notepad.exe,1024345,read,4096,0.5,0,0xFFFF800012345678,Nocache|PagingIo|Priority:Normal
```
