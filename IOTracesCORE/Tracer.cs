using IOTracesCORE.handlers;
using IOTracesCORE.snapper;
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
        private readonly ProcessSnapper psHandler;
        private readonly FilesystemSnapper fsSnapper;
        private TraceEventSession? session;
        private volatile bool isShuttingDown = false;
        private bool anonymouse = false;

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

        public Tracer(bool anonymouse, string outputPath = ".\\output")
        {
            wm = new WriterManager(outputPath);
            fsHandler = new FilesystemHandlers(wm);
            dsHandler = new DiskHandlers(wm);
            psHandler = new ProcessSnapper(wm);
            fsSnapper = new FilesystemSnapper(wm, anonymouse);
            systemSnapper = new SystemSnapper(wm);
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
                CleanupAndExit();
            }

            return true;
        }

        private void CleanupAndExit()
        {
            try
            {
                Console.WriteLine("Performing cleanup operations...");

                if (session != null)
                {
                    session.Dispose();
                    session = null;
                }
                fsSnapper.Stop();
                psHandler.Stop();
                wm.CompressAll();

                Console.WriteLine("Cleanup completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
            finally
            {
                Thread.Sleep(1000);
                Environment.Exit(0);
            }
        }

        public void Trace()
        {
            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Console.Error.WriteLine("Please run as Administrator.");
                return;
            }
            SetConsoleCtrlHandler(ConsoleCtrlHandler, true);

            Console.WriteLine("Starting IOTracer...");
            Task _ = Task.Run(() => fsSnapper.Run());
            Task __ = Task.Run(() => psHandler.Run());
            Console.WriteLine("Press CTRL + C to exit, or close the console window!");
            systemSnapper.CaptureSpecSnapshot();

            string sessionName = "IOTrace-" + Process.GetCurrentProcess().Id;

            try
            {
                using (session = new TraceEventSession(sessionName))
                {
                    session.StopOnDispose = true;

                    Console.CancelKeyPress += (_, e) =>
                    {
                        if (!isShuttingDown)
                        {
                            Console.WriteLine("\nCtrl+C pressed. Cleaning up...");
                            e.Cancel = true;
                            isShuttingDown = true;
                            CleanupAndExit();
                        }
                    };

                    session.EnableKernelProvider(
                        KernelTraceEventParser.Keywords.FileIO |
                        KernelTraceEventParser.Keywords.FileIOInit |
                        KernelTraceEventParser.Keywords.DiskIO
                    );

                    var source = session.Source;
                    var kernel = source.Kernel;

                    // FS HANDLERS
                    kernel.FileIORead += fsHandler.OnFileRead;
                    kernel.FileIOWrite += fsHandler.OnFileWrite;
                    kernel.FileIOClose += fsHandler.OnFileClose;
                    kernel.FileIOCreate += fsHandler.OnFileCreate;
                    kernel.FileIODelete += fsHandler.OnFileDelete;
                    kernel.FileIOFlush += fsHandler.OnFileFlush;

                    // DISK HANDLERS    
                    kernel.DiskIORead += dsHandler.OnDiskRead;
                    kernel.DiskIOWrite += dsHandler.OnDiskWrite;

                    // Start processing events
                    source.Process();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during tracing: {ex.Message}");
                if (!isShuttingDown)
                {
                    CleanupAndExit();
                }
            }
            finally
            {
                SetConsoleCtrlHandler(ConsoleCtrlHandler, false);
            }
        }
    }
}