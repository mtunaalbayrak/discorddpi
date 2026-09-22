using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

// Pasif CONNECT bildirimi FLOW'dan önce gelebilir. Hiçbir soket engellenmez.
// Eksik adreslerde mevcut FLOW yolu devreye girer; filtre genişletilmez.
internal sealed class EarlyConnections : IDisposable
{
    internal const string Filter = "event == CONNECT and tcp and remotePort == 443";
    private readonly ScopedEngine engine;
    private readonly Thread thread;
    private IntPtr handle;
    private volatile bool stopping;
    internal EarlyConnections(ScopedEngine engine)
    {
        this.engine = engine;
        handle = Native.WinDivertOpen(Filter, 3, 0, Native.SniffReceiveOnly);
        if (handle == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        thread = new Thread(Run) { IsBackground = true };
        try { thread.Start(); }
        catch { Native.WinDivertClose(handle); throw; }
    }
    private void Run()
    {
        try
        {
            while (!stopping)
            {
                Native.Address address; uint received;
                if (!Native.WinDivertRecv(handle, IntPtr.Zero, 0, out received, out address))
                {
                    if (!stopping) ScopedEngine.Log("ERKEN İZLEME HATASI: " + Marshal.GetLastWin32Error());
                    break;
                }
                string name;
                if (!DiscordProcess.TryIdentify(address.ProcessId, address.Timestamp, out name)) continue;
                address.Flags = (address.Flags & ~(255u << 8)) | (1u << 8);
                engine.OnFlow(address);
            }
        }
        catch (Exception ex) { ScopedEngine.Log("ERKEN İZLEME ATLANDI: " + ex.Message); }
    }
    public void Dispose()
    {
        stopping = true;
        Native.WinDivertShutdown(handle, 1);
        thread.Join();
        Native.WinDivertClose(handle);
    }
}
