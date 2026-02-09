# Memory Trace (`MEMORY`)

Captures memory management, cache coherency, and working set events. This trace offers detailed visibility into how applications interact with the Windows Memory Manager.

**CSV Header:**
`Ts,Pid,ThreadId,Comm,Type,VirtualAddress,ByteCount`

**Fields:**

| Field            | Description            | Notes                                                        |
| :--------------- | :--------------------- | :----------------------------------------------------------- |
| `Ts`             | Timestamp (UTC)        | Format: `yyyy-MM-dd HH:mm:ss.fff`                            |
| `Pid`            | Process ID             |                                                              |
| `ThreadId`       | Thread ID              | ID of the thread that triggered the event                    |
| `Comm`           | Command/Process name   | Quoted if contains spaces                                    |
| `Type`           | Event Type             | See [Type Values](#type-values) below                        |
| `VirtualAddress` | Virtual Address        | Hexadecimal format (e.g., `0x7FF...`). 0 if not applicable.  |
| `ByteCount`      | Size or Auxiliary Data | Number of bytes involved, or specific value (e.g., priority) |

## Field Values

### Type Values

**Basic Cache**

- `HIT`: Soft page fault - page found in memory (standby/modified list). No disk I/O.
- `MISS`: Hard page fault - page not in memory, requires disk I/O. Critical for performance.
- `DIRTY`: Copy-on-write fault. Page transitions from shared read-only to private writable.
- `WRITEBACK_START`: Modified page write initiated.
- `WRITEBACK_END`: Modified page write completed.
- `EVICT`: Page removed from working set (Virtual Free).
- `INVALIDATE`: Access violation - attempt to access protected or unmapped memory.
- `DROP`: Page dropped from cache.
- `READAHEAD`: Sequential read optimization.
- `RECLAIM`: Memory pressure reclaim.

**File System Cache**

- `CACHE_READ`: File data read from system cache (fast path).
- `CACHE_WRITE`: File data written to system cache (buffered write).
- `CACHE_FLUSH_START`: Cache flush initiated (forced write-through).
- `CACHE_FLUSH_END`: Cache flush completed.
- `CACHE_LAZY_WRITE`: Lazy writer background flush.
- `CACHE_READ_AHEAD`: Predictive read-ahead detected.
- `CACHE_MISS_PARTIAL`: Partial cache hit.
- `CACHE_MAP`: File section mapped into cache (Memory Mapped File).
- `CACHE_UNMAP`: File section unmapped from cache.
- `CACHE_PURGE`: Cache purged for file.

**Working Set**

- `WS_TRIM`: Working set trimmed due to memory pressure.
- `WS_EXPANSION`: Working set expanded (process allocated more memory).
- `WS_FAULT_IN`: Page faulted into working set.
- `WS_FAULT_OUT`: Page faulted out of working set.
- `WS_AGING`: Working set page aged.
- `WS_LOCK`: Page locked in working set.
- `WS_UNLOCK`: Page unlocked from working set.

**Modified Page Writer**

- `MPW_WRITE_START`: Modified page writer flushing dirty pages.
- `MPW_WRITE_END`: Modified page write completed.
- `MPW_THROTTLE`: Modified page writer throttled.
- `MPW_QUEUE`: Page queued to modified list.
- `MPW_DEQUEUE`: Page dequeued from modified list.

**Standby List**

- `STANDBY_INSERT`: Page moved to standby list (clean page cache). Priority in `ByteCount`.
- `STANDBY_REMOVE`: Page removed from standby list (repurposed/reclaimed).
- `STANDBY_REPURPOSE`: Standby page repurposed.

**Memory Pressure**

- `LOW_MEMORY`: System entered low memory condition. `ByteCount` = AvailableBytes.
- `HIGH_MEMORY`: System entered high memory condition.
- `OUT_OF_MEMORY`: System is out of memory.
- `PRIORITY_TRIM`: Priority-based trim.

**Prefetch**

- `PREFETCH_START`: Windows Prefetcher started prefetch operation.
- `PREFETCH_END`: Prefetch operation completed.
- `SUPERFETCH_QUERY`: Superfetch query.
- `SUPERFETCH_DECISION`: Superfetch decision made.

**TLB / NUMA**

- `TLB_FLUSH`: Translation Lookaside Buffer flushed.
- `TLB_MISS`: TLB miss.
- `NUMA_MIGRATION`: Page migrated across NUMA nodes.
- `NUMA_FAULT`: NUMA fault.

**Special**

- `GUARD`: Guard page fault (stack growth detection).
- `ALLOC`: Virtual memory allocation.

**Example:**

```csv
2026-02-09 10:15:30.123,4560,9812,"notepad.exe",HIT,0x7FF7A2B0000,4096
2026-02-09 10:15:30.125,4560,9812,"notepad.exe",MISS,0x0,4096
2026-02-09 10:15:30.140,4560,9812,"notepad.exe",CACHE_READ,0xFFFF8B012345678,1024
2026-02-09 10:15:30.155,0,0,"System",LOW_MEMORY,0x0,2147483648
```
