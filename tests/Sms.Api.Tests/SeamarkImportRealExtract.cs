using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sms.Api.Tests;

/// <summary>Optional: set SEAMARK_FILE to a real Overpass extract to import a full region through the API.</summary>
public class SeamarkImportRealExtract(ApiFixture f) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Imports_a_real_overpass_extract_when_configured()
    {
        var path = Environment.GetEnvironmentVariable("SEAMARK_FILE");
        if (path is null || !File.Exists(path)) return;
        var (admin, _) = await f.AdminAsync();
        using var form = new MultipartFormDataContent();
        var json = new ByteArrayContent(await File.ReadAllBytesAsync(path));
        json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(json, "files", "seamarks.json");
        var r = await admin.PostAsync("/api/admin/seamarks/import-file", form);
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, body);
        var counts = JsonDocument.Parse(body).RootElement;
        Assert.True(counts.GetProperty("created").GetInt32() > 100, body);
    }
}
