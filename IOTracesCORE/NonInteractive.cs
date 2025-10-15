using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE
{
    class NonInteractive
    {
        public static void Run()
        {
            string outputPath = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\WorkloadTrace";
            bool anonymous = true;
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            Tracer trc = new(anonymous, outputPath);
            trc.Trace();
        }
    }
}
