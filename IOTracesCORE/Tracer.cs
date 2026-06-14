using IOTracesCORE.cloudstorage;
using IOTracesCORE.handlers;
using IOTracesCORE.snapper;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IOTracesCORE
{
    class Tracer
    {
        private readonly WriterManager wm;
        private readonly FilesystemHandlers fsHandler;
        private readonly SystemSnapper systemSnapper;
        private readonly DiskHandlers dsHandler;
        private readonly NetworkHandlers nwHandler;
        private readonly DriverHandlers driverHandler;
        private readonly MemoryHandlers memHandler;
        private readonly CacheHandlers cacheHandler;
        private readonly ProcessCommandLineCache processCache;
        private readonly ProcessSnapper psHandler;
        private readonly FilesystemSnapper fsSnapper;
        private TraceEventSession? session;
        private ObjectStorageHandler objHandler;
        private bool isUploadAutomatically;
        private volatile bool isShuttingDown = false;
        private int cleanupStarted = 0;

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        delegate bool ConsoleCtrlDelegate(CtrlTypes ctrlType);

        enum CtrlTypes : uint
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        public Tracer(bool anonymouse, bool upload, ObjectStorageHandler obj, string outputPath = ".\\output", bool devMode = false)
        {
            objHandler = obj;
            isUploadAutomatically = upload;
            wm = new WriterManager($"{outputPath}\\windows_trace\\{PathHasher.deviceId}", anonymouse, upload, objHandler, devMode);
            processCache = new ProcessCommandLineCache();
            fsHandler = new FilesystemHandlers(wm, processCache);
            dsHandler = new DiskHandlers(wm);
            memHandler = new MemoryHandlers(wm);
            cacheHandler = new CacheHandlers(wm);
            psHandler = new ProcessSnapper(wm, anonymouse, processCache);
            fsSnapper = new FilesystemSnapper(wm, anonymouse);
            systemSnapper = new SystemSnapper(wm);
            nwHandler = new NetworkHandlers(wm);
            driverHandler = new DriverHandlers(wm);
        }

        private bool ConsoleCtrlHandler(CtrlTypes ctrlType)
        {
            switch (ctrlType)
            {
                case CtrlTypes.CTRL_C_EVENT:
                    Console.WriteLine("\nReceived Ctrl+C signal. Cleaning up...");
                    break;
                case CtrlTypes.CTRL_BREAK_EVENT:
                    Console.WriteLine("\nReceived Ctrl+Break signal. Cleaning up...");
                    break;
                case CtrlTypes.CTRL_CLOSE_EVENT:
                    Console.WriteLine("\nConsole window is being closed. Cleaning up...");
                    break;
                case CtrlTypes.CTRL_LOGOFF_EVENT:
                    Console.WriteLine("\nUser is logging off. Cleaning up...");
                    break;
                case CtrlTypes.CTRL_SHUTDOWN_EVENT:
                    Console.WriteLine("\nSystem is shutting down. Cleaning up...");
                    break;
            }

            if (!isShuttingDown)
            {
                isShuttingDown = true;
                CleanupAndExitAsync().GetAwaiter().GetResult();
            }

            return true;
        }

        private async Task CleanupAndExitAsync()
        {
            // Ctrl+C can arrive via both the Win32 console handler and
            // Console.CancelKeyPress; ensure the cleanup body (and Environment.Exit)
            // runs exactly once even if those paths race.
            if (Interlocked.Exchange(ref cleanupStarted, 1) != 0)
                return;

            try
            {
                Debug.WriteLine("Performing cleanup operations...");

                if (session != null)
                {
                    session.Dispose();
                    session = null;
                }

                fsHandler?.Dispose();

                // Flush the last partial per-minute network window before final compression.
                nwHandler?.Dispose();

                fsSnapper.Stop();
                psHandler.Stop();

                await Task.Delay(1000);

                // Check if filesystem snapshot completed before finalizing
                if (!fsSnapper.IsSnapshotComplete())
                {
                    Debug.WriteLine("Filesystem snapshot was incomplete, cleaning up...");
                    wm.FinalizeFilesystemSnapshot(false);
                }

                // Check if process snapshot completed before finalizing
                if (!psHandler.IsSnapshotComplete())
                {
                    Debug.WriteLine("Process snapshot was incomplete, cleaning up...");
                    wm.FinalizeProcessSnapshot(false);
                }

                await wm.CompressAllAsync();

                Console.WriteLine("Cleanup completed successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error! {ex.Message}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
            finally
            {
                Environment.Exit(0);
            }
        }


        public void Trace(CancellationToken cancellationToken = default)
        {
            string sessionName = "IOTracer";
            CleanupOrphanedSession(sessionName);
            SetConsoleCtrlHandler(ConsoleCtrlHandler, true);

            // Registered once here (not per-session) so reconnect-driven session
            // restarts in RunOneSession don't accumulate duplicate handlers.
            Console.CancelKeyPress += (_, e) =>
            {
                if (!isShuttingDown)
                {
                    Console.WriteLine("\nCtrl+C pressed. Cleaning up...");
                    e.Cancel = true;
                    isShuttingDown = true;
                    CleanupAndExitAsync().GetAwaiter().GetResult();
                }
            };

            wm.InitiateDirectory();
            Console.WriteLine("Starting IOTracer...");
            Console.WriteLine("IOTracer started, Press CTRL + C to exit, or close the console window!");
            _ = Task.Run(() => fsSnapper.Run());
            Task __ = Task.Run(() => psHandler.Run());
            systemSnapper.CaptureSpecSnapshot();

            if (isUploadAutomatically)
            {
                // Register the stop callback so the upload worker can halt the ETW session
                ObjectStorageHandler.OnStopSessionRequested = () =>
                {
                    Debug.WriteLine("[Tracer] Stop requested by upload worker — stopping ETW session.");
                    session?.Stop();
                };

                objHandler.UploadThread(cancellationToken);
            }

            cancellationToken.Register(() =>
            {
                if (!isShuttingDown)
                {
                    Console.WriteLine("\nShutdown requested. Cleaning up...");
                    isShuttingDown = true;

                    // Unblock the resume gate so the loop can observe the cancellation
                    ObjectStorageHandler.ResumeGate.Set();

                    if (session != null)
                    {
                        session.Stop();
                    }
                }
            });

            try
            {
                // ── Outer restart loop ────────────────────────────────────────────────
                // The loop runs one ETW session per iteration. When the upload worker
                // detects a connection failure it:
                //   1. Resets ResumeGate (blocking here after the session ends)
                //   2. Stops the current session via OnStopSessionRequested
                //   3. Retries connectivity; on success it Sets ResumeGate again
                // This loop then spins up a brand-new session.
                while (!cancellationToken.IsCancellationRequested && !isShuttingDown)
                {
                    // Wait until connectivity is confirmed (or we are shutting down)
                    ObjectStorageHandler.ResumeGate.Wait(cancellationToken);

                    if (cancellationToken.IsCancellationRequested || isShuttingDown)
                        break;

                    Debug.WriteLine("[Tracer] Starting new ETW session.");
                    RunOneSession(sessionName, cancellationToken);
                    Debug.WriteLine("[Tracer] ETW session ended.");
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation path — fall through to finally
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An error occurred during tracing: {ex.Message}");
                if (!isShuttingDown)
                {
                    CleanupAndExitAsync().GetAwaiter().GetResult();
                }
            }
            finally
            {
                if (!isShuttingDown)
                {
                    isShuttingDown = true;
                }
                Form pleaseWaitForm = new Form()
                {
                    Width = 400,
                    Height = 100,
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.CenterScreen,
                    BackColor = Color.White,
                    TopMost = true
                };

                Label label = new Label()
                {
                    Text = "IO Tracing session has ended. The application performing cleaning operation. Please Wait....",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 10)
                };

                pleaseWaitForm.Controls.Add(label);
                pleaseWaitForm.Show();
                Application.DoEvents();
                CleanupAndExitAsync().GetAwaiter().GetResult();
                pleaseWaitForm.Close();
                pleaseWaitForm.Dispose();
            }
        }

        /// <summary>
        /// Creates and runs a single ETW kernel session. Returns when
        /// <see cref="TraceEventSession.Stop"/> is called (either by user shutdown
        /// or by the upload worker via <see cref="ObjectStorageHandler.OnStopSessionRequested"/>).
        /// </summary>
        private void RunOneSession(string sessionName, CancellationToken cancellationToken)
        {
            CleanupOrphanedSession(sessionName);

            // Drop IRP correlation state from any previous session so reused IRP
            // pointers can't be mis-correlated across a session restart.
            driverHandler.Reset();

            try
            {
                using (session = new TraceEventSession(sessionName))
                {
                    session.StopOnDispose = true;

                    session.EnableKernelProvider(
                        KernelTraceEventParser.Keywords.FileIO |
                        KernelTraceEventParser.Keywords.FileIOInit |
                        KernelTraceEventParser.Keywords.Process |
                        KernelTraceEventParser.Keywords.DiskIO |
                        KernelTraceEventParser.Keywords.DiskIOInit |
                        KernelTraceEventParser.Keywords.Driver |
                        KernelTraceEventParser.Keywords.NetworkTCPIP |
                        KernelTraceEventParser.Keywords.Memory |
                        KernelTraceEventParser.Keywords.MemoryHardFaults |
                        KernelTraceEventParser.Keywords.VirtualAlloc
                    );

                    var memoryManagerGuid = new Guid("d1d93ef7-e1f2-4f45-9943-03d245fe6c00");
                    session.EnableProvider(memoryManagerGuid);

                    var tcpIpProviderGuid = new Guid("2F07E2EE-15DB-40F1-90EF-9D7BA282188A");
                    session.EnableProvider(tcpIpProviderGuid);

                    var source = session.Source;
                    var kernel = source.Kernel;

                    kernel.FileIORead += fsHandler.OnRead;
                    kernel.FileIOWrite += fsHandler.OnWrite;
                    kernel.FileIOClose += fsHandler.OnClose;
                    kernel.FileIOCreate += fsHandler.OnCreate;
                    kernel.FileIODelete += fsHandler.OnDelete;
                    kernel.FileIOFlush += fsHandler.OnFlush;
                    kernel.FileIODirEnum += fsHandler.OnDirEnum;
                    kernel.FileIORename += fsHandler.OnRename;
                    kernel.FileIOCleanup += fsHandler.OnCleanup;
                    kernel.FileIODirNotify += fsHandler.OnDirNotify;
                    kernel.FileIOFileCreate += fsHandler.OnFileCreate;
                    kernel.FileIOFileDelete += fsHandler.OnFileDelete;
                    kernel.FileIOFileRundown += fsHandler.OnFileRundown;
                    kernel.FileIOFSControl += fsHandler.OnFSControl;
                    kernel.FileIOMapFile += fsHandler.OnMapFile;
                    kernel.FileIOMapFileDCStart += fsHandler.OnMapFileDCStart;
                    kernel.FileIOMapFileDCStop += fsHandler.OnMapFileDCStop;
                    kernel.FileIOName += fsHandler.OnName;
                    kernel.FileIOQueryInfo += fsHandler.OnQueryInfo;
                    kernel.FileIOSetInfo += fsHandler.OnSetInfo;
                    kernel.FileIOUnmapFile += fsHandler.OnUnmapFile;
                    kernel.ProcessStart += data => processCache.RefreshFromProcess(data.ProcessID);
                    kernel.ProcessStop += data => processCache.Remove(data.ProcessID);

                    kernel.DiskIORead += dsHandler.OnDiskRead;
                    kernel.DiskIOReadInit += dsHandler.OnDiskInit;
                    kernel.DiskIOWriteInit += dsHandler.OnDiskInit;
                    kernel.DiskIOWrite += dsHandler.OnDiskWrite;
                    kernel.DiskIOFlushBuffers += dsHandler.OnDiskFlush;
                    kernel.DiskIODriverMajorFunctionCall += driverHandler.OnDriverMajorFunctionCall;
                    kernel.DiskIODriverMajorFunctionReturn += driverHandler.OnDriverMajorFunctionReturn;
                    kernel.DiskIODriverCompletionRoutine += driverHandler.OnDriverCompletionRoutine;
                    kernel.DiskIODriverCompleteRequest += driverHandler.OnDriverCompleteRequest;
                    kernel.DiskIODriverCompleteRequestReturn += driverHandler.OnDriverCompleteRequestReturn;

                    kernel.TcpIpSend += nwHandler.OnSend;
                    kernel.TcpIpRecv += nwHandler.OnReceive;
                    kernel.TcpIpRecvIPV6 += nwHandler.OnReceive;
                    kernel.TcpIpSendIPV6 += nwHandler.OnSend;
                    kernel.UdpIpSend += nwHandler.OnSend;
                    kernel.UdpIpRecv += nwHandler.OnReceive;
                    kernel.UdpIpSendIPV6 += nwHandler.OnSend;
                    kernel.UdpIpRecvIPV6 += nwHandler.OnReceive;

                    kernel.TcpIpConnect += nwHandler.OnConnect;
                    kernel.TcpIpDisconnect += nwHandler.OnDisconnect;
                    kernel.TcpIpAccept += nwHandler.OnAccept;
                    kernel.TcpIpReconnect += nwHandler.OnReconnect;
                    kernel.TcpIpRetransmit += nwHandler.OnRetransmit;
                    kernel.TcpIpFail += nwHandler.OnFail;

                    source.Dynamic.All += (TraceEvent data) =>
                    {
                        if (data.ProviderName == "Microsoft-Windows-TCPIP")
                        {
                            nwHandler.OnTcpHandshake(data);
                        }
                    };

                    source.Process(); // blocks until session.Stop() is called
                }
            }
            catch (Exception ex) when (!isShuttingDown && !cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine($"[Tracer] Session error: {ex.Message}");
            }
            finally
            {
                session = null;
            }
        }

        private void CleanupOrphanedSession(string sessionName)
        {
            try
            {
                var existingSession = TraceEventSession.GetActiveSession(sessionName);
                if (existingSession != null)
                {
                    Debug.WriteLine($"Found orphaned session '{sessionName}', cleaning up...");
                    existingSession.Stop();
                    existingSession.Dispose();
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Warning: Could not clean up orphaned session: {ex.Message}");
            }
        }
    }
}
