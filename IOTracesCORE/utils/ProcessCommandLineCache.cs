using System.Collections.Concurrent;
using System.Management;

namespace IOTracesCORE.utils
{
    internal class ProcessCommandLineCache
    {
        private readonly ConcurrentDictionary<int, string> commandLineByPid = new();

        public string Get(int pid)
        {
            if (commandLineByPid.TryGetValue(pid, out var commandLine))
                return commandLine;

            RefreshFromProcess(pid);
            return commandLineByPid.TryGetValue(pid, out commandLine) ? commandLine : "";
        }

        public void Upsert(int pid, string? commandLine)
        {
            if (pid <= 0)
                return;

            commandLineByPid[pid] = commandLine ?? "";
        }

        public void Remove(int pid)
        {
            if (pid <= 0)
                return;

            commandLineByPid.TryRemove(pid, out _);
        }

        public void RefreshFromProcess(int pid)
        {
            if (pid <= 0)
                return;

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                using var results = searcher.Get();
                var process = results.Cast<ManagementObject>().FirstOrDefault();
                if (process == null)
                    return;

                Upsert(pid, process["CommandLine"]?.ToString() ?? "");
            }
            catch
            {
                // Command-line lookup can fail for short-lived or protected processes.
            }
        }
    }
}
