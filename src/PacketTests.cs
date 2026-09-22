using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

internal static class PacketTests
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception("Paket testi: " + message);
    }
    private static void Add16(List<byte> b, int n) { b.Add((byte)(n >> 8)); b.Add((byte)n); }
    private static void Put16(byte[] b, int p, int n) { b[p] = (byte)(n >> 8); b[p + 1] = (byte)n; }
    private static int U16(byte[] b, int p) { return (b[p] << 8) | b[p + 1]; }
    internal static byte[] Sample(bool ipv6, string host)
    {
        var hello = new List<byte>();
        hello.AddRange(new byte[] { 3, 3 }); hello.AddRange(new byte[32]);
        hello.Add(0); Add16(hello, 2); hello.AddRange(new byte[] { 0x13, 1, 1, 0 });
        byte[] name = Encoding.ASCII.GetBytes(host);
        Add16(hello, name.Length + 9); Add16(hello, 0); Add16(hello, name.Length + 5);
        Add16(hello, name.Length + 3); hello.Add(0); Add16(hello, name.Length); hello.AddRange(name);
        var tls = new List<byte>(new byte[] { 22, 3, 1 }); Add16(tls, hello.Count + 4);
        tls.Add(1); tls.Add(0); Add16(tls, hello.Count); tls.AddRange(hello);
        int ip = ipv6 ? 40 : 20;
        var packet = new byte[ip + 20 + tls.Count];
        packet[0] = ipv6 ? (byte)0x60 : (byte)0x45;
        if (ipv6)
        {
            Put16(packet, 4, packet.Length - 40); packet[6] = 6; packet[7] = 64;
            packet[8] = 0x20; packet[9] = 1; packet[10] = 0x0d; packet[11] = 0xb8; packet[23] = 1;
            packet[24] = 0x20; packet[25] = 1; packet[26] = 0x0d; packet[27] = 0xb8; packet[39] = 2;
        }
        else
        {
            Put16(packet, 2, packet.Length); packet[6] = 0x40; packet[8] = 64; packet[9] = 6;
            Buffer.BlockCopy(new byte[] { 192, 0, 2, 10, 203, 0, 113, 20 }, 0, packet, 12, 8);
        }
        Put16(packet, ip, 52000); Put16(packet, ip + 2, 443);
        packet[ip + 4] = packet[ip + 5] = packet[ip + 6] = 255; packet[ip + 7] = 240;
        packet[ip + 12] = 0x50; packet[ip + 13] = 0x18; packet[ip + 14] = 0xff;
        tls.CopyTo(packet, ip + 20);
        return packet;
    }
    internal static int Run()
    {
        assertions = 0;
        foreach (bool ipv6 in new[] { false, true })
        {
            byte[] packet = Sample(ipv6, "gateway.discord.gg"), first, second;
            byte[] original = (byte[])packet.Clone();
            string host;
            int ip = ipv6 ? 40 : 20, headers = ip + 20;
            Check(TlsSplitter.TrySplit(packet, packet.Length, out first, out second, out host), "Geçerli ClientHello ayrılmalı.");
            Check(host == "gateway.discord.gg", "SNI doğru okunmalı.");
            Check(first.Length + second.Length - 2 * headers == packet.Length - headers, "Veri uzunluğu korunmalı.");
            int cut = first.Length - headers;
            for (int i = 0; i < packet.Length; i++) Check(packet[i] == original[i], "Girdi değişmemeli.");
            for (int i = headers; i < packet.Length; i++)
                Check(packet[i] == (i < headers + cut ? first[i] : second[i - cut]), "TCP yeniden birleştirme aynı baytları üretmeli.");
            uint seq = ((uint)second[ip + 4] << 24) | ((uint)second[ip + 5] << 16) | ((uint)second[ip + 6] << 8) | second[ip + 7];
            Check(seq == unchecked(0xfffffff0u + (uint)cut), "Sıra numarası taşması doğru ele alınmalı.");
            Check((first[ip + 13] & 8) == 0 && (second[ip + 13] & 8) != 0, "PSH son parçada kalmalı.");
            Check(U16(first, ipv6 ? 4 : 2) == first.Length - (ipv6 ? 40 : 0), "IP uzunluğu güncellenmeli.");
            var address = new Native.Address { Flags = (1u << 17) | (ipv6 ? 1u << 20 : 0) };
            Check(Native.WinDivertHelperCalcChecksums(first, (uint)first.Length, ref address, 0), "İlk checksum hesaplanmalı.");
            Check(Native.WinDivertHelperCalcChecksums(second, (uint)second.Length, ref address, 0), "İkinci checksum hesaplanmalı.");
            VerifyTcpChecksum(first, ip); VerifyTcpChecksum(second, ip);
            var flow = new Native.Address { LocalPort = 52000, RemotePort = 443 };
            if (ipv6) { flow.Local0 = 1; flow.Local3 = 0x20010db8; flow.Remote0 = 2; flow.Remote3 = 0x20010db8; }
            else { flow.Local0 = 0xc000020a; flow.Local1 = 0xffff; flow.Remote0 = 0xcb007114; flow.Remote1 = 0xffff; }
            string filter = ScopedEngine.MakeFilter(flow);
            IntPtr error; uint position;
            Check(filter != null && Native.WinDivertHelperCompileFilter(filter, 0, IntPtr.Zero, 0, out error, out position), "Hedef filtresi derlenmeli.");
            Check(Native.WinDivertHelperEvalFilter(filter, packet, (uint)packet.Length, ref address), "Hedef bağlantı eşleşmeli.");
            packet[ip] ^= 1;
            Check(!Native.WinDivertHelperEvalFilter(filter, packet, (uint)packet.Length, ref address), "Başka kaynak portu eşleşmemeli.");
            packet[ip] ^= 1; packet[ipv6 ? 39 : 19] ^= 1;
            Check(!Native.WinDivertHelperEvalFilter(filter, packet, (uint)packet.Length, ref address), "Başka hedef IP eşleşmemeli.");
            packet = Sample(ipv6, "discord.com.evil.example");
            Check(!TlsSplitter.TrySplit(packet, packet.Length, out first, out second, out host), "Benzer alan adı kabul edilmemeli.");
            packet = Sample(ipv6, "example.com");
            Check(!TlsSplitter.TrySplit(packet, packet.Length, out first, out second, out host), "Diğer siteler değiştirilmemeli.");
            packet = Sample(ipv6, "discord.com"); packet[ip + 13] |= 2;
            Check(!TlsSplitter.TrySplit(packet, packet.Length, out first, out second, out host), "SYN verisi değiştirilmemeli.");
            packet = Sample(ipv6, "discord.com"); packet[ipv6 ? 6 : 9] = 17;
            Check(!TlsSplitter.TrySplit(packet, packet.Length, out first, out second, out host), "UDP değiştirilmemeli.");
        }
        var random = new Random(2026);
        for (int i = 0; i < 5000; i++)
        {
            byte[] packet = i % 2 == 0 ? Sample(i % 4 == 0, "discord.com") : new byte[random.Next(0, 300)];
            if (i % 2 != 0) random.NextBytes(packet);
            else for (int n = 0; n < 4; n++) packet[random.Next(packet.Length)] = (byte)random.Next(256);
            byte[] a, b; string host;
            TlsSplitter.TrySplit(packet, i % 2 == 0 ? packet.Length : random.Next(packet.Length + 1), out a, out b, out host);
        }
        Console.WriteLine(assertions + " paket doğrulaması ve 5000 bozuk/kısmi girdi testi geçti. Sürücü açılmadı.");
        return 0;
    }
    private static void VerifyTcpChecksum(byte[] packet, int ip)
    {
        uint sum = 0;
        int length = packet.Length - ip;
        if (ip == 20) for (int i = 12; i < 20; i += 2) sum += (uint)U16(packet, i);
        else for (int i = 8; i < 40; i += 2) sum += (uint)U16(packet, i);
        sum += (uint)(6 + length);
        for (int i = ip; i < packet.Length; i += 2)
            sum += (uint)(packet[i] << 8) + (uint)(i + 1 < packet.Length ? packet[i + 1] : 0);
        while (sum > 65535) sum = (sum & 65535) + (sum >> 16);
        Check(sum == 65535, "Bağımsız TCP checksum doğrulaması.");
    }
}
