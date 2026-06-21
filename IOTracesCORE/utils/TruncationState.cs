using System;
using System.Globalization;
using System.IO;

namespace IOTracesCORE.utils
{
    /// <summary>
    /// A tiny, machine-local hint that a recent capture suffered heavy kernel-side ETW
    /// event loss (the "fs/ stream truncated to a startup burst" pathology). A run that
    /// crosses <see cref="LossThreshold"/> authoritative drops writes the marker; the next
    /// launch reads it and defaults to lightweight mode, which stops the chattiest kernel
    /// keywords at the source and so lowers the event rate the single ETW consumer thread
    /// must sustain. Safe and reversible: the user can still untick lightweight (which
    /// clears the marker via <see cref="Clear"/>), and the hint self-expires.
    /// </summary>
    internal static class TruncationState
    {
        /// <summary>
        /// A run losing at least this many events to the kernel (OS-authoritative
        /// session.EventsLost) is treated as truncating and arms the sticky-lightweight
        /// default. Well above incidental boot-burst loss, well below a real truncation
        /// (which runs to hundreds of millions).
        /// </summary>
        public const long LossThreshold = 1_000_000;

        // Honour the sticky default only while the marker is fresh, so a machine that has
        // since been fixed (or whose workload changed) stops forcing lightweight forever.
        private const double ExpiryDays = 30.0;

        // Same %LOCALAPPDATA%\IOTracer directory the stable-install copy uses, so the hint
        // lives next to the installed exe and survives moving the original download.
        private static string MarkerPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IOTracer");
            return Path.Combine(dir, "truncation.marker");
        }

        /// <summary>Record that this machine just truncated, losing <paramref name="lostEvents"/>.</summary>
        public static void Record(long lostEvents)
        {
            try
            {
                string path = MarkerPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // One line: ISO-8601 UTC timestamp, then the authoritative loss count.
                File.WriteAllText(path, $"{DateTime.UtcNow:o}\t{lostEvents}");
            }
            catch { /* best-effort hint; never let it disrupt tracing */ }
        }

        /// <summary>
        /// True if a marker written within the last <see cref="ExpiryDays"/> days exists;
        /// outputs the recorded loss count.
        /// </summary>
        public static bool WasRecentlyTruncated(out long lostEvents)
        {
            lostEvents = 0;
            try
            {
                string path = MarkerPath();
                if (!File.Exists(path)) return false;
                string[] parts = File.ReadAllText(path).Split('\t');
                if (parts.Length >= 2
                    && DateTime.TryParse(parts[0], CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out DateTime when)
                    && long.TryParse(parts[1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out lostEvents))
                {
                    return (DateTime.UtcNow - when).TotalDays <= ExpiryDays;
                }
            }
            catch { /* unreadable marker — treat as absent */ }
            return false;
        }

        /// <summary>Remove the marker (e.g. the user explicitly opted out of lightweight).</summary>
        public static void Clear()
        {
            try
            {
                string path = MarkerPath();
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best-effort */ }
        }
    }
}
