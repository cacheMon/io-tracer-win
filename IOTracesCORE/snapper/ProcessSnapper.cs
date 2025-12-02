using IOTracesCORE.trace;
using IOTracesCORE.utils;
using System.Diagnostics;
using System.Management;

namespace IOTracesCORE.snapper
{
    internal class ProcessSnapper
    {
        private readonly WriterManager wm;
        private bool interrupted;
        private bool is_anonymouse;
        public ProcessSnapper(WriterManager wm, bool is_anonymouse )
        {
            this.wm = wm;
            interrupted = false;
            this.is_anonymouse = is_anonymouse;
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

            DateTime dateTime = DateTime.Now;

            using (var searcher = new ManagementObjectSearcher(procQuery))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    uint pid = (uint)(mo["ProcessId"] ?? 0);
                    double cpuUsage = cpuDict.TryGetValue(pid, out var pct)
                        ? pct / (double)Environment.ProcessorCount 
                        : 0;
                    string commandLine = (string)mo["CommandLine"] ?? "";
                    if (is_anonymouse)
                    {
                        commandLine = PathHasher.Hash(commandLine, 16);
                    }

                    ProcessInfo pi = new(
                        ts: dateTime,
                        processId: pid,
                        name: (string)mo["Name"],
                        commandLine: commandLine,
                        virtualSize: (ulong)(mo["VirtualSize"] ?? 0UL),
                        workingSetSize: (ulong)(mo["WorkingSetSize"] ?? 0UL),
                        creationDate: ManagementDateTimeConverter
                            .ToDateTime(mo["CreationDate"]?.ToString()),
                        cpuUsage: cpuUsage
                    );
                    wm.Write(pi);
                    Thread.Sleep(500);
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
