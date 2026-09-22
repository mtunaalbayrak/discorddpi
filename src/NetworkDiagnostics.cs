using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Threading.Tasks;

internal static class NetworkDiagnostics
{
    internal static Task Run()
    {
        Log("BAŞLANGIÇ: DNS, TCP/443 ve doğrudan güncelleme HTTPS kontrolü; Discord giriş testi değildir.");
        return Task.WhenAll(Check("discord.com"), Check("gateway.discord.gg"), Check("updates.discord.com"), CheckUpdateHttps());
    }
    private static async Task CheckUpdateHttps()
    {
        Log("HTTPS BAŞLANGIÇ: updates.discord.com; doğrudan tanılama, Discord motor filtresinin dışında.");
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }))
            {
                client.Timeout = TimeSpan.FromSeconds(8);
                using (var request = new HttpRequestMessage(HttpMethod.Head, "https://updates.discord.com/"))
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    Log("HTTPS YANITI: updates.discord.com HTTP=" + (int)response.StatusCode +
                        "; TLS doğrulandı. 4xx/5xx dahil bu sonuç güncelleme indirme başarısı değildir.");
            }
        }
        catch (Exception ex)
        {
            for (int i = 0; ex != null && i < 4; i++, ex = ex.InnerException)
                Log("HTTPS HATASI: " + ex.GetType().Name + " " + ex.Message);
        }
    }
    private static void Log(string text) { Console.WriteLine("{0:HH:mm:ss} TANI {1}", DateTime.Now, text); }
    private static void ObserveFailure(Task task)
    {
        task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }
    private static async Task Check(string host)
    {
        try
        {
            Log("DNS SORGUSU: " + host);
            // Resolver çağrısının senkron başlangıcı da zaman aşımının kapsamına girer.
            var lookup = Task.Run(() => Dns.GetHostAddressesAsync(host));
            if (await Task.WhenAny(lookup, Task.Delay(4000)) != lookup)
            {
                ObserveFailure(lookup);
                Log("DNS ZAMAN AŞIMI: " + host); return;
            }
            var addresses = await lookup;
            Log("DNS " + host + " = " + string.Join(", ", addresses.Select(a => a.ToString())));
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
            if (address == null) { Log("DNS ADRES YOK: " + host); return; }
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            { Log("DNS ŞÜPHELİ YEREL ADRES: " + host); return; }
            using (var client = new TcpClient(address.AddressFamily))
            {
                var connect = client.ConnectAsync(address, 443);
                if (await Task.WhenAny(connect, Task.Delay(3000)) != connect)
                {
                    ObserveFailure(connect);
                    Log("TCP ZAMAN AŞIMI: " + host + " " + address); return;
                }
                await connect;
                Log("TCP/443 AÇIK: " + host + " (TLS kontrol edilmedi)");
            }
        }
        catch (Exception ex) { Log("BAŞARISIZ " + host + ": " + ex.GetType().Name + " " + ex.Message); }
    }
}
