using IOTracesCORE.trace;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.handlers
{
    internal class NetworkHandlers
    {
        private WriterManager wm;

        public NetworkHandlers(WriterManager old_wm)
        {
            wm = old_wm;
        }

        public void OnSend(TcpIpSendTraceData data)
        {
            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp,
                data.ProcessID,
                data.ProcessName,
                data.saddr.ToString(),
                data.daddr.ToString(),
                data.sport,
                data.dport,
                data.size,
                "send"
            );

            //Debug.WriteLine(nt.ToString());
            wm.Write(nt);
        }

        public void OnSend(TcpIpV6SendTraceData data)
        {
            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp,
                data.ProcessID,
                data.ProcessName,
                data.saddr.ToString(),
                data.daddr.ToString(),
                data.sport,
                data.dport,
                data.size,
                "send"
            );

            //Debug.WriteLine(nt.ToString());
            wm.Write(nt);
        }

        public void OnSend(UdpIpTraceData data)
        {
            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp,
                data.ProcessID,
                data.ProcessName,
                data.saddr.ToString(),
                data.daddr.ToString(),
                data.sport,
                data.dport,
                data.size,
                "send"
            );
            wm.Write(nt);
        }

        public void OnSend(UpdIpV6TraceData data)
        {
            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp,
                data.ProcessID,
                data.ProcessName,
                data.saddr.ToString(),
                data.daddr.ToString(),
                data.sport,
                data.dport,
                data.size,
                "send"
            );

            //Debug.WriteLine(nt.ToString());
            wm.Write(nt);
        }

        public void OnReceive(TcpIpTraceData data)
        {
            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp,
                data.ProcessID,
                data.ProcessName,
                data.saddr.ToString(),
                data.daddr.ToString(),
                data.sport,
                data.dport,
                data.size,
                "receive"
            );

            //Debug.WriteLine(nt.ToString());
            wm.Write(nt);
        }

        public void OnReceive(TcpIpV6TraceData data)
        {
            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp,
                data.ProcessID,
                data.ProcessName,
                data.saddr.ToString(),
                data.daddr.ToString(),
                data.sport,
                data.dport,
                data.size,
                "receive"
            );

            //Debug.WriteLine(nt.ToString());
            wm.Write(nt);
        }

        public void OnReceive(UdpIpTraceData data)
        {
            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp,
                data.ProcessID,
                data.ProcessName,
                data.saddr.ToString(),
                data.daddr.ToString(),
                data.sport,
                data.dport,
                data.size,
                "receive"
            );

            //Debug.WriteLine(nt.ToString());
            wm.Write(nt);
        }

        public void OnReceive(UpdIpV6TraceData data)
        {
            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp,
                data.ProcessID,
                data.ProcessName,
                data.saddr.ToString(),
                data.daddr.ToString(),
                data.sport,
                data.dport,
                data.size,
                "receive"
            );

            //Debug.WriteLine(nt.ToString());
            wm.Write(nt);
        }
    }
}
