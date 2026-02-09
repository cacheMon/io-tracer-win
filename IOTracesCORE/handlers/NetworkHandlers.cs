using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing;
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
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }

            if (NetHelper.IsLocalConversation(data.saddr, data.daddr))
            {
                return;
            }

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
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }

            if (NetHelper.IsLocalConversation(data.saddr, data.daddr))
            {
                return;
            }

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
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }

            if (NetHelper.IsLocalConversation(data.saddr, data.daddr))
            {
                return;
            }

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
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }

            if (NetHelper.IsLocalConversation(data.saddr, data.daddr))
            {
                return;
            }

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
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }

            if (NetHelper.IsLocalConversation(data.saddr, data.daddr))
            {
                return;
            }

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
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }

            if (NetHelper.IsLocalConversation(data.saddr, data.daddr))
            {
                return;
            }

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
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }

            if (NetHelper.IsLocalConversation(data.saddr, data.daddr))
            {
                return;
            }

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
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false)
            {
                return;
            }

            if (NetHelper.IsLocalConversation(data.saddr, data.daddr))
            {
                return;
            }

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

        // Connection Lifecycle Events
        public void OnConnect(TcpIpConnectTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;

            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp, data.ProcessID, data.ProcessName,
                data.saddr.ToString(), data.daddr.ToString(),
                data.sport, data.dport, 0, "connect"
            );
            wm.Write(nt);
        }

        public void OnDisconnect(TcpIpTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;

            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp, data.ProcessID, data.ProcessName,
                data.saddr.ToString(), data.daddr.ToString(),
                data.sport, data.dport, 0, "disconnect"
            );
            wm.Write(nt);
        }

        public void OnAccept(TcpIpConnectTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;

            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp, data.ProcessID, data.ProcessName,
                data.saddr.ToString(), data.daddr.ToString(),
                data.sport, data.dport, 0, "accept"
            );
            wm.Write(nt);
        }

        public void OnReconnect(TcpIpTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;

            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp, data.ProcessID, data.ProcessName,
                data.saddr.ToString(), data.daddr.ToString(),
                data.sport, data.dport, 0, "reconnect"
            );
            wm.Write(nt);
        }

        public void OnFail(TcpIpFailTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false) return;

            // TcpIpFailTraceData might not expose saddr/daddr directly as properties in some versions.
            // We'll try to get them from payload or default to empty.
            string saddr = data.PayloadByName("saddr")?.ToString() ?? "";
            string daddr = data.PayloadByName("daddr")?.ToString() ?? "";
            int sport = 0;
            int dport = 0;

            if (data.PayloadNames.Contains("sport")) sport = (int)((ushort)data.PayloadByName("sport"));
            if (data.PayloadNames.Contains("dport")) dport = (int)((ushort)data.PayloadByName("dport"));

            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp, data.ProcessID, data.ProcessName,
                saddr, daddr,
                sport, dport, 0, "fail",
                data.FailureCode
            );
            wm.Write(nt);
        }

        public void OnTcpHandshake(TraceEvent data)
        {
            // Event names: TcpAttemptConnect, TcpConnectionAccepted, TcpConnectionConnected
            // These dynamic events typically have fields like PID, size, daddr, saddr etc.

            // TraceEvent has ProcessID and ProcessName directly
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false) return;

            // Extract IPs and Ports. Note: Payload names might differ slightly, verification needed if fails.
            // Standard MS-TCPIP provider usually uses 'daddr', 'saddr', 'dport', 'sport'.
            // Some events use 'Tcb' pointer, but usually we have address info.

            // Checking if keys exist to be safe
            if (!data.PayloadNames.Contains("saddr") || !data.PayloadNames.Contains("daddr")) return;

            var saddr = data.PayloadByName("saddr");
            var daddr = data.PayloadByName("daddr");
            var sport = (int)((ushort)data.PayloadByName("sport"));
            var dport = (int)((ushort)data.PayloadByName("dport"));

            string eventType = "";
            switch (data.EventName)
            {
                case "TcpAttemptConnect": eventType = "syn_sent"; break;
                case "TcpConnectionAccepted": eventType = "syn_rcvd"; break;
                case "TcpConnectionConnected": eventType = "established"; break;
                default: return;
            }

            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp, data.ProcessID, data.ProcessName,
                saddr?.ToString() ?? "",
                daddr?.ToString() ?? "",
                sport, dport, 0, eventType
            );
            wm.Write(nt);
        }
    }
}
