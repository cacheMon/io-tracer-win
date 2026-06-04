# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**IO Tracer for Windows** is a real-time event collection and analysis tool that captures file I/O, disk I/O, network activity, memory events, and process state from Windows systems. Data is compressed (Zstd) and uploaded to S3 for research or monitoring purposes.

- **Framework**: .NET 8 Windows Forms
- **Core Technology**: ETW (Event Tracing for Windows) via `Microsoft.Diagnostics.Tracing.TraceEvent`
- **UI**: System tray application with configuration dialog
- **Storage**: Compressed CSV output + S3 cloud upload (AWS R2)
- **Optional**: Minifilter kernel driver for enhanced tracing (WIP)

## Project Structure

```
IOTracesCORE/
├── Program.cs               # Entry point; creates tray icon and UI
├── Tracer.cs                # ETW session management (core tracing loop)
├── WriterManager.cs         # Output buffering and file management
├── TracerConfigForm.cs      # Configuration UI
├── handlers/                # ETW event processors (FilesystemHandlers, DiskHandlers, etc.)
├── snappers/                # Periodic system snapshots (ProcessSnapper, FilesystemSnapper)
├── trace/                   # Data model classes (FilesystemTrace, DiskTrace, etc.)
├── cloudstorage/            # S3 upload and connection management
└── utils/                   # Helpers (ProcessFilter, DriverService, etc.)
```

## Build and Run

```powershell
# Build the project
dotnet build IOTracer.sln -c Release

# Run directly (requires admin for ETW)
dotnet run --project IOTracesCORE/IOTracesCORE.csproj

# Or from IDE: F5 (requires Visual Studio with admin privileges)
```

**Requirements**: Windows 10+, admin privileges (ETW sessions require elevation), .NET 8 runtime

## Key Architecture Patterns

### 1. ETW Event Flow
```
Kernel Event → ETW Session Buffer → TraceEvent Library → Handler (OnRead, OnWrite, etc.) → WriterManager → Disk
```

**Files involved**:
- `Tracer.cs`: Creates `TraceEventSession`, wires handlers to kernel events
- `handlers/FilesystemHandlers.cs`: Implements `OnRead()`, `OnWrite()`, etc.
- `WriterManager.cs`: Accumulates events in memory (StringBuilder), flushes to disk

### 2. Event Processing Pattern
Events arrive asynchronously from ETW and are processed in handlers. Each handler:
1. Parses the ETW event data
2. Applies filters (process name, file path, etc.)
3. Creates a trace object (e.g., `FilesystemTrace`)
4. Passes it to `WriterManager.AppendEvent()`

**Example**: `FilesystemHandlers.OnRead()` → creates `FilesystemTrace` → `WriterManager.LogFilesystemEvent()`

### 3. Output Buffering Strategy
`WriterManager` uses in-memory `StringBuilder` buffers per trace type (fs, disk, memory, network, driver). Events are accumulated until:
- Buffer memory pressure exceeds threshold (MEMORY_PRESSURE_RATIO = 1% of available RAM)
- Timer interval expires (FS_FLUSH_INTERVAL = 2 seconds, MIN_FLUSH_INTERVAL = 10 seconds)
- Either condition triggers a flush to CSV files

**Relevant thresholds** (`WriterManager.cs`):
- `ABSOLUTE_MAX_BYTES`: 256 MB hard limit
- `MEMORY_PRESSURE_RATIO`: 1% of available memory triggers flush
- `FS_FLUSH_INTERVAL`: 2 seconds for filesystem traces

### 4. Snapshots
Separate background threads take periodic snapshots:
- `ProcessSnapper`: Running processes and their command lines
- `FilesystemSnapper`: File tree (optional, can be slow)

These write to separate CSV files and may take multiple parts (e.g., `filesystem_snapshot_1.csv`, `filesystem_snapshot_2.csv`).

### 5. Cloud Upload
`ObjectStorageHandler` runs in a background thread:
- Monitors disk output directory for new files
- Uploads compressed (.zst) files to S3
- Reports upload progress via `UploadedFiles` counter
- Implements reconnection logic with exponential backoff

## Common Development Tasks

### Adding a New Event Type
1. Create a trace data class in `trace/` (e.g., `NewTrace.cs`)
2. Add a handler in `handlers/` with an `OnEventName()` method
3. Register the handler in `Tracer.RunOneSession()` (wire to `kernel.EventName +=`)
4. Add output columns and CSV writing logic to `WriterManager` (new StringBuilder + AppendNewEvent method)

### Debugging Event Loss
- Start tracer with all providers enabled (see `Tracer.RunOneSession()`)
- Monitor ETW buffer stats: `logman query "IOTracer" -ets` → check `Buffers Lost`
- Compare expected vs. actual events in output
- Add event counting in handlers to identify where loss occurs
- See `docs/missing_events.md` for measurement strategy

### Filtering Events
- `ProcessFilter.cs`: Logic for matching process names and PIDs
- `FilesystemHandlers.cs` and other handlers call `ProcessFilter.Match()` before logging
- To add process or path filters, update the filter logic in handlers

### Modifying Output Format
- CSV schema is defined in each handler's append method (e.g., `WriterManager.LogFilesystemEvent()`)
- Column headers are written on first event
- To add columns, update the trace data class and the append method

## ETW and TraceEvent Tips

- **Kernel providers**: Enabled via `session.EnableKernelProvider(KernelTraceEventParser.Keywords.*)`
- **Custom providers**: Enabled via `session.EnableProvider(guid)` with level and keywords
- **Real-time vs. file-based**: Current setup uses real-time sessions (events stream directly, not buffered to file first)
- **Session cleanup**: Orphaned sessions can block startup; `CleanupOrphanedSession()` handles this
- **Buffer loss tracking**: ETW exposes `Buffers Lost` counter; use `logman query` to check

## Configuration and Runtime State

- **Config file**: `IOTracerConfig.cs` loads metadata config (TOML/JSON)
- **Credentials**: `CredentialManagement` library for secure AWS credentials
- **Device ID**: `PathHasher.deviceId` — persisted device identifier for uploads
- **Tray icon**: Shows session duration, file count, upload status via `WriterManager` static properties

## Current Limitations and WIP

- **Minifilter driver**: Branch named `minifilter`, not yet integrated (see `docs/minifilter-implementation-progress.md`)
- **Event loss**: Under investigation (see `docs/missing_events.md`); suspected buffer saturation
- **Filename resolution**: ETW doesn't always resolve file paths; cache learning is ongoing

## Documentation References

- **Trace format**: `docs/trace-format-reference.md`
- **Trace types**: `docs/trace-types/` — detailed schema for each trace type
- **Incomplete snapshot handling**: `docs/incomplete-snapshot-handling.md`
- **Known issues**: `docs/issue.md`

## Dependencies

Key NuGet packages:
- `Microsoft.Diagnostics.Tracing.TraceEvent` (3.1.24): ETW event capture
- `AWSSDK.S3` (4.0.7.14): S3 upload
- `CsvHelper` (33.1.0): CSV writing
- `ZstdSharp.Port` (0.8.6): Zstandard compression
- `Velopack` (0.0.1298): Auto-update framework
- `System.Management` (9.0.9): WMI queries for process info
- `CredentialManagement` (1.0.2): Secure credential storage

## Testing and Debugging

- **Debug output**: Uses `Debug.WriteLine()` throughout; view in Visual Studio Debug Output window
- **Dev mode**: Pass `devMode: true` to `WriterManager` to enable additional logging
- **Manual ETW inspection**: `Get-WinEvent -LogName "Microsoft-Windows-Kernel-Trace"` (PowerShell)
- **Performance Monitor**: Add counters for ETW sessions to watch buffer usage in real-time
