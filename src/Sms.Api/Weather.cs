using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sms.Domain;
using Sms.Domain.Tracking;
using Sms.Infrastructure;

namespace Sms.Api;

// ---------------------------------------------------------------------------------------------------------------
// External-service layer for weather. Nothing outside this file talks to Open-Meteo or Xweather; swap providers by
// registering another IWeatherProvider / IWeatherOverlayProvider (same pattern as IAisPositionProvider).
// ---------------------------------------------------------------------------------------------------------------

public sealed record WeatherPoint(DateTimeOffset Time, double? TemperatureC, double? WindKn, double? WindDirDeg, double? GustKn, double? PrecipitationMm, int? PrecipitationChance, int? WeatherCode, double? PressureHpa, int? CloudCover, double? WaveHeightM, double? WaveDirDeg, double? WavePeriodS);
public sealed record WeatherDay(DateOnly Date, int? WeatherCode, double? TempMinC, double? TempMaxC, double? WindMaxKn, double? GustMaxKn, double? WindDirDeg, double? PrecipitationMm, DateTimeOffset? Sunrise, DateTimeOffset? Sunset);
public sealed record WeatherForecast(string Provider, double Lat, double Lng, string TimeZone, WeatherPoint Current, IReadOnlyList<WeatherPoint> Hourly, IReadOnlyList<WeatherDay> Daily, DateTimeOffset FetchedAt);
public sealed record OverlayLayer(string Id, string Name, string Attribution);
/// <summary>Browser credentials for the provider's WebGL SDK (MapsGL renders client-side). A dedicated key restricted to the site's domain in the provider account, never the server key.</summary>
public sealed record OverlayGlAccess(string ClientId, string ClientSecret);

public interface IWeatherProvider
{
    string Name { get; }
    /// <summary>Current conditions + hourly (up to 7 d, waves where the marine model has them) + daily (7 d) forecast for a position, or null when the provider fails.</summary>
    Task<WeatherForecast?> ForecastAsync(double lat, double lng, CancellationToken ct);
}

public interface IWeatherOverlayProvider
{
    string Name { get; }
    bool Configured { get; }
    IReadOnlyList<OverlayLayer> Layers { get; }
    /// <summary>Browser credentials for GL rendering, null when the provider has none or it is switched off (then the raster tiles are used).</summary>
    OverlayGlAccess? MapsGl { get; }
    /// <summary>Raster tile (PNG) of a layer, or null when not available.</summary>
    Task<byte[]?> TileAsync(string layer, int z, int x, int y, CancellationToken ct);
}

/// <summary>Open-Meteo free tier (no key): forecast + marine (waves) APIs, cached 15 min per 0.05° cell to stay well inside the limits.</summary>
public sealed class OpenMeteoProvider(IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, ILogger<OpenMeteoProvider> logger) : IWeatherProvider
{
    public string Name => "OpenMeteo";

    public Task<WeatherForecast?> ForecastAsync(double lat, double lng, CancellationToken ct)
    {
        var key = $"wx:{Math.Round(lat, 2)}:{Math.Round(lng, 2)}";
        return cache.GetOrCreateAsync(key, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            try { return await FetchAsync(lat, lng, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Open-Meteo failed for {Lat},{Lng}", lat, lng); e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1); return null; }
        });
    }

    private async Task<WeatherForecast> FetchAsync(double lat, double lng, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var baseUrl = (cfg["Weather:OpenMeteoUrl"] ?? "https://api.open-meteo.com").TrimEnd('/');
        var marineUrl = (cfg["Weather:OpenMeteoMarineUrl"] ?? "https://marine-api.open-meteo.com").TrimEnd('/');
        var client = http.CreateClient("weather");
        var la = lat.ToString("0.####", inv); var lo = lng.ToString("0.####", inv);
        using var f = JsonDocument.Parse(await client.GetStringAsync($"{baseUrl}/v1/forecast?latitude={la}&longitude={lo}&current=temperature_2m,wind_speed_10m,wind_direction_10m,wind_gusts_10m,precipitation,weather_code,pressure_msl,cloud_cover&hourly=temperature_2m,wind_speed_10m,wind_direction_10m,wind_gusts_10m,precipitation_probability,precipitation,weather_code,pressure_msl&daily=weather_code,temperature_2m_max,temperature_2m_min,wind_speed_10m_max,wind_gusts_10m_max,wind_direction_10m_dominant,precipitation_sum,sunrise,sunset&wind_speed_unit=kn&timezone=auto&forecast_days=7", ct));
        JsonDocument? marine = null;
        try { marine = JsonDocument.Parse(await client.GetStringAsync($"{marineUrl}/v1/marine?latitude={la}&longitude={lo}&current=wave_height,wave_direction,wave_period&hourly=wave_height,wave_direction,wave_period&timezone=auto&forecast_days=7", ct)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogDebug(ex, "no marine data for {Lat},{Lng}", lat, lng); } // inland positions have no wave model
        using var _ = marine;
        var root = f.RootElement;
        var tz = root.GetProperty("timezone").GetString() ?? "UTC";
        var offset = TimeSpan.FromSeconds(root.GetProperty("utc_offset_seconds").GetInt32());
        DateTimeOffset T(string s) => new(DateTime.ParseExact(s, s.Length > 10 ? "yyyy-MM-dd'T'HH:mm" : "yyyy-MM-dd", inv), offset);
        var cur = root.GetProperty("current");
        var mcur = marine?.RootElement.GetProperty("current");
        var current = new WeatherPoint(T(cur.GetProperty("time").GetString()!), D(cur, "temperature_2m"), D(cur, "wind_speed_10m"), D(cur, "wind_direction_10m"), D(cur, "wind_gusts_10m"), D(cur, "precipitation"), null, I(cur, "weather_code"), D(cur, "pressure_msl"), I(cur, "cloud_cover"),
            mcur is { } mc ? D(mc, "wave_height") : null, mcur is { } mc2 ? D(mc2, "wave_direction") : null, mcur is { } mc3 ? D(mc3, "wave_period") : null);
        var h = root.GetProperty("hourly");
        var times = h.GetProperty("time").EnumerateArray().Select(t => t.GetString()!).ToList();
        var waveByTime = new Dictionary<string, (double? h, double? d, double? p)>();
        if (marine?.RootElement.TryGetProperty("hourly", out var mh) == true)
        {
            var mt = mh.GetProperty("time").EnumerateArray().Select(t => t.GetString()!).ToList();
            for (var i = 0; i < mt.Count; i++) waveByTime[mt[i]] = (A(mh, "wave_height", i), A(mh, "wave_direction", i), A(mh, "wave_period", i));
        }
        var now = DateTimeOffset.UtcNow.AddHours(-1);
        var hourly = new List<WeatherPoint>();
        for (var i = 0; i < times.Count && hourly.Count < 168; i++)
        {
            var t = T(times[i]); if (t < now) continue;
            var w = waveByTime.GetValueOrDefault(times[i]);
            hourly.Add(new WeatherPoint(t, A(h, "temperature_2m", i), A(h, "wind_speed_10m", i), A(h, "wind_direction_10m", i), A(h, "wind_gusts_10m", i), A(h, "precipitation", i), (int?)A(h, "precipitation_probability", i), (int?)A(h, "weather_code", i), A(h, "pressure_msl", i), null, w.h, w.d, w.p));
        }
        var d = root.GetProperty("daily");
        var dates = d.GetProperty("time").EnumerateArray().Select(t => t.GetString()!).ToList();
        var daily = dates.Select((date, i) => new WeatherDay(DateOnly.ParseExact(date, "yyyy-MM-dd", inv), (int?)A(d, "weather_code", i), A(d, "temperature_2m_min", i), A(d, "temperature_2m_max", i), A(d, "wind_speed_10m_max", i), A(d, "wind_gusts_10m_max", i), A(d, "wind_direction_10m_dominant", i), A(d, "precipitation_sum", i),
            S(d, "sunrise", i) is { } sr ? T(sr) : null, S(d, "sunset", i) is { } ss ? T(ss) : null)).ToList();
        return new WeatherForecast(Name, root.GetProperty("latitude").GetDouble(), root.GetProperty("longitude").GetDouble(), tz, current, hourly, daily, DateTimeOffset.UtcNow);
    }

    private static double? D(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static int? I(JsonElement e, string name) => D(e, name) is { } d ? (int)d : null;
    private static double? A(JsonElement e, string name, int i) => e.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array && i < arr.GetArrayLength() && arr[i].ValueKind == JsonValueKind.Number ? arr[i].GetDouble() : null;
    private static string? S(JsonElement e, string name, int i) => e.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array && i < arr.GetArrayLength() ? arr[i].GetString() : null;
}

/// <summary>Vaisala Xweather raster map layers (client id/secret from user secrets or configuration, never sent to the browser).</summary>
public sealed class XweatherOverlayProvider(IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg) : IWeatherOverlayProvider
{
    public string Name => "Xweather";
    public bool Configured => !string.IsNullOrWhiteSpace(cfg["Xweather:ClientId"]) && !string.IsNullOrWhiteSpace(cfg["Xweather:ClientSecret"]);
    // a separate, domain-restricted browser key: the server credentials are never handed out
    public OverlayGlAccess? MapsGl => !string.IsNullOrWhiteSpace(cfg["Xweather:MapsGlClientId"]) && !string.IsNullOrWhiteSpace(cfg["Xweather:MapsGlClientSecret"]) ? new(cfg["Xweather:MapsGlClientId"]!, cfg["Xweather:MapsGlClientSecret"]!) : null;
    public IReadOnlyList<OverlayLayer> Layers { get; } =
    [
        new("radar", "Regenradar", "© Vaisala Xweather"), new("satellite", "Satellit", "© Vaisala Xweather"), new("wind-speeds", "Wind", "© Vaisala Xweather"),
        new("temperatures", "Temperatur", "© Vaisala Xweather"), new("wave-heights", "Wellenhöhe", "© Vaisala Xweather"), new("pressure-msl", "Luftdruck", "© Vaisala Xweather"),
    ];

    public async Task<byte[]?> TileAsync(string layer, int z, int x, int y, CancellationToken ct)
    {
        if (!Configured || Layers.All(l => l.Id != layer) || z is < 0 or > 18) return null;
        var key = $"xw:{layer}:{z}:{x}:{y}:{DateTimeOffset.UtcNow:yyyyMMddHHmm}"[..^1]; // 10-minute buckets
        return await cache.GetOrCreateAsync(key, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var url = $"{(cfg["Xweather:MapsUrl"] ?? "https://maps.api.xweather.com").TrimEnd('/')}/{cfg["Xweather:ClientId"]}_{cfg["Xweather:ClientSecret"]}/{layer}/{z}/{x}/{y}/current.png";
            using var resp = await http.CreateClient("weather").GetAsync(url, ct);
            return resp.IsSuccessStatusCode && resp.Content.Headers.ContentType?.MediaType?.StartsWith("image/") == true ? await resp.Content.ReadAsByteArrayAsync(ct) : null;
        });
    }
}

/// <summary>What a logbook entry needs from the observations of a leg: Beaufort, dominant direction, weather text, sea state (Douglas).</summary>
public sealed record WeatherSummary(int Samples, int WindBft, double WindKn, WindDirection? WindDirection, double? GustKn, string Weather, string SeaState, double? WaveHeightM, double? TemperatureC)
{
    public static WeatherSummary? From(IReadOnlyList<WeatherObservation> obs)
    {
        if (obs.Count == 0) return null;
        var winds = obs.Where(o => o.WindKn is not null).Select(o => o.WindKn!.Value).ToList();
        var windKn = winds.Count > 0 ? winds.Average() : 0;
        double sx = 0, sy = 0;
        foreach (var o in obs.Where(o => o.WindDirDeg is not null)) { var r = o.WindDirDeg!.Value * Math.PI / 180; sx += Math.Sin(r) * (o.WindKn ?? 1); sy += Math.Cos(r) * (o.WindKn ?? 1); }
        WindDirection? dir = sx == 0 && sy == 0 ? null : (WindDirection)(((int)Math.Round((((Math.Atan2(sx, sy) * 180 / Math.PI) % 360) + 360) % 360 / 22.5)) % 16);
        var temps = obs.Where(o => o.TemperatureC is not null).Select(o => o.TemperatureC!.Value).ToList();
        var codes = obs.Where(o => o.WeatherCode is not null).GroupBy(o => Group(o.WeatherCode!.Value)).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).Select(g => g.Key).ToList();
        var weather = codes.Count > 0 ? WeatherText(codes[0]) : "";
        if (temps.Count > 0) weather = $"{weather}{(weather.Length > 0 ? ", " : "")}{Math.Round(temps.Average())} °C";
        var waves = obs.Where(o => o.WaveHeightM is not null).Select(o => o.WaveHeightM!.Value).ToList();
        double? waveMax = waves.Count > 0 ? waves.Max() : null;
        return new WeatherSummary(obs.Count, Beaufort(windKn), Math.Round(windKn, 1), dir, obs.Max(o => o.GustKn), weather, waveMax is { } w ? SeaStateText(w) : "", waveMax, temps.Count > 0 ? Math.Round(temps.Average(), 1) : null);
    }

    public static int Beaufort(double kn) { double[] limits = [1, 3, 6, 10, 16, 21, 27, 33, 40, 47, 55, 63]; var i = Array.FindIndex(limits, l => kn < l); return i < 0 ? 12 : i; }
    /// <summary>WMO weather code → group key (higher = worse).</summary>
    private static int Group(int c) => c == 0 ? 0 : c <= 2 ? 1 : c == 3 ? 2 : c <= 48 ? 3 : c <= 57 ? 4 : c <= 67 ? 5 : c <= 77 ? 6 : c <= 82 ? 7 : c <= 86 ? 8 : 9;
    private static string WeatherText(int g) => g switch { 0 => "klar", 1 => "heiter", 2 => "bedeckt", 3 => "Nebel", 4 => "Niesel", 5 => "Regen", 6 => "Schnee", 7 => "Schauer", 8 => "Schneeschauer", _ => "Gewitter" };
    /// <summary>Douglas sea scale from the significant wave height.</summary>
    public static string SeaStateText(double m)
    {
        var (n, t) = m < 0.1 ? (0, "spiegelglatt") : m <= 0.5 ? (2, "schwach bewegt") : m <= 1.25 ? (3, "leicht bewegt") : m <= 2.5 ? (4, "mäßig bewegt") : m <= 4 ? (5, "grob") : m <= 6 ? (6, "sehr grob") : m <= 9 ? (7, "hoch") : m <= 14 ? (8, "sehr hoch") : (9, "außergewöhnlich hoch");
        if (m >= 0.1 && m <= 0.1) (n, t) = (1, "ruhig");
        return $"{n} – {t} ({m.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} m)";
    }
}

/// <summary>Stores the weather at received live positions, at most once per 15 min / 1 nm per ship (spec: live tracking weather).</summary>
public sealed class WeatherRecorder(IWeatherProvider weather, IServiceScopeFactory scopes, ILogger<WeatherRecorder> logger)
{
    public const double DebounceM = 1852; public static readonly TimeSpan DebounceTime = TimeSpan.FromMinutes(15);

    /// <summary>True when the position is far or late enough from the last observation to be worth a new one.</summary>
    public static bool IsDue(DateTimeOffset lastTime, double lastLat, double lastLng, DateTimeOffset time, double lat, double lng) =>
        time - lastTime >= DebounceTime || TrackMath.Haversine(lastLat, lastLng, lat, lng) >= DebounceM;

    public async Task<bool> RecordAsync(Guid shipId, Guid? tripId, TrackFix fix, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var last = await db.WeatherObservations.AsNoTracking().Where(o => o.ShipId == shipId).OrderByDescending(o => o.Time).Select(o => new { o.Time, o.Location }).FirstOrDefaultAsync(ct);
        if (last is not null && !IsDue(last.Time, last.Location.Y, last.Location.X, fix.Time, fix.Lat, fix.Lng)) return false;
        var fc = await weather.ForecastAsync(fix.Lat, fix.Lng, ct);
        if (fc is null) return false;
        var c = fc.Current;
        db.WeatherObservations.Add(new WeatherObservation
        {
            ShipId = shipId, TripId = tripId, Time = fix.Time, Location = Geo.Point(fix.Lat, fix.Lng), Provider = fc.Provider, TemperatureC = c.TemperatureC, WindKn = c.WindKn, WindDirDeg = c.WindDirDeg, GustKn = c.GustKn,
            PressureHpa = c.PressureHpa, PrecipitationMm = c.PrecipitationMm, WeatherCode = c.WeatherCode, CloudCover = c.CloudCover, WaveHeightM = c.WaveHeightM, WaveDirDeg = c.WaveDirDeg, WavePeriodS = c.WavePeriodS,
        });
        await db.SaveChangesAsync(ct);
        logger.LogDebug("Weather recorded for ship {Ship} at {Lat},{Lng}", shipId, fix.Lat, fix.Lng);
        return true;
    }
}
