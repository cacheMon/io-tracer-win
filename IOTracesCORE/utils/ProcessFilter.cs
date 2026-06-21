using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.utils
{
    internal class ProcessFilter
    {
        private static readonly HashSet<int> excludedPids = new HashSet<int>();
        private static readonly HashSet<string> excludedProcessNames = new HashSet<string>
        {
            "IOTracesCORE", "RuntimeBroker","iotrace", "System Idle Process",
            "WmiPrvSE", "winmgmt", "WmiApSrv"
        };

        // ShouldTrace runs on the single ETW consumer thread for EVERY FileIO event (tens of
        // thousands/sec under load), and the scan below is 7 case-insensitive Contains() per
        // call — it showed up as a real consumer-throughput limiter during load validation
        // (events backlog, captured timeline lags real time; see issue #68). A process's name
        // never changes, so memoize the decision per name: the scan runs once per distinct
        // process, then it's an O(1) lookup forever after.
        private static readonly ConcurrentDictionary<string, bool> _decisionByName = new();

        public static bool ShouldTrace(int pid, string processName)
        {
            if (excludedPids.Contains(pid)) return false;
            if (string.IsNullOrEmpty(processName)) return true;
            return _decisionByName.GetOrAdd(processName, static name =>
            {
                foreach (var ex in excludedProcessNames)
                {
                    if (name.Contains(ex, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                return true;
            });
        }
    }
}
