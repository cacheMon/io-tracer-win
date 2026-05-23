#pragma once

//
// Shared message structures between minifilter driver and userspace C# client.
// Must match IOTracesCORE/utils/MinifilterShared.cs exactly.
//

#define IOTRACER_PORT_NAME          L"\\IOTracerMinifilterPort"
#define IOTRACER_MAX_PATH           512

//
// Message sent from minifilter driver to userspace on file create.
// Delivers normalized absolute path for cache pre-population.
// Note: FILTER_MESSAGE_HEADER is defined in fltKernel.h
//
typedef struct _IOTRACER_MESSAGE {
    FILTER_MESSAGE_HEADER Header;           // Required first field for FilterGetMessage
    ULONG ProcessId;
    ULONGLONG FileObjectPtr;                // Maps to FilesystemHandlers.nameByObj key
    WCHAR FileName[IOTRACER_MAX_PATH];      // Normalized absolute path, null-terminated
} IOTRACER_MESSAGE, *PIOTRACER_MESSAGE;
