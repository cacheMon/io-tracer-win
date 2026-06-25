using IOTracesCORE.trace;
using IOTracesCORE.utils;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly System.Threading.Timer _flushTimer;
        // Guards the aggregate map (short critical section, shared with the ETW hot path).
        private readonly object _flushLock = new();
        // Serializes the row-write phase across overlapping or shutdown flushes
        // without blocking the ETW hot path (Add only takes _flushLock).
        private readonly object _writeLock = new();
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
            _flushTimer = new System.Threading.Timer(_ => FlushWindow(), null, FlushInterval, FlushInterval);
        }

        // ── Traffic accumulation ──────────────────────────────────────────────
        // Keyed on local/remote (not raw src/dst) so that send and receive — which
        // report mirrored saddr/daddr — fold into the same connection row.

        private void Add(int proto, int pid, string comm, ulong connId,
            string localAddr, int localPort, string remoteAddr, int remotePort, bool isSend, int size)
        {
            if (size <= 0) return;

            // Raw packet count = network probe liveness (emitted rows are only
            // per-minute summaries, so they can't tell a dead probe from idle).
            TraceStats.IncNetworkPacket();

            // Held against FlushWindow so a connection cannot be evicted between the
            // GetOrAdd below and the byte increment — otherwise bytes added to an
            // about-to-be-removed ConnAgg would be lost.
            lock (_flushLock)
            {
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

                if (isSend) agg.BytesSent += size;
                else agg.BytesReceived += size;
            }
        }

        // ── Periodic flush ────────────────────────────────────────────────────

        private void FlushWindow()
        {
            List<NetworkTrace>? rows = null;

            lock (_flushLock)
            {
                // Use the same wall-clock base as every other stream: ETW data.TimeStamp
                // is local time, so stamp these rows with local time too — otherwise the
                // network stream would be offset from disk/fs and break correlation.
                var now = DateTime.Now;
                foreach (var kvp in _conns)
                {
                    var agg = kvp.Value;
                    long sent = agg.BytesSent;
                    long recv = agg.BytesReceived;

                    if (sent == 0 && recv == 0)
                    {
                        // Evict connections idle for several windows to bound memory.
                        if (++agg.IdleWindows >= 5)
                        {
                            _conns.TryRemove(kvp.Key, out _);
                        }
                        continue;
                    }

                    agg.BytesSent = 0;
                    agg.BytesReceived = 0;
                    agg.IdleWindows = 0;
                    (rows ??= new List<NetworkTrace>()).Add(new NetworkTrace(now, agg.Pid, agg.Comm, agg.Proto,
                        agg.LocalAddr, agg.RemoteAddr, agg.LocalPort, agg.RemotePort, agg.ConnId, sent, recv));
                }
            }

            // Write under a separate lock, not _flushLock: wm.Write -> FlushWrite can
            // block on the upload ResumeGate, and Add() (the ETW hot path) takes
            // _flushLock — holding it across the write would stall every packet handler.
            // _writeLock still serializes overlapping timer callbacks so concurrent
            // appends to the shared network buffer can't corrupt it.
            if (rows != null)
            {
                lock (_writeLock)
                {
                    foreach (var row in rows) wm.Write(row);
                }
            }
        }

        public void Dispose()
        {
            // Block until any in-flight timer callback completes so it cannot append a
            // row after the final flush / while shutdown compaction reads the buffer.
            using (var done = new ManualResetEvent(false))
            {
                if (_flushTimer.Dispose(done))
                {
                    done.WaitOne();
                }
            }
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
