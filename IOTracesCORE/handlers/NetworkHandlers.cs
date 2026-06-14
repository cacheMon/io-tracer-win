using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace IOTracesCORE.handlers
{
    /// <summary>
    /// Aggregates network traffic per connection and emits a single per-minute
    /// summary row (bytes sent / received) for each active connection instead of
    /// one row per packet. Connection lifecycle events (connect/accept/etc.) are
    /// retained for ETW wiring but no longer produce output rows.
    /// </summary>
    internal class NetworkHandlers : IDisposable
    {
        private const int TCP_PROTO = 6;
        private const int UDP_PROTO = 17;

        private readonly WriterManager wm;
        private readonly Timer _flushTimer;
        private readonly object _flushLock = new();
        private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);

        private readonly ConcurrentDictionary<string, ConnAgg> _conns = new();

        private sealed class ConnAgg
        {
            public int Pid;
            public string Comm = "";
            public int Proto;
            public string LocalAddr = "";
            public string RemoteAddr = "";
            public int LocalPort;
            public int RemotePort;
            public ulong ConnId;
            public long BytesSent;
            public long BytesReceived;
            // Consecutive flush windows with no traffic, used to evict idle connections.
            public int IdleWindows;
        }

        public NetworkHandlers(WriterManager old_wm)
        {
            wm = old_wm;
            _flushTimer = new Timer(_ => FlushWindow(), null, FlushInterval, FlushInterval);
        }

        // ── Traffic accumulation ──────────────────────────────────────────────
        // Keyed on local/remote (not raw src/dst) so that send and receive — which
        // report mirrored saddr/daddr — fold into the same connection row.

        private void Add(int proto, int pid, string comm, ulong connId,
            string localAddr, int localPort, string remoteAddr, int remotePort, bool isSend, int size)
        {
            if (size <= 0) return;

            string key = $"{proto}|{localAddr}|{localPort}|{remoteAddr}|{remotePort}";
            var agg = _conns.GetOrAdd(key, _ => new ConnAgg
            {
                Pid = pid,
                Comm = comm ?? "",
                Proto = proto,
                LocalAddr = localAddr,
                RemoteAddr = remoteAddr,
                LocalPort = localPort,
                RemotePort = remotePort,
                ConnId = connId
            });

            // Backfill identity if a later event carries better data.
            if (agg.Pid <= 0 && pid > 0) agg.Pid = pid;
            if (string.IsNullOrEmpty(agg.Comm) && !string.IsNullOrEmpty(comm)) agg.Comm = comm;
            if (agg.ConnId == 0 && connId != 0) agg.ConnId = connId;

            if (isSend) Interlocked.Add(ref agg.BytesSent, size);
            else Interlocked.Add(ref agg.BytesReceived, size);
        }

        // ── Periodic flush ────────────────────────────────────────────────────

        private void FlushWindow()
        {
            lock (_flushLock)
            {
                var now = DateTime.UtcNow;
                foreach (var kvp in _conns)
                {
                    var agg = kvp.Value;
                    long sent = Interlocked.Exchange(ref agg.BytesSent, 0);
                    long recv = Interlocked.Exchange(ref agg.BytesReceived, 0);

                    if (sent == 0 && recv == 0)
                    {
                        // Evict connections idle for several windows to bound memory.
                        if (++agg.IdleWindows >= 5)
                        {
                            _conns.TryRemove(kvp.Key, out _);
                        }
                        continue;
                    }

                    agg.IdleWindows = 0;
                    wm.Write(new NetworkTrace(now, agg.Pid, agg.Comm, agg.Proto,
                        agg.LocalAddr, agg.RemoteAddr, agg.LocalPort, agg.RemotePort, agg.ConnId, sent, recv));
                }
            }
        }

        public void Dispose()
        {
            _flushTimer.Dispose();
            // Final flush so the last partial window is not lost on shutdown.
            FlushWindow();
        }

        // ── TCP send / receive ────────────────────────────────────────────────
        // Send: local = saddr/sport, remote = daddr/dport.
        // Receive: orientation is mirrored, so local = daddr/dport, remote = saddr/sport.

        public void OnSend(TcpIpSendTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName)) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;
            Add(TCP_PROTO, data.ProcessID, data.ProcessName, GetConnId(data),
                data.saddr.ToString(), data.sport, data.daddr.ToString(), data.dport, isSend: true, data.size);
        }

        public void OnSend(TcpIpV6SendTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName)) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;
            Add(TCP_PROTO, data.ProcessID, data.ProcessName, GetConnId(data),
                data.saddr.ToString(), data.sport, data.daddr.ToString(), data.dport, isSend: true, data.size);
        }

        public void OnReceive(TcpIpTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName)) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;
            Add(TCP_PROTO, data.ProcessID, data.ProcessName, GetConnId(data),
                data.daddr.ToString(), data.dport, data.saddr.ToString(), data.sport, isSend: false, data.size);
        }

        public void OnReceive(TcpIpV6TraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName)) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;
            Add(TCP_PROTO, data.ProcessID, data.ProcessName, GetConnId(data),
                data.daddr.ToString(), data.dport, data.saddr.ToString(), data.sport, isSend: false, data.size);
        }

        // ── UDP send / receive (IPv4 + IPv6) ──────────────────────────────────

        public void OnSend(UdpIpTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName)) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;
            Add(UDP_PROTO, data.ProcessID, data.ProcessName, GetConnId(data),
                data.saddr.ToString(), data.sport, data.daddr.ToString(), data.dport, isSend: true, data.size);
        }

        public void OnSend(UpdIpV6TraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName)) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;
            Add(UDP_PROTO, data.ProcessID, data.ProcessName, GetConnId(data),
                data.saddr.ToString(), data.sport, data.daddr.ToString(), data.dport, isSend: true, data.size);
        }

        public void OnReceive(UdpIpTraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName)) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;
            Add(UDP_PROTO, data.ProcessID, data.ProcessName, GetConnId(data),
                data.daddr.ToString(), data.dport, data.saddr.ToString(), data.sport, isSend: false, data.size);
        }

        public void OnReceive(UpdIpV6TraceData data)
        {
            if (!ProcessFilter.ShouldTrace(data.ProcessID, data.ProcessName)) return;
            if (NetHelper.IsLocalConversation(data.saddr, data.daddr)) return;
            Add(UDP_PROTO, data.ProcessID, data.ProcessName, GetConnId(data),
                data.daddr.ToString(), data.dport, data.saddr.ToString(), data.sport, isSend: false, data.size);
        }

        // ── Connection lifecycle ──────────────────────────────────────────────
        // Retained so the ETW wiring in Tracer stays valid. Only per-minute byte
        // summaries are logged now, so these no longer emit rows; per-event
        // identity (pid/comm) is captured directly on the send/receive events.

        public void OnConnect(TcpIpConnectTraceData data) { }
        public void OnAccept(TcpIpConnectTraceData data) { }
        public void OnReconnect(TcpIpTraceData data) { }
        public void OnDisconnect(TcpIpTraceData data) { }
        public void OnFail(TcpIpFailTraceData data) { }
        public void OnRetransmit(TcpIpTraceData data) { }
        public void OnTcpHandshake(TraceEvent data) { }

        private ulong GetConnId(TraceEvent data)
        {
            try
            {
                if (data.PayloadNames != null && Array.IndexOf(data.PayloadNames, "ConnID") >= 0)
                {
                    var val = data.PayloadByName("ConnID");
                    return val == null ? 0 : Convert.ToUInt64(val);
                }
            }
            catch { }
            return 0;
        }
    }
}
