# FileIO Trace Filename Issue

## Problem

The `filename` field in FileIO traces contains more than a single file path. Instead of clean file paths, it includes extraneous data in two distinct patterns:

### Pattern 1: Object Manager Notation

ETW reports file opens through the Windows Object Manager namespace with special notation:

```
C:\Windows\System32\process:\C:\Windows\System32\svchost.exe
```

The actual file path (`C:\Windows\System32\svchost.exe`) is prefixed with object type indicators like `process:`, `device:`, etc.

### Pattern 2: Command-Line Arguments Appended

ETW includes the process's command-line arguments in the FileName field:

```
C:\Program Files\NVIDIA Corporation\NvContainer\nvcontainer.exe -s NvContainerLocalSystem -a -f C:\ProgramData\NVIDIA Corporation\NVIDIA app\NvContainer\NvContainerLocalSystem.log -l 3 -d
```

The filename contains the full command-line invocation instead of just the path.

## Root Causes

### Pattern 1 (Object Manager Notation)
- **Intentional ETW reporting**: When processes access kernel objects (like process handles) through the Object Manager, ETW includes the object type prefix
- Occurs with APIs like `OpenProcess()`, device access, special kernel object access
- ETW faithfully reports what was accessed in the object namespace, not just file system paths

### Pattern 2 (Command-Line Arguments)
- **Buffer overflow or ETW schema mismatch**: The FileName field extends beyond its intended boundary
- Likely causes:
  - Windows kernel ETW provider buffer issue
  - Version mismatch between Windows and the TraceEvent library's event schema
  - Memory layout or structure alignment issues in specific Windows versions
  - ETW capturing adjacent memory (process command-line data) when populating the FileName field

## Impact

Both patterns result in filenames that cannot be directly used as file system paths. The data becomes:
- Unusable for file system operations
- Difficult to aggregate or analyze (same file appears with different FileName values)
- Polluted with non-path information

## Solution Required

Parse the `filename` field to extract the actual file path by:
1. **Stripping Object Manager notation**: Remove prefixes like `process:`, `device:`, etc.
2. **Removing command-line arguments**: Truncate at the first space after executable extensions (`.exe`, `.dll`, `.sys`, etc.)

This should be implemented in `FilesystemHandlers.cs` to clean the FileName before it's used in the `Resolve()` method.


## Files Involved

### 1. IOTracesCORE/Tracer.cs
**Role**: ETW session setup and event hookup

**Key section** (lines 273-315):
- Enables the Kernel FileIO provider
- Registers event handlers: `kernel.FileIOCreate += fsHandler.OnCreate;`
- This is where ETW events are captured and routed to handlers

**Relevance**: The issue originates from ETW events - this file sets up what's being captured

---

### 2. IOTracesCORE/handlers/FilesystemHandlers.cs
**Role**: Processes FileIO ETW events and extracts data

**Problem areas**:
- `Resolve()` methods (lines 56-72): Uses `d.FileName` directly without parsing object manager notation or command-line arguments
- `OnCreate()` (lines 175-189): Receives `FileIOCreateTraceData` with corrupted FileName
- `OnRead()` (lines 143-149): Uses raw filename from ETW
- `OnWrite()` (lines 151-157): Uses raw filename from ETW
- Similar issues in all handler methods that use `Resolve()`

**Where fix is needed**:
- Add helper method to extract actual file path from FileName (strip object manager notation, trim command-line args)
- Apply cleaning in `Resolve()` method before using eventName
- Alternatively, create a new method like `ExtractFilePathFromObjectManager()` and call it at the entry of handler methods

---

### 3. IOTracesCORE/trace/FilesystemTrace.cs
**Role**: Data structure for filesystem trace records

**Related field** (line 17):
- `public string Filename { get; set; }` - This is where the corrupted data ends up

**Impact**: The corrupted filename from FilesystemHandlers is stored here and eventually written to output

---

## Data Flow

```
ETW Kernel Provider (Windows)
        ↓
  Tracer.cs (captures events)
        ↓
  FilesystemHandlers.OnCreate/OnRead/OnWrite/etc (processes events)
        ↓ [PROBLEM: FileName contains extra data]
  Resolve() method (uses raw FileName)
        ↓
  FilesystemTrace constructor (stores corrupted filename)
        ↓
  Output CSV (filename with object notation or command-line args)
```

## Implementation Priority

1. **Critical**: `FilesystemHandlers.cs` - Add parsing logic to clean FileName
   - This is where the corruption enters the system
   - All handler methods pass through here

2. **No changes needed**: `Tracer.cs` - Just captures what ETW provides
   
3. **No changes needed**: `FilesystemTrace.cs` - Just stores what it receives (cleanups happen upstream)

---

## Related Handler Methods in FilesystemHandlers.cs

All of these methods need to work with the cleaned filename:
- `OnCreate()` (line 175)
- `OnRead()` (line 143)
- `OnWrite()` (line 151)
- `OnFlush()` (line 159)
- `OnDirEnum()` (line 166)
- `OnDelete()` (line 197)
- `OnFileDelete()` (line 204)
- `OnClose()` (line 210)
- `OnRename()` (line 219)
- `OnCleanup()` (line 232)
- `OnDirNotify()` (line 239)
- `OnFileRundown()` (line 247)
- `OnFSControl()` (line 255)
- `OnMapFile()` (line 262)
- `OnQueryInfo()` (line 291)
- `OnSetInfo()` (line 298)
- `OnUnmapFile()` (line 305)
