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
            "IOTracesCORE", "RuntimeBroker"
        };

        public static bool ShouldTrace(int pid, string processName)
        {
            if (excludedPids.Contains(pid)) return false;
            if (excludedProcessNames.Any(n => processName.Contains(n, StringComparison.OrdinalIgnoreCase)))
                return false;
            if (String.IsNullOrEmpty(processName)) return false;
            return true;
        }
    }
}
