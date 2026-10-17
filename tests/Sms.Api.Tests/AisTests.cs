using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sms.Api;
using Sms.Domain.Tracking;

namespace Sms.Api.Tests;

/// <summary>Registered by the fixture instead of a real AIS service: three fixes around Kiel in the last hour.</summary>
public sealed class FakeAisProvider : IAisPositionProvider
{
    public string Name => "Fake";
    public static string? LastKey; public static string? LastMmsi; public static int Calls;
    public Task<IReadOnlyList<TrackFix>> PositionsAsync(string apiKey, string mmsi, DateTimeOffset since, CancellationToken ct)
    {
        LastKey = apiKey; LastMmsi = mmsi; Calls++;
        if (apiKey == "broken") throw new InvalidOperationException("invalid key");
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<TrackFix> fixes = [new(now.AddMinutes(-30), 54.40, 10.20, 5.1, 45), new(now.AddMinutes(-20), 54.41, 10.21, 5.0, 45), new(now.AddMinutes(-10), 54.42, 10.22, 4.9, 45)];
        return Task.FromResult<IReadOnlyList<TrackFix>>(fixes.Where(f => f.Time > since).ToList());
    }
}

public class AisTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> IdOf(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }

    [Fact]
    public async Task Owner_configures_ais_key_is_never_returned_and_polling_feeds_the_track()
    {
        var (owner, _) = await f.UserAsync();
        var ship = await IdOf(await owner.PostAsJsonAsync("/api/ships", new { name = "AIS Boat", cruisingSpeedKn = 5, visibility = "Public", mmsi = "211123456" }));
        var cfg = await owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/ais-config");
        Assert.False(cfg.GetProperty("configured").GetBoolean());
        Assert.Equal("211123456", cfg.GetProperty("mmsi").GetString()); // taken from the ship
        Assert.Contains("Fake", cfg.GetProperty("providers").EnumerateArray().Select(p => p.GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/ships/{ship}/ais-config", new { provider = "Nope", apiKey = "k", enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/ships/{ship}/ais-config", new { provider = "Fake", enabled = true })).StatusCode); // no key
        var saved = await owner.PutAsJsonAsync($"/api/ships/{ship}/ais-config", new { provider = "Fake", apiKey = "secret-key", pollIntervalMinutes = 15, enabled = true });
        saved.EnsureSuccessStatusCode();
        var body = await saved.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-key", body);
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("configured").GetBoolean());

        var test = await owner.PostAsync($"/api/ships/{ship}/ais-config/test", null);
        test.EnsureSuccessStatusCode();
        Assert.Equal(3, (await test.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32());
        Assert.Equal("secret-key", FakeAisProvider.LastKey); // decrypted for the provider only
        Assert.Equal("211123456", FakeAisProvider.LastMmsi);

        // no active trip → the poller stays quiet
        var poller = f.Services.GetRequiredService<AisPoller>();
        Assert.Equal(0, await poller.PollOnceAsync(force: true, CancellationToken.None, ship));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trip = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/trips", new { name = "AIS trip", startDate = today.AddDays(-1).ToString("yyyy-MM-dd"), endDate = today.AddDays(3).ToString("yyyy-MM-dd"), visibility = "Public", status = "Active", trackVisibility = "Public", publicPositionDelayMinutes = 0 }));
        var added = await poller.PollOnceAsync(force: true, CancellationToken.None, ship);
        Assert.Equal(3, added);
        Assert.Equal(0, await poller.PollOnceAsync(force: false, CancellationToken.None, ship)); // interval not elapsed
        (await owner.PostAsync($"/api/trips/{trip}/track/reprocess", null)).EnsureSuccessStatusCode();
        var track = await owner.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/track");
        Assert.Contains(track.GetProperty("features").EnumerateArray(), x => x.GetProperty("properties").GetProperty("source").GetString() == "Ais");
        var pos = await owner.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/position");
        Assert.Equal("Ais", pos.GetProperty("source").GetString());
        cfg = await owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/ais-config");
        Assert.Equal(1, cfg.GetProperty("callsThisMonth").GetInt32());
        Assert.NotNull(cfg.GetProperty("lastPolledAt").GetString());

        // a failing provider is reported, not thrown
        (await owner.PutAsJsonAsync($"/api/ships/{ship}/ais-config", new { provider = "Fake", apiKey = "broken", pollIntervalMinutes = 15, enabled = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadGateway, (await owner.PostAsync($"/api/ships/{ship}/ais-config/test", null)).StatusCode);
        await poller.PollOnceAsync(force: true, CancellationToken.None, ship);
        Assert.Contains("invalid key", (await owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/ais-config")).GetProperty("lastError").GetString());

        var (other, _) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/ships/{ship}/ais-config")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/api/ships/{ship}/ais-config")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/ships/{ship}/ais-config")).StatusCode);
    }

    [Fact]
    public void Parses_aisstream_and_marinetraffic_messages()
    {
        using var ais = JsonDocument.Parse("""{"MessageType":"PositionReport","MetaData":{"MMSI":211123456,"latitude":54.3721,"longitude":10.1456,"time_utc":"2026-10-17 09:00:01.123456 +0000 UTC"},"Message":{"PositionReport":{"Sog":5.4,"Cog":123.5,"TrueHeading":511}}}""");
        var fix = AisStreamProvider.Parse(ais.RootElement)!.Value;
        Assert.Equal(54.3721, fix.Lat); Assert.Equal(10.1456, fix.Lng); Assert.Equal(5.4, fix.SogKn); Assert.Equal(123.5, fix.CogDeg);
        Assert.Equal(new DateTimeOffset(2026, 10, 17, 9, 0, 1, TimeSpan.Zero), fix.Time);
        using var other = JsonDocument.Parse("""{"MessageType":"ShipStaticData","MetaData":{}}""");
        Assert.Null(AisStreamProvider.Parse(other.RootElement));

        using var mt = JsonDocument.Parse("""[{"MMSI":"211123456","LAT":"54.3721","LON":"10.1456","SPEED":"54","COURSE":"123","TIMESTAMP":"2026-10-17T09:00:00"},{"LAT":"x"}]""");
        var list = MarineTrafficProvider.Parse(mt.RootElement);
        var m = Assert.Single(list);
        Assert.Equal(5.4, m.SogKn); Assert.Equal(123, m.CogDeg); Assert.Equal(DateTimeOffset.Parse("2026-10-17T09:00:00Z"), m.Time);
    }
}
