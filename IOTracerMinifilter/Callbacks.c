/*++

Module Name:

    Callbacks.c

Abstract:

    Minifilter callback routines for file I/O operations.

Environment:

    Kernel-mode minifilter driver

--*/

#include "iotracer_filter.h"

#ifdef ALLOC_PRAGMA
#pragma alloc_text (PAGE, CreatePreOpCallback)
#pragma alloc_text (PAGE, CreatePostOpCallback)
#endif

FLT_PREOP_CALLBACK_STATUS
CreatePreOpCallback (
    _Inout_ PFLT_CALLBACK_DATA Data,
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _Flt_CompletionContext_Outptr_ PVOID *CompletionContext
    )
/*++
Routine Description:
    Pre-operation callback for IRP_MJ_CREATE.
    We don't do much here; most work is in the post-op callback.

Arguments:
    Data - Pointer to the filter callout data that is associated with the I/O operation.
    FltObjects - Pointer to the FLT_RELATED_OBJECTS data structure.
    CompletionContext - Pointer to receive completion context.

Return Value:
    FLT_PREOP_SUCCESS_WITH_CALLBACK - Invoke post-op callback
    FLT_PREOP_SUCCESS_NO_CALLBACK - Skip post-op

--*/
{
    UNREFERENCED_PARAMETER( Data );
    UNREFERENCED_PARAMETER( FltObjects );
    UNREFERENCED_PARAMETER( CompletionContext );

    PAGED_CODE();

    //
    // Call our post-op callback to capture the filename.
    //
    return FLT_PREOP_SUCCESS_WITH_CALLBACK;
}

FLT_POSTOP_CALLBACK_STATUS
CreatePostOpCallback (
    _Inout_ PFLT_CALLBACK_DATA Data,
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_opt_ PVOID CompletionContext,
    _In_ FLT_POST_OPERATION_FLAGS Flags
    )
/*++
Routine Description:
    Post-operation callback for IRP_MJ_CREATE.
    Sends normalized absolute path to userspace for cache pre-population.

Arguments:
    Data - Pointer to the filter callout data.
    FltObjects - Pointer to the FLT_RELATED_OBJECTS data structure.
    CompletionContext - Not used.
    Flags - Indicates whether the post-operation callback is being made at safe IRQL.

Return Value:
    FLT_POSTOP_FINISHED_PROCESSING - Normal case

--*/
{
    NTSTATUS Status;

    UNREFERENCED_PARAMETER( CompletionContext );
    UNREFERENCED_PARAMETER( Flags );

    PAGED_CODE();

    //
    // Check if the file was successfully opened. Only send messages for
    // successful operations.
    //
    if (!NT_SUCCESS( Data->IoStatus.Status )) {
        return FLT_POSTOP_FINISHED_PROCESSING;
    }

    //
    // Only process successful creates with a valid file object.
    //
    if (FltObjects->FileObject == NULL) {
        return FLT_POSTOP_FINISHED_PROCESSING;
    }

    //
    // Get the normalized absolute filename.
    //
    PFLT_FILE_NAME_INFORMATION NameInfo;
    Status = FltGetFileNameInformation(
                 Data,
                 FLT_FILE_NAME_NORMALIZED | FLT_FILE_NAME_QUERY_DEFAULT,
                 &NameInfo );

    if (!NT_SUCCESS( Status )) {
        DbgPrint( "FltGetFileNameInformation failed (Status = 0x%08X)\n", Status );
        return FLT_POSTOP_FINISHED_PROCESSING;
    }

    //
    // Release the name information (we'll handle parsing in userspace).
    //
    Status = FltParseFileNameInformation( NameInfo );
    if (!NT_SUCCESS( Status )) {
        DbgPrint( "FltParseFileNameInformation failed (Status = 0x%08X)\n", Status );
        FltReleaseFileNameInformation( NameInfo );
        return FLT_POSTOP_FINISHED_PROCESSING;
    }

    //
    // Send the message to userspace.
    //
    Status = SendMessageToUserspace( Data, FltObjects, &NameInfo->Name );

    //
    // Release the name information.
    //
    FltReleaseFileNameInformation( NameInfo );

    if (!NT_SUCCESS( Status )) {
        DbgPrint( "SendMessageToUserspace failed (Status = 0x%08X)\n", Status );
    }

    return FLT_POSTOP_FINISHED_PROCESSING;
}
