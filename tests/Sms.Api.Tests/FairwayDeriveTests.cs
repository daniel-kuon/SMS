using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sms.Api.Tests;

/// <summary>Spec 10 §10.5 step 2: fairways derived from imported lateral marks become derived passages.</summary>
public class FairwayDeriveTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Derives_a_fairway_from_paired_lateral_marks()
    {
        var (admin, _) = await f.AdminAsync();
        using var form = new MultipartFormDataContent();
        var json = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "fairway-marks.json")));
        json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(json, "files", "marks.json");
        (await admin.PostAsync("/api/admin/seamarks/import-file", form)).EnsureSuccessStatusCode();

        var r = await admin.PostAsync("/api/admin/routing/derive-fairways?bbox=54.59,9.99,54.63,10.06", null);
        r.EnsureSuccessStatusCode();
        var result = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, result.GetProperty("marks").GetInt32()); // the cardinal mark is not lateral
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(3, result.GetProperty("lines")[0].GetProperty("pairs").GetInt32());
        var corrections = await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/corrections");
        var fairway = corrections.EnumerateArray().Single(c => c.GetProperty("note").GetString()!.StartsWith("Fahrwasser"));
        var coords = fairway.GetProperty("geometry").GetProperty("coordinates");
        Assert.Equal(3, coords.GetArrayLength());
        Assert.Equal(10.0, coords[0][0].GetDouble(), 4); // midpoints on the channel axis

        var again = await (await admin.PostAsync("/api/admin/routing/derive-fairways?bbox=54.59,9.99,54.63,10.06", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, again.GetProperty("removed").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/routing/derive-fairways?bbox=1,2", null)).StatusCode);
        var (user, _) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/admin/routing/derive-fairways?bbox=54.59,9.99,54.63,10.06", null)).StatusCode);
    }
}
