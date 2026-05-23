#pragma once

#include <fltKernel.h>
#include "shared\iotracer_shared.h"

//
// Internal minifilter driver definitions.
//

//
// Structure for per-instance data. Used to track the communication port
// and manage message sending for each filter instance.
//
typedef struct _INSTANCE_CONTEXT {
    PFLT_FILTER Filter;
    PFLT_PORT ClientPort;
} INSTANCE_CONTEXT, *PINSTANCE_CONTEXT;

//
// Global data
//
typedef struct _FILTER_GLOBALS {
    PFLT_FILTER Filter;
    PFLT_PORT ServerPort;
    PFLT_PORT ClientPort;
} FILTER_GLOBALS, *PFILTER_GLOBALS;

extern FILTER_GLOBALS FilterGlobals;

//
// Function prototypes
//
DRIVER_INITIALIZE DriverEntry;
NTSTATUS
DriverEntry (
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    );

NTSTATUS
UnloadCallback (
    _In_ FLT_FILTER_UNLOAD_FLAGS Flags
    );

NTSTATUS
InstanceSetupCallback (
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_ FLT_INSTANCE_SETUP_FLAGS Flags,
    _In_ DEVICE_TYPE VolumeDeviceType,
    _In_ FLT_FILESYSTEM_TYPE VolumeFilesystemType
    );

NTSTATUS
InstanceQueryTeardownCallback (
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_ FLT_INSTANCE_TEARDOWN_FLAGS Flags
    );

NTSTATUS
InstanceTeardownStartCallback (
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_ FLT_INSTANCE_TEARDOWN_FLAGS Flags
    );

NTSTATUS
InstanceTeardownCompleteCallback (
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_ FLT_INSTANCE_TEARDOWN_FLAGS Flags
    );

NTSTATUS
CreateCommunicationPort (
    _In_ PFLT_FILTER Filter
    );

NTSTATUS
ConnectCallback (
    _In_ PFLT_PORT ClientPort,
    _In_opt_ PVOID ServerPortCookie,
    _In_reads_bytes_(SizeOfContext) PVOID ConnectionContext,
    _In_ ULONG SizeOfContext,
    _Outptr_result_maybenull_ PVOID *ConnectionCookie
    );

VOID
DisconnectCallback (
    _In_opt_ PVOID ConnectionCookie
    );

FLT_PREOP_CALLBACK_STATUS
CreatePreOpCallback (
    _Inout_ PFLT_CALLBACK_DATA Data,
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _Flt_CompletionContext_Outptr_ PVOID *CompletionContext
    );

FLT_POSTOP_CALLBACK_STATUS
CreatePostOpCallback (
    _Inout_ PFLT_CALLBACK_DATA Data,
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_opt_ PVOID CompletionContext,
    _In_ FLT_POST_OPERATION_FLAGS Flags
    );

NTSTATUS
SendMessageToUserspace (
    _In_ PFLT_CALLBACK_DATA Data,
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_ PUNICODE_STRING FileName
    );

//
// Useful macros
//
#define PT_DBG_PRINT( _Dbg_Level, _Format ) \
    (FsRtlIsNameInExpression( (PUNICODE_STRING)&Debug, (PUNICODE_STRING)&gProcNameQuery, TRUE, NULL ) ? \
        DbgPrint _Format : ((int)0))
