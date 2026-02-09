# Network Trace (`NETWORK`)

Captures network traffic events and summaries.

**CSV Header:**
`Ts,Pid,Comm,Saddr,Daddr,Sport,Dport,Bytes,Type,Status,ConnId,SeqNum,Proto,Mss,SndWinScale,RcvWinScale,RcvWin,WsOpt,TsOpt,SackOpt,Context,DSize`

**Fields:**

| Field         | Description                         | Notes                                                    |
| :------------ | :---------------------------------- | :------------------------------------------------------- |
| `Ts`          | Timestamp (UTC) of the event        | Format: `yyyy-MM-dd HH:mm:ss.fff`                        |
| `Pid`         | Process ID initiating the operation |                                                          |
| `Comm`        | Command/Process name                |                                                          |
| `Saddr`       | Source IP address                   |                                                          |
| `Daddr`       | Destination IP address              |                                                          |
| `Sport`       | Source Port                         |                                                          |
| `Dport`       | Destination Port                    |                                                          |
| `Bytes`       | Number of bytes transferred         | _Only for `send`, `receive`_                             |
| `Type`        | Event type                          | See [Type Values](#type-values) below                    |
| `Status`      | Status Code                         | 0 for success. See [Status Values](#status-values) below |
| `ConnId`      | Connection ID                       | Unique identifier for the TCP connection (ulong)         |
| `SeqNum`      | Sequence Number                     | TCP Sequence Number                                      |
| `Proto`       | Protocol                            | Protocol ID (e.g., for failures)                         |
| `Mss`         | Maximum Segment Size                | TCP MSS option value                                     |
| `SndWinScale` | Send Window Scale                   | TCP Window Scale option (Send)                           |
| `RcvWinScale` | Receive Window Scale                | TCP Window Scale option (Receive)                        |
| `RcvWin`      | Receive Window                      | TCP Receive Window size                                  |
| `WsOpt`       | Window Scale Option                 | TCP Window Scale Option enabled                          |
| `TsOpt`       | Timestamp Option                    | TCP Timestamp Option enabled                             |
| `SackOpt`     | SACK Option                         | TCP Selective Acknowledgment Option enabled              |
| `Context`     | Context                             | Event context (ulong)                                    |
| `DSize`       | Data Size                           | Size of the data payload                                 |

## Field Values

### Type Values

- `send`: Data sent.
- `receive`: Data received.
- `connect`: Outbound connection attempt.
- `disconnect`: Connection closed.
- `accept`: Inbound connection accepted.
- `reconnect`: Reconnection event.
- `fail`: Connection failure.
- `syn_sent`: TCP Handshake SYN sent.
- `syn_rcvd`: TCP Handshake SYN received.
- `established`: TCP Connection established.
- `retransmit`: TCP Retransmission event.

### Status Values

- `0`: Success (or Info event)
- For `fail` events, contains the error code (e.g., NTSTATUS or Winsock error code).

**Example:**

```csv
2026-02-08 23:23:45.123,1234,chrome.exe,192.168.1.100,8.8.8.8,54321,443,0,syn_sent,0,0,0,0,0,0,0,0,0,0,0,0,0
2026-02-08 23:23:45.145,1234,chrome.exe,8.8.8.8,192.168.1.100,443,54321,0,syn_rcvd,0,0,100,0,1460,8,8,65535,1,1,1,0,0
2026-02-08 23:23:45.146,1234,chrome.exe,192.168.1.100,8.8.8.8,54321,443,0,established,0,12345678,101,0,0,0,0,0,0,0,0,0,0
2026-02-08 23:23:45.150,1234,chrome.exe,192.168.1.100,8.8.8.8,54321,443,1500,send,0,12345678,101,0,0,0,0,0,0,0,0,0,0
2026-02-08 23:23:45.180,1234,chrome.exe,8.8.8.8,192.168.1.100,443,54321,3200,receive,0,12345678,101,0,0,0,0,0,0,0,0,0,0
2026-02-08 23:24:00.000,1234,chrome.exe,192.168.1.100,8.8.8.8,54321,443,0,disconnect,0,12345678,1000,0,0,0,0,0,0,0,0,0,0
```
