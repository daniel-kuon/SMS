using System.Net.Http.Json;
using System.Text.Json;

namespace Sms.Api.Tests;

/// <summary>Harbour detail: ships that visited, following the logbook visibility rules.</summary>
public class HarbourVisitsTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> IdOf(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }

    [Fact]
    public async Task Lists_visiting_ships_with_counts_and_hides_crew_only_entries()
    {
        var (owner, _) = await f.UserAsync();
        var kiel = (await owner.GetFromJsonAsync<JsonElement>("/api/harbours?q=Kiel%20D"))[0].GetProperty("id").GetGuid();
        var laboe = (await owner.GetFromJsonAsync<JsonElement>("/api/harbours?q=Laboe"))[0].GetProperty("id").GetGuid();
        var ship = await IdOf(await owner.PostAsJsonAsync("/api/ships", new { name = "Visitor", cruisingSpeedKn = 5, visibility = "Public" }));
        var trip = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/trips", new { name = "Visits", startDate = "2026-05-01", endDate = "2026-05-10", visibility = "Public", status = "Completed", currency = "EUR" }));
        (await owner.PostAsJsonAsync($"/api/ships/{ship}/logbook", new { tripId = trip, startHarbourId = kiel, endHarbourId = laboe, departedAt = "2026-05-02T08:00:00Z", arrivedAt = "2026-05-02T10:00:00Z", visibility = "Public", crew = Array.Empty<object>() })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/ships/{ship}/logbook", new { tripId = trip, startHarbourId = laboe, endHarbourId = kiel, departedAt = "2026-05-03T08:00:00Z", arrivedAt = "2026-05-03T10:00:00Z", visibility = "Public", crew = Array.Empty<object>() })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/ships/{ship}/logbook", new { tripId = trip, startHarbourId = kiel, endHarbourId = laboe, departedAt = "2026-05-04T08:00:00Z", arrivedAt = "2026-05-04T10:00:00Z", visibility = "Crew", crew = Array.Empty<object>() })).EnsureSuccessStatusCode();

        var anon = await f.Anonymous().GetFromJsonAsync<JsonElement>($"/api/harbours/{laboe}/visits");
        var v = Assert.Single(anon.EnumerateArray());
        Assert.Equal("Visitor", v.GetProperty("name").GetString());
        Assert.Equal(2, v.GetProperty("visits").GetInt32()); // arrival on the 2nd, departure on the 3rd; the crew-only entry is hidden
        Assert.StartsWith("2026-05-03", v.GetProperty("lastVisit").GetString());
        Assert.Equal("Visits", v.GetProperty("lastTripName").GetString());
        var mine = await owner.GetFromJsonAsync<JsonElement>($"/api/harbours/{laboe}/visits");
        Assert.Equal(3, mine[0].GetProperty("visits").GetInt32());
        Assert.Empty((await f.Anonymous().GetFromJsonAsync<JsonElement>($"/api/harbours/{Guid.NewGuid()}/visits")).EnumerateArray());
    }
}
