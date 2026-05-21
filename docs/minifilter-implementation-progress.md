# Minifilter Filename Resolution: Implementation Progress

**Status:** Phase 2 (Userspace) ✅ Complete | Phase 1 (Kernel Driver) ⏳ Pending WDK

---

## Overview

Implement a Windows minifilter kernel driver that intercepts file creates and sends normalized absolute paths to the userspace tracer, pre-seeding the `nameByObj` cache. This solves the **pre-existing file resolution problem** in ETW (files opened before the trace started have no name events).

**Architecture:** Minifilter sends messages via filter communication port → MinifilterPortClient reads in background thread → nameByObj cache pre-populated → ETW events resolve to correct filenames.

---

## Phase 1: Kernel Driver (PENDING - Requires WDK)

### Files to Create

```
IOTracerMinifilter/
├── IOTracerMinifilter.vcxproj         WDK C++ driver project
├── IOTracerMinifilter.inf             Installation manifest (altitude 370030)
├── shared/
│   └── iotracer_shared.h              Message struct (shared with C#)
└── src/
    ├── driver.c                        DriverEntry, FltRegisterFilter, FltStartFiltering
    ├── callbacks.c                     IRP_MJ_CREATE post-op callback
    ├── communication.c                 FltCreateCommunicationPort, message sending
    └── iotracer_filter.h               Internal driver definitions
```

### Implementation Details

**Driver Behavior:**
- Register with Filter Manager at altitude `370030` (Activity Monitor — observe-only)
- Post-op CREATE callback (only on successful opens)
- Call `FltGetFileNameInformation(FLT_FILE_NAME_NORMALIZED | FLT_FILE_NAME_QUERY_DEFAULT)`
- Extract FileObject pointer, PID
- Send message via `FltSendMessage` to userspace port (non-blocking, timeout=0)

**Message Format:**
```c
typedef struct {
    FILTER_MESSAGE_HEADER Header;  // Required by FilterGetMessage
    ULONG ProcessId;
    UINT64 FileObjectPtr;          // nameByObj key
    WCHAR FileName[512];           // Normalized absolute path
} IOTRACER_MESSAGE;
```

**Key Files:**
- `driver.c`: `DriverEntry()`, `FltRegisterFilter()`, `FltStartFiltering()`
- `callbacks.c`: `CreatePostOpCallback()` — the core logic
- `communication.c`: `FltCreateCommunicationPort()`, connect/disconnect handlers
- `IOTracerMinifilter.inf`: Service registration, load order group

---

## Phase 2: Userspace Integration (COMPLETE ✅)

### Completed Files

**1. `IOTracesCORE/utils/MinifilterShared.cs` (52 lines)**
- Shared message struct definitions (C# marshaling-compatible)
- Port name constant: `\IOTracerMinifilterPort`
- Used by both driver (C header) and userspace (C# struct)

**2. `IOTracesCORE/utils/MinifilterPortClient.cs` (260 lines)**
- P/Invoke: `FilterConnectCommunicationPort`, `FilterGetMessage` from fltlib.dll
- `TryConnect(nameByObj, _pending)` → returns null if driver unavailable (graceful degradation)
- Background reader thread:
  - Blocks on `FilterGetMessage()`
  - On message: parse, extract FileObjectPtr + FileName
  - `nameByObj[FileObjectPtr] = FileName` (pre-seed cache)
  - `DrainPending(FileObjectPtr, FileName)` (flush waiting ETW events)
- Proper cleanup on `Dispose()` (signal cancellation, close port, wait for thread)

**3. `IOTracesCORE/utils/DriverService.cs` (310 lines)**
- Service Control Manager wrapper using advapi32.dll P/Invoke
- `EnsureInstalled(sysPath)` — create service if not present (idempotent)
- `EnsureLoaded()` — start driver service (with fltmc.exe fallback)
- `Unload()` — stop driver service
- `IsLoaded()` — query driver status

### Integration Points

**FilesystemHandlers.cs:**
```csharp
private readonly MinifilterPortClient? _minifilterClient;

public FilesystemHandlers(WriterManager wm, ProcessCommandLineCache processCache)
{
    // ...
    _minifilterClient = MinifilterPortClient.TryConnect(nameByObj, _pending);
}

public void Dispose()
{
    _minifilterClient?.Dispose();
    _flushTimer?.Dispose();
}
```

**Tracer.cs:**
```csharp
// In Trace() method, at startup
string sysPath = Path.Combine(AppContext.BaseDirectory, "IOTracerMinifilter.sys");
if (File.Exists(sysPath))
{
    DriverService.EnsureInstalled(sysPath);
    DriverService.EnsureLoaded();
}

// In CleanupAndExitAsync(), on shutdown
DriverService.Unload();
```

---

## Build Status

✅ **Project builds successfully.** Release build completed without errors (28 pre-existing warnings unrelated to new code).

```
IOTracesCORE\utils\MinifilterPortClient.cs (new) ✅
IOTracesCORE\utils\DriverService.cs (new) ✅
IOTracesCORE\utils\MinifilterShared.cs (new) ✅
IOTracesCORE\handlers\FilesystemHandlers.cs (modified) ✅
IOTracesCORE\Tracer.cs (modified) ✅
```

---

## What Works Now (Without Driver)

- Tracer runs normally with ETW-only resolution (graceful degradation)
- If `.sys` file missing: no error, debug message logged
- If driver unavailable: `MinifilterPortClient.TryConnect()` returns null, tracer continues
- Three-layer ETW resolution still active (decoupled cache, cross-population, deferred queue)

---

## Next Steps

### 1. Install Windows Driver Kit (WDK)
- Download WDK for Windows 11 from Microsoft
- Install matching your Visual Studio version
- Verify: `wdksetup.exe` → complete installation

### 2. Enable Test Signing (Required for Development)
```cmd
bcdedit /set testsigning on
# Reboot required
```

### 3. Create IOTracerMinifilter Project
- New → Visual Studio project → Windows Driver Kit → "Minifilter Driver"
- Add to `IOTracer.sln`
- Copy skeleton code from plan (driver.c, callbacks.c, communication.c, iotracer_filter.h, .inf)

### 4. Build & Deploy
- Build driver in VS (outputs `.sys` file)
- Copy `.sys` to `IOTracesCORE\bin\Release\net8.0-windows\` (add post-build step in driver project)
- Run io-tracer as admin
- Watch debug output for "Minifilter port client connected successfully" message

### 5. Validation
- Trace a process that opens files with **relative paths** (e.g., `notepad data/test.txt`)
- Check CSV output: `Filename` should show **absolute path** (e.g., `C:\Users\...\data\test.txt`)
- Enable dev mode and check `empty_filenames_*.txt` log — count should be significantly lower vs. ETW-only baseline
- Verify graceful shutdown: `fltmc` should show driver unloaded

---

## Known Limitations (Unavoidable in User Mode)

1. **Files open before tracing starts** — still empty unless driver is pre-loaded
   - Minifilter only captures NEW opens from when it loads onward
   - Mitigation: start driver early in trace initialization (already done in Phase 2 integration)

2. **Relative paths from pre-existing handles** — still resolve as relative or empty
   - ETW limitation: no FileIOName event for pre-existing handles
   - Minifilter solves this for **new** opens only

3. **Pre-existing handles without rundown** — if a file was open when trace started and minifilter was not loaded, it remains unfixable
   - Windows ETW design limitation, not fixable at user mode

---

## Files Modified/Created Summary

### New Files (3)
- `IOTracesCORE/utils/MinifilterShared.cs`
- `IOTracesCORE/utils/MinifilterPortClient.cs`
- `IOTracesCORE/utils/DriverService.cs`

### Modified Files (2)
- `IOTracesCORE/handlers/FilesystemHandlers.cs` — added minifilter client initialization
- `IOTracesCORE/Tracer.cs` — added driver service calls at startup/shutdown

### To Be Created (Phase 1)
- `IOTracerMinifilter/IOTracerMinifilter.vcxproj` (WDK project file)
- `IOTracerMinifilter/src/driver.c`
- `IOTracerMinifilter/src/callbacks.c`
- `IOTracerMinifilter/src/communication.c`
- `IOTracerMinifilter/src/iotracer_filter.h`
- `IOTracerMinifilter/shared/iotracer_shared.h`
- `IOTracerMinifilter/IOTracerMinifilter.inf`

---

## Testing Checklist (Phase 1)

- [ ] Driver compiles without errors
- [ ] `.sys` file generated and signed (test-signed)
- [ ] Service installs via `sc create`
- [ ] Driver loads via `fltmc load IOTracerMinifilter`
- [ ] `fltmc instances` shows the minifilter loaded
- [ ] Userspace client connects and reads messages
- [ ] Cache pre-seeding works (check debug output or empty-filename log)
- [ ] Graceful unload on trace stop
- [ ] Fallback works if driver unloaded mid-trace
- [ ] Test signing mode can be disabled afterward: `bcdedit /set testsigning off`

---

## References

- Plan file: `C:\Users\ASUS\.claude\plans\dynamic-mixing-curry.md`
- Filesystem tracing docs: `docs/trace-types/filesystem.md` (includes cache resolution details)
- Current implementation: `IOTracesCORE/handlers/FilesystemHandlers.cs` (three-layer resolution, deferred queue)
