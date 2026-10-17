using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sms.Api;
using Sms.Infrastructure;

namespace Sms.Api.Tests;

/// <summary>Registered instead of Open-Meteo: a fixed forecast, counting calls so debouncing can be asserted.</summary>
public sealed class FakeWeatherProvider : IWeatherProvider
{
    public string Name => "FakeWeather";
    public static int Calls; public static bool Fail;
    public Task<WeatherForecast?> ForecastAsync(double lat, double lng, CancellationToken ct)
    {
        Calls++;
        if (Fail) return Task.FromResult<WeatherForecast?>(null);
        var now = DateTimeOffset.UtcNow;
        var cur = new WeatherPoint(now, 14.2, 12.5, 250, 18.0, 0, null, 3, 1012.4, 75, 0.6, 260, 4.1);
        var hourly = Enumerable.Range(0, 48).Select(i => cur with { Time = now.AddHours(i) }).ToList();
        var daily = Enumerable.Range(0, 7).Select(i => new WeatherDay(DateOnly.FromDateTime(now.UtcDateTime).AddDays(i), 3, 10, 16, 15, 22, 250, 0.4, now, now.AddHours(10))).ToList();
        return Task.FromResult<WeatherForecast?>(new WeatherForecast(Name, lat, lng, "Europe/Berlin", cur, hourly, daily, now));
    }
}

/// <summary>Overlay provider that serves a 1×1 PNG for the "radar" layer only.</summary>
public sealed class FakeOverlayProvider : IWeatherOverlayProvider
{
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    public string Name => "FakeOverlay";
    public bool Configured => true;
    public OverlayGlAccess? MapsGl => null;
    public IReadOnlyList<OverlayLayer> Layers { get; } = [new("radar", "Regenradar", "© test")];
    public Task<byte[]?> TileAsync(string layer, int z, int x, int y, CancellationToken ct) => Task.FromResult(layer == "radar" ? Png : null);
}

public class WeatherTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> IdOf(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }

    [Fact]
    public async Task Forecast_for_a_position_and_for_a_harbour_comes_from_the_provider()
    {
        var anon = f.Anonymous();
        var fc = await anon.GetFromJsonAsync<JsonElement>("/api/weather?lat=54.33&lng=10.15");
        Assert.Equal("FakeWeather", fc.GetProperty("provider").GetString());
        Assert.Equal(12.5, fc.GetProperty("current").GetProperty("windKn").GetDouble());
        Assert.Equal(48, fc.GetProperty("hourly").GetArrayLength());
        Assert.Equal(7, fc.GetProperty("daily").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.GetAsync("/api/weather?lat=95&lng=10")).StatusCode);

        var harbour = (await anon.GetFromJsonAsync<JsonElement>("/api/harbours?q=Kiel%20D"))[0].GetProperty("id").GetGuid();
        var hfc = await anon.GetFromJsonAsync<JsonElement>($"/api/harbours/{harbour}/weather");
        Assert.InRange(hfc.GetProperty("lat").GetDouble(), 54.2, 54.5);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/api/harbours/{Guid.NewGuid()}/weather")).StatusCode);

        FakeWeatherProvider.Fail = true;
        try { Assert.Equal(HttpStatusCode.BadGateway, (await anon.GetAsync("/api/weather?lat=50&lng=10")).StatusCode); }
        finally { FakeWeatherProvider.Fail = false; }
    }

    [Fact]
    public async Task Live_positions_record_weather_debounced_by_one_mile_or_fifteen_minutes()
    {
        var (skipper, _) = await f.UserAsync();
        var ship = await IdOf(await skipper.PostAsJsonAsync("/api/ships", new { name = "Weather Boat", cruisingSpeedKn = 5, visibility = "Public" }));
        var trip = await IdOf(await skipper.PostAsJsonAsync($"/api/ships/{ship}/trips", new { name = "Wx trip", startDate = "2026-10-10", endDate = "2026-10-20", visibility = "Public", status = "Active", currency = "EUR", trackVisibility = "Public", publicPositionDelayMinutes = 0 }));
        var token = (await (await skipper.PostAsJsonAsync($"/api/ships/{ship}/tracking-devices", new { name = "Phone", protocol = "OsmAnd" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var anon = f.Anonymous();
        var t0 = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        async Task Fix(double lat, double lng, long t) => (await anon.GetAsync($"/api/tracking/osmand?id={token}&lat={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}&timestamp={t}&speed=5&bearing=45")).EnsureSuccessStatusCode();

        var before = FakeWeatherProvider.Calls;
        await Fix(54.340, 10.160, t0);            // first fix: recorded
        await Fix(54.342, 10.162, t0 + 300);      // 5 min, ~250 m later: debounced
        await Fix(54.345, 10.165, t0 + 600);      // 10 min, ~600 m: still debounced
        await Fix(54.346, 10.166, t0 + 960);      // 16 min: recorded (time)
        await Fix(54.370, 10.166, t0 + 1200);     // 4 min later but 2.6 km away: recorded (distance)
        Assert.Equal(3, FakeWeatherProvider.Calls - before);

        var obs = await anon.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/weather");
        Assert.Equal(3, obs.GetArrayLength());
        Assert.Equal(14.2, obs[0].GetProperty("temperatureC").GetDouble());
        Assert.Equal(54.370, obs[2].GetProperty("lat").GetDouble(), 3);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        Assert.Equal(3, await db.WeatherObservations.CountAsync(o => o.ShipId == ship && o.TripId == trip));

        // a privacy zone around the first two positions hides them from strangers but not from the skipper
        (await skipper.PostAsJsonAsync($"/api/ships/{ship}/privacy-zones", new { name = "home", lat = 54.343, lng = 10.163, radiusM = 800 })).EnsureSuccessStatusCode();
        var outside = await anon.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/weather");
        Assert.Single(outside.EnumerateArray());
        Assert.Equal(54.370, outside[0].GetProperty("lat").GetDouble(), 3);
        Assert.Equal(3, (await skipper.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/weather")).GetArrayLength());

        // a private track hides the observations from strangers but not from the skipper
        (await skipper.PutAsJsonAsync($"/api/trips/{trip}", new { name = "Wx trip", startDate = "2026-10-10", endDate = "2026-10-20", visibility = "Public", status = "Active", trackVisibility = "Crew", publicPositionDelayMinutes = 0 })).EnsureSuccessStatusCode();
        Assert.Equal(0, (await anon.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/weather")).GetArrayLength());
        Assert.Equal(3, (await skipper.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/weather")).GetArrayLength());
    }

    [Fact]
    public void Debounce_rule_is_one_nautical_mile_or_fifteen_minutes()
    {
        var t = DateTimeOffset.UtcNow;
        Assert.False(WeatherRecorder.IsDue(t, 54.0, 10.0, t.AddMinutes(14), 54.005, 10.0)); // 556 m, 14 min
        Assert.True(WeatherRecorder.IsDue(t, 54.0, 10.0, t.AddMinutes(15), 54.0, 10.0));
        Assert.True(WeatherRecorder.IsDue(t, 54.0, 10.0, t.AddMinutes(1), 54.017, 10.0));   // 1.9 km
    }

    [Fact]
    public async Task Overlay_layers_and_tiles_are_served_through_the_api()
    {
        var anon = f.Anonymous();
        var layers = await anon.GetFromJsonAsync<JsonElement>("/api/weather/overlay/layers");
        Assert.True(layers.GetProperty("configured").GetBoolean());
        Assert.Equal("radar", layers.GetProperty("layers")[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, layers.GetProperty("mapsGl").ValueKind);
        var tile = await anon.GetAsync("/api/weather/overlay/radar/8/136/82.png");
        tile.EnsureSuccessStatusCode();
        Assert.Equal("image/png", tile.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FakeOverlayProvider.Png, await tile.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/weather/overlay/nope/8/136/82.png")).StatusCode);
    }

    [Fact]
    public void Xweather_is_unconfigured_without_credentials_and_lists_its_layers()
    {
        var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var p = new XweatherOverlayProvider(null!, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), cfg);
        Assert.False(p.Configured);
        Assert.Contains(p.Layers, l => l.Id == "radar");
        Assert.Null(p.TileAsync("radar", 8, 136, 82, CancellationToken.None).Result);
        Assert.Null(p.MapsGl);
        var withKey = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Xweather:ClientId"] = "id", ["Xweather:ClientSecret"] = "s" }).Build();
        Assert.Equal(new OverlayGlAccess("id", "s"), new XweatherOverlayProvider(null!, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), withKey).MapsGl);
        var off = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Xweather:ClientId"] = "id", ["Xweather:ClientSecret"] = "s", ["Xweather:MapsGl"] = "false" }).Build();
        Assert.Null(new XweatherOverlayProvider(null!, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), off).MapsGl);
    }
}
