using System;
using System.Collections.Generic;
using System.Text;

internal static class DnsWire
{
    internal static int U16(byte[] b, int p) { return (b[p] << 8) | b[p + 1]; }
    internal static void Put16(byte[] b, int p, int n) { b[p] = (byte)(n >> 8); b[p + 1] = (byte)n; }
    internal static bool Question(byte[] data, bool response, out string host, out int type)
    {
        host = null; type = 0;
        if (data == null || data.Length < 17 || ((data[2] & 128) != 0) != response ||
            (data[2] & 0x78) != 0 || U16(data, 4) != 1) return false;
        var labels = new List<string>();
        int p = 12, total = 0;
        while (p < data.Length)
        {
            int length = data[p++];
            if (length == 0)
            {
                if (labels.Count == 0 || p + 4 > data.Length || U16(data, p + 2) != 1) return false;
                host = string.Join(".", labels); type = U16(data, p);
                return true;
            }
            if (length > 63 || p + length > data.Length || (total += length + 1) > 254) return false;
            for (int i = p; i < p + length; i++)
                if (!(data[i] >= 'a' && data[i] <= 'z') && !(data[i] >= 'A' && data[i] <= 'Z') &&
                    !(data[i] >= '0' && data[i] <= '9') && data[i] != '-' && data[i] != '_') return false;
            labels.Add(Encoding.ASCII.GetString(data, p, length)); p += length;
        }
        return false;
    }
    internal static byte[] Query(string host, int type)
    {
        var data = new List<byte>(new byte[] { 0x44, 0x44, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        foreach (string label in host.Split('.')) { data.Add((byte)label.Length); data.AddRange(Encoding.ASCII.GetBytes(label)); }
        data.AddRange(new byte[] { 0, (byte)(type >> 8), (byte)type, 0, 1 });
        return data.ToArray();
    }
    internal static bool ValidResponse(byte[] query, byte[] answer)
    {
        string qname, aname; int qtype, atype;
        return answer != null && answer.Length <= 1232 && Question(query, false, out qname, out qtype) &&
            Question(answer, true, out aname, out atype) && query[0] == answer[0] && query[1] == answer[1] &&
            string.Equals(qname, aname, StringComparison.OrdinalIgnoreCase) && qtype == atype && (answer[2] & 2) == 0;
    }
    internal static bool Extract(byte[] packet, int length, out byte[] query, out int ip)
    {
        query = null; ip = 0;
        if (packet == null || length > packet.Length || length < 28) return false;
        if ((packet[0] >> 4) == 4)
        {
            ip = (packet[0] & 15) * 4;
            if (ip != 20 || packet[9] != 17 || U16(packet, 2) != length || (U16(packet, 6) & 0x3fff) != 0) return false;
        }
        else if ((packet[0] >> 4) == 6)
        {
            ip = 40;
            if (length < 48 || packet[6] != 17 || U16(packet, 4) + 40 != length) return false;
        }
        else return false;
        if (ip + 8 > length || U16(packet, ip + 2) != 53 || U16(packet, ip + 4) != length - ip) return false;
        query = new byte[length - ip - 8];
        Buffer.BlockCopy(packet, ip + 8, query, 0, query.Length);
        return true;
    }
    internal static byte[] Reply(byte[] queryPacket, int ip, byte[] answer)
    {
        var reply = new byte[ip + 8 + answer.Length];
        Buffer.BlockCopy(queryPacket, 0, reply, 0, ip + 8);
        int addressLength = ip == 20 ? 4 : 16, source = ip == 20 ? 12 : 8;
        Buffer.BlockCopy(queryPacket, source, reply, source + addressLength, addressLength);
        Buffer.BlockCopy(queryPacket, source + addressLength, reply, source, addressLength);
        Put16(reply, ip, 53); Put16(reply, ip + 2, U16(queryPacket, ip));
        Put16(reply, ip + 4, answer.Length + 8);
        if (ip == 20) { Put16(reply, 2, reply.Length); reply[8] = 64; }
        else { Put16(reply, 4, reply.Length - 40); reply[7] = 64; }
        Buffer.BlockCopy(answer, 0, reply, ip + 8, answer.Length);
        return reply;
    }
}
