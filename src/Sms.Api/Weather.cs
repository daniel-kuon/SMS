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

public interface IWeatherProvider
{
    string Name { get; }
    /// <summary>Current conditions + hourly (48 h) + daily (7 d) forecast for a position, or null when the provider fails.</summary>
    Task<WeatherForecast?> ForecastAsync(double lat, double lng, CancellationToken ct);
}

public interface IWeatherOverlayProvider
{
    string Name { get; }
    bool Configured { get; }
    IReadOnlyList<OverlayLayer> Layers { get; }
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
        try { marine = JsonDocument.Parse(await client.GetStringAsync($"{marineUrl}/v1/marine?latitude={la}&longitude={lo}&current=wave_height,wave_direction,wave_period&hourly=wave_height,wave_direction,wave_period&timezone=auto&forecast_days=3", ct)); }
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
        for (var i = 0; i < times.Count && hourly.Count < 48; i++)
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
