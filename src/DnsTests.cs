using System;
using System.Runtime.InteropServices;

internal static class DnsTests
{
    private static int count;
    private static void Check(bool condition, string text)
    { count++; if (!condition) throw new Exception("DNS testi: " + text); }
    private static byte[] Packet(bool ipv6, byte[] query)
    {
        int ip = ipv6 ? 40 : 20;
        var b = new byte[ip + 8 + query.Length];
        b[0] = ipv6 ? (byte)0x60 : (byte)0x45;
        if (ipv6)
        {
            DnsWire.Put16(b, 4, b.Length - 40); b[6] = 17; b[7] = 64;
            b[8] = b[24] = 0xfe; b[9] = b[25] = 0x80; b[23] = 2; b[39] = 1;
        }
        else
        {
            DnsWire.Put16(b, 2, b.Length); b[8] = 64; b[9] = 17;
            Buffer.BlockCopy(new byte[] { 192, 0, 2, 10, 192, 0, 2, 1 }, 0, b, 12, 8);
        }
        DnsWire.Put16(b, ip, 54000); DnsWire.Put16(b, ip + 2, 53); DnsWire.Put16(b, ip + 4, query.Length + 8);
        Buffer.BlockCopy(query, 0, b, ip + 8, query.Length); return b;
    }
    internal static int Run()
    {
        count = 0;
        foreach (bool ipv6 in new[] { false, true })
        foreach (bool edns in new[] { false, true })
        foreach (string domain in new[] { "discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media", "discordcdn.com" })
        foreach (string prefix in new[] { "", "gateway.", "a.b.", "_service." })
        foreach (int kind in new[] { 1, 28, 65 })
        {
            var q = DnsWire.Query(prefix + domain, kind);
            if (edns)
            {
                int oldLength = q.Length;
                Array.Resize(ref q, oldLength + 11);
                q[11] = 1; q[oldLength + 2] = 41; q[oldLength + 3] = 4; q[oldLength + 4] = 208;
            }
            var p = Packet(ipv6, q);
            var a = new Native.Address { Flags = (1u << 17) | (ipv6 ? 1u << 20 : 0) };
            Check(Native.WinDivertHelperEvalFilter(DiscordDns.Filter, p, (uint)p.Length, ref a), "Discord DNS biçimi yakalanmalı");
        }
        foreach (bool ipv6 in new[] { false, true })
        foreach (string domain in new[] { "google.com", "youtube.com", "example.net", "discord.com.evil.example", "evildiscord.com", "notdiscord.gg", "discordapp.net.example", "Discord.COM" })
        {
            var p = Packet(ipv6, DnsWire.Query(domain, 1));
            var a = new Native.Address { Flags = (1u << 17) | (ipv6 ? 1u << 20 : 0) };
            Check(!Native.WinDivertHelperEvalFilter(DiscordDns.Filter, p, (uint)p.Length, ref a), "Diğer veya desteklenmeyen sorgu uygulamaya alınmamalı: " + domain);
        }
        foreach (bool ipv6 in new[] { false, true })
        {
            byte[] query = DnsWire.Query("gateway.discord.gg", 28), extracted;
            byte[] packet = Packet(ipv6, query);
            int ip, type; string host;
            Check(DnsWire.Extract(packet, packet.Length, out extracted, out ip), "UDP sorgusu çıkarılmalı");
            Check(DnsWire.Question(extracted, false, out host, out type) && host == "gateway.discord.gg" && type == 28, "AAAA sorusu okunmalı");
            var answer = (byte[])query.Clone(); answer[2] |= 0x80; answer[3] = 0x80;
            Check(DnsWire.ValidResponse(query, answer), "Eşleşen boş cevap kabul edilmeli");
            var reply = DnsWire.Reply(packet, ip, answer);
            Check(DnsWire.U16(reply, ip) == 53 && DnsWire.U16(reply, ip + 2) == 54000, "Yanıt portları ters olmalı");
            int offset = ipv6 ? 8 : 12, addressLength = ipv6 ? 16 : 4;
            for (int i = 0; i < addressLength; i++)
            {
                Check(reply[offset + i] == packet[offset + addressLength + i], "Kaynak IP resolver olmalı");
                Check(reply[offset + addressLength + i] == packet[offset + i], "Hedef IP istemci olmalı");
            }
            Check(DnsWire.U16(reply, ip + 4) == answer.Length + 8, "UDP uzunluğu doğru olmalı");
            for (int i = 0; i < answer.Length; i++) Check(reply[ip + 8 + i] == answer[i], "DNS baytları korunmalı");
            var address = new Native.Address { Flags = ipv6 ? 1u << 20 : 0 };
            Check(Native.WinDivertHelperCalcChecksums(reply, (uint)reply.Length, ref address, 0), "Checksum hesaplanmalı");
            uint sum = 17u + (uint)(reply.Length - ip);
            for (int i = offset; i < offset + 2 * addressLength; i += 2) sum += (uint)DnsWire.U16(reply, i);
            for (int i = ip; i < reply.Length; i += 2) sum += (uint)(reply[i] << 8) + (uint)(i + 1 < reply.Length ? reply[i + 1] : 0);
            while (sum > 65535) sum = (sum & 65535) + (sum >> 16);
            Check(sum == 65535, "Bağımsız UDP checksum kontrolü");
            IntPtr error; uint pos;
            Check(Native.WinDivertHelperCompileFilter(DiscordDns.Filter, 0, IntPtr.Zero, 0, out error, out pos), "DNS filtresi derlenmeli");
            address.Flags |= 1u << 17;
            Check(Native.WinDivertHelperEvalFilter(DiscordDns.Filter, packet, (uint)packet.Length, ref address), "UDP/53 sorgusu eşleşmeli");
            DnsWire.Put16(packet, ip + 2, 443);
            Check(!Native.WinDivertHelperEvalFilter(DiscordDns.Filter, packet, (uint)packet.Length, ref address), "Diğer UDP portu yakalanmamalı");
            answer[0] ^= 1;
            Check(!DnsWire.ValidResponse(query, answer), "Yanlış işlem kimliği reddedilmeli");
            answer[0] ^= 1; answer[answer.Length - 3] = 1;
            Check(!DnsWire.ValidResponse(query, answer), "Yanlış kayıt türü reddedilmeli");
        }
        foreach (string host in new[] { "example.com", "discord.com.evil.example", "evildiscord.com" })
        {
            string parsed; int type;
            Check(DnsWire.Question(DnsWire.Query(host, 1), false, out parsed, out type) && !TlsSplitter.AllowedHost(parsed), "Diğer alan adları yönlendirilmemeli");
        }
        var bad = DnsWire.Query("discord.com", 1); bad[12] = 0xc0;
        string unused; int unusedType;
        Check(!DnsWire.Question(bad, false, out unused, out unusedType), "Desteklenmeyen sıkıştırılmış soru atlanmalı");
        var random = new Random(53);
        for (int i = 0; i < 3000; i++)
        {
            var b = i % 2 == 0 ? Packet(i % 4 == 0, DnsWire.Query("discord.com", 1)) : new byte[random.Next(200)];
            if (i % 2 != 0) random.NextBytes(b);
            else for (int n = 0; n < 3; n++) b[random.Next(b.Length)] = (byte)random.Next(256);
            byte[] extracted; int ip;
            DnsWire.Extract(b, b.Length, out extracted, out ip);
            DnsWire.Question(b, false, out unused, out unusedType);
        }
        Console.WriteLine(count + " DNS doğrulaması ve 3000 bozuk girdi testi geçti. Sürücü açılmadı.");
        return 0;
    }
}
