using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sms.Api.Tests;

/// <summary>Fahrwasser-Feedback: groups, a passage marked wrong (snapshot), a drawn fairway, export and the regression check.</summary>
public class FeedbackTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static object Line(params double[][] c) => new { type = "LineString", coordinates = c };
    private static async Task<JsonElement> Json(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return await r.Content.ReadFromJsonAsync<JsonElement>(); }

    [Fact]
    public async Task Admin_collects_feedback_exports_it_and_checks_it_against_the_current_passages()
    {
        var (admin, _) = await f.AdminAsync();
        var geometry = Line([11.90, 55.00], [11.92, 55.00]);
        var corr = (await Json(await admin.PostAsJsonAsync("/api/admin/routing/corrections", new { kind = "Passage", geometry, note = "Fahrwasser (3 Tonnenpaare)", active = true }))).GetProperty("id").GetGuid();

        var layers = await admin.GetFromJsonAsync<JsonElement>("/api/admin/feedback/layers?bbox=54.9,11.8,55.1,12.0");
        Assert.Contains(layers.GetProperty("passages").EnumerateArray(), p => p.GetProperty("id").GetGuid() == corr);

        var group = (await Json(await admin.PostAsJsonAsync("/api/admin/feedback/groups", new { name = "Bøgestrøm", description = "Tonnenstrich" }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/feedback/groups/{group}/items", new { kind = "MissingFairway", note = "ohne Linie" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/feedback/groups/{group}/items", new { kind = "WrongObject", objectRef = $"correction:{Guid.NewGuid()}" })).StatusCode);

        var wrong = await Json(await admin.PostAsJsonAsync($"/api/admin/feedback/groups/{group}/items", new { kind = "WrongObject", objectRef = $"correction:{corr}", expectation = "sollte nicht existieren" }));
        Assert.Equal("Fahrwasser (3 Tonnenpaare)", wrong.GetProperty("snapshot").GetProperty("note").GetString());
        Assert.Equal("manual", wrong.GetProperty("snapshot").GetProperty("category").GetString());
        Assert.Equal("LineString", wrong.GetProperty("geometry").GetProperty("type").GetString());
        (await admin.PostAsJsonAsync($"/api/admin/feedback/groups/{group}/items", new { kind = "MissingFairway", geometry = Line([11.90, 55.0002], [11.91, 55.0002], [11.92, 55.0002]), note = "Hauptfahrwasser" })).EnsureSuccessStatusCode();

        var groups = await admin.GetFromJsonAsync<JsonElement>("/api/admin/feedback/groups");
        var g = groups.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == group);
        Assert.Equal(2, g.GetProperty("items").GetInt32());
        Assert.Equal(1, g.GetProperty("wrong").GetInt32());

        var export = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/feedback/groups/{group}/export");
        var fc = export.GetProperty("group");
        Assert.Equal("FeatureCollection", fc.GetProperty("type").GetString());
        Assert.Equal(2, fc.GetProperty("features").GetArrayLength());
        Assert.Equal("Fahrwasser (3 Tonnenpaare)", fc.GetProperty("features")[0].GetProperty("properties").GetProperty("snapshot").GetProperty("note").GetString());
        Assert.Contains((await admin.GetFromJsonAsync<JsonElement>("/api/admin/feedback/export")).GetProperty("groups").EnumerateArray(), x => x.GetProperty("id").GetGuid() == group);

        var items = (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/feedback/regression?group={group}")).GetProperty("groups")[0].GetProperty("items");
        Assert.Equal("present", items[0].GetProperty("status").GetString()); // the wrong fairway is still derived
        Assert.True(items[0].GetProperty("identical").GetBoolean());
        Assert.True(items[1].GetProperty("satisfied").GetBoolean()); // and runs along the drawn one

        (await admin.DeleteAsync($"/api/admin/routing/corrections/{corr}")).EnsureSuccessStatusCode(); // "re-derived" without it
        items = (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/feedback/regression?group={group}")).GetProperty("groups")[0].GetProperty("items");
        Assert.Equal("gone", items[0].GetProperty("status").GetString());
        Assert.False(items[0].GetProperty("refExists").GetBoolean());
        Assert.Equal("missing", items[1].GetProperty("status").GetString());

        (await admin.PutAsJsonAsync($"/api/admin/feedback/groups/{group}", new { name = "Bøgestrøm", status = "Resolved" })).EnsureSuccessStatusCode();
        Assert.DoesNotContain((await admin.GetFromJsonAsync<JsonElement>("/api/admin/feedback/export?status=open")).GetProperty("groups").EnumerateArray(), x => x.GetProperty("id").GetGuid() == group);

        var (user, _) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/feedback/groups")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/feedback/groups/{group}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/feedback/groups/{group}/items")).StatusCode);
    }

    [Fact]
    public async Task A_corrected_object_keeps_the_original_and_is_checked_against_both_lines()
    {
        var (admin, _) = await f.AdminAsync();
        // the "derived" line detours ≈ 1.1 km north; the correction runs straight
        var original = Line([12.10, 55.0], [12.11, 55.0], [12.115, 55.01], [12.12, 55.0], [12.13, 55.0]);
        var corr = (await Json(await admin.PostAsJsonAsync("/api/admin/routing/corrections", new { kind = "Passage", geometry = original, note = "Fahrwasser (5 Tonnenpaare)", active = true }))).GetProperty("id").GetGuid();
        var group = (await Json(await admin.PostAsJsonAsync("/api/admin/feedback/groups", new { name = "Korrekturen" }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/feedback/groups/{group}/items", new { kind = "CorrectedObject", geometry = Line([12.10, 55.0], [12.13, 55.0]) })).StatusCode); // no object
        var item = await Json(await admin.PostAsJsonAsync($"/api/admin/feedback/groups/{group}/items",
            new { kind = "CorrectedObject", objectRef = $"correction:{corr}", geometry = Line([12.10, 55.0], [12.12, 55.0]), expectation = "geradeaus" }));
        var id = item.GetProperty("id").GetGuid();
        Assert.Equal(2, item.GetProperty("geometry").GetProperty("coordinates").GetArrayLength());
        Assert.Equal(5, item.GetProperty("snapshot").GetProperty("geometry").GetProperty("coordinates").GetArrayLength());

        // the corrected course is edited later (✎): the item takes it, the correction row stays as it was
        (await admin.PutAsJsonAsync($"/api/admin/feedback/items/{id}", new { kind = "CorrectedObject", geometry = Line([12.10, 55.0], [12.13, 55.0]), expectation = "geradeaus" })).EnsureSuccessStatusCode();
        var row = (await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/corrections?bbox=54.9,12.0,55.1,12.2")).EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == corr);
        Assert.Equal(5, row.GetProperty("geometry").GetProperty("coordinates").GetArrayLength());

        var export = (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/feedback/groups/{group}/export")).GetProperty("group").GetProperty("features")[0];
        Assert.Equal(2, export.GetProperty("geometry").GetProperty("coordinates").GetArrayLength());
        Assert.Equal(5, export.GetProperty("properties").GetProperty("originalGeometry").GetProperty("coordinates").GetArrayLength());

        var check = (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/feedback/regression?group={group}")).GetProperty("groups")[0].GetProperty("items")[0];
        Assert.False(check.GetProperty("satisfied").GetBoolean()); // still the detour
        // "re-derived": the straight line replaces the detour
        (await admin.PutAsJsonAsync($"/api/admin/routing/corrections/{corr}", new { kind = "Passage", geometry = Line([12.10, 55.0], [12.13, 55.0]), note = "Fahrwasser (5 Tonnenpaare)", active = true })).EnsureSuccessStatusCode();
        check = (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/feedback/regression?group={group}")).GetProperty("groups")[0].GetProperty("items")[0];
        Assert.Equal("corrected", check.GetProperty("status").GetString());
        Assert.True(check.GetProperty("satisfied").GetBoolean());
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>("/api/admin/feedback/groups")).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == group).GetProperty("corrected").GetInt32());
    }

    [Fact]
    public async Task A_drawn_fairway_with_a_branch_is_one_multi_line_item()
    {
        var (admin, _) = await f.AdminAsync();
        var group = (await Json(await admin.PostAsJsonAsync("/api/admin/feedback/groups", new { name = "Abzweig" }))).GetProperty("id").GetGuid();
        var net = new { type = "MultiLineString", coordinates = new[] { new[] { new[] { 12.30, 55.0 }, new[] { 12.31, 55.0 }, new[] { 12.32, 55.0 } }, new[] { new[] { 12.31, 55.0 }, new[] { 12.31, 55.01 } } } };
        var item = await Json(await admin.PostAsJsonAsync($"/api/admin/feedback/groups/{group}/items", new { kind = "MissingFairway", geometry = net }));
        Assert.Equal("MultiLineString", item.GetProperty("geometry").GetProperty("type").GetString());
        // the two branches as passages sharing the junction (how the corrections editor saves them) satisfy it
        foreach (var line in new[] { Line([12.30, 55.0], [12.31, 55.0], [12.32, 55.0]), Line([12.31, 55.0], [12.31, 55.01]) })
            (await admin.PostAsJsonAsync("/api/admin/routing/corrections", new { kind = "Passage", geometry = line, note = "Test", active = true })).EnsureSuccessStatusCode();
        var check = (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/feedback/regression?group={group}")).GetProperty("groups")[0].GetProperty("items")[0];
        Assert.True(check.GetProperty("satisfied").GetBoolean());
    }
}
