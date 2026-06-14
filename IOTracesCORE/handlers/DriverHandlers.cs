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
        private static readonly ConcurrentDictionary<ulong, long> _irpToReq = new();
        private static long _reqSeq = 0;

        public DriverHandlers(WriterManager old_wm)
        {
            wm = old_wm;
        }

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

        private static long GetRequestId(string operation, ulong irp)
        {
            if (irp == 0)
            {
                // No IRP pointer to correlate on — still hand out a unique id.
                return Interlocked.Increment(ref _reqSeq);
            }

            switch (operation)
            {
                case "driver_call":
                    // Start of a new request lifetime: always mint a fresh id.
                    long id = Interlocked.Increment(ref _reqSeq);
                    _irpToReq[irp] = id;
                    return id;

                case "driver_completion":
                case "driver_complete_req_ret":
                    // Terminal events: reuse the live id then retire the mapping.
                    if (_irpToReq.TryRemove(irp, out var done)) return done;
                    return Interlocked.Increment(ref _reqSeq);

                default:
                    // Intermediate events (return, complete_req): reuse the live id.
                    if (_irpToReq.TryGetValue(irp, out var cur)) return cur;
                    long fresh = Interlocked.Increment(ref _reqSeq);
                    _irpToReq[irp] = fresh;
                    return fresh;
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
