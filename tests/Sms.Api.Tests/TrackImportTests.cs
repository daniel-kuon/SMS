using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sms.Domain.Tracking;
using Sms.Infrastructure;

namespace Sms.Api.Tests;

/// <summary>Background track import (GPX/zip/gz), idempotence, trip assignment, voyages without a trip, the companion app's device-token upload and sensor values.</summary>
public class TrackImportTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> IdOf(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }
    private static string N(double d) => d.ToString(CultureInfo.InvariantCulture);

    /// <summary>A TimeZero-like export: one "Ownship" track per (start, minutes) sailing north at ~5 kn with fractional seconds, plus an event and a route.</summary>
    private static string Gpx(params (DateTimeOffset Start, double Lat, int Minutes)[] tracks)
    {
        var sb = new StringBuilder("""<?xml version="1.0"?><gpx version="1.1" xmlns="http://www.topografix.com/GPX/1/1">""");
        sb.Append("""<wpt lat="54.3" lon="10.1"><time>2026-06-01T10:25:15Z</time><name>Event 0336</name><extensions><MxTimeZeroSymbol>67</MxTimeZeroSymbol></extensions></wpt>""");
        sb.Append("""<rte><name>Rte</name><rtept lat="54.3" lon="10.1"><extensions><T0StepSide>Über</T0StepSide></extensions></rtept></rte>""");
        foreach (var (start, lat, minutes) in tracks)
        {
            sb.Append($"<trk><name>Ownship</name><extensions><guid>{Guid.NewGuid()}</guid></extensions><trkseg>");
            for (var s = 0; s <= minutes * 60; s += 10)
                sb.Append($"""<trkpt lat="{N(lat + s * 0.0000231)}" lon="10.2"><time>{start.AddSeconds(s).AddTicks(4106144):yyyy-MM-ddTHH:mm:ss.fffffffZ}</time></trkpt>""");
            sb.Append("</trkseg></trk>");
        }
        return sb.Append("</gpx>").ToString();
    }

    private static MultipartFormDataContent Form(string fileName, byte[] bytes, Guid? tripId = null)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes); content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "files", fileName);
        if (tripId is { } t) form.Add(new StringContent(t.ToString()), "tripId");
        return form;
    }

    private static async Task<JsonElement> WaitAsync(Func<Task<JsonElement>> get)
    {
        for (var i = 0; i < 300; i++)
        {
            var s = await get();
            if (s.GetProperty("status").GetString() is "Done" or "Failed") return s;
            await Task.Delay(100);
        }
        throw new TimeoutException("import did not finish");
    }

    private static byte[] Zip(string name, string text)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true)) { using var w = new StreamWriter(zip.CreateEntry(name).Open()); w.Write(text); }
        return ms.ToArray();
    }

    private static byte[] Gz(string text)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress, true)) gz.Write(Encoding.UTF8.GetBytes(text));
        return ms.ToArray();
    }

    [Fact]
    public async Task Archive_import_runs_in_the_background_assigns_trips_is_idempotent_and_leaves_voyages_to_adopt()
    {
        var (owner, _) = await f.UserAsync();
        var ship = await IdOf(await owner.PostAsJsonAsync("/api/ships", new { name = "Import Boat", cruisingSpeedKn = 5, visibility = "Public" }));
        var trip = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/trips", new { name = "July", startDate = "2026-07-10", endDate = "2026-07-11", visibility = "Public", status = "Completed", trackVisibility = "Public", publicPositionDelayMinutes = 0 }));
        var d = (int day, int h) => new DateTimeOffset(2026, 6, day, h, 0, 0, TimeSpan.Zero);
        var gpx = Gpx((new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero), 54.3, 60), (d(1, 8), 54.4, 60), (d(3, 9), 54.6, 90));
        var perTrack = (new[] { 60, 60, 90 }).Select(m => m * 6 + 1).ToArray();

        var r = await owner.PostAsync($"/api/ships/{ship}/track-imports", Form("export.zip", Zip("tz/export.gpx", gpx)));
        r.EnsureSuccessStatusCode();
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetGuid();
        var s = await WaitAsync(() => owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/track-imports/{id}"));
        Assert.Equal("Done", s.GetProperty("status").GetString());
        Assert.Equal(perTrack.Sum(), s.GetProperty("parsed").GetInt32());
        Assert.Equal(perTrack.Sum(), s.GetProperty("added").GetInt32());
        Assert.Equal((3, 1, 1), (s.GetProperty("tracks").GetInt32(), s.GetProperty("waypoints").GetInt32(), s.GetProperty("routes").GetInt32()));
        Assert.Equal(100, s.GetProperty("progressPercent").GetInt32());

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
            Assert.Equal(perTrack[0], await db.TrackPoints.CountAsync(p => p.ShipId == ship && p.TripId == trip));
            Assert.Equal(perTrack[1] + perTrack[2], await db.TrackPoints.CountAsync(p => p.ShipId == ship && p.TripId == null && p.Source == TrackSource.GpsFile));
        }
        var track = await owner.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/track");
        Assert.Contains(track.GetProperty("features").EnumerateArray(), x => x.GetProperty("properties").GetProperty("source").GetString() == "GpsFile");

        // the same data again (gzipped this time): nothing new
        var again = (await (await owner.PostAsync($"/api/ships/{ship}/track-imports", Form("export.gpx.gz", Gz(gpx)))).Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetGuid();
        s = await WaitAsync(() => owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/track-imports/{again}"));
        Assert.Equal(0, s.GetProperty("added").GetInt32());
        Assert.Equal(perTrack.Sum(), s.GetProperty("duplicates").GetInt32());
        Assert.Equal(2, (await owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/track-imports")).GetArrayLength());

        // unsupported files are refused up front; strangers see nothing
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/ships/{ship}/track-imports", Form("notes.pdf", [1, 2, 3]))).StatusCode);
        var (stranger, _) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/ships/{ship}/track-imports/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/ships/{ship}/voyages")).StatusCode);

        // the two June tracks are voyages without a trip, newest first
        var voyages = await owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/voyages");
        Assert.Equal(2, voyages.GetProperty("total").GetInt32());
        var newest = voyages.GetProperty("items")[0];
        Assert.Equal(d(3, 9), newest.GetProperty("from").GetDateTimeOffset(), TimeSpan.FromSeconds(1));
        Assert.InRange(newest.GetProperty("distanceNm").GetDouble(), 6.5, 8.5);

        // "Törn anlegen": a trip with exactly that voyage's fixes
        var created = await owner.PostAsJsonAsync($"/api/ships/{ship}/voyages", new { from = newest.GetProperty("from").GetDateTimeOffset(), to = newest.GetProperty("to").GetDateTimeOffset(), name = "Juni-Törn" });
        var newTrip = await IdOf(created);
        var t = await owner.GetFromJsonAsync<JsonElement>($"/api/trips/{newTrip}");
        Assert.Equal(("Juni-Törn", "2026-06-03", "Completed"), (t.GetProperty("name").GetString(), t.GetProperty("startDate").GetString(), t.GetProperty("status").GetString()));
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/voyages")).GetProperty("total").GetInt32());
        track = await owner.GetFromJsonAsync<JsonElement>($"/api/trips/{newTrip}/track");
        Assert.Single(track.GetProperty("features").EnumerateArray(), x => x.GetProperty("properties").GetProperty("kind").GetString() == "track");
    }

    /// <summary>Opt-in: SMS_TRACK_ARCHIVE=path to a real export (e.g. the 70 MB TimeZero .7z). Imports it twice into a fresh ship and writes timings and counts next to it.</summary>
    [Fact]
    public async Task Real_archive_import_timings()
    {
        var path = Environment.GetEnvironmentVariable("SMS_TRACK_ARCHIVE");
        Assert.SkipUnless(path is not null && File.Exists(path), "SMS_TRACK_ARCHIVE not set");
        var report = new StringBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stats = new GpxStats(); var parsed = 0;
        using (var archive = SharpCompress.Archives.SevenZip.SevenZipArchive.OpenArchive(path!, new SharpCompress.Readers.ReaderOptions()))
            foreach (var e in archive.Entries.Where(e => !e.IsDirectory)) { using var s = e.OpenEntryStream(); parsed += TrackParsers.ReadGpx(s, stats).Count(); }
        report.AppendLine($"decompress + parse only: {sw.Elapsed.TotalSeconds:0.0} s, {parsed} fixes, {stats.Tracks} trk, {stats.Waypoints} wpt, {stats.Routes} rte");

        var (admin, _) = await f.AdminAsync();
        var ship = await IdOf(await admin.PostAsJsonAsync("/api/ships", new { name = "TimeZero Import Test", cruisingSpeedKn = 6, visibility = "Private" }));
        for (var run = 1; run <= 2; run++)
        {
            sw.Restart();
            var r = await admin.PostAsync($"/api/ships/{ship}/track-imports", Form(Path.GetFileName(path!), await File.ReadAllBytesAsync(path!)));
            r.EnsureSuccessStatusCode();
            var id = (await r.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetGuid();
            var upload = sw.Elapsed.TotalSeconds;
            JsonElement s;
            do { await Task.Delay(1000); s = await admin.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/track-imports/{id}"); }
            while (s.GetProperty("status").GetString() is "Queued" or "Running" && sw.Elapsed < TimeSpan.FromHours(1));
            var secs = sw.Elapsed.TotalSeconds;
            report.AppendLine($"run {run}: upload {upload:0.0} s, total {secs:0.0} s, {s}");
            report.AppendLine($"  {s.GetProperty("parsed").GetInt32() / secs:0} fixes/s parsed+deduplicated, {s.GetProperty("added").GetInt32() / secs:0} fixes/s added");
        }
        sw.Restart();
        var voyages = await admin.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/voyages?take=5");
        report.AppendLine($"voyages: {voyages.GetProperty("total").GetInt32()} in {sw.Elapsed.TotalSeconds:0.0} s (first page with harbours); newest: {voyages.GetProperty("items")}");
        sw.Restart();
        await admin.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/voyages?take=50&skip=5");
        report.AppendLine($"voyages page 2 (cached): {sw.Elapsed.TotalSeconds:0.0} s");
        await File.WriteAllTextAsync(Environment.GetEnvironmentVariable("SMS_TRACK_REPORT") ?? path + ".result.txt", report.ToString());
    }

    [Fact]
    public async Task Companion_app_uploads_files_with_its_device_token_and_reads_the_status()
    {
        var (owner, _) = await f.UserAsync();
        var ship = await IdOf(await owner.PostAsJsonAsync("/api/ships", new { name = "Companion Boat", cruisingSpeedKn = 5, visibility = "Public" }));
        var token = (await (await owner.PostAsJsonAsync($"/api/ships/{ship}/tracking-devices", new { name = "TimeZero PC", protocol = "Json" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var anon = f.Anonymous();
        HttpRequestMessage Req(HttpMethod m, string url, string? tok, HttpContent? body = null) { var q = new HttpRequestMessage(m, url) { Content = body }; if (tok is not null) q.Headers.Add("X-Device-Token", tok); return q; }
        var gpx = Encoding.UTF8.GetBytes(Gpx((new DateTimeOffset(2026, 5, 2, 8, 0, 0, TimeSpan.Zero), 54.3, 30)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Req(HttpMethod.Post, "/api/ingest/files", "wrong", Form("a.gpx", gpx)))).StatusCode);
        var r = await anon.SendAsync(Req(HttpMethod.Post, "/api/ingest/files", token, Form("a.gpx", gpx)));
        r.EnsureSuccessStatusCode();
        var importId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("importId").GetGuid();
        var s = await WaitAsync(async () => await (await anon.SendAsync(Req(HttpMethod.Get, $"/api/ingest/files/{importId}", token))).Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(181, s.GetProperty("added").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(Req(HttpMethod.Get, $"/api/ingest/files/{importId}", null))).StatusCode);

        // another ship's device cannot read it
        var other = await IdOf(await owner.PostAsJsonAsync("/api/ships", new { name = "Other Boat", cruisingSpeedKn = 5, visibility = "Public" }));
        var otherToken = (await (await owner.PostAsJsonAsync($"/api/ships/{other}/tracking-devices", new { name = "x", protocol = "Json" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.NotFound, (await anon.SendAsync(Req(HttpMethod.Get, $"/api/ingest/files/{importId}", otherToken))).StatusCode);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var import = await db.TrackImports.SingleAsync(i => i.Id == importId);
        Assert.NotNull(import.DeviceId);
        Assert.Equal(181, await db.TrackPoints.CountAsync(p => p.ImportId == importId && p.DeviceId == import.DeviceId));
    }

    [Fact]
    public async Task Points_batch_with_sensor_values_records_onboard_weather_next_to_the_model_and_the_live_position_wins()
    {
        var (owner, _) = await f.UserAsync();
        var ship = await IdOf(await owner.PostAsJsonAsync("/api/ships", new { name = "Sensor Boat", cruisingSpeedKn = 5, visibility = "Public", mmsi = "211999888" }));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trip = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/trips", new { name = "Live", startDate = today.AddDays(-1).ToString("yyyy-MM-dd"), endDate = today.AddDays(2).ToString("yyyy-MM-dd"), visibility = "Public", status = "Active", trackVisibility = "Public", publicPositionDelayMinutes = 0 }));
        var token = (await (await owner.PostAsJsonAsync($"/api/ships/{ship}/tracking-devices", new { name = "Companion", protocol = "Json" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        var anon = f.Anonymous();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);

        // the existing contract still works on /api/tracking/points; the companion uses /api/ingest/points with sensor values
        (await anon.PostAsJsonAsync("/api/tracking/points", new { deviceToken = token, points = new[] { new { t = t0.AddMinutes(-60), lat = 54.30, lon = 10.10, sog = 5.0, cog = 0.0, acc = 5.0 } } })).EnsureSuccessStatusCode();
        var r = await anon.PostAsJsonAsync("/api/ingest/points", new
        {
            deviceToken = token,
            points = new object[]
            {
                new { t = t0, lat = 54.40, lon = 10.20, sog = 5.0, cog = 0.0, hdg = 0.0, twsKn = 14.0, twdDeg = 250.0, pressureHpa = 1009.5, airTempC = 17.5, waterTempC = 15.2, depthM = 12.4 },
                new { t = t0.AddSeconds(10), lat = 54.4001, lon = 10.20, sog = 5.0, cog = 0.0 },
            },
        });
        r.EnsureSuccessStatusCode();
        Assert.Equal(2, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("added").GetInt32());

        var obs = await owner.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/weather");
        var board = Assert.Single(obs.EnumerateArray(), o => o.GetProperty("onboard").GetBoolean());
        Assert.Equal((14.0, 250.0, 15.2, 12.4, 17.5), (board.GetProperty("windKn").GetDouble(), board.GetProperty("windDirDeg").GetDouble(), board.GetProperty("waterTempC").GetDouble(), board.GetProperty("depthM").GetDouble(), board.GetProperty("temperatureC").GetDouble()));
        Assert.Contains(obs.EnumerateArray(), o => !o.GetProperty("onboard").GetBoolean()); // model weather recorded as before

        // rate-limited like the model weather (15 min / 1 nm), independently of it; apparent wind alone yields true wind
        using (var viaHeader = new HttpRequestMessage(HttpMethod.Post, "/api/ingest/points") { Content = JsonContent.Create(new { points = new[] { new { t = t0.AddMinutes(1), lat = 54.4002, lon = 10.20, sog = 5.0, cog = 0.0, awsKn = 5.0, awaDeg = 0.0 } } }) })
        {
            viaHeader.Headers.Add("X-Device-Token", token); // token in the header instead of the body
            Assert.Equal(1, (await (await anon.SendAsync(viaHeader)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("added").GetInt32());
        }
        Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/weather")).EnumerateArray(), o => o.GetProperty("onboard").GetBoolean());
        (await anon.PostAsJsonAsync("/api/ingest/points", new { deviceToken = token, points = new[] { new { t = t0.AddMinutes(4), lat = 54.45, lon = 10.20, sog = 5.0, cog = 0.0, awsKn = 5.0, awaDeg = 0.0 } } })).EnsureSuccessStatusCode();
        var calm = (await owner.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/weather")).EnumerateArray().Where(o => o.GetProperty("onboard").GetBoolean()).Last();
        Assert.Equal(0, calm.GetProperty("windKn").GetDouble(), 6);

        // the logbook summary prefers the measured wind
        var summary = await owner.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/weather/summary?from={Uri.EscapeDataString(t0.AddMinutes(-1).ToString("O"))}&to={Uri.EscapeDataString(t0.AddMinutes(2).ToString("O"))}");
        Assert.Equal(14, summary.GetProperty("windKn").GetDouble());

        // the live position is the device's (freshest); a configured AIS poll is skipped while the device delivers
        var pos = await owner.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/position");
        Assert.Equal(("GpsLive", 54.45), (pos.GetProperty("source").GetString(), Math.Round(pos.GetProperty("lat").GetDouble(), 4)));
        (await owner.PutAsJsonAsync($"/api/ships/{ship}/ais-config", new { provider = "Fake", apiKey = "k", pollIntervalMinutes = 30, enabled = true })).EnsureSuccessStatusCode();
        Assert.Equal(0, await f.Services.GetRequiredService<AisPoller>().PollOnceAsync(force: false, CancellationToken.None, ship));
        Assert.Equal(0, (await owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/ais-config")).GetProperty("callsThisMonth").GetInt32());
    }
}
