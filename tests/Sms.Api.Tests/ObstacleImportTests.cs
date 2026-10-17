using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sms.Api.Tests;

/// <summary>Bridges and locks found in the waterway import become point corrections with clearance.</summary>
public class ObstacleImportTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Import_stores_bridges_with_clearance_and_locks()
    {
        var (admin, _) = await f.AdminAsync();
        using var form = new MultipartFormDataContent();
        var json = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "obstacles.json")));
        json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(json, "files", "obstacles.json");
        var r = await admin.PostAsync("/api/admin/routing/import-waterways-file", form);
        r.EnsureSuccessStatusCode();
        var result = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(1, result.GetProperty("bridges").GetInt32());
        Assert.Equal(1, result.GetProperty("locks").GetInt32());

        var corrections = (await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/corrections")).EnumerateArray().ToList();
        var bridge = corrections.Single(c => c.GetProperty("kind").GetString() == "Bridge" && c.GetProperty("note").GetString()!.Contains("Niedrige"));
        Assert.Equal(4.5, bridge.GetProperty("clearanceM").GetDouble());
        Assert.Equal("Point", bridge.GetProperty("geometry").GetProperty("type").GetString());
        Assert.Contains("4.5 m", bridge.GetProperty("note").GetString());
        Assert.Contains(corrections, c => c.GetProperty("kind").GetString() == "Lock" && c.GetProperty("note").GetString()!.Contains("Testschleuse"));

        // manual bridge with clearance, validated as a point
        var bad = await admin.PostAsJsonAsync("/api/admin/routing/corrections", new { kind = "Bridge", geometry = new { type = "LineString", coordinates = new[] { new[] { 9.5, 54.5 }, new[] { 9.6, 54.5 } } }, note = "x", active = true, clearanceM = 3.0 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, bad.StatusCode);
        var ok = await admin.PostAsJsonAsync("/api/admin/routing/corrections", new { kind = "Bridge", geometry = new { type = "Point", coordinates = new[] { 9.55, 54.5 } }, note = "Manuelle Brücke", active = true, clearanceM = 3.0 });
        Assert.Equal(System.Net.HttpStatusCode.Created, ok.StatusCode);
        corrections = (await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/corrections")).EnumerateArray().ToList();
        Assert.Equal(3.0, corrections.Single(c => c.GetProperty("note").GetString() == "Manuelle Brücke").GetProperty("clearanceM").GetDouble());
    }
}
