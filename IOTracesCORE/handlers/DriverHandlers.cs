using IOTracesCORE;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.handlers
{
    class DriverHandlers
    {
        private WriterManager wm;

        public DriverHandlers(WriterManager old_wm)
        {
            wm = old_wm;
        }



        private ulong GetUlongPayload(Microsoft.Diagnostics.Tracing.TraceEvent data, string name)
        {
            var val = data.PayloadByName(name);
            if (val == null) return 0;
            try { return Convert.ToUInt64(val); } catch { return 0; }
        }

        private int GetIntPayload(Microsoft.Diagnostics.Tracing.TraceEvent data, string name)
        {
            var val = data.PayloadByName(name);
            if (val == null) return -1;
            try { return Convert.ToInt32(val); } catch { return -1; }
        }

        public void OnDriverMajorFunctionCall(DriverMajorFunctionCallTraceData data)
        {
            // Debug.WriteLine($"[DriverHandler] MajorFunctionCall received. PID: {data.ProcessID}, Name: '{data.ProcessName}'");
            string processName = string.IsNullOrEmpty(data.ProcessName) ? "" : data.ProcessName;
            if (ProcessFilter.ShouldTrace(data.ProcessID, processName) == false) return;

            DriverTrace dt = new DriverTrace(
                ts: data.TimeStamp,
                pid: data.ProcessID,
                threadId: data.ThreadID,
                comm: processName,
                operation: "driver_call",
                irp: GetUlongPayload(data, "Irp"),
                majorFunction: GetIntPayload(data, "MajorFunction"),
                minorFunction: GetIntPayload(data, "MinorFunction"),
                routineAddr: GetUlongPayload(data, "RoutineAddr"),
                fileObject: GetUlongPayload(data, "FileObject"),
                deviceObject: GetUlongPayload(data, "DeviceObject")
            );
            wm.Write(dt);
        }

        public void OnDriverMajorFunctionReturn(DriverMajorFunctionReturnTraceData data)
        {
            // Debug.WriteLine($"[DriverHandler] MajorFunctionReturn received. PID: {data.ProcessID}, Name: '{data.ProcessName}'");
            string processName = string.IsNullOrEmpty(data.ProcessName) ? "" : data.ProcessName;
            if (ProcessFilter.ShouldTrace(data.ProcessID, processName) == false) return;

            DriverTrace dt = new DriverTrace(
               ts: data.TimeStamp,
               pid: data.ProcessID,
               threadId: data.ThreadID,
               comm: processName,
               operation: "driver_return",
               irp: GetUlongPayload(data, "Irp"),
               majorFunction: GetIntPayload(data, "MajorFunction"),
               minorFunction: GetIntPayload(data, "MinorFunction"),
               routineAddr: GetUlongPayload(data, "RoutineAddr"),
               fileObject: GetUlongPayload(data, "FileObject"),
               deviceObject: GetUlongPayload(data, "DeviceObject")
           );
            wm.Write(dt);
        }

        public void OnDriverCompletionRoutine(DriverCompletionRoutineTraceData data)
        {
            // Debug.WriteLine($"[DriverHandler] CompletionRoutine received. PID: {data.ProcessID}, Name: '{data.ProcessName}'");
            string processName = string.IsNullOrEmpty(data.ProcessName) ? "" : data.ProcessName;
            if (ProcessFilter.ShouldTrace(data.ProcessID, processName) == false) return;

            DriverTrace dt = new DriverTrace(
               ts: data.TimeStamp,
               pid: data.ProcessID,
               threadId: data.ThreadID,
               comm: processName,
               operation: "driver_completion",
               irp: GetUlongPayload(data, "Irp"),
               majorFunction: GetIntPayload(data, "MajorFunction"),
               minorFunction: GetIntPayload(data, "MinorFunction"),
               routineAddr: GetUlongPayload(data, "RoutineAddr"),
               fileObject: GetUlongPayload(data, "FileObject"),
               deviceObject: GetUlongPayload(data, "DeviceObject")
           );
            wm.Write(dt);
        }

        public void OnDriverCompleteRequest(DriverCompleteRequestTraceData data)
        {
            // Debug.WriteLine($"[DriverHandler] CompleteRequest received. PID: {data.ProcessID}, Name: '{data.ProcessName}'");
            string processName = string.IsNullOrEmpty(data.ProcessName) ? "" : data.ProcessName;
            if (ProcessFilter.ShouldTrace(data.ProcessID, processName) == false) return;

            DriverTrace dt = new DriverTrace(
               ts: data.TimeStamp,
               pid: data.ProcessID,
               threadId: data.ThreadID,
               comm: processName,
               operation: "driver_complete_req",
               irp: GetUlongPayload(data, "Irp"),
               majorFunction: GetIntPayload(data, "MajorFunction"),
               minorFunction: GetIntPayload(data, "MinorFunction"),
               routineAddr: GetUlongPayload(data, "RoutineAddr"),
               fileObject: GetUlongPayload(data, "FileObject"),
               deviceObject: GetUlongPayload(data, "DeviceObject")
           );
            wm.Write(dt);
        }

        public void OnDriverCompleteRequestReturn(DriverCompleteRequestReturnTraceData data)
        {
            // Debug.WriteLine($"[DriverHandler] CompleteRequestReturn received. PID: {data.ProcessID}, Name: '{data.ProcessName}'");
            string processName = string.IsNullOrEmpty(data.ProcessName) ? "" : data.ProcessName;
            if (ProcessFilter.ShouldTrace(data.ProcessID, processName) == false) return;

            DriverTrace dt = new DriverTrace(
               ts: data.TimeStamp,
               pid: data.ProcessID,
               threadId: data.ThreadID,
               comm: processName,
               operation: "driver_complete_req_ret",
               irp: GetUlongPayload(data, "Irp"),
               majorFunction: GetIntPayload(data, "MajorFunction"),
               minorFunction: GetIntPayload(data, "MinorFunction"),
               routineAddr: GetUlongPayload(data, "RoutineAddr"),
               fileObject: GetUlongPayload(data, "FileObject"),
               deviceObject: GetUlongPayload(data, "DeviceObject")
           );
            wm.Write(dt);
        }

    }
}
