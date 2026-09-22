using System;
using System.Text;

// Bağlantının başlangıç mesajındaki SNI alanını iki TCP paketine böler.
// Şifreli uygulama verisi çözülmez. Tanınmayan paket değiştirilmez.
internal static class TlsSplitter
{
    internal static bool AllowedHost(string host)
    {
        foreach (string domain in new[] { "discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media", "discordcdn.com" })
            if (string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    private static int U16(byte[] b, int p) { return (b[p] << 8) | b[p + 1]; }
    private static void Put16(byte[] b, int p, int n) { b[p] = (byte)(n >> 8); b[p + 1] = (byte)n; }

    internal static bool TrySplit(byte[] packet, int length, out byte[] first, out byte[] second, out string host)
    {
        first = second = null;
        host = null;
        if (packet == null || length > packet.Length || length < 40) return false;
        int version = packet[0] >> 4;
        int ip;
        if (version == 4)
        {
            ip = (packet[0] & 15) * 4;
            // IP parçaları ve TCP dışı protokoller kapsam dışında.
            if (ip < 20 || ip + 20 > length || packet[9] != 6 || U16(packet, 2) != length || (U16(packet, 6) & 0x3fff) != 0) return false;
        }
        else if (version == 6)
        {
            ip = 40;
            // IPv6 uzantı başlıkları: değiştirmeden geçir.
            if (length < 60 || packet[6] != 6 || U16(packet, 4) + 40 != length) return false;
        }
        else return false;
        int tcp = (packet[ip + 12] >> 4) * 4;
        if (tcp < 20 || ip + tcp + 9 > length || U16(packet, ip + 2) != 443) return false;
        // ACK gerekli; SYN/RST/FIN/URG ve TCP Fast Open kapsam dışında.
        if ((packet[ip + 13] & 0x37) != 0x10) return false;
        int data = ip + tcp;
        if (packet[data] != 22 || packet[data + 1] != 3 || packet[data + 2] > 3 || packet[data + 5] != 1) return false;
        int recordEnd = data + 5 + U16(packet, data + 3);
        if (recordEnd > length) return false;
        int helloLength = (packet[data + 6] << 16) | (packet[data + 7] << 8) | packet[data + 8];
        int end = data + 9 + helloLength;
        if (end > recordEnd || helloLength < 38) return false;
        int p = data + 9 + 34; // legacy_version + random
        int session = packet[p++];
        if (p + session + 2 > end) return false;
        p += session;
        int ciphers = U16(packet, p); p += 2;
        if (ciphers < 2 || (ciphers & 1) != 0 || p + ciphers + 1 > end) return false;
        p += ciphers;
        int compression = packet[p++];
        if (compression < 1 || p + compression + 2 > end) return false;
        p += compression;
        int extensions = U16(packet, p); p += 2;
        if (p + extensions != end) return false;
        int cut = -1;
        while (p + 4 <= end)
        {
            int type = U16(packet, p), size = U16(packet, p + 2); p += 4;
            int next = p + size;
            if (next > end) return false;
            if (type == 0)
            {
                if (cut >= 0 || size < 6 || U16(packet, p) != size - 2 || packet[p + 2] != 0) return false;
                int hostLength = U16(packet, p + 3);
                if (hostLength < 3 || hostLength > 253 || hostLength + 5 != size) return false;
                for (int i = p + 5; i < next; i++)
                    if (!(packet[i] >= 'a' && packet[i] <= 'z') && !(packet[i] >= 'A' && packet[i] <= 'Z') && !(packet[i] >= '0' && packet[i] <= '9') && packet[i] != '.' && packet[i] != '-') return false;
                host = Encoding.ASCII.GetString(packet, p + 5, hostLength);
                if (!AllowedHost(host)) return false;
                cut = p + 5 + hostLength / 2 - data;
            }
            p = next;
        }
        if (p != end || cut <= 0 || data + cut >= length) return false;
        first = new byte[data + cut];
        second = new byte[length - cut];
        Buffer.BlockCopy(packet, 0, first, 0, first.Length);
        Buffer.BlockCopy(packet, 0, second, 0, data);
        Buffer.BlockCopy(packet, data + cut, second, data, length - data - cut);
        uint sequence = ((uint)packet[ip + 4] << 24) | ((uint)packet[ip + 5] << 16) | ((uint)packet[ip + 6] << 8) | packet[ip + 7];
        sequence = unchecked(sequence + (uint)cut);
        for (int i = 0; i < 4; i++) second[ip + 4 + i] = (byte)(sequence >> (24 - 8 * i));
        first[ip + 13] &= 0xf7; // PSH yalnızca son parçada kalır.
        if (version == 4) { Put16(first, 2, first.Length); Put16(second, 2, second.Length); }
        else { Put16(first, 4, first.Length - 40); Put16(second, 4, second.Length - 40); }
        // Checksum, native katmanda her iki paket için yeniden hesaplanır.
        return true;
    }
}
