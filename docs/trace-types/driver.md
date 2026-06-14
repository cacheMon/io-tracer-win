# Driver Trace (`DRIVER`)

Captures lower-level driver interactions, such as major function calls, completion routines, and request completions.

**CSV Header:**
`Ts,Pid,ThreadId,Comm,Operation,Irp,RequestId,MajorFunction,MinorFunction,RoutineAddr,FileObject,DeviceObject`

**Fields:**

| Field           | Description                                          | Notes                                           |
| :-------------- | :--------------------------------------------------- | :---------------------------------------------- |
| `Ts`            | Timestamp (local wall-clock)                         | Format: `yyyy-MM-dd HH:mm:ss.ffffff`            |
| `Pid`           | Process ID                                           |                                                 |
| `ThreadId`      | Thread ID                                            |                                                 |
| `Comm`          | Command/Process name                                 |                                                 |
| `Operation`     | Driver operation type                                | See [Operation Values](#operation-values) below |
| `Irp`           | I/O Request Packet pointer                           | Hex format: `0x...`. **Reused** by the kernel across requests — do not use alone to pair events. |
| `RequestId`     | Session-unique request ID                            | Assigned at `driver_call` and retired at completion. Use this (not `Irp`) to correlate call/return/completion of one request. |
| `MajorFunction` | Major function code of the IRP                       | Integer (e.g., `0` for IRP_MJ_CREATE). Empty when not reported by the event |
| `MinorFunction` | Minor function code of the IRP                       | Integer. Empty when not reported by the event   |
| `RoutineAddr`   | Address of the routine being called or returned from | Hex format: `0x...`                             |
| `FileObject`    | Address of the file object associated with the IRP   | Hex format: `0x...`                             |
| `DeviceObject`  | Address of the device object associated with the IRP | Hex format: `0x...`                             |

## Field Values

### Operation Values

- `driver_call`: Start of a driver major function call.
- `driver_return`: Return from a driver major function call.
- `driver_completion`: Completion routine execution.
- `driver_complete_req`: `IoCompleteRequest` call.
- `driver_complete_req_ret`: `IoCompleteRequest` return.

**Example:**

```csv
2026-02-09 10:15:30.123456,4568,9102,explorer.exe,driver_call,0xFFFFFA80036F5010,42,0,0,0xFFFFF80002E71000,0xFFFFFA80036F5050,0xFFFFFA80036F6000
```
