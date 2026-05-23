/*++

Module Name:

    Communication.c

Abstract:

    Filter communication port setup and message sending.

Environment:

    Kernel-mode minifilter driver

--*/

#include "iotracer_filter.h"

#ifdef ALLOC_PRAGMA
#pragma alloc_text (PAGE, CreateCommunicationPort)
#pragma alloc_text (PAGE, ConnectCallback)
#pragma alloc_text (PAGE, DisconnectCallback)
#pragma alloc_text (PAGE, SendMessageToUserspace)
#endif

//
// Connection callback - called when userspace connects
//
NTSTATUS
ConnectCallback (
    _In_ PFLT_PORT ClientPort,
    _In_opt_ PVOID ServerPortCookie,
    _In_reads_bytes_(SizeOfContext) PVOID ConnectionContext,
    _In_ ULONG SizeOfContext,
    _Outptr_result_maybenull_ PVOID *ConnectionCookie
    )
{
    UNREFERENCED_PARAMETER( ClientPort );
    UNREFERENCED_PARAMETER( ServerPortCookie );
    UNREFERENCED_PARAMETER( ConnectionContext );
    UNREFERENCED_PARAMETER( SizeOfContext );
    UNREFERENCED_PARAMETER( ConnectionCookie );

    PAGED_CODE();

    DbgPrint( "IOTracerMinifilter client connected\n" );
    return STATUS_SUCCESS;
}

//
// Disconnect callback - called when userspace disconnects
//
VOID
DisconnectCallback (
    _In_opt_ PVOID ConnectionCookie
    )
{
    UNREFERENCED_PARAMETER( ConnectionCookie );

    PAGED_CODE();

    DbgPrint( "IOTracerMinifilter client disconnected\n" );
}

NTSTATUS
CreateCommunicationPort (
    _In_ PFLT_FILTER Filter
    )
/*++
Routine Description:
    Creates a communication port for userspace I/O.

Arguments:
    Filter - Minifilter driver object.

Return Value:
    STATUS_SUCCESS if successful, else error status.

--*/
{
    NTSTATUS Status;
    UNICODE_STRING PortName;
    OBJECT_ATTRIBUTES ObjectAttributes;

    PAGED_CODE();

    //
    // Initialize the port name.
    //
    RtlInitUnicodeString( &PortName, IOTRACER_PORT_NAME );

    //
    // Set up the object attributes for the port.
    //
    InitializeObjectAttributes( &ObjectAttributes,
                               &PortName,
                               OBJ_CASE_INSENSITIVE | OBJ_KERNEL_HANDLE,
                               NULL,
                               NULL );

    //
    // Create the communication port.
    // This allows userspace to connect via FilterConnectCommunicationPort().
    //
    Status = FltCreateCommunicationPort( Filter,
                                        &FilterGlobals.ServerPort,
                                        &ObjectAttributes,
                                        NULL,
                                        ConnectCallback,
                                        DisconnectCallback,
                                        NULL,
                                        1024 );

    if (!NT_SUCCESS( Status )) {
        DbgPrint( "FltCreateCommunicationPort failed (Status = 0x%08X)\n", Status );
        return Status;
    }

    DbgPrint( "Communication port created successfully\n" );
    return STATUS_SUCCESS;
}

NTSTATUS
SendMessageToUserspace (
    _In_ PFLT_CALLBACK_DATA Data,
    _In_ PCFLT_RELATED_OBJECTS FltObjects,
    _In_ PUNICODE_STRING FileName
    )
/*++
Routine Description:
    Sends a file creation message to the userspace client.

Arguments:
    Data - The filter callback data.
    FltObjects - Filter object pointers.
    FileName - Normalized absolute path (UNICODE_STRING).

Return Value:
    STATUS_SUCCESS if message sent (or port closed gracefully).
    Error status if a critical error occurred.

--*/
{
    NTSTATUS Status;
    IOTRACER_MESSAGE Message = {0};
    LARGE_INTEGER TimeOut;

    PAGED_CODE();

    //
    // Return gracefully if no server port exists.
    //
    if (FilterGlobals.ServerPort == NULL) {
        return STATUS_SUCCESS;
    }

    //
    // Prepare the message.
    //
    Message.Header.ReplyLength = 0;
    Message.Header.MessageId = 0;

    //
    // Get the process ID from the thread in the callback data.
    //
    Message.ProcessId = (ULONG)(ULONG_PTR)FltGetRequestorProcessId( Data );
    Message.FileObjectPtr = (ULONGLONG)FltObjects->FileObject;

    //
    // Copy the filename, ensuring null termination.
    //
    if (FileName->Length > 0) {
        size_t CopyLength = (FileName->Length / sizeof(WCHAR));
        if (CopyLength >= IOTRACER_MAX_PATH) {
            CopyLength = IOTRACER_MAX_PATH - 1;
        }
        RtlCopyMemory( Message.FileName,
                      FileName->Buffer,
                      CopyLength * sizeof(WCHAR) );
        Message.FileName[CopyLength] = L'\0';
    }

    //
    // Set a 100ms timeout for sending the message.
    // We use a short timeout because we don't want to block I/O operations.
    //
    TimeOut.QuadPart = -100 * 10 * 1000;  // 100ms in 100-nanosecond intervals (negative = relative)

    //
    // Send the message to userspace. Use FltSendMessage with a timeout.
    // Non-blocking send: if userspace is not reading, we just skip and continue.
    //
    Status = FltSendMessage( FilterGlobals.Filter,
                            &FilterGlobals.ServerPort,
                            &Message,
                            sizeof( Message ),
                            NULL,
                            0,
                            &TimeOut );

    //
    // It's normal for the send to time out or fail if no one is listening.
    // We log but continue processing.
    //
    if (Status == STATUS_TIMEOUT) {
        return STATUS_SUCCESS;
    }

    if (!NT_SUCCESS( Status )) {
        DbgPrint( "FltSendMessage failed (Status = 0x%08X)\n", Status );
        return STATUS_SUCCESS;  // Still return success to not block I/O
    }

    return STATUS_SUCCESS;
}
