using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Sms.Companion;

internal static class Program
{
    private const string Usage =
        """
        SmsCompanion - sends the boat's position and on-board sensor values to the SMS portal.

          SmsCompanion.exe                         tray app (configured NMEA sources and watch folders)
          SmsCompanion.exe --simulate <file>       tray app fed from an NMEA log or a GPX track instead of the sources
                           [--rate <lines/s>]      replay speed (default 20; 0 = as fast as possible)
          SmsCompanion.exe --simulate <file> --once
                                                   headless: replay the whole file, send one point per interval (by fix time), exit
          SmsCompanion.exe --send-position [--timeout <s>]
                                                   headless: read the configured sources until a fix (default 30 s), send it, exit
          SmsCompanion.exe --upload <file>         headless: upload a track/log file and wait for the import status

        Options (not saved): --server <url>  --token <device token> (or env SMS_DEVICE_TOKEN)  --interval <s>  --data-dir <dir>
        Exit codes: 0 ok, 1 send/upload failed, 2 no fix / bad arguments.
        """;

    [STAThread]
    private static int Main(string[] argv)
    {
        var args = Args.Parse(argv);
        var headless = args.Has("once") || args.Has("send-position") || args.Has("upload") || args.Has("help") || args.Has("h");
        if (headless) AttachParentConsole();
        if (args.Has("help") || args.Has("h")) { Console.WriteLine(Usage); return 0; }

        var dataDir = args.Get("data-dir") ?? Settings.DefaultDir;
        Directory.CreateDirectory(dataDir);
        var settings = Settings.Load(dataDir);
        if (args.Get("server") is { } server) settings.ServerUrl = server;
        if ((args.Get("token") ?? Environment.GetEnvironmentVariable("SMS_DEVICE_TOKEN")) is { Length: > 0 } token) settings.DeviceToken = token;
        if (args.Get("interval") is { } iv && int.TryParse(iv, out var interval)) settings.SendIntervalSeconds = interval;

        if (headless)
        {
            try { return Headless(args, settings, dataDir).GetAwaiter().GetResult(); }
            catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); return 1; }
        }

        using var single = new Mutex(true, "SmsCompanion-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataDir.ToLowerInvariant())))[..16], out var first);
        if (!first) { MessageBox.Show("SMS Companion is already running (see the tray).", "SMS Companion"); return 0; }
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext(settings, dataDir, args.Get("simulate"), args.Double("rate") ?? 20));
        return 0;
    }

    private static async Task<int> Headless(Args args, Settings settings, string dataDir)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var ct = cts.Token;

        if (args.Get("upload") is { } file)
        {
            if (!File.Exists(file)) { Console.Error.WriteLine($"File not found: {file}"); return 2; }
            using var c = new Companion(settings, dataDir);
            var fi = new FileInfo(file);
            var entry = await c.UploadAsync(fi.FullName, fi.Length, fi.LastWriteTimeUtc, ct);
            if (entry is null) { Console.WriteLine(c.LastError ?? "Already uploaded (identical file), nothing to do."); return c.LastError is null ? 0 : 1; }
            Console.WriteLine($"Uploaded {fi.Name}: import {entry.ImportId}");
            await c.FollowImportAsync(entry, ct);
            Console.WriteLine($"Import status: {entry.Status}");
            return entry.Status?.Contains("status Failed", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0;
        }

        if (args.Get("simulate") is { } sim)
        {
            if (!File.Exists(sim)) { Console.Error.WriteLine($"File not found: {sim}"); return 2; }
            using var c = new Companion(settings, dataDir);
            var before = c.Queue.Count;
            await c.SimulateAsync(sim, args.Double("rate") ?? 0, ct);
            Console.WriteLine($"Replayed {c.Nav.Sentences} sentences ({c.Nav.Used} used, {c.BadLines} rejected); queued {c.Queue.Count - before} point(s), {c.Queue.Count} in queue");
            return await SendAndReport(c, ct);
        }

        // --send-position / --once
        {
            using var c = new Companion(settings, dataDir) { AutoQueue = false };
            if (!settings.Sources.Any(s => s.Enabled)) { Console.Error.WriteLine("No NMEA source configured (settings.json)."); return 2; }
            using var sources = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var run = c.RunSourcesAsync(sources.Token);
            var deadline = DateTime.UtcNow.AddSeconds(args.Double("timeout") ?? 30);
            while (c.Nav.Position is null && DateTime.UtcNow < deadline) await Task.Delay(200, ct);
            if (c.Nav.Position is not null) await Task.Delay(3000, ct); // collect heading/wind/depth sentences of the same second(s)
            await sources.CancelAsync();
            try { await run; } catch (OperationCanceledException) { }
            var p = c.QueueCurrent();
            if (p is null) { Console.Error.WriteLine($"No position within the timeout. {string.Join("; ", c.SourceState.Select(x => $"{x.Key}: {x.Value}"))}"); return 2; }
            Console.WriteLine($"Fix {p.T:u} {p.Lat:0.00000} {p.Lon:0.00000}");
            return await SendAndReport(c, ct);
        }
    }

    private static async Task<int> SendAndReport(Companion c, CancellationToken ct)
    {
        var ok = await c.FlushAsync(ct);
        Console.WriteLine(ok ? $"Sent; queue now {c.Queue.Count}" : $"Not sent: {c.LastError} ({c.Queue.Count} queued, will be resent)");
        if (c.LastSent is { } p)
            Console.WriteLine($"On board: depth {p.DepthM} m, TWS {p.TwsKn} kn / TWD {p.TwdDeg}°, AWS {p.AwsKn} kn / AWA {p.AwaDeg}°, {p.PressureHpa} hPa, air {p.AirTempC} °C, water {p.WaterTempC} °C");
        if (ok) Console.WriteLine(c.WeatherText());
        return ok ? 0 : 1;
    }

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);

    /// <summary>A WinExe has no console: write to the calling console unless stdout is already redirected (pipe/file).</summary>
    private static void AttachParentConsole()
    {
        if (GetStdHandle(-11) is var h && h != IntPtr.Zero && h != new IntPtr(-1)) return;
        if (AttachConsole(-1))
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            Console.WriteLine();
        }
    }
}

/// <summary>--name value / --flag command-line options.</summary>
internal sealed class Args
{
    private readonly Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);

    public static Args Parse(string[] argv)
    {
        var a = new Args();
        for (var i = 0; i < argv.Length; i++)
        {
            if (!argv[i].StartsWith('-')) continue;
            var name = argv[i].TrimStart('-');
            a.values[name] = i + 1 < argv.Length && !argv[i + 1].StartsWith("--") ? argv[++i] : null;
        }
        return a;
    }

    public bool Has(string name) => values.ContainsKey(name);
    public string? Get(string name) => values.GetValueOrDefault(name);
    public double? Double(string name) => double.TryParse(Get(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
}
