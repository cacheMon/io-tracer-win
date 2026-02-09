# Driver Trace

Driver traces capture lower-level driver interactions, such as major function calls, completion routines, and request completions. These events are separated from standard Disk traces to provide more granular visibility into driver behavior.

## CSV Output

The driver trace output is located in the `driver` folder within the trace directory.

| Field             | Description                                          | Possible Values                                                                                               |
| :---------------- | :--------------------------------------------------- | :------------------------------------------------------------------------------------------------------------ |
| **Ts**            | Timestamp of the event                               | yyyy-MM-dd HH:mm:ss.fff                                                                                       |
| **Pid**           | Process ID associated with the thread                | Integer                                                                                                       |
| **ThreadId**      | Thread ID that generated the event                   | Integer                                                                                                       |
| **Comm**          | Name of the process                                  | String (e.g., `explorer.exe`)                                                                                 |
| **Operation**     | The type of driver operation being traced            | `driver_call`<br>`driver_return`<br>`driver_completion`<br>`driver_complete_req`<br>`driver_complete_req_ret` |
| **Irp**           | Address of the I/O Request Packet (IRP)              | Hex String (e.g., `0xFFFFFA80036F5010`)                                                                       |
| **MajorFunction** | The major function code of the IRP                   | Integer (e.g., `0` for IRP_MJ_CREATE)                                                                         |
| **MinorFunction** | The minor function code of the IRP                   | Integer                                                                                                       |
| **RoutineAddr**   | Address of the routine being called or returned from | Hex String                                                                                                    |
| **FileObject**    | Address of the file object associated with the IRP   | Hex String                                                                                                    |
| **DeviceObject**  | Address of the device object associated with the IRP | Hex String                                                                                                    |

## Operations Detail

- **driver_call**: Indicates the start of a driver major function call.
- **driver_return**: Indicates the return from a driver major function call.
- **driver_completion**: Triggers when a completion routine is executed.
- **driver_complete_req**: Triggers when `IoCompleteRequest` is called.
- **driver_complete_req_ret**: Triggers when `IoCompleteRequest` returns.

## Example

```csv
Ts,Pid,ThreadId,Comm,Operation,Irp,MajorFunction,MinorFunction,RoutineAddr,FileObject,DeviceObject
2026-02-09 10:15:30.123,4568,9102,explorer.exe,driver_call,0xFFFFFA80036F5010,0,0,0xFFFFF80002E71000,0xFFFFFA80036F5050,0xFFFFFA80036F6000
2026-02-09 10:15:30.125,4568,9102,explorer.exe,driver_return,0xFFFFFA80036F5010,0,0,0xFFFFF80002E71000,0xFFFFFA80036F5050,0xFFFFFA80036F6000
```
