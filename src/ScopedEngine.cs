using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;

// Global paket yakalayıcı açılmaz: her handle tek bir Discord 5-tuple'ını seçer.
internal sealed class ScopedEngine : IDisposable
{
    private readonly Dictionary<ulong, Connection> connections = new Dictionary<ulong, Connection>();
    private readonly object connectionsGate = new object();
    internal void OnFlow(Native.Address flow)
    {
        lock (connectionsGate) OnFlowLocked(flow);
    }
    private void OnFlowLocked(Native.Address flow)
    {
        Connection old;
        if (flow.Event == 2)
        {
            if (connections.TryGetValue(flow.Endpoint, out old)) old.Stop();
            return;
        }
        var expired = new List<ulong>();
        int activeCount = 0;
        foreach (var entry in connections)
        {
            if (!entry.Value.Done) activeCount++;
            // CONNECT sonrası tamamlanan işi gecikmiş FLOW için yeniden açma.
            else if (entry.Value.RetentionExpired) expired.Add(entry.Key);
        }
        foreach (ulong key in expired) connections.Remove(key);
        if (flow.Event != 1 || flow.Protocol != 6 || flow.RemotePort != 443 || flow.LocalPort == 0 || connections.ContainsKey(flow.Endpoint)) return;
        if (activeCount >= 32 || connections.Count >= 1024) { Log("SINIR: Yeni bağlantı değiştirilmeden geçiyor."); return; }
        string filter = MakeFilter(flow);
        if (filter == null) return;
        try
        {
            var connection = new Connection(flow, filter);
            connections.Add(flow.Endpoint, connection);
            connection.Start();
            Log("HEDEF: Discord TCP/443, PID=" + flow.ProcessId + ", yerel port=" + flow.LocalPort);
        }
        catch (Exception ex) { Log("ATLANDI: " + ex.Message); }
    }
    internal static string MakeFilter(Native.Address flow)
    {
        var local = IPAddress.Parse(Native.Format(flow.Local0, flow.Local1, flow.Local2, flow.Local3));
        var remote = IPAddress.Parse(Native.Format(flow.Remote0, flow.Remote1, flow.Remote2, flow.Remote3));
        if (local.IsIPv4MappedToIPv6) local = local.MapToIPv4();
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (local.Equals(IPAddress.Any) || local.Equals(IPAddress.IPv6Any) || IPAddress.IsLoopback(remote) || remote.Equals(IPAddress.Any) || remote.Equals(IPAddress.IPv6Any) || local.AddressFamily != remote.AddressFamily) return null;
        string family = local.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "ip" : "ipv6";
        return "outbound and !loopback and !impostor and tcp and tcp.PayloadLength > 0 and " + family +
            ".SrcAddr == " + local + " and " + family + ".DstAddr == " + remote +
            " and tcp.SrcPort == " + flow.LocalPort + " and tcp.DstPort == 443";
    }
    internal static void Log(string text) { Console.WriteLine("{0:HH:mm:ss} MOTOR {1}", DateTime.Now, text); }
    public void Dispose()
    {
        foreach (var c in connections.Values) c.Stop();
        foreach (var c in connections.Values) c.Join();
        connections.Clear();
    }
    private sealed class Connection
    {
        private readonly Native.Address owner;
        private readonly object gate = new object();
        private readonly Thread thread;
        private IntPtr handle;
        private Timer deadline;
        private bool stop;
        private volatile bool done;
        private readonly long created = Stopwatch.GetTimestamp();
        internal bool RetentionExpired { get { return (Stopwatch.GetTimestamp() - created) / (double)Stopwatch.Frequency > 30; } }
        internal bool Done { get { return done; } }
        internal Connection(Native.Address flow, string filter)
        {
            owner = flow;
            handle = Native.WinDivertOpen(filter, 0, 0, 0);
            if (handle == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            thread = new Thread(Run) { IsBackground = true };
        }
        internal void Start()
        {
            try
            {
                deadline = new Timer(delegate { Stop(); }, null, 8000, Timeout.Infinite);
                thread.Start();
            }
            catch
            {
                if (deadline != null) deadline.Dispose();
                lock (gate) { Native.WinDivertClose(handle); handle = new IntPtr(-1); }
                done = true;
                throw;
            }
        }
        internal void Stop()
        {
            lock (gate)
            {
                stop = true;
                if (handle != new IntPtr(-1)) Native.WinDivertShutdown(handle, 1);
            }
        }
        internal void Join() { if (thread.IsAlive) thread.Join(); }
        private bool Send(byte[] packet, int length, ref Native.Address address, bool original = false)
        {
            uint sent;
            if (!Native.WinDivertHelperCalcChecksums(packet, (uint)length, ref address, 0) && !original) return false;
            return Native.WinDivertSend(handle, packet, (uint)length, out sent, ref address) && sent == length;
        }
        private void Run()
        {
            var packet = new byte[65575];
            bool attempted = false;
            try
            {
                // Shutdown sonrasında kuyruktaki paketleri de geri gönder; sonra handle'ı kapat.
                while (true)
                {
                    uint length;
                    Native.Address address;
                    if (!Native.ReceivePacket(handle, packet, (uint)packet.Length, out length, out address))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == 232) break;
                        Log("ALIM HATASI: " + error); break;
                    }
                    bool sent = false;
                    try
                    {
                        string name, host;
                        byte[] first, second;
                        bool active;
                        lock (gate) active = !stop;
                        if (!attempted && active && DiscordProcess.TryIdentify(owner.ProcessId, owner.Timestamp, out name) &&
                            TlsSplitter.TrySplit(packet, (int)length, out first, out second, out host))
                        {
                            var firstAddress = address;
                            var secondAddress = address;
                            bool reverse = string.Equals(host, "updates.discord.com", StringComparison.OrdinalIgnoreCase);
                            bool a = reverse ? Send(second, second.Length, ref secondAddress) : Send(first, first.Length, ref firstAddress);
                            bool b = a && (reverse ? Send(first, first.Length, ref firstAddress) : Send(second, second.Length, ref secondAddress));
                            sent = a && b;
                            Log(sent ? "BÖLÜNDÜ: " + host + (reverse ? " sıra=2,1" : " sıra=1,2") + " (erişim sonucu ayrıca test edilmeli)" : "GÖNDERİM HATASI: Özgün paket tekrar gönderiliyor.");
                        }
                        else if (!attempted) Log("DEĞİŞMEDİ: Tam Discord ClientHello bulunamadı, port=" + owner.LocalPort);
                    }
                    catch (Exception ex) { Log("PAKET ATLANDI: " + ex.Message); }
                    finally
                    {
                        if (!sent && !Send(packet, (int)length, ref address, true)) Log("GÖNDERİM HATASI: TCP yeniden iletimi gerekebilir.");
                    }
                    attempted = true;
                    Stop();
                }
                if (!attempted) Log("VERİ YAKALANMADI: Bağlantı kapandı veya bekleme sınırı doldu, port=" + owner.LocalPort);
            }
            catch (Exception ex) { Log("BAĞLANTI İŞLEYİCİ HATASI: " + ex.Message); }
            finally
            {
                if (deadline != null) deadline.Dispose();
                lock (gate) { Native.WinDivertClose(handle); handle = new IntPtr(-1); }
                done = true;
            }
        }
    }
}
