using IOTracesCORE.trace;
using IOTracesCORE.utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;

namespace IOTracesCORE.snapper
{
    internal class ProcessSnapper
    {
        private readonly WriterManager wm;
        private bool interrupted;
        private readonly bool is_anonymouse;

        private readonly Dictionary<int, List<(DateTime ts, TimeSpan totalCpu)>> samples =
            new Dictionary<int, List<(DateTime, TimeSpan)>>();

        private static readonly TimeSpan Retention = TimeSpan.FromSeconds(3600 + 30);

        private static readonly TimeSpan Interval5s = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan Interval2m = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan Interval1h = TimeSpan.FromHours(1);

        public ProcessSnapper(WriterManager wm, bool is_anonymouse)
        {
            this.wm = wm;
            interrupted = false;
            this.is_anonymouse = is_anonymouse;
        }

        public void Stop() => interrupted = true;

        public void Run()
        {
            while (!interrupted)
            {
                try
                {
                    SampleAndWriteSnapshot();
                }
                catch (Exception ex)
                {
                    // keep running even if one iteration fails
                    Console.Error.WriteLine($"ProcessSnapper iteration error: {ex}");
                }

                // wait the base interval
                Thread.Sleep((int)Interval5s.TotalMilliseconds);
            }
        }

        private void SampleAndWriteSnapshot()
        {
            DateTime now = DateTime.UtcNow;

            string procFullQuery = @"
SELECT ProcessId, Name, CommandLine, VirtualSize, WorkingSetSize, CreationDate, Status
FROM Win32_Process";

            var currentPids = new HashSet<int>();

            using (var searcher = new ManagementObjectSearcher(procFullQuery))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject mo in results)
                {
                    int pid = Convert.ToInt32(mo["ProcessId"] ?? 0);
                    if (pid == 0) continue; // skip idle/system if needed

                    currentPids.Add(pid);

                    TimeSpan totalCpu = TimeSpan.Zero;
                    bool sampled = false;
                    try
                    {
                        var p = Process.GetProcessById(pid);
                        totalCpu = p.TotalProcessorTime;
                        sampled = true;
                    }
                    catch
                    {
                        sampled = false;
                    }

                    if (sampled)
                    {
                        lock (samples)
                        {
                            if (!samples.TryGetValue(pid, out var list))
                            {
                                list = new List<(DateTime, TimeSpan)>();
                                samples[pid] = list;
                            }

                            list.Add((now, totalCpu));

                            DateTime cutoff = now - Retention;
                            int removeCount = list.TakeWhile(s => s.ts < cutoff).Count();
                            if (removeCount > 0)
                                list.RemoveRange(0, removeCount);
                        }
                    }

                    double cpu5s = ComputeCpuPercent(pid, now, Interval5s);
                    double cpu2m = ComputeCpuPercent(pid, now, Interval2m);
                    double cpu1h = ComputeCpuPercent(pid, now, Interval1h);

                    string name = (string)mo["Name"] ?? "";
                    string commandLine = (string)mo["CommandLine"] ?? "";
                    if (is_anonymouse)
                    {
                        commandLine = PathHasher.Hash(commandLine, 16);
                    }

                    DateTime? creation = null;
                    try
                    {
                        var cd = mo["CreationDate"]?.ToString();
                        if (!string.IsNullOrEmpty(cd))
                            creation = ManagementDateTimeConverter.ToDateTime(cd);
                    }
                    catch
                    {
                        creation = null;
                    }

                    var pi = new ProcessInfo(
                        ts: DateTime.Now,
                        processId: (uint)pid,
                        name: name,
                        commandLine: commandLine,
                        virtualSize: (ulong)(mo["VirtualSize"] ?? 0UL),
                        workingSetSize: (ulong)(mo["WorkingSetSize"] ?? 0UL),
                        creationDate: creation,
                        cpuUsage_5s: cpu5s,
                        cpuUsage_2m: cpu2m,
                        cpuUsage_1h: cpu1h
                    );

                    wm.Write(pi);
                }
            }

            lock (samples)
            {
                var toRemove = samples.Keys.Where(k => !currentPids.Contains(k)).ToList();
                foreach (var k in toRemove)
                    samples.Remove(k);
            }
        }

        private double ComputeCpuPercent(int pid, DateTime nowUtc, TimeSpan interval)
        {
            List<(DateTime ts, TimeSpan totalCpu)> list;
            lock (samples)
            {
                if (!samples.TryGetValue(pid, out list) || list.Count == 0)
                    return 0.0;

                list = [.. list];
            }

            var curr = list.Last();

            DateTime target = nowUtc - interval;

            (DateTime ts, TimeSpan totalCpu)? prev = null;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].ts <= target)
                {
                    prev = list[i];
                    break;
                }
            }

            if (prev == null)
                return 0.0;

            double elapsedMs = (curr.ts - prev.Value.ts).TotalMilliseconds;
            if (elapsedMs <= 0.0)
                return 0.0;

            double cpuDeltaMs = (curr.totalCpu - prev.Value.totalCpu).TotalMilliseconds;

            double percent = (cpuDeltaMs / (elapsedMs)) * 100.0;
            if (percent < 0) percent = 0.0;

            return percent;
        }
    }
}
