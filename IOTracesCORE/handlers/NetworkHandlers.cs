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

        private ulong GetConnId(TraceEvent data)
        {
            return GetUlongPayload(data, "ConnID");
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
                "send",
                0,
                GetConnId(data)
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
                "send",
                0,
                GetConnId(data)
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
                "send",
                0,
                GetConnId(data),
                context: data.context,
                dSize: data.dsize
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
                "send",
                0,
                GetConnId(data),
                seqNum: data.seqnum
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
                "receive",
                0,
                GetConnId(data),
                seqNum: data.seqnum
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
                "receive",
                0,
                GetConnId(data),
                seqNum: data.seqnum
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
                "receive",
                0,
                GetConnId(data),
                context: data.context,
                dSize: data.dsize
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
                "receive",
                0,
                GetConnId(data),
                seqNum: data.seqnum
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
                data.sport, data.dport, 0, "connect",
                0, GetConnId(data),
                seqNum: data.seqnum,
                mss: data.mss,
                sndWinScale: data.sndwinscale,
                rcvWinScale: data.rcvwinscale,
                rcvWin: data.rcvwin,
                wsOpt: data.wsopt,
                tsOpt: data.tsopt,
                sackOpt: data.sackopt
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
                data.sport, data.dport, 0, "disconnect",
                0, GetConnId(data),
                seqNum: data.seqnum
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
                data.sport, data.dport, 0, "accept",
                0, GetConnId(data),
                seqNum: data.seqnum,
                mss: data.mss,
                sndWinScale: data.sndwinscale,
                rcvWinScale: data.rcvwinscale,
                rcvWin: data.rcvwin,
                wsOpt: data.wsopt,
                tsOpt: data.tsopt,
                sackOpt: data.sackopt
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
                data.sport, data.dport, 0, "reconnect",
                0, GetConnId(data),
                seqNum: data.seqnum
            );
            wm.Write(nt);
        }

        public void OnFail(TcpIpFailTraceData data)
        {
            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false) return;

            // TcpIpFailTraceData might not expose saddr/daddr directly as properties in some versions.
            // We'll try to get them from payload or default to empty.
            string saddr = GetStringPayload(data, "saddr");
            string daddr = GetStringPayload(data, "daddr");
            int sport = GetIntPayload(data, "sport");
            int dport = GetIntPayload(data, "dport");

            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp, data.ProcessID, data.ProcessName,
                saddr, daddr,
                sport, dport, 0, "fail",
                data.FailureCode,
                GetConnId(data),
                proto: data.Proto
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

            string saddr = GetStringPayload(data, "saddr");
            string daddr = GetStringPayload(data, "daddr");
            int sport = GetIntPayload(data, "sport");
            int dport = GetIntPayload(data, "dport");

            string eventType = "";
            switch (data.EventName)
            {
                case "TcpAttemptConnect": eventType = "syn_sent"; break;
                case "TcpConnectionAccepted": eventType = "syn_rcvd"; break;
                case "TcpConnectionConnected": eventType = "established"; break;
                default: return;
            }

            ulong connId = GetConnId(data);

            NetworkTrace nt = new NetworkTrace(
                data.TimeStamp, data.ProcessID, data.ProcessName,
                saddr,
                daddr,
                sport, dport, 0, eventType,
                0, connId
            );
            wm.Write(nt);
        }

        public void OnRetransmit(TcpIpTraceData data)
        {

            if (ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName) == false) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;

            NetworkTrace nt = new NetworkTrace(
                 data.TimeStamp,
                 data.ProcessID,
                 data.ProcessName,
                 data.saddr.ToString(),
                data.daddr.ToString(),
                data.sport,
                data.dport,
                data.size,
                "retransmit",
                0,
                GetConnId(data),
                seqNum: data.seqnum
            );

            wm.Write(nt);
        }

        #region Safe Payload Access Helpers

        private ulong GetUlongPayload(TraceEvent data, string name, ulong defaultValue = 0)
        {
            try
            {
                if (data.PayloadNames.Contains(name))
                {
                    var val = data.PayloadByName(name);
                    return val == null ? defaultValue : Convert.ToUInt64(val);
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private int GetIntPayload(TraceEvent data, string name, int defaultValue = 0)
        {
            try
            {
                if (data.PayloadNames.Contains(name))
                {
                    var val = data.PayloadByName(name);
                    return val == null ? defaultValue : Convert.ToInt32(val);
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private string GetStringPayload(TraceEvent data, string name, string defaultValue = "")
        {
            try
            {
                if (data.PayloadNames.Contains(name))
                {
                    var val = data.PayloadByName(name);
                    return val?.ToString() ?? defaultValue;
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        #endregion
    }
}
