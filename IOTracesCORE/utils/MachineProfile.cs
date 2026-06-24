using System;

namespace IOTracesCORE.utils
{
    /// <summary>
    /// Picks the default capture mode from machine spec. The single ETW FileIO consumer thread has
    /// a fixed throughput ceiling (~2-5k ev/s; see issue #68), so the truncation risk is highest on
    /// small machines: few cores means the consumer competes with the workload for CPU, and little
    /// RAM means a small kernel buffer with little burst headroom. On such machines we DEFAULT to
    /// lightweight, which (since this change) captures fs via the modern Microsoft-Windows-Kernel-File
    /// provider with a reduced keyword set — the kernel never generates the metadata-op mass, ~3x
    /// fewer FileIO events, so the consumer keeps pace. Big machines default to full fidelity.
    ///
    /// This is only the DEFAULT; an explicit --lightweight / --full (or the UI toggle) always wins,
    /// and a recent truncation (TruncationState) escalates to lightweight regardless of spec.
    /// </summary>
    internal static class MachineProfile
    {
        // At or below these, default to lightweight. The EC2 4-vCPU / 8GB box truncated fs under
        // load in validation, so 4 cores is the boundary; RAM scales the kernel buffer pool.
        public const int LightweightMaxCores = 4;
        public const double LightweightMaxRamGb = 8.0;

        /// <summary>Total physical RAM in GB, via the GC's machine-memory view (same source the
        /// buffer-pool sizing already uses, so the two decisions stay consistent). 0 if unknown.</summary>
        public static double TotalRamGb()
        {
            try
            {
                long bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                return bytes > 0 ? bytes / (1024.0 * 1024.0 * 1024.0) : 0;
            }
            catch { return 0; }
        }

        /// <summary>True when the machine is small enough that lightweight should be the default.</summary>
        public static bool RecommendLightweight()
        {
            int cores = Environment.ProcessorCount;
            double ramGb = TotalRamGb();
            // RAM unknown (0) => decide on cores alone rather than forcing lightweight.
            bool lowRam = ramGb > 0 && ramGb < LightweightMaxRamGb;
            return cores <= LightweightMaxCores || lowRam;
        }

        /// <summary>One-line description for the headless/startup log.</summary>
        public static string Describe()
            => $"cores={Environment.ProcessorCount} ramGb={TotalRamGb():F1} recommendLightweight={RecommendLightweight()}";
    }
}
