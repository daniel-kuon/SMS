using System.Globalization;
using System.Text.Json;
using Sms.Companion.Nmea;

namespace Sms.Companion.Tests;

public sealed class CompanionTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("sms-companion-tests").FullName;
    public void Dispose() => Directory.Delete(dir, recursive: true);

    private static PointDto P(int minute, double? depth = null) => new(new DateTime(2026, 10, 30, 12, minute, 0, DateTimeKind.Utc), 54.5, 10.25 + minute * 0.01, Sog: 5, DepthM: depth);

    private static string Rmc(DateTime t, double lat, double lon) =>
        NmeaParser.Sentence(string.Create(CultureInfo.InvariantCulture, $"GPRMC,{t:HHmmss},A,{(int)lat:00}{(lat % 1) * 60:00.0000},N,{(int)lon:000}{(lon % 1) * 60:00.0000},E,5.0,90.0,{t:ddMMyy},,,A"));

    [Fact]
    public void Batch_serialisation_matches_the_ingest_contract()
    {
        var p = new PointDto(new DateTime(2026, 10, 30, 12, 0, 5, DateTimeKind.Utc), 54.5, 10.25, 5.1, 90, 6, 92, 12.3, 15, 250, 18, 40, 1013.2, 14.5, 12);
        var json = JsonSerializer.Serialize(new PointsBatch("tok", [p, new PointDto(p.T, 1, 2)]), Wire.Json);
        Assert.Equal(
            """{"deviceToken":"tok","points":[{"t":"2026-10-30T12:00:05Z","lat":54.5,"lon":10.25,"sog":5.1,"cog":90,"acc":6,"hdg":92,"depthM":12.3,"twsKn":15,"twdDeg":250,"awsKn":18,"awaDeg":40,"pressureHpa":1013.2,"airTempC":14.5,"waterTempC":12},{"t":"2026-10-30T12:00:05Z","lat":1,"lon":2}]}""",
            json);
        // the portal binds it to PointsBatch(string DeviceToken, List<PointInput>) with PointInput(DateTimeOffset T, double Lat, double Lon, double? Sog, double? Cog, double? Acc)
        var server = JsonSerializer.Deserialize<ServerBatch>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("tok", server.DeviceToken);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 12, 0, 5, TimeSpan.Zero), server.Points[0].T);
        Assert.Equal((54.5, 10.25, 5.1, 90.0, 6.0), (server.Points[0].Lat, server.Points[0].Lon, server.Points[0].Sog!.Value, server.Points[0].Cog!.Value, server.Points[0].Acc!.Value));
        Assert.Null(server.Points[1].Sog);
    }

    private record ServerBatch(string DeviceToken, List<ServerPoint> Points);
    private record ServerPoint(DateTimeOffset T, double Lat, double Lon, double? Sog, double? Cog, double? Acc);

    [Fact]
    public void Offline_queue_keeps_order_across_restarts()
    {
        var path = Path.Combine(dir, "queue.jsonl");
        var q = new OfflineQueue(path);
        for (var i = 0; i < 5; i++) q.Enqueue(P(i, depth: i));
        q.Remove(2);
        q.Enqueue(P(9));
        var reloaded = new OfflineQueue(path);
        Assert.Equal(4, reloaded.Count);
        Assert.Equal([2, 3, 4, 9], reloaded.Peek(10).Select(p => p.T.Minute));
        Assert.Equal(2.0, reloaded.Peek(1)[0].DepthM);
        reloaded.Remove(10);
        Assert.Equal(0, new OfflineQueue(path).Count);
    }

    [Fact]
    public void One_point_per_interval_by_fix_time_and_minimum_movement()
    {
        using var c = new Companion(new Settings { SendIntervalSeconds = 60, MinMovementM = 20 }, dir);
        var t0 = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        for (var s = 0; s < 300; s++) c.OnLine(Rmc(t0.AddSeconds(s), 54.5, 10.25 + s * 0.0001)); // ~6.5 m/s
        Assert.Equal([0, 60, 120, 180, 240], c.Queue.Peek(100).Select(p => (int)(p.T - t0).TotalSeconds));

        using var anchored = new Companion(new Settings { SendIntervalSeconds = 60, MinMovementM = 20 }, Path.Combine(dir, "b"));
        for (var s = 0; s < 1200; s += 10) anchored.OnLine(Rmc(t0.AddSeconds(s), 54.5, 10.25));
        Assert.Equal([0, 900], anchored.Queue.Peek(100).Select(p => (int)(p.T - t0).TotalSeconds)); // only the 15-minute heartbeat
    }

    [Fact]
    public void Bad_checksums_are_counted_not_used()
    {
        using var c = new Companion(new Settings(), dir);
        c.OnLine("$GPRMC,120000,A,5430.000,N,01015.000,E,5.0,90.0,301026,,,A*00");
        Assert.Equal(1, c.BadLines);
        Assert.Null(c.Nav.Position);
    }

    [Fact]
    public async Task Watched_file_is_hashed_once_and_identical_copies_are_skipped()
    {
        using var c = new Companion(new Settings { DeviceToken = "x", ServerUrl = "http://127.0.0.1:9" }, dir);
        var a = Path.Combine(dir, "a.gpx");
        await File.WriteAllTextAsync(a, "<gpx/>", TestContext.Current.CancellationToken);
        c.Uploads.Add(new UploadEntry(Path.Combine(dir, "old", "a.gpx"), 6, DateTime.UtcNow, UploadStore.Sha256(a), "imp-1", DateTime.Now));

        var t = DateTime.UtcNow;
        Assert.False(await c.CheckFileAsync(a, t, TestContext.Current.CancellationToken));              // first sight: wait for a stable size
        Assert.False(await c.CheckFileAsync(a, t.AddSeconds(2), TestContext.Current.CancellationToken)); // stable, but not for 5 s yet
        Assert.False(await c.CheckFileAsync(a, t.AddSeconds(6), TestContext.Current.CancellationToken)); // identical content already uploaded: skipped
        var fi = new FileInfo(a);
        Assert.True(c.Uploads.IsUnchanged(a, fi.Length, fi.LastWriteTimeUtc));          // and remembered by path/size/time

        var reloaded = new UploadStore(Path.Combine(dir, "uploads.json"));
        Assert.True(reloaded.IsUnchanged(a, fi.Length, fi.LastWriteTimeUtc));
        Assert.True(reloaded.HasHash(UploadStore.Sha256(a)));
        Assert.Single(reloaded.Recent(10)); // only real uploads are listed
    }

    [Fact]
    public async Task Changed_file_is_not_uploaded_while_growing()
    {
        using var c = new Companion(new Settings { DeviceToken = "x", ServerUrl = "http://127.0.0.1:9" }, dir);
        var f = Path.Combine(dir, "log.nmea");
        await File.WriteAllTextAsync(f, "$GPRMC", TestContext.Current.CancellationToken);
        var t = DateTime.UtcNow;
        await c.CheckFileAsync(f, t, TestContext.Current.CancellationToken);
        await File.AppendAllTextAsync(f, ",more", TestContext.Current.CancellationToken);
        Assert.False(await c.CheckFileAsync(f, t.AddSeconds(10), TestContext.Current.CancellationToken)); // size changed: the clock restarts
    }

    [Fact]
    public void Gpx_replay_produces_valid_rmc()
    {
        var gpx = Path.Combine(dir, "t.gpx");
        File.WriteAllText(gpx, """
            <?xml version="1.0"?>
            <gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="1" lon="1"><time>2026-01-01T00:00:00Z</time></wpt>
              <trk><trkseg>
                <trkpt lat="54.5" lon="-10.25"><time>2026-08-13T10:00:00Z</time></trkpt>
                <trkpt lat="54.501" lon="-10.25"><time>2026-08-13T10:00:10Z</time></trkpt>
              </trkseg></trk>
            </gpx>
            """);
        var lines = Companion.ReadSimulation(gpx).ToList();
        Assert.Equal(2, lines.Count);
        var nav = new NavState();
        foreach (var l in lines) nav.Apply(NmeaParser.Parse(l)!);
        var p = nav.Snapshot(TimeSpan.FromMinutes(5))!;
        Assert.Equal(new DateTime(2026, 8, 13, 10, 0, 10, DateTimeKind.Utc), p.T);
        Assert.Equal(54.501, p.Lat, 5);
        Assert.Equal(-10.25, p.Lon, 5);
        Assert.Equal(111.2 / 1852 * 360, p.Sog!.Value, 1); // 111 m in 10 s
        Assert.Equal(0, p.Cog!.Value, 0);
    }

    [Fact]
    public void Import_status_of_the_portal()
    {
        var running = JsonDocument.Parse("""{"id":"a","fileName":"x.gpx","status":"Running","progressPercent":40,"parsed":0,"added":0,"error":null}""").RootElement;
        var done = JsonDocument.Parse("""{"id":"a","fileName":"x.gpx","status":"Done","progressPercent":100,"parsed":300,"added":298}""").RootElement;
        Assert.True(Companion.IsPending(running));
        Assert.False(Companion.IsPending(done));
        Assert.Equal("status Done, progressPercent 100, parsed 300, added 298", Companion.DescribeImport(done));
    }

    [Fact]
    public void Settings_round_trip_protects_the_token()
    {
        var s = new Settings { DeviceToken = "secret-token", Sources = [new SourceConfig { Kind = SourceKind.Tcp, Host = "10.0.0.5", Port = 2000 }] };
        s.ApplyTimeZeroPreset(Path.Combine(dir, "TimeZero Export"));
        s.Save(dir);
        var raw = File.ReadAllText(Path.Combine(dir, "settings.json"));
        Assert.DoesNotContain("secret-token", raw);
        var loaded = Settings.Load(dir);
        Assert.Equal("secret-token", loaded.DeviceToken);
        Assert.Equal(2, loaded.Sources.Count);
        Assert.Contains(loaded.Sources, x => x is { Kind: SourceKind.Udp, Port: 10110 });
        Assert.Single(loaded.WatchFolders);
    }
}
