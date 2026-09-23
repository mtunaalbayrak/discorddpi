using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class DesktopProgram
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ObserverWindow());
    }
}

public sealed class ObserverWindow : Form
{
    private readonly Label discord = new Label();
    private readonly Label state = new Label();
    private readonly Label count = new Label();
    private readonly Button start = new Button();
    private readonly Button stop = new Button();
    private readonly Button engine = new Button();
    private readonly ListView events = new ListView();
    private readonly Timer timer = new Timer();
    private readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();
    private Process worker;
    private bool stopping;
    private bool closing;
    private bool exitRequested;
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
    private int eventCount;
    private int ticks;
    private DateTime lastHealth = DateTime.MinValue;
    private long outputLines;
    private SessionLog session;
    private readonly Label logStatus = new Label();

    public ObserverWindow()
    {
        Text = "Discord DPI — DNS + TLS Denemesi";
        ClientSize = new Size(960, 650);
        MinimumSize = new Size(976, 689);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(18, 22, 32);
        ForeColor = Color.FromArgb(229, 234, 244);
        Font = new Font("Segoe UI", 10);

        var title = LabelAt("Discord DPI", 28, 23, 450, 42);
        title.Font = new Font("Segoe UI", 23, FontStyle.Bold);
        LabelAt("BAĞLANTI GÖZLEMCİSİ  /  DISCORD DNS + TLS MOTORU", 30, 70, 800, 26).ForeColor = Color.FromArgb(153, 167, 195);
        discord.SetBounds(30, 116, 650, 28);
        discord.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        state.SetBounds(30, 151, 880, 45);
        state.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        state.Text = "Motor denemesinde Discord alan adları Cloudflare DoH ile çözülür; genel DNS ayarı değişmez.";
        Controls.AddRange(new Control[] { discord, state });

        ConfigureButton(start, "İzlemeyi başlat", 30, 209, 195);
        start.BackColor = Color.FromArgb(88, 101, 242);
        start.Click += delegate { StartObserver(false); };
        ConfigureButton(engine, "Motoru dene", 239, 209, 165);
        engine.BackColor = Color.FromArgb(108, 65, 154);
        engine.Click += delegate { StartObserver(true); };
        ConfigureButton(stop, "Durdur", 418, 209, 110);
        stop.Enabled = false;
        stop.Click += async delegate { await StopObserver(); };
        var clear = new Button();
        ConfigureButton(clear, "Listeyi temizle", 542, 209, 150);
        clear.Click += delegate { events.Items.Clear(); eventCount = 0; UpdateCount(); };
        count.SetBounds(705, 219, 220, 25);
        count.TextAlign = ContentAlignment.MiddleRight;
        count.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Controls.Add(count);

        events.SetBounds(30, 274, 900, 261);
        events.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        events.View = View.Details;
        events.FullRowSelect = true;
        events.MultiSelect = false;
        events.BackColor = Color.FromArgb(26, 32, 45);
        events.ForeColor = ForeColor;
        events.BorderStyle = BorderStyle.FixedSingle;
        events.Font = new Font("Consolas", 10);
        events.Columns.Add("Saat", 88);
        events.Columns.Add("Discord bağlantı olayı", 780);
        Controls.Add(events);
        var hint = LabelAt("X veya küçültme düğmesi uygulamayı saatin yanına gizler; çalışan motor devam eder.\nYeniden açmak veya tamamen çıkmak için saatin yanındaki Discord DPI simgesini kullan.", 30, 551, 900, 46);
        hint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logStatus.SetBounds(30, 609, 900, 25);
        logStatus.Text = "Kayıtlar bu bilgisayarda logs klasöründe tutulur; GitHub’a gönderilmez.";
        logStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logStatus.ForeColor = Color.FromArgb(153, 167, 195);
        Controls.Add(logStatus);

        timer.Interval = 250;
        timer.Tick += delegate { Drain(); if (Visible && ++ticks % 8 == 0) RefreshDiscord(); };
        VisibleChanged += delegate
        {
            // Gizliyken yalnız işçi durumu ve sınırlı olay kuyruğu takip edilir.
            timer.Interval = Visible ? 250 : 1000;
            if (Visible) { RefreshDiscord(); Drain(); }
        };
        RefreshDiscord();
        UpdateCount();
        timer.Start();
        tray.Icon = SystemIcons.Application;
        tray.Text = "Discord DPI — hazır";
        trayMenu.Items.Add("Pencereyi aç", null, delegate { RestoreWindow(); });
        trayMenu.Items.Add("Tamamen çık", null, delegate { exitRequested = true; Close(); });
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += delegate { RestoreWindow(); };
        tray.Visible = true;
        Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += async delegate(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !exitRequested)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            if (worker == null) return;
            e.Cancel = true;
            if (closing) return;
            closing = true;
            await StopObserver();
        };
        FormClosed += delegate { timer.Dispose(); tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); if (session != null) session.Dispose(); };
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private Label LabelAt(string text, int x, int y, int width, int height)
    {
        var label = new Label { Text = text };
        label.SetBounds(x, y, width, height);
        Controls.Add(label);
        return label;
    }
    private void ConfigureButton(Button button, string text, int x, int y, int width)
    {
        button.Text = text;
        button.SetBounds(x, y, width, 44);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Color.FromArgb(43, 52, 71);
        button.ForeColor = ForeColor;
        Controls.Add(button);
    }
    private void RefreshDiscord()
    {
        int found = 0;
        foreach (string channel in new[] { "Discord", "DiscordPTB", "DiscordCanary" })
            foreach (var process in Process.GetProcessesByName(channel))
                using (process)
                {
                    string name;
                    if (DiscordProcess.TryIdentify((uint)process.Id, 0, out name)) found++;
                }
        discord.Text = found > 0 ? "● Discord bulundu — " + found + " işlem" : "○ Discord bekleniyor — uygulamayı açabilirsin";
        discord.ForeColor = found > 0 ? Color.FromArgb(92, 215, 166) : Color.FromArgb(242, 193, 102);
    }
    private void StartObserver(bool experimental)
    {
        if (worker != null || stopping) return;
        try
        {
            if (session != null) session.Dispose();
            session = new SessionLog(Path.Combine(Path.GetDirectoryName(typeof(ObserverWindow).Assembly.Location), "logs"));
            string oldLine;
            while (pending.TryDequeue(out oldLine)) { }
            session.Write("Discord DPI v0.1.4. Mode=" + (experimental ? "engine" : "observe") + " Started=" + DateTime.Now.ToString("O"));
            System.Threading.Interlocked.Exchange(ref outputLines, 0);
            lastHealth = DateTime.UtcNow;
            session.Write(discord.Text);
            logStatus.Text = "Kayıt: logs\\" + Path.GetFileName(session.FilePath) + "  •  yalnızca bu bilgisayarda";
            var info = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(typeof(ObserverWindow).Assembly.Location), "DiscordDpi.exe"), experimental ? "--engine" : "--observe");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = info.RedirectStandardError = info.RedirectStandardInput = true;
            info.StandardOutputEncoding = info.StandardErrorEncoding = Encoding.UTF8;
            worker = new Process { StartInfo = info };
            worker.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { Enqueue(e.Data); };
            worker.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) Enqueue("HATA: " + e.Data); };
            worker.Start();
            worker.BeginOutputReadLine();
            worker.BeginErrorReadLine();
            start.Enabled = false;
            engine.Enabled = false;
            stop.Enabled = true;
            state.Text = experimental ? "Deneysel motor başlatılıyor…" : "Gözlemci başlatılıyor…";
            tray.Text = experimental ? "Discord DPI — motor çalışıyor" : "Discord DPI — izleme açık";
        }
        catch (Exception ex)
        {
            if (worker != null) { worker.Dispose(); worker = null; }
            state.Text = "Başlatılamadı: " + ex.Message;
            if (session != null) { session.Write(state.Text); session.Dispose(); }
        }
    }
    private void Enqueue(string line)
    {
        if (line == null) return;
        System.Threading.Interlocked.Increment(ref outputLines);
        if (session != null) session.Write(line);
        if (pending.Count < 1000) pending.Enqueue(line);
    }
    private void Drain()
    {
        if (worker != null && session != null && (DateTime.UtcNow - lastHealth).TotalSeconds >= 15)
        {
            lastHealth = DateTime.UtcNow;
            session.Write(DateTime.Now.ToString("HH:mm:ss") + " DURUM: arayüz açık; motor=" +
                (worker.HasExited ? "sonlandı" : "işlem çalışıyor") + "; alınan satır=" +
                System.Threading.Interlocked.Read(ref outputLines) + "; " + discord.Text);
        }
        if (session != null && session.Error != null) logStatus.Text = "Kayıt yazılamadı: " + session.Error;
        string line;
        bool changed = false;
        events.BeginUpdate();
        try
        {
        for (int i = 0; i < 100 && pending.TryDequeue(out line); i++)
        {
            if (line.StartsWith("HATA:")) state.Text = line;
            else if (line.StartsWith("Yeni Discord")) state.Text = "İzleme açık — yeni Discord bağlantıları bekleniyor.";
            else if (line.StartsWith("Deneysel motor")) state.Text = line;
            else if (line.Length > 8 && line[2] == ':' && line[5] == ':')
            {
                events.Items.Add(new ListViewItem(new[] { line.Substring(0, 8), line.Substring(9) }));
                eventCount++;
                if (events.Items.Count > 500) events.Items.RemoveAt(0);
                changed = true;
            }
        }
        }
        finally { events.EndUpdate(); }
        if (changed)
        {
            if (Visible && events.Items.Count > 0) events.EnsureVisible(events.Items.Count - 1);
            UpdateCount();
        }
        if (worker != null && !stopping && worker.HasExited)
        {
            worker.WaitForExit();
            if (session != null) { session.Write("Worker exit=" + worker.ExitCode); session.Dispose(); }
            if (!state.Text.StartsWith("HATA:")) state.Text = "Gözlemci sonlandı (kod " + worker.ExitCode + ").";
            worker.Dispose(); worker = null;
            tray.Text = "Discord DPI — durdu";
            start.Enabled = engine.Enabled = true; stop.Enabled = false;
        }
    }
    private void UpdateCount() { count.Text = eventCount + " olay  •  son 500 satır"; }
    private async Task StopObserver()
    {
        if (worker == null || stopping) return;
        stopping = true;
        stop.Enabled = start.Enabled = engine.Enabled = false;
        var active = worker;
        try
        {
            if (!active.HasExited)
            {
                try { active.StandardInput.WriteLine("stop"); active.StandardInput.Flush(); }
                catch (IOException) { }
                await Task.Run(delegate
                {
                    if (!active.WaitForExit(10000)) throw new TimeoutException("Motor hâlâ kapanıyor. Kuyruktaki paketlerin boşalması bekleniyor; tekrar Durdur'a basabilirsin.");
                });
            }
            state.Text = "Durduruldu. Sistem DNS ve bağlantı ayarları değiştirilmedi.";
        }
        catch (Exception ex) { state.Text = "Durdurma hatası: " + ex.Message; }
        finally
        {
            if (active.HasExited)
            {
                active.WaitForExit();
                if (session != null) { session.Write("Worker exit=" + active.ExitCode); session.Dispose(); }
                active.Dispose(); worker = null;
                tray.Text = "Discord DPI — durdu";
            }
            stopping = false;
            start.Enabled = worker == null;
            engine.Enabled = worker == null;
            stop.Enabled = worker != null;
            if (closing && worker == null) BeginInvoke(new Action(Close));
            else if (closing && worker != null) closing = false;
        }
    }
}
