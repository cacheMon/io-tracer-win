using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.utils
{
    internal class Converter
    {
        public static ulong SafeToUInt64(object? o)
        {
            if (o == null) return 0UL;
            try { return Convert.ToUInt64(o); }
            catch { return 0UL; }
        }

        public static long SafeToInt64(object? o)
        {
            if (o == null) return 0L;
            try { return Convert.ToInt64(o); }
            catch { return 0L; }
        }

        public static string SafeToString(object? o)
        {
            if (o == null) return "";
            try { return o.ToString() ?? ""; }
            catch { return ""; }
        }
    }
}
