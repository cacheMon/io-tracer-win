# IO Tracer for Windows

A Windows system-tray application that captures low-level I/O activity using
[ETW (Event Tracing for Windows)](https://learn.microsoft.com/en-us/windows/win32/etw/about-event-tracing),
compresses each trace stream with [Zstandard](https://facebook.github.io/zstd/),
and (optionally) uploads the results to cloud object storage for research.

📖 Full documentation: [raflyhangga.github.io/iotracerdocs](https://raflyhangga.github.io/iotracerdocs/)
The Markdown sources also live in [`docs/`](docs/).

## What it captures

The tracer subscribes to the NT Kernel Logger and a few user-mode providers and
writes one CSV stream per category (see [`docs/trace-types/`](docs/trace-types/)):

| Category | Contents |
| --- | --- |
| Filesystem | create / read / write / close / rename / delete / dir-enum / FSCTL … |
| Disk | physical disk reads, writes, flushes and their init events |
| Driver | driver major-function calls, completion routines |
| Network | TCP/UDP send, receive, connect, disconnect, retransmit, handshakes |
| Memory | hard faults, virtual allocations, memory-manager events |
| Snapshots | one-time filesystem and process snapshots taken at startup |
| System spec | a one-time hardware/OS specification capture |

Each run writes to `./output/windows_trace/<deviceId>/<timestamp>/` and rotates +
compresses files (`*.csv.zst`) as buffers fill, based on adaptive
memory-pressure thresholds (see [`WriterManager`](IOTracesCORE/WriterManager.cs)).

## Requirements

- **Windows 10/11** (x64).
- **Administrator privileges** — ETW kernel sessions require elevation, so the
  app ships with a `requireAdministrator` manifest and will prompt via UAC.
- **.NET 8 SDK** to build (the published artifact is self-contained, so end
  users do not need .NET installed).

## Build & run

```powershell
# Restore and run from source (must be an elevated shell)
dotnet run --project IOTracesCORE/IOTracesCORE.csproj

# Produce a self-contained single-file build (what CI ships)
dotnet publish IOTracesCORE/IOTracesCORE.csproj `
  -c Release -r win-x64 --self-contained `
  -o publish/win-x64 /p:PublishSingleFile=true
```

On launch a configuration dialog lets you choose:

- **Anonymous** — hash file paths / process identifiers before writing.
- **Enable upload** — stream compressed traces to the cloud worker endpoint.
- **Auto-start** — register the app under `HKCU\…\CurrentVersion\Run`.
- **Dev mode** — extra diagnostics (e.g. logging events with empty filenames).

The app then minimizes to the system tray; left-click the tray icon or use
**Show Status** to see live counters, and **Exit** to stop and flush cleanly.

## Tests

Pure-logic helpers are covered by an xUnit project:

```powershell
dotnet test IOTracesCORE.Tests/IOTracesCORE.Tests.csproj
```

## Project layout

```
IOTracesCORE/
  Program.cs            Tray-app entry point (NotifyIcon + lifecycle)
  Tracer.cs             ETW session wiring, reconnect/restart loop
  WriterManager.cs      Buffering, rotation, zstd compression, flush policy
  handlers/             ETW event callbacks per category
  trace/                CSV record types + flag-formatting helpers
  snapper/              One-time filesystem / process / system snapshots
  cloudstorage/         R2 worker client + upload queue
  utils/                Path hashing, privilege, process cache, config …
IOTracesCORE.Tests/     xUnit tests for pure helpers
docs/                   Trace-format reference and per-type documentation
```

## Continuous integration

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on `windows-latest`
for every push / PR: it restores, runs the unit tests, then publishes the
self-contained `win-x64` build and uploads it as an artifact.
