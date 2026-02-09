# Network Trace (`NETWORK`)

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
