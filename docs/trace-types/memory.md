# Memory Trace (`MEMORY`)

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
