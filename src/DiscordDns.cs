using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

internal sealed class DiscordDns : IDisposable
{
    internal static readonly string Filter = DnsCaptureFilter.Build();
    private readonly HttpClient client;
    private readonly CancellationTokenSource cancel = new CancellationTokenSource();
    private readonly SemaphoreSlim slots = new SemaphoreSlim(16);
    private readonly List<Task> requests = new List<Task>();
    private readonly Thread receiver;
    private IntPtr handle = new IntPtr(-1);
    private volatile bool stopping;
    internal DiscordDns(bool capture)
    {
        // Sabit IP, DoH sunucusunun adını engelli yerel DNS ile çözme ihtiyacını kaldırır.
        // Normal TLS sertifika doğrulaması korunur.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        client.Timeout = TimeSpan.FromSeconds(4);
        client.MaxResponseContentBufferSize = 4096;
        if (!capture) return;
        handle = Native.WinDivertOpen(Filter, 0, 50, 0);
        if (handle == new IntPtr(-1)) { client.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        receiver = new Thread(Receive) { IsBackground = true };
        try { receiver.Start(); }
        catch { Native.WinDivertClose(handle); handle = new IntPtr(-1); client.Dispose(); cancel.Dispose(); slots.Dispose(); throw; }
        Log("AÇIK: Yalnız Discord alan adları Cloudflare DoH ile çözülüyor; sistem DNS ayarı değişmedi.");
    }
    private static void Log(string message) { Console.WriteLine("{0:HH:mm:ss} DNS {1}", DateTime.Now, message); }
    internal async Task<byte[]> Resolve(byte[] query)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, "https://1.1.1.1/dns-query"))
        {
            request.Content = new ByteArrayContent(query);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
            using (var response = await client.SendAsync(request, cancel.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType == null || response.Content.Headers.ContentType.MediaType != "application/dns-message") throw new InvalidOperationException("DoH içerik türü beklenenden farklı.");
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (!DnsWire.ValidResponse(query, bytes)) throw new InvalidOperationException("DoH yanıtı sorguyla eşleşmedi veya desteklenen boyutu aştı.");
                return bytes;
            }
        }
    }
    private bool Send(byte[] packet, ref Native.Address address)
    {
        uint sent;
        Native.WinDivertHelperCalcChecksums(packet, (uint)packet.Length, ref address, 0);
        return Native.WinDivertSend(handle, packet, (uint)packet.Length, out sent, ref address) && sent == packet.Length;
    }
    private void Receive()
    {
        var buffer = new byte[65575];
        try
        {
            while (true)
            {
                uint length; Native.Address address;
                if (!Native.ReceivePacket(handle, buffer, (uint)buffer.Length, out length, out address))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != 232) Log("YAKALAMA HATASI: " + error);
                    break;
                }
                var packet = new byte[length]; Buffer.BlockCopy(buffer, 0, packet, 0, (int)length);
                byte[] query; int ip, type; string host;
                if (!stopping && DnsWire.Extract(packet, packet.Length, out query, out ip) &&
                    DnsWire.Question(query, false, out host, out type) && TlsSplitter.AllowedHost(host) && slots.Wait(0))
                {
                    requests.RemoveAll(t => t.IsCompleted);
                    requests.Add(Answer(packet, address, query, ip, host, type));
                }
                else if (!Send(packet, ref address)) Log("GEÇİŞ HATASI: " + Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex) { Log("İŞLEYİCİ HATASI: " + ex.Message); }
        finally
        {
            // Yeni yakalamayı kes; kayıtlı cevap işleri tamamlanana kadar handle açık kalır.
            Native.WinDivertShutdown(handle, 1);
            // Hata yolunda bile kuyrukta kalan başka alan adı sorgularını iletmeyi dene.
            uint remaining; Native.Address queuedAddress;
            while (Native.ReceivePacket(handle, buffer, (uint)buffer.Length, out remaining, out queuedAddress))
            {
                var queued = new byte[remaining]; Buffer.BlockCopy(buffer, 0, queued, 0, (int)remaining);
                if (!Send(queued, ref queuedAddress)) Log("KUYRUK GEÇİŞ HATASI: " + Marshal.GetLastWin32Error());
            }
        }
    }
    private async Task Answer(byte[] original, Native.Address address, byte[] query, int ip, string host, int type)
    {
        bool delivered = false;
        try
        {
            var answer = await Resolve(query).ConfigureAwait(false);
            if (stopping) return;
            var packet = DnsWire.Reply(original, ip, answer);
            var responseAddress = address;
            responseAddress.Flags &= ~(1u << 17); // Cevabı gelen paket olarak enjekte et.
            delivered = Send(packet, ref responseAddress);
            Log((delivered ? "DOH YANITI: " : "YANIT GÖNDERİLEMEDİ: ") + host + " tür=" + type + " rcode=" + (answer[3] & 15) + " cevap=" + DnsWire.U16(answer, 6));
        }
        catch (Exception ex) { Log("DOH BAŞARISIZ: " + host + " " + ex.Message); }
        finally
        {
            if (!delivered && !Send(original, ref address)) Log("ÖZGÜN SORGU GÖNDERİLEMEDİ: " + host);
            slots.Release();
        }
    }
    public void Dispose()
    {
        stopping = true;
        cancel.Cancel();
        if (handle != new IntPtr(-1))
        {
            Native.WinDivertShutdown(handle, 1);
            if (receiver != null) receiver.Join();
            try { Task.WaitAll(requests.ToArray()); }
            finally { Native.WinDivertClose(handle); handle = new IntPtr(-1); }
        }
        client.Dispose(); cancel.Dispose(); slots.Dispose();
    }
}
