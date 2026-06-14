using IOTracesCORE;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace IOTracesCORE.handlers
{
    class DriverHandlers
    {
        private WriterManager wm;

        // Maps a live IRP pointer to a session-unique request id. The kernel reuses
        // IRP pointers once a request completes, so pairing call/return/completion by
        // raw pointer over a long capture is ambiguous; the id below is assigned at
        // driver_call and retired at completion, giving a stable per-request key.
        // Instance (not static) so a session restart starts with a clean map; the id
        // sequence stays monotonic so ids remain unique across the whole trace.
        private readonly ConcurrentDictionary<ulong, ReqEntry> _irpToReq = new();
        private long _reqSeq = 0;

        // Bounded-memory pruning: terminal events can be missed/filtered, which would
        // otherwise leak IRP mappings forever on a long session. IRP lifetimes are
        // sub-second, so anything older than MaxEntryAgeMs is safe to drop.
        private int _callsSincePrune = 0;
        private const int PruneEveryCalls = 50_000;
        private const long MaxEntryAgeMs = 60_000;

        private readonly struct ReqEntry
        {
            public readonly long Id;
            public readonly long Tick;
            public ReqEntry(long id, long tick) { Id = id; Tick = tick; }
        }

        public DriverHandlers(WriterManager old_wm)
        {
            wm = old_wm;
        }

        /// <summary>
        /// Clears in-flight IRP correlation state. Call when a new ETW session
        /// starts so IRP pointers from a previous session cannot be mistakenly
        /// correlated with new requests. The request-id sequence is preserved.
        /// </summary>
        public void Reset() => _irpToReq.Clear();

        private ulong GetUlongPayload(Microsoft.Diagnostics.Tracing.TraceEvent data, string name)
        {
            try
            {
                if (data.PayloadNames.Contains(name))
                {
                    var val = data.PayloadByName(name);
                    return val == null ? 0 : Convert.ToUInt64(val);
                }
                return 0;
            }
            catch { return 0; }
        }

        // Returns null (not -1) when the field is absent, so the CSV can emit an
        // explicit empty value rather than an undocumented -1 sentinel.
        private int? GetIntPayloadNullable(Microsoft.Diagnostics.Tracing.TraceEvent data, string name)
        {
            try
            {
                if (data.PayloadNames.Contains(name))
                {
                    var val = data.PayloadByName(name);
                    return val == null ? (int?)null : Convert.ToInt32(val);
                }
                return null;
            }
            catch { return null; }
        }

        private long GetRequestId(string operation, ulong irp)
        {
            if (irp == 0)
            {
                // No IRP pointer to correlate on — still hand out a unique id.
                return Interlocked.Increment(ref _reqSeq);
            }

            long now = Environment.TickCount64;

            switch (operation)
            {
                case "driver_call":
                    // Start of a new request lifetime: always mint a fresh id.
                    long id = Interlocked.Increment(ref _reqSeq);
                    _irpToReq[irp] = new ReqEntry(id, now);
                    MaybePrune(now);
                    return id;

                case "driver_complete_req_ret":
                    // The only true terminal event: reuse the live id then retire the
                    // mapping. (driver_completion is NOT terminal — multiple completion
                    // routines may run before the request is fully completed.)
                    if (_irpToReq.TryRemove(irp, out var done)) return done.Id;
                    return Interlocked.Increment(ref _reqSeq);

                default:
                    // Intermediate events (return, completion, complete_req): reuse id.
                    if (_irpToReq.TryGetValue(irp, out var cur)) return cur.Id;
                    long fresh = Interlocked.Increment(ref _reqSeq);
                    _irpToReq[irp] = new ReqEntry(fresh, now);
                    return fresh;
            }
        }

        private void MaybePrune(long now)
        {
            if (Interlocked.Increment(ref _callsSincePrune) < PruneEveryCalls) return;
            Interlocked.Exchange(ref _callsSincePrune, 0);

            foreach (var kvp in _irpToReq)
            {
                if (now - kvp.Value.Tick > MaxEntryAgeMs)
                    _irpToReq.TryRemove(kvp.Key, out _);
            }
        }

        private void Emit(Microsoft.Diagnostics.Tracing.TraceEvent data, string operation)
        {
            string processName = string.IsNullOrEmpty(data.ProcessName) ? "" : data.ProcessName;
            if (ProcessFilter.ShouldTrace(data.ProcessID, processName) == false) return;

            ulong irp = GetUlongPayload(data, "Irp");

            DriverTrace dt = new DriverTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                threadId: data.ThreadID,
                comm: processName,
                operation: operation,
                irp: irp,
                majorFunction: GetIntPayloadNullable(data, "MajorFunction"),
                minorFunction: GetIntPayloadNullable(data, "MinorFunction"),
                routineAddr: GetUlongPayload(data, "RoutineAddr"),
                fileObject: GetUlongPayload(data, "FileObject"),
                deviceObject: GetUlongPayload(data, "DeviceObject"),
                requestId: GetRequestId(operation, irp)
            );
            wm.Write(dt);
        }

        public void OnDriverMajorFunctionCall(DriverMajorFunctionCallTraceData data)
            => Emit(data, "driver_call");

        public void OnDriverMajorFunctionReturn(DriverMajorFunctionReturnTraceData data)
            => Emit(data, "driver_return");

        public void OnDriverCompletionRoutine(DriverCompletionRoutineTraceData data)
            => Emit(data, "driver_completion");

        public void OnDriverCompleteRequest(DriverCompleteRequestTraceData data)
            => Emit(data, "driver_complete_req");

        public void OnDriverCompleteRequestReturn(DriverCompleteRequestReturnTraceData data)
            => Emit(data, "driver_complete_req_ret");
    }
}
