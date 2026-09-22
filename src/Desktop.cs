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
    private readonly ListView events = new ListView();
    private readonly Timer timer = new Timer();
    private readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();
    private Process worker;
    private bool stopping;
    private bool closing;
    private int eventCount;
    private int ticks;

    public ObserverWindow()
    {
        Text = "Discord DPI — Bağlantı Gözlemcisi";
        ClientSize = new Size(960, 650);
        MinimumSize = new Size(850, 600);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(18, 22, 32);
        ForeColor = Color.FromArgb(229, 234, 244);
        Font = new Font("Segoe UI", 10);

        var title = LabelAt("Discord DPI", 28, 23, 450, 42);
        title.Font = new Font("Segoe UI", 23, FontStyle.Bold);
        LabelAt("BAĞLANTI GÖZLEMCİSİ  /  İLK AŞAMA", 30, 70, 600, 26).ForeColor = Color.FromArgb(153, 167, 195);
        discord.SetBounds(30, 116, 650, 28);
        discord.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        state.SetBounds(30, 151, 880, 45);
        state.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        state.Text = "Hazır. İzlemeyi başlatınca yeni Discord bağlantıları burada görünecek.";
        Controls.AddRange(new Control[] { discord, state });

        ConfigureButton(start, "İzlemeyi başlat", 30, 209, 195);
        start.BackColor = Color.FromArgb(88, 101, 242);
        start.Click += delegate { StartObserver(); };
        ConfigureButton(stop, "Durdur", 239, 209, 130);
        stop.Enabled = false;
        stop.Click += async delegate { await StopObserver(); };
        var clear = new Button();
        ConfigureButton(clear, "Listeyi temizle", 383, 209, 165);
        clear.Click += delegate { events.Items.Clear(); eventCount = 0; UpdateCount(); };
        count.SetBounds(575, 219, 350, 25);
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
        var hint = LabelAt("İzlemeyi başlat, ardından Discord’da bir sesli kanala gir veya yeni bağlantı oluştur.\nÖnceden açık bağlantılar ve her mesaj ayrı bir satır olarak görünmez.", 30, 551, 900, 46);
        hint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        var note = LabelAt("Yalnızca gözlem • Paket değiştirme ve engel aşma henüz yok • GoodbyeDPI açık kalabilir", 30, 609, 900, 25);
        note.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        note.ForeColor = Color.FromArgb(153, 167, 195);

        timer.Interval = 250;
        timer.Tick += delegate { Drain(); if (++ticks % 8 == 0) RefreshDiscord(); };
        RefreshDiscord();
        UpdateCount();
        timer.Start();
        FormClosing += async delegate(object sender, FormClosingEventArgs e)
        {
            if (worker == null) return;
            e.Cancel = true;
            if (closing) return;
            closing = true;
            await StopObserver();
        };
        FormClosed += delegate { timer.Dispose(); };
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
    private void StartObserver()
    {
        if (worker != null || stopping) return;
        try
        {
            var info = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(typeof(ObserverWindow).Assembly.Location), "DiscordDpi.exe"), "--observe");
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
            stop.Enabled = true;
            state.Text = "Gözlemci başlatılıyor…";
        }
        catch (Exception ex)
        {
            if (worker != null) { worker.Dispose(); worker = null; }
            state.Text = "Başlatılamadı: " + ex.Message;
        }
    }
    private void Enqueue(string line)
    {
        if (line == null) return;
        if (pending.Count < 1000) pending.Enqueue(line);
    }
    private void Drain()
    {
        string line;
        for (int i = 0; i < 100 && pending.TryDequeue(out line); i++)
        {
            if (line.StartsWith("HATA:")) state.Text = line;
            else if (line.StartsWith("Yeni Discord")) state.Text = "İzleme açık — yeni Discord bağlantıları bekleniyor.";
            else if (line.Length > 8 && line[2] == ':' && line[5] == ':')
            {
                events.Items.Add(new ListViewItem(new[] { line.Substring(0, 8), line.Substring(9) }));
                eventCount++;
                if (events.Items.Count > 500) events.Items.RemoveAt(0);
                events.EnsureVisible(events.Items.Count - 1);
                UpdateCount();
            }
        }
        if (worker != null && !stopping && worker.HasExited)
        {
            worker.WaitForExit();
            if (!state.Text.StartsWith("HATA:")) state.Text = "Gözlemci sonlandı (kod " + worker.ExitCode + ").";
            worker.Dispose(); worker = null;
            start.Enabled = true; stop.Enabled = false;
        }
    }
    private void UpdateCount() { count.Text = eventCount + " olay  •  son 500 satır"; }
    private async Task StopObserver()
    {
        if (worker == null || stopping) return;
        stopping = true;
        stop.Enabled = start.Enabled = false;
        var active = worker;
        try
        {
            if (!active.HasExited)
            {
                try { active.StandardInput.WriteLine("stop"); active.StandardInput.Flush(); }
                catch (IOException) { }
                await Task.Run(delegate
                {
                    if (!active.WaitForExit(3000))
                    {
                        try { active.Kill(); } catch (InvalidOperationException) { }
                        active.WaitForExit();
                    }
                });
            }
            state.Text = "İzleme durduruldu. Bağlantı ayarları değiştirilmedi.";
        }
        catch (Exception ex) { state.Text = "Durdurma hatası: " + ex.Message; }
        finally
        {
            if (active.HasExited) { active.Dispose(); worker = null; }
            stopping = false;
            start.Enabled = worker == null;
            stop.Enabled = worker != null;
            if (closing && worker == null) BeginInvoke(new Action(Close));
            else if (closing && worker != null) closing = false;
        }
    }
}
