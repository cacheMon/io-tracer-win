using IOTracesCORE.trace;
using System.Diagnostics;
using System.Management;

namespace IOTracesCORE.snapper
{
    internal class ProcessSnapper
    {
        private readonly WriterManager wm;
        private bool interrupted;
        public ProcessSnapper(WriterManager wm)
        {
            this.wm = wm;
            interrupted = false;
        }
        public void Stop()
        {
            interrupted = true;
        }

        public void GetProcesses()
        {
            string query = @"
SELECT IDProcess, Name, PercentProcessorTime
FROM Win32_PerfFormattedData_PerfProc_Process";
            var cpuDict = new Dictionary<uint, ulong>();

            using (var cpuSearcher = new ManagementObjectSearcher(query))
            using (var cpuResults = cpuSearcher.Get())
            {
                foreach (ManagementObject mo in cpuResults)
                {
                    uint pid = (uint)(mo["IDProcess"] ?? 0);
                    ulong pct = (ulong)(mo["PercentProcessorTime"] ?? 0UL);
                    cpuDict[pid] = pct;
                }
            }

            string procQuery = @"
SELECT ProcessId, Name, CommandLine, VirtualSize,
       WorkingSetSize, CreationDate, Status
FROM Win32_Process";

            using (var searcher = new ManagementObjectSearcher(procQuery))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    uint pid = (uint)(mo["ProcessId"] ?? 0);
                    double cpuUsage = cpuDict.TryGetValue(pid, out var pct)
                        ? pct / (double)Environment.ProcessorCount 
                        : 0;

                    ProcessInfo pi = new(
                        processId: pid,
                        name: (string)mo["Name"],
                        commandLine: (string)mo["CommandLine"] ?? "",
                        virtualSize: (ulong)(mo["VirtualSize"] ?? 0UL),
                        workingSetSize: (ulong)(mo["WorkingSetSize"] ?? 0UL),
                        creationDate: ManagementDateTimeConverter
                            .ToDateTime(mo["CreationDate"]?.ToString()),
                        cpuUsage: cpuUsage
                    );

                    wm.Write(pi);
                }
            }
        }

        public void Run()
        {
            while (!interrupted)
            {
                Console.WriteLine("Capturing Process Info...");
                GetProcesses();
                Console.WriteLine("Capturing Process Info Completed.");
                Thread.Sleep(60000);
            }

        }
    }
}
