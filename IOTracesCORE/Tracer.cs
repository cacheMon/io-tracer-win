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
        private readonly ProcessCommandLineCache processCache;
        private readonly ProcessSnapper psHandler;
        private readonly FilesystemSnapper fsSnapper;
        private TraceEventSession? session;
        // Lever 4: FileIO is the highest-volume kernel provider, so it runs in its OWN kernel
        // session (Win8+ allows multiple) with its own buffer pool AND its own consumer thread.
        // That stops a FileIO burst from starving — or being starved by — Process/Disk/Net/Memory
        // on the main session, and gives FileIO a full thread to drain at line rate.
        private TraceEventSession? fileSession;
        private ObjectStorageHandler objHandler;
        private bool isUploadAutomatically;
        private volatile bool isShuttingDown = false;

        // Cumulative ETW dropped-event count from any prior (restarted) sessions; the
        // live total published to TraceStats is this plus the current source.EventsLost
        // (consumer-side) / session.EventsLost (OS-authoritative) respectively.
        private long _etwLostBaseline = 0;
        private long _sessionLostBaseline = 0;
        // Same baseline accumulation for the MAIN session's authoritative lost count (DiskIO/
        // Process/byte-network), reported separately from the FILE session above.
        private long _mainSessionLostBaseline = 0;
        // Set once we've written the cross-run truncation marker this process, so the 2s
        // lost-poller records it a single time rather than re-writing on every tick.
        private bool _truncationMarkerWritten = false;
        private int cleanupStarted = 0;
        // Lightweight mode for resource-constrained / high-load machines. Two reductions:
        //   (1) the main session drops op_end completions + the memory/virtual-alloc keywords; and
        //   (2) fs capture switches from the legacy NT-Kernel-Logger FileIO keywords to the modern
        //       Microsoft-Windows-Kernel-File provider with a REDUCED keyword set (read/write/create/
        //       name/delete/rename only), so the kernel never GENERATES the metadata-op mass
        //       (getattr/close/cleanup/dir_enum/fsctl) — ~3x fewer FileIO events at the source
        //       (A/B-measured 4779->1597 ev/s on Win Server 2025), pushing the single-consumer
        //       ceiling (#68) ~3x further out while keeping read/write/create + filenames at full
        //       fidelity (modern filename resolution measured 100% vs the legacy path's 57%).
        // The default is chosen by machine spec (see MachineProfile); --lightweight/--full override,
        // and a recent truncation escalates to lightweight. map_file + metadata ops are intentionally
        // absent in lightweight. (net8.0 requires Win10+, where the modern provider always exists.)
        private readonly bool lowOverheadLogging;
        // The modern Microsoft-Windows-Kernel-File manifest provider GUID.
        private static readonly Guid KernelFileProviderGuid = new Guid("EDD08927-9CC4-4E65-B970-C2560FB5C289");
        // Headless/CLI mode: no WinForms UI (no please-wait form, no error MessageBox) so the
        // tracer can run over SSH / in a non-interactive session for automated validation.
        private readonly bool headless;

        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        delegate bool ConsoleCtrlDelegate(CtrlTypes ctrlType);

        // Rooted reference to the console-control callback. Without this field the delegate
        // created from the ConsoleCtrlHandler method group is unreferenced managed memory and
        // can be garbage-collected while the native console subsystem still holds its function
        // pointer — a later Ctrl+C / console-close / logoff would then call freed memory and
        // crash the process (taking the clean-shutdown + manifest-finalize path down with it).
        private ConsoleCtrlDelegate? _consoleCtrlHandler;

        enum CtrlTypes : uint
        {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        public Tracer(bool anonymouse, bool upload, ObjectStorageHandler obj, string outputPath = ".\\output", bool devMode = false, bool lowOverheadLogging = false, bool headless = false)
        {
            objHandler = obj;
            isUploadAutomatically = upload;
            this.lowOverheadLogging = lowOverheadLogging;
            this.headless = headless;
            wm = new WriterManager($"{outputPath}\\windows_trace\\{PathHasher.deviceId}", anonymouse, upload, objHandler, devMode, lowOverheadLogging);
            processCache = new ProcessCommandLineCache();
            fsHandler = new FilesystemHandlers(wm, processCache, lowOverheadLogging);
            dsHandler = new DiskHandlers(wm);
            psHandler = new ProcessSnapper(wm, anonymouse, processCache);
            fsSnapper = new FilesystemSnapper(wm, anonymouse);
            systemSnapper = new SystemSnapper(wm, anonymouse);
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

                // Dispose the FileIO session first (it depends on nothing else), then the main
                // session. StopOnDispose stops each. Its consumer thread is a background thread and
                // its Process() unblocks on Stop, so no explicit join is needed here.
                if (fileSession != null)
                {
                    fileSession.Dispose();
                    fileSession = null;
                }

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
                // No MessageBox in headless mode — it would block forever with no desktop.
                if (!headless)
                    MessageBox.Show($"Error! {ex.Message}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.WriteLine($"Error during cleanup: {ex.Message}");
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
            CleanupOrphanedSession(sessionName + "FileIO"); // dedicated FileIO session (Lever 4)
            // Keep the delegate rooted for the process lifetime (see _consoleCtrlHandler).
            _consoleCtrlHandler = ConsoleCtrlHandler;
            SetConsoleCtrlHandler(_consoleCtrlHandler, true);

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
            // Fire-and-forget background tasks. Guard each Run() body: an unobserved
            // exception would silently end the census (fsSnapper.Run lacks a top-level
            // catch) and — on some GC configs — resurface later as an UnobservedTask
            // exception. At minimum it must be logged, not vanish.
            _ = Task.Run(() =>
            {
                try { fsSnapper.Run(); }
                catch (Exception ex) { Debug.WriteLine($"[fsSnapper] filesystem census aborted: {ex}"); }
            });
            Task __ = Task.Run(() =>
            {
                try { psHandler.Run(); }
                catch (Exception ex) { Debug.WriteLine($"[psHandler] process snapshot loop aborted: {ex}"); }
            });
            // system_spec capture does WMI + DNS + a geolocation HTTP call. Run synchronously
            // here it (a) blocked ETW session start for up to ~13s on a slow/offline network —
            // a pure lost-event window — and (b) let a WMI/DNS failure throw straight out of
            // Trace(), crashing the (headless) tracer before it captured anything. Run it
            // guarded and OFF the start path so neither can happen.
            _ = Task.Run(() =>
            {
                try { systemSnapper.CaptureSpecSnapshot(); }
                catch (Exception ex) { Debug.WriteLine($"[systemSnapper] spec capture failed: {ex}"); }
            });

            if (isUploadAutomatically)
            {
                // Register the stop callback so the upload worker can halt the ETW session
                ObjectStorageHandler.OnStopSessionRequested = () =>
                {
                    Debug.WriteLine("[Tracer] Stop requested by upload worker — stopping ETW session.");
                    session?.Stop();
                    fileSession?.Stop();
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
                    fileSession?.Stop();
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
                if (headless)
                {
                    // No interactive desktop in headless mode — skip the please-wait window and
                    // clean up directly (CleanupAndExitAsync ends in Environment.Exit).
                    CleanupAndExitAsync().GetAwaiter().GetResult();
                }
                else
                {
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
        }

        /// <summary>
        /// Creates and runs a single ETW kernel session. Returns when
        /// <see cref="TraceEventSession.Stop"/> is called (either by user shutdown
        /// or by the upload worker via <see cref="ObjectStorageHandler.OnStopSessionRequested"/>).
        /// </summary>
        private void RunOneSession(string sessionName, CancellationToken cancellationToken)
        {
            // The dedicated FileIO session (Lever 4) runs alongside the main one under a derived
            // name; a non-NT-Kernel-Logger name is what lets Win8+ run a second kernel session.
            string fileSessionName = sessionName + "FileIO";
            CleanupOrphanedSession(sessionName);
            CleanupOrphanedSession(fileSessionName);

            // Drop cross-event correlation state from any previous session so kernel
            // pointers (IRPs, FileObject/FileKey) reused by the new session can't be
            // mis-correlated across a session restart.
            dsHandler.Reset();
            fsHandler.Reset();

            try
            {
                using (session = new TraceEventSession(sessionName))
                using (fileSession = new TraceEventSession(fileSessionName))
                {
                    session.StopOnDispose = true;
                    fileSession.StopOnDispose = true;

                    // Split the kernel providers across two sessions. The MAIN session carries
                    // everything EXCEPT FileIO; the dedicated FILE session carries only the
                    // high-volume FileIO provider (+ Process, so FileIO events still resolve a
                    // process name). Each gets its own buffer pool and — further below — its own
                    // consumer thread, so a FileIO burst can neither starve nor be starved by the
                    // other providers (the "fs/ stream truncated to a startup burst" pathology was
                    // FileIO overflowing a pool/consumer it shared with everything else). Win8+
                    // permits multiple kernel sessions as long as the session name is not the
                    // NT-Kernel-Logger name (both names here are custom).
                    var mainKeywords =
                        KernelTraceEventParser.Keywords.Process |
                        KernelTraceEventParser.Keywords.DiskIO |
                        KernelTraceEventParser.Keywords.DiskIOInit |
                        KernelTraceEventParser.Keywords.NetworkTCPIP;
                    // Memory/virtual-alloc keywords are intentionally NOT enabled. No handler ever
                    // consumed these events (MemoryHandlers/CacheHandlers were built but never
                    // subscribed, so the mr/ stream was always empty), yet they are the chattiest
                    // kernel events and competed for the very ETW buffers that drop FileIO — a dead
                    // stream that actively worsened fs-truncation. The collection path (these
                    // keywords, the memory-manager provider, and the MemoryHandlers/CacheHandlers
                    // wiring) is removed; the dormant mr/ schema entry remains (always empty, and
                    // no longer mis-flagged as a dead probe — see TraceManifest).

                    // FileIOInit carries the per-operation file events we consume (read/write/
                    // create/cleanup/...); the FileIO keyword ADDS the op_end completions, which
                    // roughly DOUBLE FileIO volume (validated on Windows Server 2025 via a keyword
                    // probe: op_end was ~64% of FileIO events) and are the single biggest contributor
                    // to kernel-side fs drops. op_end is now dropped in BOTH modes (op_end off AT THE
                    // KERNEL = real volume relief, ~halving the FileIO rate the single consumer must
                    // sustain, which directly reduces the truncation seen on heavy-I/O machines). The
                    // trade is acceptable: op_end's per-op latency was already unreliable (irp pointers
                    // are recycled, so the begin/end join mis-pairs and yields absurd tails), and the
                    // block/ stream carries a real MEASURED disk latency; the only genuine loss is the
                    // fs nt_status completion code.
                    //
                    // DiskFileIO is the FileKey->path rundown keyword (FileIo/Name + FileIo/Rundown).
                    // read/write events are keyed by FileKey, and their name is resolved from the
                    // nameByObj cache that OnName (FileIo/Name) populates — NOT from the create event
                    // (which only stores FileObject). Without DiskFileIO that cache is starved:
                    // a controlled A/B on Win Server 2025 measured **55% of lightweight read/write
                    // rows with an empty filename vs 0% once DiskFileIO is enabled**, at negligible
                    // added volume (the rundown is one-shot at start + on create, not per-op). So it
                    // belongs in BOTH modes and is now the sole FileKey->path source for full mode too
                    // (which no longer enables the FileIO keyword), keeping filenames resolved.
                    // Size + enable the MAIN pool (step-down + 1MB buffers, see helper).
                    EnableSizedKernelProvider(session, mainKeywords);

                    // The FILE session carries fs capture. In HIGH-LOAD mode it runs the modern
                    // Microsoft-Windows-Kernel-File manifest provider with a REDUCED keyword set, so
                    // the kernel never generates the metadata-op mass (getattr/close/cleanup/dir_enum/
                    // fsctl) — ~3x fewer FileIO events at the source. Otherwise it runs the legacy
                    // NT-Kernel-Logger FileIO keywords. EITHER WAY we also enable the kernel Process
                    // keyword on this session so fs events resolve a ProcessName (a single session can
                    // host both a kernel provider and a manifest provider, as the main session does).
                    // The FILE pool is the truncation-relevant one, so its granted size is published.
                    int fileBufferMb;
                    if (lowOverheadLogging)
                    {
                        // Lightweight: modern reduced-keyword provider. Enable the kernel Process
                        // keyword (for ProcessName resolution) + the Kernel-File manifest provider.
                        fileBufferMb = EnableSizedKernelProvider(fileSession, KernelTraceEventParser.Keywords.Process);
                        // Filename(0x10)|Create(0x80)|Read(0x100)|Write(0x200)|Deletepath(0x400)|
                        // Renamesetlinkpath(0x800) = 0xF90. OMITS Fileio(0x20) (the metadata firehose:
                        // close/cleanup/getattr/setattr/fsctl/dir_enum/flush) and Opend(0x40), so they
                        // are never generated. Validated on Win Server 2025: 4779->1597 ev/s, read/
                        // write/create kept 100%, filenames resolved 100%, ProcessName resolved 100%.
                        const ulong kernelFileDataKeywords = 0x10UL | 0x80UL | 0x100UL | 0x200UL | 0x400UL | 0x800UL;
                        fileSession.EnableProvider(KernelFileProviderGuid, TraceEventLevel.Informational, kernelFileDataKeywords);
                    }
                    else
                    {
                        // Full: legacy NT-Kernel-Logger FileIO, op_end DROPPED (no FileIO keyword) to
                        // halve the kernel FileIO rate. FileIOInit carries the per-op events we emit
                        // (read/write/create/cleanup/...); DiskFileIO is the FileKey->path name rundown;
                        // Process gives ProcessName. (op_end = the FileIO keyword, intentionally absent.)
                        var fileKeywords =
                            KernelTraceEventParser.Keywords.FileIOInit |
                            KernelTraceEventParser.Keywords.DiskFileIO |
                            KernelTraceEventParser.Keywords.Process;
                        fileBufferMb = EnableSizedKernelProvider(fileSession, fileKeywords);
                    }
                    utils.TraceStats.SetBufferSizeMb(fileBufferMb);

                    // (memory-manager provider removed — see the memory-keyword note above; it fed
                    // the same never-consumed mr/ stream.)

                    // (Microsoft-Windows-TCPIP manifest provider removed: it was enabled UNFILTERED
                    // — EnableProvider(Guid) defaults to Verbose level + matchAnyKeywords=ulong.MaxValue,
                    // i.e. the full per-segment/ack/retransmit firehose — yet its ONLY sink was the
                    // empty no-op OnTcpHandshake. It produced ZERO output rows while the main-session
                    // consumer decoded every event and ran a per-event ProviderName compare, stealing
                    // buffers/CPU from DiskIO + the kernel byte-network events under load. The real nw/
                    // rows come from the kernel NetworkTCPIP keyword via kernel.TcpIp*/UdpIp* below.)

                    var source = session.Source;
                    var kernel = source.Kernel;

                    // The FileIO handlers wire onto the DEDICATED file session's source, so they
                    // are dispatched by that session's own consumer thread (started below). All
                    // FileIO* events (incl. the Name/Rundown filename-mapping events) are on this
                    // one session, so fsHandler's cross-event correlation stays self-contained.
                    var fileSource = fileSession.Source;

                    if (lowOverheadLogging)
                    {
                        // Lightweight: the modern provider's events are MANIFEST events (not the typed
                        // kernel FileIO* callbacks), so they arrive via Dynamic. A single dispatcher
                        // adapts read/write/create/namecreate/deletepath/renamepath into the SAME
                        // output via the existing resolve/pending/shed/Emit path. This session enables
                        // ONLY the Process kernel provider + the Kernel-File manifest provider, so
                        // Dynamic.All here sees only Kernel-File events.
                        fileSource.Dynamic.All += fsHandler.OnModernFileEvent;
                    }
                    else
                    {
                        // Legacy NT-Kernel-Logger FileIO. The FileIO handlers wire onto the DEDICATED
                        // file session's source, dispatched by its own consumer thread (started below).
                        // All FileIO* events (incl. the Name/Rundown filename-mapping events) are on
                        // this one session, so fsHandler's cross-event correlation stays self-contained.
                        var fileKernel = fileSource.Kernel;

                        fileKernel.FileIORead += fsHandler.OnRead;
                        fileKernel.FileIOWrite += fsHandler.OnWrite;
                        fileKernel.FileIOClose += fsHandler.OnClose;
                        fileKernel.FileIOCreate += fsHandler.OnCreate;
                        fileKernel.FileIODelete += fsHandler.OnDelete;
                        fileKernel.FileIOFlush += fsHandler.OnFlush;
                        fileKernel.FileIODirEnum += fsHandler.OnDirEnum;
                        fileKernel.FileIORename += fsHandler.OnRename;
                        fileKernel.FileIOCleanup += fsHandler.OnCleanup;
                        fileKernel.FileIODirNotify += fsHandler.OnDirNotify;
                        fileKernel.FileIOFileCreate += fsHandler.OnFileCreate;
                        fileKernel.FileIOFileDelete += fsHandler.OnFileDelete;
                        fileKernel.FileIOFileRundown += fsHandler.OnFileRundown;
                        fileKernel.FileIOFSControl += fsHandler.OnFSControl;
                        fileKernel.FileIOMapFile += fsHandler.OnMapFile;
                        fileKernel.FileIOMapFileDCStart += fsHandler.OnMapFileDCStart;
                        fileKernel.FileIOMapFileDCStop += fsHandler.OnMapFileDCStop;
                        fileKernel.FileIOName += fsHandler.OnName;
                        fileKernel.FileIOQueryInfo += fsHandler.OnQueryInfo;
                        fileKernel.FileIOSetInfo += fsHandler.OnSetInfo;
                        fileKernel.FileIOUnmapFile += fsHandler.OnUnmapFile;
                        // op_end (FileIOOperationEnd) is intentionally NOT wired: the FileIO keyword
                        // that generates it is no longer enabled (op_end dropped at the kernel for
                        // volume relief). OnOperationEnd is retained for the unit tests / a possible
                        // future opt-in, but no op_end events are delivered in this configuration.
                    }

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

                    // (the dead Microsoft-Windows-TCPIP Dynamic.All sink was removed with the
                    // provider above — it routed the unfiltered firehose into an empty handler.)

                    // Poll the kernel's dropped-event count live (it rises as consumer
                    // buffers overrun) and publish it to TraceStats every couple of seconds,
                    // so the periodically-refreshed manifest reports capture loss even when
                    // the session is killed before the clean-shutdown path below runs. The
                    // running total is baseline (from any prior, restarted sessions) plus
                    // this session's source.EventsLost. The flag stops THIS session's poller
                    // when Process() returns (a session restart creates a fresh source/poller).
                    // Poll the FILE session's dropped-event count live (it rises as the FileIO
                    // consumer/buffers overrun) and publish it to TraceStats every couple of
                    // seconds, so the periodically-refreshed manifest reports fs capture loss even
                    // when the session is killed before clean shutdown. fs truncation is the
                    // concern, so the authoritative count we track + the sticky-lightweight marker
                    // both key off the FILE session. The flag stops THIS poller when Process()
                    // returns (a restart creates a fresh source/poller).
                    bool sessionEnded = false;
                    var lostPoller = new Thread(() =>
                    {
                        while (!Volatile.Read(ref sessionEnded) && !isShuttingDown)
                        {
                            // Consumer-side count — often a false 0 for real-time kernel
                            // sessions even when the kernel dropped (e.g. fs truncation).
                            try { utils.TraceStats.SetEtwEventsLost(_etwLostBaseline + fileSource.EventsLost); }
                            catch { /* source not ready / torn read — try again next tick */ }
                            // AUTHORITATIVE count: session.EventsLost queries the OS
                            // (ControlTrace -> EVENT_TRACE_PROPERTIES). This is the one that
                            // actually reveals kernel drops when the consumer-side reads 0.
                            try
                            {
                                var fs = fileSession;
                                if (fs != null)
                                {
                                    long authoritativeLost = _sessionLostBaseline + fs.EventsLost;
                                    utils.TraceStats.SetSessionEventsLost(authoritativeLost);
                                    // A run is truncating either by kernel OVERFLOW (authoritativeLost
                                    // crosses the threshold) OR by consumer FREEZE — the FileIO consumer
                                    // falls so far behind that the fs stream stops while the kernel keeps
                                    // a low/zero EventsLost (issue #68; observed: session_events_lost can
                                    // be < threshold yet the fs stream is dead for hours). The freeze is
                                    // visible as the LoadShedder pinned at its top level with a large
                                    // event-time lag. Either way, drop the machine-local marker ONCE so
                                    // the NEXT launch defaults to lightweight. Best-effort.
                                    if (!_truncationMarkerWritten
                                        && utils.TruncationState.ShouldArm(
                                               authoritativeLost,
                                               utils.LoadShedder.ShedLevelNow(),
                                               utils.LoadShedder.ConsumerLagMsNow()))
                                    {
                                        // Record the real overflow count, or the threshold as a freeze
                                        // sentinel when the freeze (not overflow) is what tripped it.
                                        utils.TruncationState.Record(
                                            authoritativeLost >= utils.TruncationState.LossThreshold
                                                ? authoritativeLost
                                                : utils.TruncationState.LossThreshold);
                                        _truncationMarkerWritten = true;
                                    }
                                }
                            }
                            catch { /* OS query failed / session stopping — retry next tick */ }
                            // MAIN session authoritative lost count (DiskIO/Process/byte-network),
                            // reported separately — NOT folded into the fs-truncation signal above.
                            try
                            {
                                var ms = session;
                                if (ms != null)
                                    utils.TraceStats.SetMainSessionEventsLost(_mainSessionLostBaseline + ms.EventsLost);
                            }
                            catch { /* OS query failed / session stopping — retry next tick */ }
                            Thread.Sleep(2000);
                        }
                    })
                    { IsBackground = true, Name = "etw-lost-poller" };
                    lostPoller.Start();

                    // Run the dedicated FileIO consumer on its own thread, then run the main
                    // consumer on this thread. Both block in Process() until their session is
                    // Stop()ed. The MAIN session is the lifecycle anchor: when it ends (user
                    // shutdown or upload-driven restart), we stop the file session too and wait for
                    // its consumer to drain out BEFORE the `using` blocks dispose either session —
                    // disposing a session whose Process() is still running would crash.
                    var fileConsumer = new Thread(() =>
                    {
                        try { fileSource.Process(); }
                        catch (Exception ex) { Debug.WriteLine($"[Tracer] FileIO consumer ended: {ex.Message}"); }
                    })
                    { IsBackground = true, Name = "etw-fileio-consumer" };
                    fileConsumer.Start();

                    source.Process(); // blocks until session.Stop() is called

                    // Main session ended: stop the file session + poller, and join the FileIO
                    // consumer so nothing is touching fileSession when it is disposed.
                    Volatile.Write(ref sessionEnded, true);
                    try { fileSession?.Stop(); } catch { /* already stopping */ }
                    fileConsumer.Join(TimeSpan.FromSeconds(10));

                    // Freeze the consumer-lag clock at the stop point: the FILE consumer has now
                    // drained everything it will (or been cut off at the Join timeout), so the
                    // lag at this instant IS the silent-tail-loss span. Without this, the FINAL
                    // manifest (written later, after the compress/upload drain) would compute lag
                    // against a live DateTime.Now and inflate it past the capture span.
                    utils.LoadShedder.MarkStopped();

                    // Fold this run's dropped-event counts (FILE session = the fs-truncation
                    // signal) into the baselines so a subsequent restarted session accumulates
                    // rather than overwrites.
                    _etwLostBaseline += fileSource.EventsLost;
                    utils.TraceStats.SetEtwEventsLost(_etwLostBaseline);
                    try
                    {
                        var fs = fileSession;
                        if (fs != null)
                        {
                            _sessionLostBaseline += fs.EventsLost;
                            utils.TraceStats.SetSessionEventsLost(_sessionLostBaseline);
                        }
                    }
                    catch { /* session already stopped; the last polled value stands */ }
                    try
                    {
                        var ms = session;
                        if (ms != null)
                        {
                            _mainSessionLostBaseline += ms.EventsLost;
                            utils.TraceStats.SetMainSessionEventsLost(_mainSessionLostBaseline);
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
                fileSession = null;
            }
        }

        /// <summary>
        /// Sizes a kernel session's buffer pool and enables <paramref name="keywords"/>, stepping
        /// the TOTAL pool down until ETW accepts it rather than silently collapsing to a tiny pool.
        /// Uses 1MB buffers so a large pool maps to a sane buffer count. Returns the pool size (MB)
        /// actually granted. Must be called before any handler is wired.
        /// </summary>
        private int EnableSizedKernelProvider(TraceEventSession s, KernelTraceEventParser.Keywords keywords)
        {
            long ramBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            int ramMb = ramBytes > 0 ? (int)(ramBytes / (1024L * 1024L)) : 0;
            const int largeQuantumKb = 1024;   // 1MB per buffer (ETW's practical max)
            const int defaultQuantumKb = 64;   // library default, used in the fallback
            s.BufferQuantumKB = largeQuantumKb;
            int maxBufferMb = lowOverheadLogging ? 256 : 1024;
            int bufferMb = ramMb > 0 ? Math.Clamp(ramMb / 32, 128, maxBufferMb) : maxBufferMb;
            int applied = 0;
            foreach (int mb in new[] { bufferMb, 768, 512, 384, 256, 192, 128, 96, 64 })
            {
                if (mb > bufferMb) continue; // never exceed the RAM-scaled request
                try
                {
                    s.BufferSizeMB = mb;
                    s.EnableKernelProvider(keywords);
                    applied = mb;
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Tracer] {s.SessionName}: EnableKernelProvider rejected {mb}MB " +
                        $"buffers ({ex.Message}); stepping down.");
                }
            }
            if (applied == 0)
            {
                // Every sized attempt was rejected — reset to library defaults (including the 64KB
                // quantum, in case 1MB buffers were what this system refused) and let ETW choose.
                s.BufferQuantumKB = defaultQuantumKb;
                s.EnableKernelProvider(keywords);
                applied = s.BufferSizeMB;
            }
            return applied;
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
