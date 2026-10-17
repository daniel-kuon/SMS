using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Domain.Tracking;
using Sms.Infrastructure;
using Sms.Infrastructure.Tracking;

namespace Sms.Api;

/// <summary>Spec 12 §12.1 C: pluggable AIS position source per ship (API key stored encrypted, polled during active trips).</summary>
public interface IAisPositionProvider
{
    string Name { get; }
    /// <summary>Positions of the vessel newer than <paramref name="since"/>; empty when nothing was received.</summary>
    Task<IReadOnlyList<TrackFix>> PositionsAsync(string apiKey, string mmsi, DateTimeOffset since, CancellationToken ct);
}

public sealed class AisKeys(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("sms.ais");
    public string Protect(string apiKey) => _protector.Protect(apiKey);
    public string Unprotect(string stored) => _protector.Unprotect(stored);
}

/// <summary>
/// aisstream.io: free websocket feed (API key from a free account). A "poll" subscribes with the MMSI filter and
/// listens for a short window; class B yachts report every 30 s under way, so a 25 s window usually catches one.
/// </summary>
public sealed class AisStreamProvider(IConfiguration cfg) : IAisPositionProvider
{
    public string Name => "AisStream";

    public async Task<IReadOnlyList<TrackFix>> PositionsAsync(string apiKey, string mmsi, DateTimeOffset since, CancellationToken ct)
    {
        var url = cfg["Ais:AisStreamUrl"] ?? "wss://stream.aisstream.io/v0/stream";
        var listen = TimeSpan.FromSeconds(cfg.GetValue("Ais:AisStreamListenSeconds", 25));
        using var ws = new ClientWebSocket();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(listen + TimeSpan.FromSeconds(10));
        await ws.ConnectAsync(new Uri(url), cts.Token);
        var subscription = JsonSerializer.Serialize(new { APIKey = apiKey, BoundingBoxes = new[] { new[] { new[] { -90.0, -180.0 }, new[] { 90.0, 180.0 } } }, FiltersShipMMSI = new[] { mmsi }, FilterMessageTypes = new[] { "PositionReport" } });
        await ws.SendAsync(Encoding.UTF8.GetBytes(subscription), WebSocketMessageType.Text, true, cts.Token);
        var fixes = new List<TrackFix>();
        var buffer = new byte[64 * 1024];
        var deadline = DateTimeOffset.UtcNow + listen;
        try
        {
            while (DateTimeOffset.UtcNow < deadline && ws.State == WebSocketState.Open)
            {
                using var msg = new MemoryStream();
                WebSocketReceiveResult r;
                do { r = await ws.ReceiveAsync(buffer, cts.Token); msg.Write(buffer, 0, r.Count); } while (!r.EndOfMessage);
                if (r.MessageType == WebSocketMessageType.Close) break;
                using var doc = JsonDocument.Parse(msg.ToArray());
                if (doc.RootElement.TryGetProperty("error", out var err)) throw new InvalidOperationException($"aisstream: {err.GetString()}");
                if (Parse(doc.RootElement) is { } f && f.Time > since) fixes.Add(f);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* listen window over */ }
        catch (WebSocketException ex) when (fixes.Count == 0) { throw new InvalidOperationException($"aisstream hat die Verbindung beendet ({ex.Message}) – API-Schlüssel ungültig?"); }
        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { /* ignore */ }
        return fixes;
    }

    /// <summary>{"MessageType":"PositionReport","MetaData":{"latitude":..,"longitude":..,"time_utc":"2026-10-17 09:00:01.123 +0000 UTC"},"Message":{"PositionReport":{"Sog":..,"Cog":..}}}</summary>
    public static TrackFix? Parse(JsonElement root)
    {
        if (!root.TryGetProperty("MessageType", out var type) || type.GetString() != "PositionReport" || !root.TryGetProperty("MetaData", out var meta)) return null;
        var lat = meta.GetProperty("latitude").GetDouble(); var lng = meta.GetProperty("longitude").GetDouble();
        var timeText = meta.TryGetProperty("time_utc", out var t) ? t.GetString() ?? "" : "";
        var time = DateTime.TryParseExact(timeText.Length >= 19 ? timeText[..19] : timeText, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? new DateTimeOffset(dt, TimeSpan.Zero) : DateTimeOffset.UtcNow;
        double? sog = null, cog = null;
        if (root.TryGetProperty("Message", out var m) && m.TryGetProperty("PositionReport", out var pr))
        {
            if (pr.TryGetProperty("Sog", out var s) && s.ValueKind == JsonValueKind.Number) sog = s.GetDouble();
            if (pr.TryGetProperty("Cog", out var c) && c.ValueKind == JsonValueKind.Number && c.GetDouble() < 360) cog = c.GetDouble();
        }
        return new TrackFix(time, lat, lng, sog is >= 102.3 ? null : sog, cog, null);
    }
}

/// <summary>MarineTraffic single-vessel positions (PS07-style export, paid per call); URL template configurable (Ais:MarineTrafficUrl).</summary>
public sealed class MarineTrafficProvider(IHttpClientFactory http, IConfiguration cfg) : IAisPositionProvider
{
    public string Name => "MarineTraffic";

    public async Task<IReadOnlyList<TrackFix>> PositionsAsync(string apiKey, string mmsi, DateTimeOffset since, CancellationToken ct)
    {
        var minutes = Math.Clamp((int)(DateTimeOffset.UtcNow - since).TotalMinutes, 5, 2880);
        var url = (cfg["Ais:MarineTrafficUrl"] ?? "https://services.marinetraffic.com/api/exportvessel/v:5/{key}/timespan:{minutes}/mmsi:{mmsi}/protocol:jsono")
            .Replace("{key}", Uri.EscapeDataString(apiKey)).Replace("{mmsi}", Uri.EscapeDataString(mmsi)).Replace("{minutes}", minutes.ToString(CultureInfo.InvariantCulture));
        using var resp = await http.CreateClient("marinetraffic").GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStreamAsync(ct));
        return Parse(doc.RootElement).Where(f => f.Time > since).ToList();
    }

    /// <summary>[{"MMSI":"..","LAT":"54.3","LON":"10.1","SPEED":"52","COURSE":"180","TIMESTAMP":"2026-10-17T09:00:00"}] – SPEED in tenths of knots.</summary>
    public static List<TrackFix> Parse(JsonElement root)
    {
        var list = new List<TrackFix>();
        if (root.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in root.EnumerateArray())
        {
            if (!TryNum(e, "LAT", out var lat) || !TryNum(e, "LON", out var lng)) continue;
            var ts = e.TryGetProperty("TIMESTAMP", out var t) ? t.GetString() : null;
            if (!DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)) continue;
            list.Add(new TrackFix(new DateTimeOffset(dt, TimeSpan.Zero), lat, lng, TryNum(e, "SPEED", out var sp) ? sp / 10 : null, TryNum(e, "COURSE", out var co) ? co : null, null));
        }
        return list;
    }

    private static bool TryNum(JsonElement e, string name, out double value)
    {
        value = 0;
        if (!e.TryGetProperty(name, out var p)) return false;
        return p.ValueKind == JsonValueKind.Number ? (value = p.GetDouble()) == value : double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>Polls every enabled ship configuration while the ship has an active trip; stops by itself after the trip (spec 12 §12.1 C).</summary>
public sealed class AisPoller(IServiceScopeFactory scopes, IEnumerable<IAisPositionProvider> providers, AisKeys keys, ILogger<AisPoller> logger) : BackgroundService
{
    public IAisPositionProvider? Provider(string name) => providers.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    public IEnumerable<string> ProviderNames => providers.Select(p => p.Name);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await PollOnceAsync(force: false, stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "AIS polling failed"); }
        }
    }

    /// <summary>Polls all due ships; returns the number of positions added. Public for tests and the manual "poll now" action.</summary>
    public async Task<int> PollOnceAsync(bool force, CancellationToken ct, Guid? onlyShip = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<TrackProcessor>();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var configs = await db.ShipAisConfigs.Where(c => c.Enabled && c.ApiKeyEncrypted != null && (onlyShip == null || c.ShipId == onlyShip)).ToListAsync(ct);
        var added = 0;
        foreach (var c in configs)
        {
            if (!force && c.LastPolledAt is { } last && last.AddMinutes(c.PollIntervalMinutes) > now) continue;
            // only while a trip is running: no calls (and no costs) between trips
            if (!await db.Trips.AnyAsync(t => t.ShipId == c.ShipId && t.Status == TripStatus.Active && t.StartDate <= today.AddDays(1) && t.EndDate >= today.AddDays(-1), ct)) continue;
            var mmsi = c.Mmsi ?? await db.Ships.Where(s => s.Id == c.ShipId).Select(s => s.Mmsi).FirstOrDefaultAsync(ct);
            var provider = Provider(c.Provider);
            if (provider is null || string.IsNullOrWhiteSpace(mmsi)) { c.LastError = "Provider oder MMSI fehlt."; continue; }
            var month = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (c.CallsMonth != month) { c.CallsMonth = month; c.CallsThisMonth = 0; }
            c.LastPolledAt = now; c.CallsThisMonth++;
            try
            {
                var since = await db.TrackPoints.Where(p => p.ShipId == c.ShipId && p.Source == TrackSource.Ais).MaxAsync(p => (DateTimeOffset?)p.Time, ct) ?? now.AddHours(-24);
                var fixes = await provider.PositionsAsync(keys.Unprotect(c.ApiKeyEncrypted!), mmsi.Trim(), since, ct);
                if (fixes.Count > 0) added += await processor.IngestAsync(db, c.ShipId, null, null, null, TrackSource.Ais, fixes, ct);
                c.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { c.LastError = ex.Message; logger.LogWarning(ex, "AIS poll for ship {Ship} failed", c.ShipId); }
            await db.SaveChangesAsync(ct);
        }
        return added;
    }
}
