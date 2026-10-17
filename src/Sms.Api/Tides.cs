using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace Sms.Api;

// Tide predictions behind an interface (same rule as weather: no feature code talks to the vendor).
public sealed record TideExtreme(DateTimeOffset Time, double LevelM, bool High);
public sealed record TideForecast(string Provider, string StationId, string StationName, double StationLat, double StationLng, double DistanceKm, string Datum, string Attribution, bool CommercialUse, IReadOnlyList<TideExtreme> Extremes);

public interface ITideProvider
{
    string Name { get; }
    /// <summary>High/low water for the next days at the nearest station within <paramref name="maxKm"/>, null when there is none or the service fails.</summary>
    Task<TideForecast?> ForecastAsync(double lat, double lng, double maxKm, CancellationToken ct);
}

/// <summary>Open Waters tide API (Slackwater engine, TICON/NOAA station database; no key, CC-BY per station). Cached 6 h per 0.05° cell.</summary>
public sealed class OpenWatersTideProvider(IHttpClientFactory http, IMemoryCache cache, IConfiguration cfg, ILogger<OpenWatersTideProvider> logger) : ITideProvider
{
    public string Name => "OpenWaters";

    public Task<TideForecast?> ForecastAsync(double lat, double lng, double maxKm, CancellationToken ct) =>
        cache.GetOrCreateAsync($"tide:{Math.Round(lat, 2)}:{Math.Round(lng, 2)}:{maxKm}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            try { return await FetchAsync(lat, lng, maxKm, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Open Waters tides failed for {Lat},{Lng}", lat, lng); e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5); return null; }
        });

    private async Task<TideForecast?> FetchAsync(double lat, double lng, double maxKm, CancellationToken ct)
    {
        var inv = CultureInfo.InvariantCulture;
        var baseUrl = (cfg["Tides:OpenWatersUrl"] ?? "https://api.openwaters.io/tides").TrimEnd('/');
        var start = DateTimeOffset.UtcNow.Date; var end = start.AddDays(4);
        using var doc = JsonDocument.Parse(await http.CreateClient("weather").GetStringAsync(
            $"{baseUrl}/extremes?latitude={lat.ToString("0.####", inv)}&longitude={lng.ToString("0.####", inv)}&start={start:yyyy-MM-dd'T'HH:mm:ss'Z'}&end={end:yyyy-MM-dd'T'HH:mm:ss'Z'}", ct));
        var root = doc.RootElement;
        if (!root.TryGetProperty("station", out var st)) return null;
        var distanceKm = root.TryGetProperty("distance", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
        if (distanceKm > maxKm) return null;
        var extremes = root.GetProperty("extremes").EnumerateArray()
            .Select(x => new TideExtreme(DateTimeOffset.Parse(x.GetProperty("time").GetString()!, inv), Math.Round(x.GetProperty("level").GetDouble(), 2), x.GetProperty("high").GetBoolean())).ToList();
        var commercial = !(st.TryGetProperty("license", out var lic) && lic.TryGetProperty("commercial_use", out var cu) && cu.ValueKind == JsonValueKind.False);
        return new TideForecast(Name, st.GetProperty("id").GetString()!, st.GetProperty("name").GetString() ?? "", st.GetProperty("latitude").GetDouble(), st.GetProperty("longitude").GetDouble(),
            Math.Round(distanceKm, 1), root.GetProperty("datum").GetString() ?? "", st.TryGetProperty("attribution", out var at) ? at.GetString() ?? "" : "", commercial, extremes);
    }
}
