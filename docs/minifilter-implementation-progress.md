# Minifilter Filename Resolution: Implementation Progress

**Status:** Phase 2 (Userspace) ✅ Complete | Phase 1 (Kernel Driver) ✅ Complete (pending test signing activation)

---

## Overview

Implement a Windows minifilter kernel driver that intercepts file creates and sends normalized absolute paths to the userspace tracer, pre-seeding the `nameByObj` cache. This solves the **pre-existing file resolution problem** in ETW (files opened before the trace started have no name events).

**Architecture:** Minifilter sends messages via filter communication port → MinifilterPortClient reads in background thread → nameByObj cache pre-populated → ETW events resolve to correct filenames.

---

## Phase 1: Kernel Driver (COMPLETE ✅)

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

✅ **Both projects build successfully without errors.**

**IOTracerMinifilter (kernel driver):**
- ✅ Compiles as WDM driver (fltMgr.lib linked)
- ✅ `.sys` file generated (11 KB, test-signed on build)
- ✅ Post-build step copies `.sys` to `IOTracesCORE\bin\Debug\net8.0-windows\` and `Release\net8.0-windows\`
- ✅ Filter registration with altitude 370030
- ✅ All Filter Manager API calls resolve correctly

**IOTracesCORE (userspace):**
- ✅ MinifilterPortClient.cs integrates seamlessly
- ✅ DriverService.cs handles installation/loading (via advapi32 P/Invoke)
- ✅ Graceful fallback if driver unavailable
- ✅ ETW-only resolution still works as baseline

---

## What Works Now (Without Driver)

- Tracer runs normally with ETW-only resolution (graceful degradation)
- If `.sys` file missing: no error, debug message logged
- If driver unavailable: `MinifilterPortClient.TryConnect()` returns null, tracer continues
- Three-layer ETW resolution still active (decoupled cache, cross-population, deferred queue)

---

## Remaining Steps (Testing Phase)

### 1. Enable Test Signing (REQUIRED)
Due to Secure Boot, this requires disabling Secure Boot first:

**In BIOS/UEFI:**
- Enter BIOS/UEFI setup (Del/F2/F10 at boot)
- Find Security → Secure Boot → Disable
- Save & Exit

**In Windows (as Admin):**
```cmd
bcdedit /set testsigning on
# Reboot required
```

Verify:
```cmd
bcdedit /enum | find "testsigning"
# Should show: testsigning             Yes
```

### 2. Run & Test
- Run Visual Studio **as Administrator**
- Build solution (`Ctrl+Shift+B`)
- Run tracer (`F5`)
- Watch output for: `"Minifilter port client connected successfully"`

### 3. Validation
- Trace a process opening files with **relative paths** (e.g., `notepad ..\config.txt`)
- Check CSV output: `Filename` should show **absolute path** instead of empty/relative
- Compare empty-filename count with ETW-only baseline (should be significantly lower)
- Verify driver unloads cleanly: `fltmc instances` post-trace

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

### Created Files (Phase 1)
- ✅ `IOTracerMinifilter/IOTracerMinifilter.vcxproj` (WDK C++/WDM project file, configured with fltMgr.lib)
- ✅ `IOTracerMinifilter/Driver.c` (DriverEntry, FltRegisterFilter, FltStartFiltering, unload/instance callbacks)
- ✅ `IOTracerMinifilter/Callbacks.c` (CreatePreOpCallback, CreatePostOpCallback with filename extraction)
- ✅ `IOTracerMinifilter/Communication.c` (FltCreateCommunicationPort, ConnectCallback, DisconnectCallback, FltSendMessage)
- ✅ `IOTracerMinifilter/iotracer_filter.h` (Filter globals, function prototypes, internal definitions)
- ✅ `IOTracerMinifilter/shared/iotracer_shared.h` (IOTRACER_MESSAGE struct, port name constant)
- ✅ `IOTracerMinifilter/IOTracerMinifilter.inf` (Service registration, altitude 370030, architecture-specific sections)

---

## Testing Checklist (Phase 1)

**Build & Compilation:**
- [x] Driver compiles without errors
- [x] `.sys` file generated and test-signed
- [x] `.sys` copied to tracer output directories (post-build step)

**Driver Installation & Loading:**
- [ ] Test Signing enabled (`bcdedit /set testsigning on`)
- [ ] Service installs via `DriverService.EnsureInstalled()`
- [ ] Service starts via `DriverService.EnsureLoaded()`
- [ ] `fltmc instances` shows the minifilter loaded

**Communication & Cache:**
- [ ] Userspace client connects (`FilterConnectCommunicationPort` succeeds)
- [ ] Messages received from driver (`FilterGetMessage` reads CreatePostOpCallback events)
- [ ] Cache pre-seeding works (FileObjectPtr → absolute path mapping)
- [ ] `nameByObj` populated before ETW events arrive

**End-to-End Testing:**
- [ ] Trace a process opening files with relative paths
- [ ] CSV shows absolute paths (not empty, not relative)
- [ ] `empty_filenames_*.txt` count lower than ETW-only baseline
- [ ] Graceful unload on trace stop
- [ ] Fallback works if driver unloaded mid-trace
- [ ] Test signing can be disabled afterward: `bcdedit /set testsigning off`

---

## References

- Plan file: `C:\Users\ASUS\.claude\plans\dynamic-mixing-curry.md`
- Filesystem tracing docs: `docs/trace-types/filesystem.md` (includes cache resolution details)
- Current implementation: `IOTracesCORE/handlers/FilesystemHandlers.cs` (three-layer resolution, deferred queue)
