using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sms.Api.Tests;

/// <summary>Spec 10 §10.5: waterway centrelines imported as derived passages, reviewable as routing corrections.</summary>
public class WaterwayImportTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static MultipartFormDataContent Fixture()
    {
        var form = new MultipartFormDataContent();
        var json = new ByteArrayContent(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "waterways.json")));
        json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(json, "files", "waterways.json");
        return form;
    }

    [Fact]
    public async Task Import_creates_derived_passages_and_replaces_them_on_reimport()
    {
        var (admin, _) = await f.AdminAsync();
        using var form = Fixture();
        var r = await admin.PostAsync("/api/admin/routing/import-waterways-file", form);
        r.EnsureSuccessStatusCode();
        var result = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal("Nord-Ostsee-Kanal", result.GetProperty("lines")[0].GetProperty("name").GetString());
        Assert.True(result.GetProperty("lines")[0].GetProperty("lengthNm").GetDouble() > 10);

        var corrections = await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/corrections");
        var derived = corrections.EnumerateArray().Where(c => c.GetProperty("source").GetString() == "Derived" && c.GetProperty("note").GetString()!.StartsWith("Nord-Ostsee-Kanal")).ToList();
        Assert.Single(derived);
        Assert.Equal("Passage", derived[0].GetProperty("kind").GetString());
        Assert.Equal("LineString", derived[0].GetProperty("geometry").GetProperty("type").GetString());

        using var again = Fixture();
        var r2 = await (await admin.PostAsync("/api/admin/routing/import-waterways-file", again)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, r2.GetProperty("created").GetInt32());
        Assert.Equal(1, r2.GetProperty("removed").GetInt32()); // idempotent: the old derived line inside the extent is replaced
        var status = await f.Anonymous().GetFromJsonAsync<JsonElement>("/api/routing/status");
        Assert.Equal(1, status.GetProperty("waterways").GetInt32());

        var (user, _) = await f.UserAsync();
        using var denied = Fixture();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/admin/routing/import-waterways-file", denied)).StatusCode);
        var bad = new MultipartFormDataContent { { new ByteArrayContent("nope"u8.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }, "files", "x.json" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/routing/import-waterways-file", bad)).StatusCode);
    }
}
