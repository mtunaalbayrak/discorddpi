using System;
using System.Runtime.InteropServices;
using System.Text;

internal static class Native
{
    internal const int FlowLayer = 2;
    internal const ulong SniffReceiveOnly = 1 | 4;
    // WinDivert 2.2: 16 bayt başlık ve 64 bayt birleşim.
    [StructLayout(LayoutKind.Explicit, Size = 80)]
    internal struct Address
    {
        [FieldOffset(0)] public long Timestamp;
        [FieldOffset(8)] public uint Flags;
        [FieldOffset(16)] public ulong Endpoint;
        [FieldOffset(32)] public uint ProcessId;
        [FieldOffset(36)] public uint Local0;
        [FieldOffset(40)] public uint Local1;
        [FieldOffset(44)] public uint Local2;
        [FieldOffset(48)] public uint Local3;
        [FieldOffset(52)] public uint Remote0;
        [FieldOffset(56)] public uint Remote1;
        [FieldOffset(60)] public uint Remote2;
        [FieldOffset(64)] public uint Remote3;
        [FieldOffset(68)] public ushort LocalPort;
        [FieldOffset(70)] public ushort RemotePort;
        [FieldOffset(72)] public byte Protocol;
        public int Event { get { return (int)((Flags >> 8) & 255); } }
    }
    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    internal static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);
    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertRecv(IntPtr handle, IntPtr packet, uint length, out uint received, out Address address);
    [DllImport("WinDivert.dll", EntryPoint = "WinDivertRecv", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReceivePacket(IntPtr handle, [Out] byte[] packet, uint length, out uint received, out Address address);
    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertSend(IntPtr handle, byte[] packet, uint length, out uint sent, ref Address address);
    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertHelperCalcChecksums([In, Out] byte[] packet, uint length, ref Address address, ulong flags);
    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertShutdown(IntPtr handle, int how);
    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertClose(IntPtr handle);
    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertHelperFormatIPv6Address(uint[] address, StringBuilder buffer, uint length);
    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertHelperCompileFilter(string filter, int layer, IntPtr output, uint length, out IntPtr error, out uint position);
    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinDivertHelperEvalFilter(string filter, byte[] packet, uint length, ref Address address);
    internal static string Format(uint a, uint b, uint c, uint d)
    {
        var buffer = new StringBuilder(64);
        if (!WinDivertHelperFormatIPv6Address(new uint[] { a, b, c, d }, buffer, 64))
            throw new InvalidOperationException("IP adresi çözümlenemedi.");
        return buffer.ToString();
    }
}
