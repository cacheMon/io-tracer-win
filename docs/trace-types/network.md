# Network Trace (`NETWORK`)

Captures network traffic **aggregated per connection, per minute**. Rather than
one row per packet, the tracer accumulates bytes for each connection (keyed by
`proto` + 5-tuple) and emits a single summary row per active connection every
minute, carrying the bytes sent and received during that window. Connections with
no traffic for several windows are evicted. Local-only conversations (both
endpoints private/loopback) are excluded. Connection lifecycle events
(connect/accept/disconnect/retransmit/handshake) are **not** emitted as rows; they
only seed connection identity.

**CSV Header:**
`Ts,Pid,Comm,Proto,Saddr,Daddr,Sport,Dport,ConnId,BytesSent,BytesReceived`

**Fields:**

| Field           | Description                                            | Notes                                            |
| :-------------- | :---------------------------------------------------- | :----------------------------------------------- |
| `Ts`            | Flush time (local wall-clock) marking the end of the 1-minute window | Format: `yyyy-MM-dd HH:mm:ss.ffffff` |
| `Pid`           | Process ID owning the connection                      |                                                  |
| `Comm`          | Command/Process name                                  |                                                  |
| `Proto`         | IP protocol number                                    | `6` = TCP, `17` = UDP                            |
| `Saddr`         | Local (this host) IP address                          |                                                  |
| `Daddr`         | Remote peer IP address                                |                                                  |
| `Sport`         | Local port                                            |                                                  |
| `Dport`         | Remote port                                           |                                                  |
| `ConnId`        | Connection ID                                         | Kernel connection identifier when available, else `0` |
| `BytesSent`     | Bytes sent on this connection during the window       |                                                  |
| `BytesReceived` | Bytes received on this connection during the window   |                                                  |

**Example:**

```csv
2026-02-08 23:24:00.000000,1234,chrome.exe,6,192.168.1.100,8.8.8.8,54321,443,12345678,8421,153002
2026-02-08 23:25:00.000000,1234,chrome.exe,6,192.168.1.100,8.8.8.8,54321,443,12345678,512,2048
2026-02-08 23:25:00.000000,4321,svchost.exe,17,192.168.1.100,1.1.1.1,53124,53,0,88,264
```
