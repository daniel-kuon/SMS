using Microsoft.Win32;

namespace Sms.Companion;

/// <summary>Tray icon + background loops. Starts minimised; status and settings windows on demand.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly string dataDir;
    private readonly string? simulateFile;
    private readonly double simulateRate;
    private readonly NotifyIcon icon;
    private Settings settings;
    private Companion companion = null!;
    private CancellationTokenSource cts = null!;
    private StatusForm? status;

    public TrayContext(Settings settings, string dataDir, string? simulateFile, double simulateRate)
    {
        this.settings = settings;
        this.dataDir = dataDir;
        this.simulateFile = simulateFile;
        this.simulateRate = simulateRate;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Status…", null, (_, _) => ShowStatus());
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Send now", null, async (_, _) => await companion.FlushAsync(cts.Token));
        menu.Items.Add("Upload file…", null, async (_, _) => await UploadFile());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        icon = new NotifyIcon { Icon = AppIcon(), Text = "SMS Companion", Visible = true, ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => ShowStatus();
        Start();
        if (string.IsNullOrWhiteSpace(settings.DeviceToken))
        {
            icon.ShowBalloonTip(5000, "SMS Companion", "No device token yet: open the settings.", ToolTipIcon.Info);
            ShowSettings();
        }
    }

    private void Start()
    {
        cts = new CancellationTokenSource();
        companion = new Companion(settings, dataDir);
        var ct = cts.Token;
        if (simulateFile is not null) _ = Guard(() => companion.SimulateAsync(simulateFile, simulateRate, ct), "simulator");
        else _ = Guard(() => companion.RunSourcesAsync(ct), "sources");
        _ = Guard(() => companion.RunSenderAsync(ct), "sender");
        _ = Guard(() => companion.RunFolderWatchAsync(ct), "folder watch");
        companion.Log($"Started{(simulateFile is null ? "" : $" (simulating {Path.GetFileName(simulateFile)})")}, data in {dataDir}");
    }

    private async Task Guard(Func<Task> loop, string name)
    {
        try { await Task.Run(loop); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { companion.Log($"{name} stopped: {ex.Message}"); }
    }

    private void Restart()
    {
        cts.Cancel();
        companion.Dispose();
        Start();
    }

    private void ShowStatus()
    {
        if (status is { IsDisposed: false }) { status.Activate(); return; }
        status = new StatusForm(() => companion, () => cts.Token, ShowSettings, UploadFile);
        status.Show();
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(settings.Clone());
        if (form.ShowDialog() != DialogResult.OK) return;
        settings = form.Result;
        settings.Save(dataDir);
        try { SetAutostart(settings.Autostart); }
        catch (Exception ex) { MessageBox.Show($"Autostart: {ex.Message}", "SMS Companion"); }
        Restart();
    }

    private async Task UploadFile()
    {
        using var dlg = new OpenFileDialog { Filter = "Tracks and logs|*.gpx;*.kml;*.kmz;*.nmea;*.log;*.txt;*.zip;*.gz;*.7z|All files|*.*" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        var fi = new FileInfo(dlg.FileName);
        var entry = await companion.UploadAsync(fi.FullName, fi.Length, fi.LastWriteTimeUtc, cts.Token);
        icon.ShowBalloonTip(3000, "SMS Companion", entry is null ? companion.LastError ?? "Identical file was already uploaded." : $"{fi.Name} uploaded.", ToolTipIcon.Info);
        if (entry is not null) await companion.FollowImportAsync(entry, cts.Token);
    }

    private static void SetAutostart(bool on)
    {
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (on) run.SetValue("SmsCompanion", $"\"{Environment.ProcessPath}\"");
        else run.DeleteValue("SmsCompanion", throwOnMissingValue: false);
    }

    /// <summary>A small blue sail on white, drawn at runtime (no icon file to ship).</summary>
    private static Icon AppIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillEllipse(Brushes.White, 1, 1, 30, 30);
            g.FillPolygon(new SolidBrush(Color.FromArgb(0, 90, 170)), [new Point(16, 4), new Point(16, 22), new Point(6, 22)]);
            g.FillPolygon(new SolidBrush(Color.FromArgb(0, 140, 210)), [new Point(18, 8), new Point(18, 22), new Point(25, 22)]);
            g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 60)), 6, 24, 20, 3);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        cts.Cancel();
        icon.Visible = false;
        icon.Dispose();
        companion.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class StatusForm : Form
{
    public StatusForm(Func<Companion> companion, Func<CancellationToken> ct, Action settings, Func<Task> upload)
    {
        Text = "SMS Companion – Status";
        Size = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;
        var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        Button B(string caption, EventHandler click) { var b = new Button { Text = caption, AutoSize = true }; b.Click += click; buttons.Controls.Add(b); return b; }
        B("Close", (_, _) => Close());
        B("Settings…", (_, _) => settings());
        B("Upload file…", async (_, _) => await upload());
        B("Send now", async (_, _) => await companion().FlushAsync(ct()));
        Controls.Add(text);
        Controls.Add(buttons);
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        void Refresh() { var t = companion().StatusText(); if (t != text.Text) text.Text = t; }
        timer.Tick += (_, _) => Refresh();
        Load += (_, _) => { Refresh(); timer.Start(); };
        FormClosed += (_, _) => timer.Dispose();
    }
}

internal sealed class SettingsForm : Form
{
    public Settings Result { get; }

    public SettingsForm(Settings settings)
    {
        Result = settings;
        Text = "SMS Companion – Settings";
        Size = new Size(620, 600);
        StartPosition = FormStartPosition.CenterScreen;
        var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = settings, PropertySort = PropertySort.Categorized, ToolbarVisible = false };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        Button B(string caption, EventHandler click) { var b = new Button { Text = caption, AutoSize = true }; b.Click += click; buttons.Controls.Add(b); return b; }
        CancelButton = B("Cancel", (_, _) => DialogResult = DialogResult.Cancel);
        AcceptButton = B("Save", (_, _) =>
        {
            if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http")) { MessageBox.Show("Server URL must be an http(s) URL."); return; }
            if (settings.SendIntervalSeconds < 5) { MessageBox.Show("Send interval must be at least 5 s."); return; }
            DialogResult = DialogResult.OK;
        });
        B("TimeZero preset", (_, _) =>
        {
            var folder = settings.ApplyTimeZeroPreset();
            grid.Refresh();
            MessageBox.Show($"Added a UDP source on port 10110 and the watch folder\n{folder}\n\n{Settings.TimeZeroHelp}", "TimeZero");
        });
        Controls.Add(grid);
        Controls.Add(buttons);
    }
}
