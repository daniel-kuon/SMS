using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml;
using Sms.Companion.Nmea;

namespace Sms.Companion;

/// <summary>
/// The app's engine, shared by the tray UI and the headless command line: NMEA sources feed <see cref="OnLine"/>, one point per
/// send interval goes into the offline queue, a timer flushes the queue to the portal, watch folders upload new files.
/// </summary>
public sealed class Companion : IDisposable
{
    public static readonly TimeSpan SensorMaxAge = TimeSpan.FromMinutes(2);
    // ponytail: fixed heartbeat while at anchor/in harbour; make it a setting if anyone asks
    public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(15);

    public Settings Settings { get; }
    public string DataDir { get; }
    public OfflineQueue Queue { get; }
    public UploadStore Uploads { get; }
    public NavState Nav { get; } = new();

    public DateTime? LastSentAt { get; private set; }
    public PointDto? LastSent { get; private set; }
    public string? LastError { get; private set; }
    public JsonElement? Weather { get; private set; }
    public DateTime? WeatherAt { get; private set; }
    public ConcurrentDictionary<string, string> SourceState { get; } = new();
    public long BadLines;
    /// <summary>Queue a point per interval from the stream (off for --send-position, which queues one snapshot itself).</summary>
    public bool AutoQueue { get; init; } = true;

    private readonly PortalClient portal;
    private readonly ConcurrentQueue<string> log = new();
    private readonly SemaphoreSlim flushGate = new(1, 1);
    private readonly ConcurrentDictionary<string, (long Size, DateTime Write, DateTime Seen)> pendingFiles = new(StringComparer.OrdinalIgnoreCase);
    private PointDto? lastQueued;
    private (double Lat, double Lon)? weatherPos;

    public Companion(Settings settings, string dataDir)
    {
        Settings = settings;
        DataDir = dataDir;
        Queue = new OfflineQueue(Path.Combine(dataDir, "queue.jsonl"));
        Uploads = new UploadStore(Path.Combine(dataDir, "uploads.json"));
        portal = new PortalClient(settings.ServerUrl);
    }

    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(1, Settings.SendIntervalSeconds));

    public void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {message}";
        log.Enqueue(line);
        while (log.Count > 200) log.TryDequeue(out _);
        try { File.AppendAllText(Path.Combine(DataDir, "companion.log"), $"{DateTime.Now:yyyy-MM-dd} {line}{Environment.NewLine}"); } catch (IOException) { }
    }

    public IEnumerable<string> RecentLog(int n) => log.TakeLast(n);

    // ---------------------------------------------------------------- NMEA in, queue out

    /// <summary>One NMEA line from any source. Queues a point when the fix is at least one interval newer than the last queued one.</summary>
    public void OnLine(string line)
    {
        lock (Nav)
        {
            if (NmeaParser.Parse(line) is not { } s) { if (line.Contains('$')) BadLines++; return; }
            if (!Nav.Apply(s) || !AutoQueue || Nav.Snapshot(SensorMaxAge) is not { } p) return;
            if (lastQueued is { } last)
            {
                var dt = p.T - last.T;
                if (dt < Interval && dt > -TimeSpan.FromHours(1)) return; // a jump back in time (replayed file, GPS reset) starts over
                if (dt < Heartbeat && dt > TimeSpan.Zero && Distance(last.Lat, last.Lon, p.Lat, p.Lon) < Settings.MinMovementM) return;
            }
            Queue.Enqueue(p);
            lastQueued = p;
        }
    }

    /// <summary>Queues the current snapshot now (--send-position).</summary>
    public PointDto? QueueCurrent()
    {
        lock (Nav)
        {
            if (Nav.Snapshot(SensorMaxAge) is not { } p) return null;
            Queue.Enqueue(p);
            return lastQueued = p;
        }
    }

    /// <summary>Sends the queue oldest first in batches of 500. Stops at the first failure; the points stay queued.</summary>
    public async Task<bool> FlushAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Settings.DeviceToken)) { LastError = "No device token configured."; return false; }
        if (!await flushGate.WaitAsync(0, ct)) return false;
        try
        {
            var sent = 0;
            while (Queue.Count > 0)
            {
                var batch = Queue.Peek(500);
                try
                {
                    var added = await portal.PostPointsAsync(Settings.DeviceToken, batch, ct);
                    Queue.Remove(batch.Count);
                    sent += batch.Count;
                    LastSent = batch[^1];
                    LastSentAt = DateTime.Now;
                    LastError = null;
                    Log($"Sent {batch.Count} point(s){(added is { } a ? $", {a} new" : "")}, last {LastSent.T:u} {LastSent.Lat:0.00000} {LastSent.Lon:0.00000}");
                }
                catch (PortalException ex) when (ex.Status is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
                {
                    // the portal will never take this batch: park it instead of blocking the queue forever
                    File.AppendAllLines(Path.Combine(DataDir, "rejected.jsonl"), batch.Select(p => JsonSerializer.Serialize(p, Wire.Json)));
                    Queue.Remove(batch.Count);
                    LastError = $"Batch rejected, moved to rejected.jsonl: {ex.Message}";
                    Log(LastError);
                }
                catch (Exception ex) when (ex is PortalException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    LastError = $"Send failed ({Queue.Count} queued): {ex.Message}";
                    Log(LastError);
                    return false;
                }
            }
            if (sent > 0 && LastSent is { } last) await RefreshWeatherAsync(last, ct);
            return true;
        }
        finally { flushGate.Release(); }
    }

    /// <summary>Sends queued points within 5 s (so the first fix goes out at once); after a failure it retries every 30 s.</summary>
    public async Task RunSenderAsync(CancellationToken ct)
    {
        var retryAt = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            if (Queue.Count > 0 && DateTime.UtcNow >= retryAt && !await FlushAsync(ct)) retryAt = DateTime.UtcNow.AddSeconds(30);
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>The portal records model weather for live positions itself; this only shows it. Fetched at most every 10 min or after 5 km.</summary>
    private async Task RefreshWeatherAsync(PointDto p, CancellationToken ct)
    {
        if (WeatherAt is { } at && DateTime.Now - at < TimeSpan.FromMinutes(10) && weatherPos is { } wp && Distance(wp.Lat, wp.Lon, p.Lat, p.Lon) < 5000) return;
        try
        {
            Weather = await portal.WeatherAsync(p.Lat, p.Lon, ct);
            WeatherAt = DateTime.Now;
            weatherPos = (p.Lat, p.Lon);
        }
        catch (Exception ex) when (ex is PortalException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            Log($"Weather: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- sources

    /// <summary>Runs every enabled source until cancelled (reconnecting after errors).</summary>
    public Task RunSourcesAsync(CancellationToken ct) =>
        Task.WhenAll(Settings.Sources.Where(s => s.Enabled).Select(s => Task.Run(() => RunSourceAsync(s, ct), ct)));

    private async Task RunSourceAsync(SourceConfig cfg, CancellationToken ct)
    {
        var name = cfg.ToString();
        while (!ct.IsCancellationRequested)
        {
            long lines = 0;
            void Line(string l) { lines++; OnLine(l); if (lines % 50 == 1) SourceState[name] = $"receiving ({lines} lines, {DateTime.Now:HH:mm:ss})"; }
            try
            {
                SourceState[name] = "connecting";
                switch (cfg.Kind)
                {
                    case SourceKind.Udp:
                    {
                        using var udp = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = false };
                        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); // share the port with other NMEA apps
                        udp.Client.Bind(new IPEndPoint(IPAddress.Any, cfg.Port));
                        SourceState[name] = "listening";
                        while (!ct.IsCancellationRequested)
                        {
                            var r = await udp.ReceiveAsync(ct);
                            foreach (var l in Encoding.ASCII.GetString(r.Buffer).Split('\n', StringSplitOptions.RemoveEmptyEntries)) Line(l);
                        }
                        break;
                    }
                    case SourceKind.Tcp:
                    {
                        using var tcp = new TcpClient();
                        await tcp.ConnectAsync(cfg.Host, cfg.Port, ct);
                        SourceState[name] = "connected";
                        using var reader = new StreamReader(tcp.GetStream(), Encoding.ASCII);
                        using var silence = CancellationTokenSource.CreateLinkedTokenSource(ct); // a vanished multiplexer (Wi-Fi) never closes the socket
                        silence.CancelAfter(TimeSpan.FromSeconds(60));
                        try
                        {
                            while (await reader.ReadLineAsync(silence.Token) is { } l) { Line(l); silence.CancelAfter(TimeSpan.FromSeconds(60)); }
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("no data for 60 s"); }
                        throw new IOException("connection closed");
                    }
                    case SourceKind.Serial:
                    {
                        using var port = new SerialPort(cfg.ComPort, cfg.Baud) { ReadTimeout = 30_000, NewLine = "\n", Encoding = Encoding.ASCII };
                        port.Open();
                        SourceState[name] = "open";
                        using var reg = ct.Register(port.Close);
                        await Task.Run(() => { while (!ct.IsCancellationRequested) Line(port.ReadLine()); }, ct);
                        break;
                    }
                }
            }
            catch (Exception) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                SourceState[name] = $"error: {ex.Message} (retry in 10 s)";
                Log($"{name}: {ex.Message}");
                try { await Task.Delay(10_000, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Simulator: feeds an NMEA log (or a GPX track as RMC sentences) into the pipeline. linesPerSecond 0 = as fast as possible.</summary>
    public async Task SimulateAsync(string file, double linesPerSecond, CancellationToken ct)
    {
        var name = $"Simulator {Path.GetFileName(file)}";
        long n = 0;
        var delay = linesPerSecond > 0 ? TimeSpan.FromSeconds(1 / linesPerSecond) : TimeSpan.Zero;
        foreach (var line in ReadSimulation(file))
        {
            OnLine(line);
            if (++n % 100 == 1) SourceState[name] = $"replaying ({n} lines)";
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            else ct.ThrowIfCancellationRequested();
        }
        SourceState[name] = $"done ({n} lines)";
        Log($"{name}: {n} lines replayed");
    }

    public static IEnumerable<string> ReadSimulation(string file) =>
        Path.GetExtension(file).Equals(".gpx", StringComparison.OrdinalIgnoreCase) ? GpxAsRmc(file) : File.ReadLines(file);

    /// <summary>Streams a GPX file's track points (any size) as $GPRMC sentences; speed/course from the points or derived from the previous one.</summary>
    public static IEnumerable<string> GpxAsRmc(string file)
    {
        using var reader = XmlReader.Create(file, new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
        (DateTime T, double Lat, double Lon)? prev = null;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "trkpt") continue;
            var lat = double.Parse(reader.GetAttribute("lat")!, CultureInfo.InvariantCulture);
            var lon = double.Parse(reader.GetAttribute("lon")!, CultureInfo.InvariantCulture);
            DateTime? t = null;
            using (var pt = reader.ReadSubtree())
                while (pt.Read())
                    if (pt.NodeType == XmlNodeType.Element && pt.LocalName == "time")
                        t = DateTime.Parse(pt.ReadElementContentAsString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            if (t is not { } time) continue;
            double sog = 0, cog = 0;
            if (prev is { } p && time > p.T)
            {
                sog = Distance(p.Lat, p.Lon, lat, lon) / 1852 / (time - p.T).TotalHours;
                cog = Bearing(p.Lat, p.Lon, lat, lon);
            }
            prev = (time, lat, lon);
            yield return NmeaParser.Sentence(string.Create(CultureInfo.InvariantCulture,
                $"GPRMC,{time:HHmmss.ff},A,{Dm(lat, 2)},{(lat >= 0 ? 'N' : 'S')},{Dm(lon, 3)},{(lon >= 0 ? 'E' : 'W')},{sog:0.0},{cog:0.0},{time:ddMMyy},,,A"));
        }
    }

    private static string Dm(double deg, int width)
    {
        var a = Math.Abs(deg);
        var d = (int)a;
        return string.Create(CultureInfo.InvariantCulture, $"{d.ToString(new string('0', width))}{(a - d) * 60:00.00000}");
    }

    // ---------------------------------------------------------------- folder watch

    /// <summary>Polls the watch folders every 5 s (more robust than FileSystemWatcher on network shares and with apps that write in bursts).</summary>
    public async Task RunFolderWatchAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var w in Settings.WatchFolders.Where(w => Directory.Exists(w.Folder)))
                foreach (var file in w.Patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .SelectMany(p => SafeEnumerate(w.Folder, p)).Distinct(StringComparer.OrdinalIgnoreCase))
                    await CheckFileAsync(file, DateTime.UtcNow, ct);
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private IEnumerable<string> SafeEnumerate(string folder, string pattern)
    {
        try { return Directory.GetFiles(folder, pattern); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log($"{folder}: {ex.Message}"); return []; }
    }

    /// <summary>Uploads a file once its size and write time stayed the same for 5 s and it is not open for writing. Returns true when uploaded.</summary>
    public async Task<bool> CheckFileAsync(string file, DateTime nowUtc, CancellationToken ct)
    {
        FileInfo fi;
        try { fi = new FileInfo(file); if (!fi.Exists || fi.Length == 0) return false; } catch (IOException) { return false; }
        if (Uploads.IsUnchanged(file, fi.Length, fi.LastWriteTimeUtc)) return false;
        if (!pendingFiles.TryGetValue(file, out var seen) || seen.Size != fi.Length || seen.Write != fi.LastWriteTimeUtc)
        {
            pendingFiles[file] = (fi.Length, fi.LastWriteTimeUtc, nowUtc);
            return false;
        }
        if (nowUtc - seen.Seen < TimeSpan.FromSeconds(5)) return false;
        try { using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) { } } // still being written by another program
        catch (IOException) { return false; }
        pendingFiles.TryRemove(file, out _);
        if (await UploadAsync(file, fi.Length, fi.LastWriteTimeUtc, ct) is not { } entry) return false;
        _ = Task.Run(() => FollowImportAsync(entry, ct), ct);
        return true;
    }

    /// <summary>Uploads one file unless an identical one (SHA-256) was uploaded before.</summary>
    public async Task<UploadEntry?> UploadAsync(string file, long size, DateTime writeUtc, CancellationToken ct)
    {
        var hash = UploadStore.Sha256(file);
        if (Uploads.HasHash(hash))
        {
            Uploads.Add(new UploadEntry(file, size, writeUtc, hash, null, DateTime.Now)); // remember the path so it is not hashed again
            Log($"{Path.GetFileName(file)}: identical file already uploaded, skipped");
            return null;
        }
        if (string.IsNullOrWhiteSpace(Settings.DeviceToken)) { LastError = "No device token configured."; return null; }
        try
        {
            var id = await portal.UploadFileAsync(Settings.DeviceToken, file, ct);
            var entry = new UploadEntry(file, size, writeUtc, hash, id, DateTime.Now) { Status = "uploaded" };
            Uploads.Add(entry);
            Log($"{Path.GetFileName(file)}: uploaded, import {id}");
            return entry;
        }
        catch (Exception ex) when (ex is PortalException or HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            LastError = $"Upload {Path.GetFileName(file)} failed: {ex.Message}";
            Log(LastError);
            return null;
        }
    }

    /// <summary>Polls the import status every 5 s for up to 2 minutes, until it is no longer pending.</summary>
    public async Task FollowImportAsync(UploadEntry entry, CancellationToken ct)
    {
        for (var i = 0; i < 24 && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var s = await portal.ImportStatusAsync(Settings.DeviceToken, entry.ImportId!, ct);
                entry.Status = DescribeImport(s);
                Uploads.Save();
                if (!IsPending(s)) { Log($"{Path.GetFileName(entry.Path)}: {entry.Status}"); return; }
            }
            catch (Exception ex) when (ex is PortalException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { entry.Status = $"status unknown: {ex.Message}"; }
            try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: not JsonValueKind.Undefined } p
            ? p.Value.ToString() : null;

    public static bool IsPending(JsonElement s) => Str(s, "status")?.ToLowerInvariant() is "queued" or "running" or "pending" or "processing";

    private static readonly string[] ImportNoise = ["id", "importId", "fileName", "tripId", "createdAt", "finishedAt"];

    /// <summary>Status, progress, counters and error of TrackImportDto, compact ("status Done, progressPercent 100, parsed 300, added 298, ...").</summary>
    public static string DescribeImport(JsonElement s) =>
        s.ValueKind != JsonValueKind.Object ? s.ToString()
            : string.Join(", ", s.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number && !ImportNoise.Contains(p.Name))
                .Select(p => $"{p.Name} {p.Value}"));

    // ---------------------------------------------------------------- status

    public string StatusText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Server: {Settings.ServerUrl}   token: {(string.IsNullOrWhiteSpace(Settings.DeviceToken) ? "MISSING" : "set")}   interval {Settings.SendIntervalSeconds} s, min. movement {Settings.MinMovementM} m");
        sb.AppendLine();
        sb.AppendLine("Sources:");
        if (SourceState.IsEmpty) sb.AppendLine("  (none configured)");
        foreach (var (k, v) in SourceState.OrderBy(x => x.Key)) sb.AppendLine($"  {k}: {v}");
        sb.AppendLine($"  sentences {Nav.Sentences}, used {Nav.Used}, bad checksum/format {BadLines}{(Nav.LastTalker is { } t ? $", last talker {t}" : "")}");
        sb.AppendLine();
        PointDto? now;
        lock (Nav) now = Nav.Snapshot(SensorMaxAge);
        sb.AppendLine(now is null ? "Position: no fix yet" : $"Position: {now.Lat:0.00000} {now.Lon:0.00000}  {now.T:yyyy-MM-dd HH:mm:ss} UTC  SOG {F(now.Sog, "kn")}  COG {F(now.Cog, "°")}  HDG {F(now.Hdg, "°")}");
        if (now is not null)
            sb.AppendLine($"On board: depth {F(now.DepthM, "m")}  true wind {F(now.TwsKn, "kn")} from {F(now.TwdDeg, "°")}  apparent {F(now.AwsKn, "kn")} at {F(now.AwaDeg, "°")}  " +
                          $"pressure {F(now.PressureHpa, "hPa")}  air {F(now.AirTempC, "°C")}  water {F(now.WaterTempC, "°C")}");
        sb.AppendLine();
        sb.AppendLine($"Last sent: {(LastSentAt is { } ls ? $"{ls:HH:mm:ss} (fix {LastSent!.T:HH:mm:ss} UTC)" : "never")}   queued: {Queue.Count}");
        if (LastError is not null) sb.AppendLine($"Error: {LastError}");
        sb.AppendLine();
        sb.AppendLine(WeatherText());
        var uploads = Uploads.Recent(10);
        if (uploads.Count > 0 || Settings.WatchFolders.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Uploads ({string.Join("; ", Settings.WatchFolders.Select(w => w.Folder))}):");
            foreach (var u in uploads) sb.AppendLine($"  {u.UploadedAt:dd.MM HH:mm} {Path.GetFileName(u.Path)}: {u.Status}");
        }
        sb.AppendLine();
        sb.AppendLine("Log:");
        foreach (var l in RecentLog(15).Reverse()) sb.AppendLine($"  {l}");
        return sb.ToString();
    }

    public string WeatherText()
    {
        if (Weather is not { } w) return "Weather (portal model): none yet (shown after the first successful send)";
        if (!w.TryGetProperty("current", out var c)) return $"Weather: {w}";
        string V(string n, string unit) => c.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? $"{v.GetDouble():0.#} {unit}" : "–";
        var provider = w.TryGetProperty("provider", out var pr) ? pr.GetString() : "?";
        return $"Weather ({provider}, {WeatherAt:HH:mm}): wind {V("windKn", "kn")} from {V("windDirDeg", "°")}, gusts {V("gustKn", "kn")}, {V("temperatureC", "°C")}, " +
               $"{V("pressureHpa", "hPa")}, waves {V("waveHeightM", "m")}, rain {V("precipitationMm", "mm")}";
    }

    private static string F(double? v, string unit) => v is { } x ? $"{x:0.#} {unit}" : "–";

    public static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371008.8;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Sqrt(Math.Min(1, a)));
    }

    private static double Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        var f1 = lat1 * Math.PI / 180; var f2 = lat2 * Math.PI / 180; var dl = (lon2 - lon1) * Math.PI / 180;
        var y = Math.Sin(dl) * Math.Cos(f2);
        var x = Math.Cos(f1) * Math.Sin(f2) - Math.Sin(f1) * Math.Cos(f2) * Math.Cos(dl);
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    public void Dispose() => portal.Dispose();
}
