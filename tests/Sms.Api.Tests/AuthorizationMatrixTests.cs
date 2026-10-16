using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sms.Api.Tests;

/// <summary>Spec 08 §8.3/§8.4: visibility × role matrix for posts, logbook entries, costs, trips.</summary>
public class AuthorizationMatrixTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<Guid> IdOf(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<(Guid ship, Guid trip, Guid kiel, Guid laboe)> SetupShipAndTrip(HttpClient owner, string shipVisibility, string tripVisibility)
    {
        var harbours = await owner.GetFromJsonAsync<JsonElement>("/api/harbours?q=Kiel%20D");
        var kiel = harbours[0].GetProperty("id").GetGuid();
        var laboe = (await owner.GetFromJsonAsync<JsonElement>("/api/harbours?q=Laboe"))[0].GetProperty("id").GetGuid();
        var ship = await IdOf(await owner.PostAsJsonAsync("/api/ships", new { name = "Matrix", cruisingSpeedKn = 5, visibility = shipVisibility }));
        var trip = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/trips", new { name = "Matrix trip", startDate = "2026-10-10", endDate = "2026-10-20", visibility = tripVisibility, status = "Active", currency = "EUR" }));
        return (ship, trip, kiel, laboe);
    }

    [Theory]
    [InlineData("Public", "Public", "Public", true, true, true, true)]
    [InlineData("Public", "Public", "Crew", false, false, false, true)]
    [InlineData("Public", "Public", "Private", false, false, false, false)]
    [InlineData("Public", "Crew", "Public", false, false, false, true)]   // public item inside a crew-only trip stays crew-only (07 §7.6 #1)
    [InlineData("Private", "Public", "Public", false, false, false, true)] // private ship caps everything at crew
    public async Task Post_visibility_matrix(string ship, string trip, string item, bool anonymous, bool registered, bool ownerNotCrew, bool crew)
    {
        var (skipper, _) = await f.UserAsync();
        var (ids, tripId, _, _) = await SetupShipAndTrip(skipper, ship, trip);
        var (ownerClient, ownerId) = await f.UserAsync();
        (await skipper.PostAsJsonAsync($"/api/ships/{ids}/members", new { userId = ownerId, role = "Owner" })).EnsureSuccessStatusCode();
        var (crewClient, crewId) = await f.UserAsync();
        var member = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{tripId}/crew", new { userId = crewId }));
        (await crewClient.PostAsync($"/api/crew/{member}/accept", null)).EnsureSuccessStatusCode();
        var (registeredClient, _) = await f.UserAsync();
        var post = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{tripId}/posts", new { title = "t", content = "<p>x</p>", visibility = item }));

        Assert.Equal(anonymous, (await f.Anonymous().GetAsync($"/api/posts/{post}")).IsSuccessStatusCode);
        Assert.Equal(registered, (await registeredClient.GetAsync($"/api/posts/{post}")).IsSuccessStatusCode);
        Assert.Equal(ownerNotCrew, (await ownerClient.GetAsync($"/api/posts/{post}")).IsSuccessStatusCode);
        Assert.Equal(crew, (await crewClient.GetAsync($"/api/posts/{post}")).IsSuccessStatusCode);
        Assert.True((await skipper.GetAsync($"/api/posts/{post}")).IsSuccessStatusCode); // author always
        // timeline applies the same filter
        var days = await crewClient.GetFromJsonAsync<JsonElement>($"/api/trips/{tripId}/timeline");
        Assert.Equal(crew, days.EnumerateArray().Any(d => d.GetProperty("posts").EnumerateArray().Any(p => p.GetProperty("id").GetGuid() == post)));
    }

    [Fact]
    public async Task Costs_are_crew_only_and_logbook_needs_permission()
    {
        var (skipper, _) = await f.UserAsync();
        var (ship, trip, kiel, laboe) = await SetupShipAndTrip(skipper, "Public", "Public");
        var (ownerClient, ownerId) = await f.UserAsync();
        (await skipper.PostAsJsonAsync($"/api/ships/{ship}/members", new { userId = ownerId, role = "Owner" })).EnsureSuccessStatusCode();
        var (crewClient, crewId) = await f.UserAsync();
        var member = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{trip}/crew", new { userId = crewId }));
        (await crewClient.PostAsync($"/api/crew/{member}/accept", null)).EnsureSuccessStatusCode();
        var (invitedClient, invitedId) = await f.UserAsync();
        await skipper.PostAsJsonAsync($"/api/trips/{trip}/crew", new { userId = invitedId }); // invited, not accepted

        var expense = new { amount = 30, currency = "EUR", paidOn = "2026-10-12", category = "Hafengeld", splitMode = "Equal" };
        Assert.Equal(HttpStatusCode.Created, (await crewClient.PostAsJsonAsync($"/api/trips/{trip}/expenses", expense)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.PostAsJsonAsync($"/api/trips/{trip}/expenses", expense)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await invitedClient.GetAsync($"/api/trips/{trip}/balances")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.GetAsync($"/api/trips/{trip}/balances")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous().GetAsync($"/api/trips/{trip}/balances")).StatusCode);
        var balances = await crewClient.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/balances");
        Assert.Equal(30m, balances.GetProperty("total").GetDecimal());

        var entry = new { tripId = trip, startHarbourId = kiel, endHarbourId = laboe, departedAt = "2026-10-12T08:00:00Z", arrivedAt = "2026-10-12T10:00:00Z", visibility = "Public", crew = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.Forbidden, (await crewClient.PostAsJsonAsync($"/api/ships/{ship}/logbook", entry)).StatusCode);   // no "Logbuch schreiben"
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.PostAsJsonAsync($"/api/ships/{ship}/logbook", entry)).StatusCode);  // owner not crew
        (await skipper.PutAsJsonAsync($"/api/crew/{member}", new { canWriteLogbook = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, (await crewClient.PostAsJsonAsync($"/api/ships/{ship}/logbook", entry)).StatusCode);
        // entries without trip: ship members only
        var noTrip = new { startHarbourId = kiel, endHarbourId = laboe, departedAt = "2026-09-01T08:00:00Z", arrivedAt = "2026-09-01T10:00:00Z", visibility = "Crew", crew = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.Created, (await ownerClient.PostAsJsonAsync($"/api/ships/{ship}/logbook", noTrip)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await crewClient.PostAsJsonAsync($"/api/ships/{ship}/logbook", noTrip)).StatusCode);
        var listForCrew = await crewClient.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/logbook");
        Assert.Equal(1, listForCrew.GetProperty("total").GetInt32()); // crew sees the trip entry, not the ship-only crew entry
        var listForOwner = await ownerClient.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/logbook");
        Assert.Equal(2, listForOwner.GetProperty("total").GetInt32()); // owner sees public trip entry + own ship entry
    }

    [Fact]
    public async Task Trip_crud_rights_and_last_owner_rule()
    {
        var (owner, ownerId) = await f.UserAsync();
        var (ship, trip, _, _) = await SetupShipAndTrip(owner, "Public", "Crew");
        var (other, otherId) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync($"/api/trips/{trip}", new { name = "x", startDate = "2026-10-10", endDate = "2026-10-20", visibility = "Crew", status = "Active" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/trips/{trip}")).StatusCode); // crew-only trip invisible
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/ships/{ship}/members/{ownerId}")).StatusCode); // last owner stays
        (await owner.PostAsJsonAsync($"/api/ships/{ship}/members", new { userId = otherId, role = "Skipper" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/ships/{ship}/members", new { userId = otherId, role = "Owner" })).StatusCode); // skipper cannot manage members
        Assert.True((await other.GetAsync($"/api/trips/{trip}")).IsSuccessStatusCode); // ship members see the trip header
    }

    [Fact]
    public async Task Csrf_header_is_required_for_state_changes()
    {
        var (client, _) = await f.UserAsync();
        client.DefaultRequestHeaders.Remove("X-Requested-With");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/ships", new { name = "x", cruisingSpeedKn = 5, visibility = "Public" })).StatusCode);
    }
}
