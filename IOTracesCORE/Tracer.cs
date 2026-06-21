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
        private readonly MemoryHandlers memHandler;
        private readonly CacheHandlers cacheHandler;
        private readonly ProcessCommandLineCache processCache;
        private readonly ProcessSnapper psHandler;
        private readonly FilesystemSnapper fsSnapper;
        private TraceEventSession? session;
        private ObjectStorageHandler objHandler;
        private bool isUploadAutomatically;
        private volatile bool isShuttingDown = false;

        // Cumulative ETW dropped-event count from any prior (restarted) sessions; the
        // live total published to TraceStats is this plus the current source.EventsLost
        // (consumer-side) / session.EventsLost (OS-authoritative) respectively.
        private long _etwLostBaseline = 0;
        private long _sessionLostBaseline = 0;
        // Set once we've written the cross-run truncation marker this process, so the 2s
        // lost-poller records it a single time rather than re-writing on every tick.
        private bool _truncationMarkerWritten = false;
        private int cleanupStarted = 0;
        // Lightweight mode for resource-constrained machines: suppresses the highest-overhead
        // streams (per-operation op_end completions, and the memory/virtual-alloc keywords).
        private readonly bool lowOverheadLogging;

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

        public Tracer(bool anonymouse, bool upload, ObjectStorageHandler obj, string outputPath = ".\\output", bool devMode = false, bool lowOverheadLogging = false)
        {
            objHandler = obj;
            isUploadAutomatically = upload;
            this.lowOverheadLogging = lowOverheadLogging;
            wm = new WriterManager($"{outputPath}\\windows_trace\\{PathHasher.deviceId}", anonymouse, upload, objHandler, devMode, lowOverheadLogging);
            processCache = new ProcessCommandLineCache();
            fsHandler = new FilesystemHandlers(wm, processCache, lowOverheadLogging);
            dsHandler = new DiskHandlers(wm);
            memHandler = new MemoryHandlers(wm);
            cacheHandler = new CacheHandlers(wm);
            psHandler = new ProcessSnapper(wm, anonymouse, processCache);
            fsSnapper = new FilesystemSnapper(wm, anonymouse);
            systemSnapper = new SystemSnapper(wm);
            nwHandler = new NetworkHandlers(wm);
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

                // Surface the zero-disk-event case explicitly. The ds/ stream is
                // written lazily, so a short or cache-served run that produced no
                // physical disk I/O leaves no ds/ folder at all — which looks like
                // a broken stream. Saying so distinguishes "no block I/O happened"
                // (expected) from "disk tracing failed".
                long diskEvents = Interlocked.Read(ref TraceStats.DiskEvents);
                if (diskEvents == 0)
                {
                    Console.WriteLine(
                        "Disk diagnostics: 0 disk I/O events captured, so no ds/ stream was " +
                        "written. This is expected on short or cache-served runs — reads are " +
                        "served from the cache and write-back may not have flushed, so no " +
                        "physical disk I/O occurs. The ds/ stream only appears once real device " +
                        "I/O happens (cache misses, flushes, write-back).");
                }
                else
                {
                    Console.WriteLine($"Disk diagnostics: {diskEvents} disk I/O events captured.");
                }

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

            // Drop cross-event correlation state from any previous session so kernel
            // pointers (IRPs, FileObject/FileKey) reused by the new session can't be
            // mis-correlated across a session restart.
            dsHandler.Reset();
            fsHandler.Reset();

            try
            {
                using (session = new TraceEventSession(sessionName))
                {
                    session.StopOnDispose = true;

                    var keywords =
                        KernelTraceEventParser.Keywords.FileIO |
                        KernelTraceEventParser.Keywords.FileIOInit |
                        KernelTraceEventParser.Keywords.Process |
                        KernelTraceEventParser.Keywords.DiskIO |
                        KernelTraceEventParser.Keywords.DiskIOInit |
                        KernelTraceEventParser.Keywords.NetworkTCPIP;

                    // Lightweight mode drops the memory/virtual-alloc keywords. These are by
                    // far the chattiest kernel events, so not enabling them means the kernel
                    // never generates them — a real CPU/buffer saving, not just smaller files.
                    if (!lowOverheadLogging)
                    {
                        keywords |=
                            KernelTraceEventParser.Keywords.Memory |
                            KernelTraceEventParser.Keywords.MemoryHardFaults |
                            KernelTraceEventParser.Keywords.VirtualAlloc;
                    }

                    // FileIO is an extremely high-volume kernel provider. The default ETW
                    // buffer pool is only a few MB, so during a launch/boot burst the kernel
                    // real-time buffers overflow within seconds; once the consumer is behind,
                    // the kernel drops the highest-volume provider (FileIO) wholesale for the
                    // rest of the session while low-rate providers (TcpIp) still find slots.
                    // This is the cause of the observed "fs/ stream truncated to a startup
                    // burst" pathology. Size the buffer pool generously so bursts are absorbed,
                    // but scale to physical RAM and cap it so we never lock an unreasonable
                    // share of non-paged pool on a small machine. Must be set BEFORE
                    // EnableKernelProvider. An over-large request can be rejected by ETW, so
                    // fall back to a conservative size rather than losing the whole session.
                    long ramBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                    int ramMb = ramBytes > 0 ? (int)(ramBytes / (1024L * 1024L)) : 0;
                    // Buffer COUNT matters as much as total size. TraceEvent derives the count as
                    // BufferSizeMB*1024 / BufferQuantumKB; with the default 64KB quantum a 1GB pool
                    // needs ~16,384 buffers, which ETW refuses (too many) — so the step-down below
                    // would silently shrink the pool even when the RAM is available. Use 1MB buffers
                    // so the same pool maps to a sane count (1GB -> 1024 buffers): fewer, larger
                    // buffers absorb a high-volume FileIO burst with far less per-buffer flush
                    // overhead, AND the large pool actually gets granted. 1MB is ETW's practical
                    // max buffer size; if a system rejects it, the last-resort fallback resets the
                    // quantum to the library default. Must be set BEFORE EnableKernelProvider.
                    const int largeQuantumKb = 1024;   // 1MB per buffer
                    const int defaultQuantumKb = 64;   // library default, used in the fallback
                    session.BufferQuantumKB = largeQuantumKb;
                    int maxBufferMb = lowOverheadLogging ? 256 : 1024;
                    int bufferMb = ramMb > 0 ? Math.Clamp(ramMb / 32, 128, maxBufferMb) : maxBufferMb;
                    // Step DOWN through decreasing pool sizes rather than collapsing straight to
                    // 64MB when the first request is rejected: a machine that won't grant 1024MB
                    // may still grant 512/256MB, and every extra MB is burst-absorption headroom
                    // against kernel-side FileIO drops. Record the size that actually took
                    // (TraceStats -> manifest) so a capture never silently reports health while
                    // running on a tiny pool. EnableKernelProvider must SUCCEED only once; the
                    // first size that does wins and we stop.
                    int appliedBufferMb = 0;
                    foreach (int mb in new[] { bufferMb, 768, 512, 384, 256, 192, 128, 96, 64 })
                    {
                        if (mb > bufferMb) continue; // never exceed the RAM-scaled request
                        try
                        {
                            session.BufferSizeMB = mb;
                            session.EnableKernelProvider(keywords);
                            appliedBufferMb = mb;
                            break;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Tracer] EnableKernelProvider rejected {mb}MB buffers " +
                                $"({ex.Message}); stepping down.");
                        }
                    }
                    if (appliedBufferMb == 0)
                    {
                        // Every sized attempt was rejected — reset to library defaults (including
                        // the 64KB quantum, in case 1MB buffers were what this system refused) and
                        // let ETW choose, so a too-large request never takes down the whole session.
                        session.BufferQuantumKB = defaultQuantumKb;
                        session.EnableKernelProvider(keywords);
                        appliedBufferMb = session.BufferSizeMB;
                    }
                    utils.TraceStats.SetBufferSizeMb(appliedBufferMb);

                    if (!lowOverheadLogging)
                    {
                        var memoryManagerGuid = new Guid("d1d93ef7-e1f2-4f45-9943-03d245fe6c00");
                        session.EnableProvider(memoryManagerGuid);
                    }

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
                    kernel.FileIOOperationEnd += fsHandler.OnOperationEnd;
                    kernel.ProcessStart += data => processCache.RefreshFromProcess(data.ProcessID);
                    kernel.ProcessStop += data => processCache.Remove(data.ProcessID);

                    kernel.DiskIORead += dsHandler.OnDiskRead;
                    kernel.DiskIOReadInit += dsHandler.OnDiskInit;
                    kernel.DiskIOWriteInit += dsHandler.OnDiskInit;
                    kernel.DiskIOWrite += dsHandler.OnDiskWrite;
                    kernel.DiskIOFlushBuffers += dsHandler.OnDiskFlush;

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

                    // Poll the kernel's dropped-event count live (it rises as consumer
                    // buffers overrun) and publish it to TraceStats every couple of seconds,
                    // so the periodically-refreshed manifest reports capture loss even when
                    // the session is killed before the clean-shutdown path below runs. The
                    // running total is baseline (from any prior, restarted sessions) plus
                    // this session's source.EventsLost. The flag stops THIS session's poller
                    // when Process() returns (a session restart creates a fresh source/poller).
                    bool sessionEnded = false;
                    var lostPoller = new Thread(() =>
                    {
                        while (!Volatile.Read(ref sessionEnded) && !isShuttingDown)
                        {
                            // Consumer-side count — often a false 0 for real-time kernel
                            // sessions even when the kernel dropped (e.g. fs truncation).
                            try { utils.TraceStats.SetEtwEventsLost(_etwLostBaseline + source.EventsLost); }
                            catch { /* source not ready / torn read — try again next tick */ }
                            // AUTHORITATIVE count: session.EventsLost queries the OS
                            // (ControlTrace -> EVENT_TRACE_PROPERTIES). This is the one that
                            // actually reveals kernel drops when the consumer-side reads 0.
                            try
                            {
                                var s = session;
                                if (s != null)
                                {
                                    long authoritativeLost = _sessionLostBaseline + s.EventsLost;
                                    utils.TraceStats.SetSessionEventsLost(authoritativeLost);
                                    // A run that has already lost this many events to kernel-side
                                    // drops is truncating; drop a machine-local marker (once) so the
                                    // NEXT launch defaults to lightweight, lowering the kernel event
                                    // rate this single consumer thread must sustain. Best-effort.
                                    if (!_truncationMarkerWritten
                                        && authoritativeLost >= utils.TruncationState.LossThreshold)
                                    {
                                        utils.TruncationState.Record(authoritativeLost);
                                        _truncationMarkerWritten = true;
                                    }
                                }
                            }
                            catch { /* OS query failed / session stopping — retry next tick */ }
                            Thread.Sleep(2000);
                        }
                    })
                    { IsBackground = true, Name = "etw-lost-poller" };
                    lostPoller.Start();

                    source.Process(); // blocks until session.Stop() is called

                    // Session ended: stop the poller and fold this session's dropped-event
                    // counts into the baselines so a subsequent restarted session accumulates
                    // rather than overwrites.
                    Volatile.Write(ref sessionEnded, true);
                    _etwLostBaseline += source.EventsLost;
                    utils.TraceStats.SetEtwEventsLost(_etwLostBaseline);
                    try
                    {
                        var s = session;
                        if (s != null)
                        {
                            _sessionLostBaseline += s.EventsLost;
                            utils.TraceStats.SetSessionEventsLost(_sessionLostBaseline);
                        }
                    }
                    catch { /* session already stopped; the last polled value stands */ }
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
