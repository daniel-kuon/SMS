using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sms.Api.Tests;

/// <summary>Spec 10 §10.4: admin routing corrections (blocks, passages) CRUD with GeoJSON geometries.</summary>
public class RoutingCorrectionTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Admin_creates_toggles_and_deletes_corrections_with_validation()
    {
        var (admin, _) = await f.AdminAsync();
        var passage = new { kind = "Passage", geometry = new { type = "LineString", coordinates = new[] { new[] { 10.15, 54.40 }, new[] { 10.20, 54.45 } } }, note = "Testpassage", active = true };
        var created = await admin.PostAsJsonAsync("/api/admin/routing/corrections", passage);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // a Block needs a polygon, a Passage a line
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/routing/corrections", new { kind = "Block", geometry = passage.geometry, note = "x", active = true })).StatusCode);
        var block = new { kind = "Block", geometry = new { type = "Polygon", coordinates = new[] { new[] { new[] { 10.0, 54.0 }, new[] { 10.1, 54.0 }, new[] { 10.1, 54.1 }, new[] { 10.0, 54.0 } } } }, note = "Sperrgebiet", active = true };
        (await admin.PostAsJsonAsync("/api/admin/routing/corrections", block)).EnsureSuccessStatusCode();

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/corrections");
        var mine = list.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
        Assert.Equal("Manual", mine.GetProperty("source").GetString());
        Assert.Equal("LineString", mine.GetProperty("geometry").GetProperty("type").GetString());
        Assert.True(mine.GetProperty("active").GetBoolean());

        (await admin.PutAsJsonAsync($"/api/admin/routing/corrections/{id}", new { kind = "Passage", geometry = passage.geometry, note = "Testpassage", active = false })).EnsureSuccessStatusCode();
        list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/corrections");
        Assert.False(list.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id).GetProperty("active").GetBoolean());

        var (user, _) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/routing/corrections")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/admin/routing/corrections/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/routing/corrections/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/admin/routing/corrections/{id}")).StatusCode);
    }
}
