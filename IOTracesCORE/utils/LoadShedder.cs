using System;
using System.Threading;

namespace IOTracesCORE.utils
{
    /// <summary>Analytic priority of a filesystem event, used for load shedding.</summary>
    public enum FsPriority { High, Med, Low }

    /// <summary>
    /// Decides, under sustained backpressure, which low-value filesystem events to SHED
    /// (aggregate into per-(process,file,op) counts instead of emitting full rows) so the
    /// single ETW consumer thread keeps pace and the KERNEL never has to drop events
    /// randomly. The backpressure signal is event-time LAG = wall-clock now minus the
    /// newest event timestamp the consumer has processed: when the consumer falls behind
    /// it is processing older events, so lag grows BEFORE the kernel buffer overflows — a
    /// leading indicator (unlike the format-queue depth, which stays low precisely because
    /// the consumer is the bottleneck). A 1s timer maps lag -> shed level with hysteresis.
    ///
    /// HIGH (read/write/create/delete/rename/...) is never shed. Because shedding routes
    /// events to the lossless fs_summary stream rather than dropping them, occasional
    /// false-positive shedding (e.g. just after an idle period) costs nothing.
    /// </summary>
    internal static class LoadShedder
    {
        // 0 = keep everything; 1 = shed LOW; 2 = shed LOW + MED.
        private static volatile int _shedLevel;
        // Newest event timestamp processed, in LOCAL DateTime ticks. ETW TraceData.TimeStamp
        // is machine-local wall-clock (manifest clock_source = local_wall_clock / windows_qpc),
        // so the lag must be computed against DateTime.Now, NOT DateTime.UtcNow — using UtcNow
        // would inject the machine's UTC offset and pin shedding at max (or never) forever.
        private static long _lastEventTicks;

        // Wall-clock tick captured at ETW stop, or 0 while still running. ConsumerLagMsNow anchors
        // on this once set, so the FINAL manifest's fs_consumer_lag_ms reflects the lag AT STOP (the
        // true silent-tail-loss span) rather than (manifest-build-time now − last event), which would
        // include the post-stop compress/upload drain and overstate the lag beyond the capture span.
        private static long _stopTicks;

        // Lag thresholds (seconds). Recovery thresholds sit below entry thresholds (hysteresis)
        // so the level can't flap on every timer tick.
        private const double EnterLowLagSec = 3.0;
        private const double EnterMedLagSec = 12.0;
        private const double ExitLowLagSec = 1.5;
        private const double ExitMedLagSec = 6.0;

        // Process-lifetime: cheap (a single timer tick/sec) and idle-safe (Update no-ops with no events).
        // Fully qualified: this is a WinForms (net8.0-windows) project, so bare Timer is ambiguous
        // with System.Windows.Forms.Timer.
        private static readonly System.Threading.Timer _timer =
            new System.Threading.Timer(_ => Update(), null, 1000, 1000);

        /// <summary>
        /// Records this event's timestamp (feeds the lag signal) and returns true if an event
        /// of this priority should be SHED right now. HIGH always returns false.
        /// </summary>
        public static bool ShouldShed(FsPriority pri, DateTime eventTs)
        {
            // Monotonic newest-event update via lock-free CAS-max: a late/out-of-order OLDER
            // event (e.g. a deferred-filename emit) must not deflate the lag signal.
            long t = eventTs.Ticks, cur;
            while (t > (cur = Interlocked.Read(ref _lastEventTicks)))
                if (Interlocked.CompareExchange(ref _lastEventTicks, t, cur) == cur) break;

            return ShedDecision(pri, _shedLevel);
        }

        /// <summary>Pure shed decision for a priority at a given level (unit-testable).</summary>
        internal static bool ShedDecision(FsPriority pri, int level)
        {
            if (pri == FsPriority.High) return false;
            if (level >= 2) return true;                 // shed LOW + MED
            return level >= 1 && pri == FsPriority.Low;  // shed LOW only
        }

        /// <summary>Pure hysteresis transition for the shed level (unit-testable).</summary>
        internal static int NextLevel(int current, double lagSec) => current switch
        {
            <= 0 => lagSec > EnterLowLagSec ? 1 : 0,
            1 => lagSec > EnterMedLagSec ? 2 : (lagSec < ExitLowLagSec ? 0 : 1),
            _ => lagSec < ExitMedLagSec ? 1 : 2,
        };

        /// <summary>Clear lag/level state on ETW session restart.</summary>
        public static void Reset()
        {
            Interlocked.Exchange(ref _lastEventTicks, 0);
            Volatile.Write(ref _stopTicks, 0);
            _shedLevel = 0;
        }

        /// <summary>Freeze the lag clock at ETW stop so the final lag reflects the stop point,
        /// not the later manifest-build time. Idempotent; the first call wins.</summary>
        public static void MarkStopped()
        {
            Interlocked.CompareExchange(ref _stopTicks, DateTime.Now.Ticks, 0);
        }

        /// <summary>Current shed level (0/1/2) — exposed so the truncation detector can recognise a
        /// consumer FREEZE (top level + large lag) even when kernel-overflow loss stays low.</summary>
        public static int ShedLevelNow() => _shedLevel;

        /// <summary>
        /// Current consumer event-time lag in milliseconds: wall-clock now minus the newest event
        /// timestamp the single FileIO consumer has processed. 0 if no events have been seen yet.
        ///
        /// Read AT SESSION STOP this is the load-bearing honesty signal: it is the span of
        /// wall-clock time whose FileIO events were successfully BUFFERED by the kernel but never
        /// drained by the consumer (it was still replaying an earlier backlog when the session
        /// stopped). Those events are silently abandoned — they are NOT counted by
        /// session_events_lost, which only sees kernel buffer OVERFLOW, not consumer-abandoned
        /// backlog. A large value here next to session_events_lost == 0 means the fs stream is
        /// truncated to the consumer's freeze point, i.e. roughly its last ~lag ms are missing.
        /// </summary>
        public static long ConsumerLagMsNow()
        {
            long last = Interlocked.Read(ref _lastEventTicks);
            if (last == 0) return 0;
            long stop = Volatile.Read(ref _stopTicks);
            long anchor = stop != 0 ? stop : DateTime.Now.Ticks;
            long ms = (anchor - last) / TimeSpan.TicksPerMillisecond;
            return ms > 0 ? ms : 0;
        }

        private static void Update()
        {
            long last = Interlocked.Read(ref _lastEventTicks);
            if (last == 0) return; // no events seen yet (or idle) — nothing to shed anyway
            double lagSec = (DateTime.Now.Ticks - last) / (double)TimeSpan.TicksPerSecond;
            if (lagSec < 0) lagSec = 0;
            TraceStats.NoteFsShedPeakLagMs((long)(lagSec * 1000));
            _shedLevel = NextLevel(_shedLevel, lagSec);
        }
    }
}
