using System;
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
            "IOTracesCORE", "RuntimeBroker","iotrace", "System Idle Process"
        };

        public static bool ShouldTrace(int pid, string processName)
        {
            if (excludedPids.Contains(pid)) return false;
            if (excludedProcessNames.Any(n => processName.Contains(n, StringComparison.OrdinalIgnoreCase)))
                return false;
            return true;
        }
    }
}
