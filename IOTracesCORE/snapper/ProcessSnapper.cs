using IOTracesCORE.trace;
using System.Management;
using System.Xml.Linq;

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
            string query = "SELECT ProcessId, Name, CommandLine, VirtualSize, WorkingSetSize, CreationDate, Status FROM Win32_Process";
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    ProcessInfo pi = new(
                        processId: (uint)(mo["ProcessId"] ?? 0),
                        name: (string)mo["Name"],
                        commandLine: (string)mo["CommandLine"] ?? "",
                        virtualSize: (ulong)(mo["VirtualSize"] ?? 0UL),
                        workingSetSize: (ulong)(mo["WorkingSetSize"] ?? 0UL),
                        creationDate: ManagementDateTimeConverter.ToDateTime(mo["CreationDate"]?.ToString()),
                        status: (string)mo["Status"]
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
