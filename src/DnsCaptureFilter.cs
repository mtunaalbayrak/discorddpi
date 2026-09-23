using System;
using System.Collections.Generic;

// Küçük harfli tek sorulu DNS ve seçeneksiz EDNS. Diğer biçimler özgün resolver'a gider.
internal static class DnsCaptureFilter
{
    internal static string Build()
    {
        return "outbound and !loopback and !impostor and udp.DstPort == 53 and " +
            "udp.PayloadLength >= 17 and udp.Payload[2] < 8 and udp.Payload16[4b] == 1 and " +
            "udp.Payload32[6b] == 0 and ((udp.Payload16[10b] == 0 and " + Suffixes(0) +
            ") or (udp.Payload16[10b] == 1 and udp.Payload[-11] == 0 and " +
            "udp.Payload16[-10b] == 41 and udp.Payload16[-2b] == 0 and " + Suffixes(11) + "))";
    }
    private static string Suffixes(int extra)
    {
        var domains = new List<string>();
        foreach (string host in new[] { "discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media", "discordcdn.com" })
        {
            byte[] query = DnsWire.Query(host, 1);
            int size = query.Length - 12 - 4;
            var tests = new List<string>();
            for (int i = 0; i < size;)
            {
                int width = size - i >= 4 ? 4 : 1;
                uint value = 0;
                for (int j = 0; j < width; j++) value = (value << 8) | query[12 + i + j];
                string field = width == 4 ? "udp.Payload32[" : "udp.Payload[";
                tests.Add(field + (i - size - 4 - extra) + (width == 4 ? "b]" : "]") + " == " + value);
                i += width;
            }
            tests.Add("udp.Payload16[" + (-2 - extra) + "b] == 1");
            domains.Add("(" + string.Join(" and ", tests) + ")");
        }
        return "(" + string.Join(" or ", domains) + ")";
    }
}