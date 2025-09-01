using IOTracer.handlers;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracer
{
    class Tracer
    {
        private readonly WriterManager wm;
        private readonly FilesystemHandlers fsHandler;
        private readonly DiskHandlers dsHandler;
        //private readonly MemoryHandlers  mrHandler;

        public Tracer()
        {
            wm = new WriterManager(".\\output");
            fsHandler = new FilesystemHandlers(wm);
            dsHandler = new DiskHandlers(wm);
            //mrHandler = new MemoryHandlers(wm);
        }

        public void Trace()
        {
            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Console.Error.WriteLine("Please run as Administrator.");
                return;
            }

            Console.WriteLine("Starting IOTracer...");
            Console.WriteLine("Press CTRL + C to exit!");
            string sessionName = "IOTrace-" + Process.GetCurrentProcess().Id;
            using (var session = new TraceEventSession(sessionName))
            {
                session.StopOnDispose = true;
                Console.CancelKeyPress += (_, e) =>
                {
                    Console.WriteLine("Cleaing up!");
                    wm.CompressAll();
                    e.Cancel = true;
                    session.Dispose();
                    Console.WriteLine("Cleaing up complete.");
                };

                session.EnableKernelProvider(
                    //KernelTraceEventParser.Keywords.Process |
                    //KernelTraceEventParser.Keywords.Memory |
                    //KernelTraceEventParser.Keywords.MemoryHardFaults |
                    //KernelTraceEventParser.Keywords.VirtualAlloc |
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


                //// MEMORY HANDLERS
                //// ---- PAGE FAULT FAMILY ----
                //kernel.MemoryTransitionFault += mrHandler.OnMemoryTransitionFault;
                //kernel.MemoryDemandZeroFault += mrHandler.OnMemoryDemandZeroFault;
                //kernel.MemoryCopyOnWrite += mrHandler.OnMemoryCopyOnWrite;
                //kernel.MemoryGuardMemory += mrHandler.OnMemoryGuardMemory;
                //kernel.MemoryGuardMemory += mrHandler.OnMemoryGuardMemory;
                //kernel.MemoryAccessViolation += mrHandler.OnMemoryAccessViolation;
                //kernel.MemoryHardFault += mrHandler.OnMemoryHardFault;

                //// ---- VIRTUAL MEMORY OPS (note: VirtualMem*) ----
                //kernel.VirtualMemAlloc += mrHandler.OnVirtualMemAlloc;
                //kernel.VirtualMemFree += mrHandler.OnVirtualMemFree;


                // Pump
                source.Process();
            }
        }
    }
}
