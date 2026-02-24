# Disk Trace (`DISK`)

Captures low-level disk I/O operations.

## How Latency is Measured

Disk latency is calculated by tracking the lifecycle of an I/O Request Packet (IRP) from its initiation to its completion using ETW (Event Tracing for Windows) events:

1. **Initialization**: The tracer listens for initialization events like `DiskIOReadInit` and `DiskIOWriteInit`. When an I/O request is initiated, the tracer records the start time (`TimeStampRelativeMSec`) in an active requests dictionary, using the `Irp` (I/O Request Packet pointer) as the key.
2. **Completion**: When the corresponding completion event occurs (`DiskIORead`, `DiskIOWrite`, or `DiskIOFlushBuffers`), the tracer uses the `Irp` of the completion event to look up the start time from the active requests dictionary.
3. **Calculation**: The `Latency` (in milliseconds) is calculated as the difference between the completion event's timestamp and the recorded start timestamp.

_Note: For `read` and `write` operations, if no matching start time is found for a given `Irp`, the completion event is ignored to avoid inaccurate data. For `flush` operations, if no start time is found, a latency of `0` is reported._

**CSV Header:**
`Ts,Pid,ThreadId,Comm,Sector,Operation,TraceSize,Latency,DiskNumber,Irp,IrpFlags`

**Fields:**

| Field        | Description                               | Notes                                           |
| :----------- | :---------------------------------------- | :---------------------------------------------- |
| `Ts`         | Timestamp (UTC)                           | Format: `yyyy-MM-dd HH:mm:ss.fff`               |
| `Pid`        | Process ID                                |                                                 |
| `ThreadId`   | Thread ID                                 |                                                 |
| `Comm`       | Command/Process name                      |                                                 |
| `Sector`     | Logical sector number on the disk         |                                                 |
| `Operation`  | Operation type                            | See [Operation Values](#operation-values) below |
| `TraceSize`  | Size of the I/O request (bytes)           |                                                 |
| `Latency`    | Duration of the operation in milliseconds |                                                 |
| `DiskNumber` | Disk number                               |                                                 |
| `Irp`        | I/O Request Packet pointer                | Hex format: `0x...`                             |
| `IrpFlags`   | IRP Flags                                 | See [IrpFlags Values](#irpflags-values) below   |

## Field Values

### Operation Values

`read`, `write`, `flush`

### IrpFlags Values

Pipe-separated flags:

- `Nocache`, `PagingIo`, `SynchronousApi`, `AssociatedIrp`
- `BufferedIO`, `DeallocateBuffer`, `SynchronousPagingIO`
- `Create`, `Read`, `Write`, `Close`, `DeferIOCompletion`
- `ObQueryName`, `HoldDeviceQueue`
- `Priority:Low`, `Priority:Normal`, `Priority:High`, `Priority:Critical`, `Priority:<val>`

**Example:**

```csv
2026-02-08 23:23:45.123,1234,5678,notepad.exe,1024345,read,4096,0.5,0,0xFFFF800012345678,Nocache|PagingIo|Priority:Normal
```
