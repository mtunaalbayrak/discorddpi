using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

internal static class Program
{
    private const string Filter = "outbound and (tcp or udp)";
    private static readonly object HandleLock = new object();
    private static IntPtr handle = new IntPtr(-1);
    private static volatile bool stopping;
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
            if (args.Length == 1 && args[0] == "--native-check") return NativeCheck();
            if (args.Length == 1 && args[0] == "--diagnose") { NetworkDiagnostics.Run().GetAwaiter().GetResult(); return 0; }
            if (args.Length == 1 && args[0] == "--dns-test") return DnsTests.Run();
            if (args.Length == 1 && args[0] == "--doh-check")
            {
                using (var dns = new DiscordDns(false))
                    foreach (string host in new[] { "discord.com", "gateway.discord.gg", "updates.discord.com" })
                    {
                        byte[] reply = dns.Resolve(DnsWire.Query(host, 1)).GetAwaiter().GetResult();
                        Console.WriteLine("DoH " + host + " rcode=" + (reply[3] & 15) + " cevap=" + DnsWire.U16(reply, 6));
                    }
                return 0;
            }
            if (args.Length == 1 && args[0] == "--observe") return Observe(false);
            if (args.Length == 1 && args[0] == "--engine")
            {
                bool created;
                using (var singleEngine = new Mutex(true, @"Local\DiscordDpi.ExperimentalEngine", out created))
                {
                    if (!created) throw new InvalidOperationException("Deneysel motor zaten çalışıyor. Önce diğer pencereyi durdur.");
                    try { return Observe(true); }
                    finally { singleEngine.ReleaseMutex(); }
                }
            }
            if (args.Length == 1 && args[0] == "--packet-test") return PacketTests.Run();
            if (args.Length > 0 && (args.Length != 1 || args[0] != "--check"))
            {
                Console.Error.WriteLine("Kullanım: DiscordDpi.exe [--check | --observe | --engine | --self-test | --native-check | --packet-test]");
                return 2;
            }
            Check();
            if (args.Length == 0 && !Console.IsInputRedirected) { Console.WriteLine("Kapatmak için Enter."); Console.ReadLine(); }
            return 0;
        }
        catch (DllNotFoundException) { Console.Error.WriteLine("WinDivert bulunamadı. setup-windivert.ps1 çalıştırılmalı."); return 1; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    private static void Check()
    {
        Console.WriteLine("Discord DPI — kendi motorumuzun ilk parçası: bağlantı gözlemcisi.");
        int count = 0;
        foreach (string channel in new[] { "Discord", "DiscordPTB", "DiscordCanary" })
            foreach (var process in Process.GetProcessesByName(channel))
                using (process)
                {
                    string name;
                    if (DiscordProcess.TryIdentify((uint)process.Id, 0, out name))
                    { count++; Console.WriteLine("Tanındı: {0}, PID {1}", name, process.Id); }
                }
        Console.WriteLine("Tanınan işlem: " + count);
        Console.WriteLine("Bu kontrol sürücü açmaz ve trafiği değiştirmez. Deneysel motor ayrı başlatılır.");
        Console.WriteLine("Canlı gözlem: yönetici terminalinde DiscordDpi.exe --observe");
    }
    private static int Observe(bool experimental)
    {
        using (var identity = WindowsIdentity.GetCurrent())
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Gözlem için terminali yönetici olarak açmalısın.");
        if (experimental)
        {
            var other = Process.GetProcessesByName("goodbyedpi");
            bool conflict = other.Length > 0;
            foreach (var process in other) process.Dispose();
            if (conflict) throw new InvalidOperationException("Deneysel test için önce GoodbyeDPI'ı durdur. Programımız onu otomatik kapatmaz.");
        }
        handle = Native.WinDivertOpen(experimental ? "outbound and tcp and remotePort == 443" : Filter, Native.FlowLayer, 0, Native.SniffReceiveOnly);
        if (handle == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        Console.CancelKeyPress += Cancel;
        if (Console.IsInputRedirected) Task.Run(delegate
        {
            string command = Console.ReadLine();
            if (command == "stop" || command == null) StopCapture();
        });
        var engine = experimental ? new ScopedEngine() : null;
        EarlyConnections early = null;
        DiscordDns dnsRedirector = null;
        Task diagnostics = null;
        try
        {
            if (engine != null)
            {
                try { early = new EarlyConnections(engine); Console.WriteLine("Erken Discord bağlantı izlemesi açık."); }
                catch (Exception ex) { Console.WriteLine("Erken izleme açılamadı; FLOW yöntemi sürüyor: " + ex.Message); }
            }
            if (experimental) dnsRedirector = new DiscordDns(true);
            Console.WriteLine(experimental ? "Deneysel motor açık — Discord DNS + TLS deneniyor. Erişim henüz doğrulanmadı." : "Yeni Discord bağlantıları gösteriliyor. TCP/UDP, IPv4/IPv6. Paket değiştirme yok.");
            Console.WriteLine("Discord'u şimdi açabilir veya yeni bağlantı oluşturabilirsin. Çıkış: Ctrl+C.");
            if (experimental) diagnostics = Task.Run(() => NetworkDiagnostics.Run());
            Console.WriteLine("Motor bağlantı olaylarını dinliyor.");
            while (!stopping)
            {
                Native.Address address;
                uint received;
                if (!Native.WinDivertRecv(handle, IntPtr.Zero, 0, out received, out address))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (stopping && error == 232) break;
                    throw new Win32Exception(error);
                }
                if (engine != null && address.Event == 2) engine.OnFlow(address);
                string name;
                if (!DiscordProcess.TryIdentify(address.ProcessId, address.Timestamp, out name)) continue;
                if (engine != null && address.Event == 1) engine.OnFlow(address);
                Console.WriteLine("{0:HH:mm:ss} {1} PID={2} {3} {4} [{5}]:{6} -> [{7}]:{8}",
                    DateTime.Now, name, address.ProcessId, address.Event == 1 ? "AÇILDI" : "KAPANDI",
                    address.Protocol == 6 ? "TCP" : "UDP",
                    Native.Format(address.Local0, address.Local1, address.Local2, address.Local3), address.LocalPort,
                    Native.Format(address.Remote0, address.Remote1, address.Remote2, address.Remote3), address.RemotePort);
            }
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
            lock (HandleLock) { Native.WinDivertClose(handle); handle = new IntPtr(-1); }
            if (early != null) early.Dispose();
            if (engine != null) engine.Dispose();
            if (dnsRedirector != null) dnsRedirector.Dispose();
            if (diagnostics != null) diagnostics.Wait(7500);
        }
    }
    private static void Cancel(object sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        StopCapture();
    }
    private static void StopCapture()
    {
        stopping = true;
        lock (HandleLock) if (handle != new IntPtr(-1)) Native.WinDivertShutdown(handle, 1);
    }
    private static int NativeCheck()
    {
        IntPtr error;
        uint position;
        if (!Native.WinDivertHelperCompileFilter(Filter, Native.FlowLayer, IntPtr.Zero, 0, out error, out position))
            throw new InvalidOperationException("Filtre hatası: " + Marshal.PtrToStringAnsi(error));
        if (!Native.WinDivertHelperCompileFilter(EarlyConnections.Filter, 3, IntPtr.Zero, 0, out error, out position))
            throw new InvalidOperationException("Erken bağlantı filtresi hatası: " + Marshal.PtrToStringAnsi(error));
        string loopback = Native.Format(1, 0, 0, 0);
        if (loopback != "::1") throw new InvalidOperationException("Adres dönüşümü hatası: " + loopback);
        Console.WriteLine("WinDivert DLL, FLOW filtresi ve IPv6 dönüşümü doğrulandı. Sürücü açılmadı.");
        return 0;
    }
    private static int SelfTest()
    {
        string root = @"C:\Users\Test\AppData\Local";
        if (Marshal.SizeOf(typeof(Native.Address)) != 80 || Marshal.OffsetOf(typeof(Native.Address), "ProcessId").ToInt32() != 32 || Marshal.OffsetOf(typeof(Native.Address), "Protocol").ToInt32() != 72)
            throw new Exception("Native bellek düzeni testi başarısız.");
        string[] accepted = { @"Discord\app-1.0.0\Discord.exe", @"DiscordPTB\app-1.0.0\DiscordPTB.exe", @"DiscordCanary\app-1.0.0\DiscordCanary.exe" };
        foreach (string path in accepted)
            if (!DiscordProcess.Matches(root + "\\" + path, root)) throw new Exception("Discord tanınmadı: " + path);
        string[] rejected = { @"Other\app-1.0.0\Discord.exe", @"Discord\app-1.0.0\chrome.exe", @"Discord\Update.exe", @"Discord\app-1.0.0\nested\Discord.exe", @"DiscordFake\app-1.0.0\Discord.exe", @"Discord\app-1.0.0\..\..\Other\Discord.exe" };
        foreach (string path in rejected)
            if (DiscordProcess.Matches(root + "\\" + path, root)) throw new Exception("Yanlış işlem kabul edildi: " + path);
        Console.WriteLine("10 test geçti: native bellek düzeni ve Discord işlem yolu ayrımı.");
        return 0;
    }
}
