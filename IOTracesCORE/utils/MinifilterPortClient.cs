using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace IOTracesCORE.utils
{
    /// <summary>
    /// Userspace client for reading filename messages from the IOTracerMinifilter kernel driver.
    /// Pre-seeds the FilesystemHandlers.nameByObj cache with normalized absolute paths
    /// obtained at file creation time from the kernel.
    ///
    /// Gracefully degrades if driver is not loaded — tracing continues with ETW-only resolution.
    /// </summary>
    internal class MinifilterPortClient : IDisposable
    {
        private SafeHandle? _portHandle;
        private Task? _readerTask;
        private CancellationTokenSource? _cancellation;
        private readonly ConcurrentDictionary<ulong, string> _nameByObj;
        private readonly ConcurrentDictionary<ulong, ConcurrentQueue<(DateTime addedAt, Action<string> emit)>> _pending;

        private MinifilterPortClient(
            ConcurrentDictionary<ulong, string> nameByObj,
            ConcurrentDictionary<ulong, ConcurrentQueue<(DateTime addedAt, Action<string> emit)>> pending)
        {
            _nameByObj = nameByObj;
            _pending = pending;
        }

        /// <summary>
        /// Attempt to connect to the minifilter communication port.
        /// Returns null if driver is not loaded (graceful degradation).
        /// </summary>
        public static MinifilterPortClient? TryConnect(
            ConcurrentDictionary<ulong, string> nameByObj,
            ConcurrentDictionary<ulong, ConcurrentQueue<(DateTime addedAt, Action<string> emit)>> pending)
        {
            try
            {
                var client = new MinifilterPortClient(nameByObj, pending);
                if (client.ConnectToPort())
                {
                    client.StartReaderThread();
                    Debug.WriteLine("[IOTracer] Minifilter port client connected successfully");
                    return client;
                }
                else
                {
                    Debug.WriteLine("[IOTracer] Minifilter driver not loaded (port unavailable) — falling back to ETW-only");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IOTracer] Failed to initialize minifilter port client: {ex.Message}");
                return null;
            }
        }

        private bool ConnectToPort()
        {
            try
            {
                int result = FilterConnectCommunicationPort(
                    MinifilterShared.PortName,
                    0,      // options: 0 = synchronous
                    IntPtr.Zero,  // context
                    0,      // sizeOfContext
                    IntPtr.Zero,  // securityAttributes (use default)
                    out SafeFileHandle hPort);

                if (result == 0)  // ERROR_SUCCESS
                {
                    _portHandle = hPort;
                    return true;
                }
                else
                {
                    int lastError = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[IOTracer] FilterConnectCommunicationPort failed: 0x{lastError:X8}");
                    return false;
                }
            }
            catch (DllNotFoundException)
            {
                Debug.WriteLine("[IOTracer] fltlib.dll not found — driver framework unavailable");
                return false;
            }
        }

        private void StartReaderThread()
        {
            _cancellation = new CancellationTokenSource();
            _readerTask = Task.Run(() => ReaderThreadMain(_cancellation.Token), _cancellation.Token);
        }

        private void ReaderThreadMain(CancellationToken ct)
        {
            if (_portHandle == null) return;

            byte[] messageBuffer = new byte[Marshal.SizeOf<MinifilterShared.IotracerMessage>()];
            GCHandle bufferHandle = GCHandle.Alloc(messageBuffer, GCHandleType.Pinned);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int result = FilterGetMessage(
                        _portHandle,
                        bufferHandle.AddrOfPinnedObject(),
                        (uint)messageBuffer.Length,
                        IntPtr.Zero);  // overlapped = null (synchronous)

                    if (result != 0 && result != 0x038)  // ERROR_SUCCESS or WAIT_TIMEOUT
                    {
                        // Error reading — driver may have been unloaded or port closed
                        Debug.WriteLine($"[IOTracer] FilterGetMessage failed: 0x{result:X8}, stopping reader");
                        break;
                    }

                    // Parse the message
                    GCHandle parsedHandle = GCHandle.Alloc(messageBuffer, GCHandleType.Pinned);
                    try
                    {
                        var msg = Marshal.PtrToStructure<MinifilterShared.IotracerMessage>(parsedHandle.AddrOfPinnedObject());
                        if (msg.FileObjectPtr != 0 && !string.IsNullOrEmpty(msg.FileName))
                        {
                            ProcessFileCreateMessage(msg);
                        }
                    }
                    finally
                    {
                        parsedHandle.Free();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IOTracer] Minifilter reader thread exception: {ex}");
            }
            finally
            {
                bufferHandle.Free();
            }
        }

        private void ProcessFileCreateMessage(MinifilterShared.IotracerMessage msg)
        {
            var fileName = msg.FileName.TrimEnd('\0');
            if (string.IsNullOrEmpty(fileName)) return;

            // Add to cache keyed by FileObject pointer
            _nameByObj.TryAdd(msg.FileObjectPtr, fileName);

            // Drain any pending events waiting on this key
            if (_pending.TryRemove(msg.FileObjectPtr, out var queue))
            {
                while (queue.TryDequeue(out var item))
                {
                    item.emit(fileName);
                }
            }

            Debug.WriteLine($"[IOTracer] Minifilter: cached {msg.FileName} (FileObj=0x{msg.FileObjectPtr:X}, PID={msg.ProcessId})");
        }

        public void Dispose()
        {
            if (_cancellation != null)
            {
                _cancellation.Cancel();
                try
                {
                    _readerTask?.Wait(TimeSpan.FromSeconds(2));
                }
                catch { }
                _cancellation.Dispose();
            }

            _portHandle?.Dispose();
            Debug.WriteLine("[IOTracer] Minifilter port client disconnected");
        }

        // P/Invoke declarations for fltlib.dll

        [DllImport("fltlib.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int FilterConnectCommunicationPort(
            string portName,
            uint options,
            IntPtr context,
            ushort sizeOfContext,
            IntPtr securityAttributes,
            out SafeFileHandle hPort);

        [DllImport("fltlib.dll", SetLastError = true)]
        private static extern int FilterGetMessage(
            SafeHandle hPort,
            IntPtr lpMessageBuffer,
            uint dwMessageBufferSize,
            IntPtr lpOverlapped);

        [DllImport("fltlib.dll", SetLastError = true)]
        private static extern int FilterSendMessage(
            SafeHandle hPort,
            IntPtr lpInBuffer,
            uint dwInBufferSize,
            IntPtr lpOutBuffer,
            uint dwOutBufferSize,
            uint lpBytesReturned);
    }
}
