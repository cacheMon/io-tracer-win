/*++

Module Name:

    driver.c

Abstract:

    This file contains the driver entry points and callbacks.

Environment:

    Kernel-mode Driver Framework

--*/

#include "iotracer_filter.h"

//
// Global filter state
//
FILTER_GLOBALS FilterGlobals;

//
// Minifilter registration
//
const FLT_OPERATION_REGISTRATION Callbacks[] = {
    { IRP_MJ_CREATE,
      0,
      CreatePreOpCallback,
      CreatePostOpCallback },

    { IRP_MJ_OPERATION_END }
};

const FLT_REGISTRATION FilterRegistration = {
    sizeof( FLT_REGISTRATION ),         //  Size
    FLT_REGISTRATION_VERSION,           //  Version
    0,                                  //  Flags
    NULL,                               //  Context
    Callbacks,                          //  Operation callbacks
    UnloadCallback,                     //  MiniFilterUnload
    InstanceSetupCallback,              //  InstanceSetup
    InstanceQueryTeardownCallback,      //  InstanceQueryTeardown
    InstanceTeardownStartCallback,      //  InstanceTeardownStart
    InstanceTeardownCompleteCallback,   //  InstanceTeardownComplete
    NULL,                               //  GenerateFileName
    NULL,                               //  GenerateDestinationFileName
    NULL                                //  NormalizeNameComponent
};

#ifdef ALLOC_PRAGMA
#pragma alloc_text (INIT, DriverEntry)
#pragma alloc_text (PAGE, UnloadCallback)
#pragma alloc_text (PAGE, InstanceSetupCallback)
#pragma alloc_text (PAGE, InstanceQueryTeardownCallback)
#pragma alloc_text (PAGE, InstanceTeardownStartCallback)
#pragma alloc_text (PAGE, InstanceTeardownCompleteCallback)
#endif

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT  DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
/*++

Routine Description:
    DriverEntry initializes the driver and is the first routine called by the
    system after the driver is loaded. DriverEntry specifies the other entry
    points in the function driver, such as EvtDevice and DriverUnload.

Parameters Description:

    DriverObject - represents the instance of the function driver that is loaded
    into memory. DriverEntry must initialize members of DriverObject before it
    returns to the caller. DriverObject is allocated by the system before the
    driver is loaded, and it is released by the system after the system unloads
    the function driver from memory.

    RegistryPath - represents the driver specific path in the Registry.
    The function driver can use the path to store driver related data between
    reboots. The path does not store hardware instance specific data.

Return Value:

    STATUS_SUCCESS if successful,
    STATUS_UNSUCCESSFUL otherwise.

--*/
{
    NTSTATUS Status;

    UNREFERENCED_PARAMETER( RegistryPath );

    DbgPrint( "IOTracerMinifilter DriverEntry\n" );

    //
    // Register the filter with the filter manager.
    //
    Status = FltRegisterFilter( DriverObject,
                                &FilterRegistration,
                                &FilterGlobals.Filter );

    if (!NT_SUCCESS( Status )) {
        DbgPrint( "FltRegisterFilter failed (Status = 0x%08X)\n", Status );
        return Status;
    }

    //
    // Create the communication port for userspace communication.
    //
    Status = CreateCommunicationPort( FilterGlobals.Filter );

    if (!NT_SUCCESS( Status )) {
        DbgPrint( "CreateCommunicationPort failed (Status = 0x%08X)\n", Status );
        FltUnregisterFilter( FilterGlobals.Filter );
        return Status;
    }

    //
    // Start filtering I/O.
    //
    Status = FltStartFiltering( FilterGlobals.Filter );

    if (!NT_SUCCESS( Status )) {
        DbgPrint( "FltStartFiltering failed (Status = 0x%08X)\n", Status );
        FltCloseCommunicationPort( FilterGlobals.ServerPort );
        FltUnregisterFilter( FilterGlobals.Filter );
        return Status;
    }

    DbgPrint( "IOTracerMinifilter loaded successfully\n" );
    return STATUS_SUCCESS;
}

NTSTATUS
UnloadCallback (
    _In_ FLT_FILTER_UNLOAD_FLAGS Flags
    )
/*++
Routine Description:
    Called when filter is being unloaded.
--*/
{
    UNREFERENCED_PARAMETER( Flags );

    PAGED_CODE();

    DbgPrint( "IOTracerMinifilter unloading\n" );

    //
    // Close the server port (this will close client connection too).
    //
    if (FilterGlobals.ServerPort != NULL) {
        FltCloseCommunicationPort( FilterGlobals.ServerPort );
        FilterGlobals.ServerPort = NULL;
    }

    //
    // Unregister the filter.
    //
    FltUnregisterFilter( FilterGlobals.Filter );

    return STATUS_SUCCESS;
}

NTSTATUS
InstanceSetupCallback (
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_ FLT_INSTANCE_SETUP_FLAGS Flags,
    _In_ DEVICE_TYPE VolumeDeviceType,
    _In_ FLT_FILESYSTEM_TYPE VolumeFilesystemType
    )
/*++
Routine Description:
    Called when a new volume is mounted.
--*/
{
    UNREFERENCED_PARAMETER( FltObjects );
    UNREFERENCED_PARAMETER( Flags );
    UNREFERENCED_PARAMETER( VolumeDeviceType );
    UNREFERENCED_PARAMETER( VolumeFilesystemType );

    PAGED_CODE();

    DbgPrint( "IOTracerMinifilter instance setup\n" );
    return STATUS_SUCCESS;
}

NTSTATUS
InstanceQueryTeardownCallback (
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_ FLT_INSTANCE_TEARDOWN_FLAGS Flags
    )
/*++
Routine Description:
    Called before instance teardown.
--*/
{
    UNREFERENCED_PARAMETER( FltObjects );
    UNREFERENCED_PARAMETER( Flags );

    PAGED_CODE();

    return STATUS_SUCCESS;
}

NTSTATUS
InstanceTeardownStartCallback (
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_ FLT_INSTANCE_TEARDOWN_FLAGS Flags
    )
/*++
Routine Description:
    Called at start of instance teardown.
--*/
{
    UNREFERENCED_PARAMETER( FltObjects );
    UNREFERENCED_PARAMETER( Flags );

    PAGED_CODE();

    return STATUS_SUCCESS;
}

NTSTATUS
InstanceTeardownCompleteCallback (
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_ FLT_INSTANCE_TEARDOWN_FLAGS Flags
    )
/*++
Routine Description:
    Called at end of instance teardown.
--*/
{
    UNREFERENCED_PARAMETER( FltObjects );
    UNREFERENCED_PARAMETER( Flags );

    PAGED_CODE();

    return STATUS_SUCCESS;
}
