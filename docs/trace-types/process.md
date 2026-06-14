# Process Snapshot (`PROCESS`)

Periodic snapshot (5 minutes) of running processes.

**CSV Header:**
`Ts,ProcessId,Name,CommandLine,VirtualSize,WorkingSetSize,CreationDate,CpuUsage_5s,CpuUsage_2m,CpuUsage_1h`

**Fields:**

| Field            | Description                                | Notes                             |
| :--------------- | :----------------------------------------- | :-------------------------------- |
| `Ts`             | Timestamp (local wall-clock) of the snapshot | Format: `yyyy-MM-dd HH:mm:ss.ffffff` |
| `ProcessId`      | Process ID                                 |                                   |
| `Name`           | Process name                               |                                   |
| `CommandLine`    | Process command line arguments             |                                   |
| `VirtualSize`    | Virtual memory size (bytes)                |                                   |
| `WorkingSetSize` | Working set (physical memory) size (bytes) |                                   |
| `CreationDate`   | Process creation time                      | Format: `yyyy-MM-dd HH:mm:ss.ffffff` |
| `CpuUsage_5s`    | CPU usage over the last 5 seconds          | Percentage                        |
| `CpuUsage_2m`    | CPU usage over the last 2 minutes          | Percentage                        |
| `CpuUsage_1h`    | CPU usage over the last 1 hour             | Percentage                        |

**Example:**

```csv
2026-02-08 23:23:45.123456,1234,notepad.exe,"C:\Windows\System32\notepad.exe",104857600,20971520,2026-02-08 23:00:00.000000,0.5,0.2,0.1
```
