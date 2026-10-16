using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sms.Api.Tests;

/// <summary>Ship websites (pages, sub pages, domains), tenant resolution by host, global page tree, sea-mark import.</summary>
public class CmsAndTilesTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> IdOf(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }

    [Fact]
    public async Task Owner_builds_a_page_tree_and_visitors_see_published_pages_only()
    {
        var (owner, _) = await f.UserAsync();
        var ship = await IdOf(await owner.PostAsJsonAsync("/api/ships", new { name = "Kairos", cruisingSpeedKn = 5, visibility = "Public" }));
        var home = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/pages", new { title = "Über uns", content = "<p>Hallo</p><script>x()</script>" }));
        var crew = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/pages", new { title = "Die Crew", content = "<p>c</p>", parentId = home }));
        var draft = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/pages", new { title = "Entwurf", content = "", parentId = crew, published = false }));
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/ships/{ship}/pages", new { title = "Über uns", content = "" })).StatusCode); // duplicate slug on root
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/ship-pages/{home}", new { title = "Über uns", content = "", parentId = crew })).StatusCode); // cycle

        var visitor = f.Anonymous();
        var page = await visitor.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/pages/ueber-uns/die-crew");
        Assert.Equal("die-crew", page.GetProperty("slug").GetString());
        Assert.Empty(page.GetProperty("children").EnumerateArray()); // draft hidden
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/api/ships/{ship}/pages/ueber-uns/die-crew/entwurf")).StatusCode);
        var ownerView = await owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/pages/ueber-uns/die-crew");
        Assert.Single(ownerView.GetProperty("children").EnumerateArray());
        var homePage = await visitor.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/pages/ueber-uns");
        Assert.DoesNotContain("<script", homePage.GetProperty("content").GetString());

        var (other, _) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/ships/{ship}/pages", new { title = "x", content = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync($"/api/ship-pages/{home}")).StatusCode);

        // deleting the parent lifts children one level up
        (await owner.DeleteAsync($"/api/ship-pages/{home}")).EnsureSuccessStatusCode();
        Assert.True((await visitor.GetAsync($"/api/ships/{ship}/pages/die-crew")).IsSuccessStatusCode);
        _ = draft;
    }

    [Fact]
    public async Task Domain_switches_the_site_into_ship_mode_with_latest_logbook()
    {
        var (owner, _) = await f.UserAsync();
        var ship = await IdOf(await owner.PostAsJsonAsync("/api/ships", new { name = "Domain Boat", cruisingSpeedKn = 5, visibility = "Public" }));
        var kiel = (await owner.GetFromJsonAsync<JsonElement>("/api/harbours?q=Kiel%20D"))[0].GetProperty("id").GetGuid();
        var laboe = (await owner.GetFromJsonAsync<JsonElement>("/api/harbours?q=Laboe"))[0].GetProperty("id").GetGuid();
        (await owner.PostAsJsonAsync($"/api/ships/{ship}/logbook", new { startHarbourId = kiel, endHarbourId = laboe, departedAt = "2026-09-01T08:00:00Z", arrivedAt = "2026-09-01T10:00:00Z", visibility = "Public", crew = Array.Empty<object>() })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/ships/{ship}/logbook", new { startHarbourId = laboe, endHarbourId = kiel, departedAt = "2026-09-02T08:00:00Z", arrivedAt = "2026-09-02T10:00:00Z", visibility = "Crew", crew = Array.Empty<object>() })).EnsureSuccessStatusCode();
        await owner.PostAsJsonAsync($"/api/ships/{ship}/pages", new { title = "Start", content = "<p>Moin</p>" });

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/ships/{ship}/domains", new { host = "not a host" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/ships/{ship}/domains", new { host = "localhost" })).StatusCode);
        var created = await owner.PostAsJsonAsync($"/api/ships/{ship}/domains", new { host = "HTTPS://Domain-Boat.Example.com:443/" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("domain-boat.example.com", (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("host").GetString());
        var (other, _) = await f.UserAsync();
        var otherShip = await IdOf(await other.PostAsJsonAsync("/api/ships", new { name = "Other", cruisingSpeedKn = 5, visibility = "Public" }));
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync($"/api/ships/{otherShip}/domains", new { host = "domain-boat.example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/ships/{ship}/domains")).StatusCode);

        var visitor = f.Anonymous();
        var portal = await visitor.GetFromJsonAsync<JsonElement>("/api/site");
        Assert.Equal("portal", portal.GetProperty("mode").GetString());
        visitor.DefaultRequestHeaders.Host = "domain-boat.example.com";
        var site = await visitor.GetFromJsonAsync<JsonElement>("/api/site");
        Assert.Equal("ship", site.GetProperty("mode").GetString());
        Assert.Equal("Domain Boat", site.GetProperty("ship").GetProperty("name").GetString());
        Assert.Single(site.GetProperty("pages").EnumerateArray());
        Assert.Single(site.GetProperty("latestLogbook").EnumerateArray()); // crew-only entry hidden from the public
        var ownerSite = await owner.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/site-preview");
        Assert.Equal(2, ownerSite.GetProperty("latestLogbook").EnumerateArray().Count());
    }

    [Fact]
    public async Task Global_pages_form_a_tree_for_navigation()
    {
        var (admin, _) = await f.AdminAsync();
        (await admin.PutAsJsonAsync("/api/pages/hilfe", new { slug = "hilfe", title = "Hilfe", content = "<p>h</p>", language = "de", showInFooter = true, showInNav = true })).EnsureSuccessStatusCode();
        var tree = await f.Anonymous().GetFromJsonAsync<JsonElement>("/api/pages?lang=de");
        var hilfe = tree.EnumerateArray().Single(p => p.GetProperty("slug").GetString() == "hilfe");
        (await admin.PutAsJsonAsync("/api/pages/faq", new { slug = "faq", title = "FAQ", content = "<p>?</p>", language = "de", showInFooter = false, parentId = hilfe.GetProperty("id").GetGuid(), order = 1 })).EnsureSuccessStatusCode();
        tree = await f.Anonymous().GetFromJsonAsync<JsonElement>("/api/pages?lang=de");
        hilfe = tree.EnumerateArray().Single(p => p.GetProperty("slug").GetString() == "hilfe");
        Assert.Equal("hilfe/faq", hilfe.GetProperty("children")[0].GetProperty("path").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/pages/hilfe", new { slug = "hilfe", title = "Hilfe", content = "", language = "de", showInFooter = true, parentId = hilfe.GetProperty("children")[0].GetProperty("id").GetGuid() })).StatusCode); // cycle
        var (user, _) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync("/api/pages/x", new { slug = "x", title = "x", content = "", language = "de", showInFooter = false })).StatusCode);
    }

    [Fact]
    public async Task Seamarks_import_from_file_and_render_as_vector_tiles()
    {
        var (admin, _) = await f.AdminAsync();
        using var form = new MultipartFormDataContent();
        var json = new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "seamarks.json")));
        json.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(json, "files", "seamarks.json");
        var r = await admin.PostAsync("/api/admin/seamarks/import-file", form);
        r.EnsureSuccessStatusCode();
        var counts = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, counts.GetProperty("created").GetInt32());
        Assert.Equal(1, counts.GetProperty("skipped").GetInt32());
        r = await admin.PostAsync("/api/admin/seamarks/import-file", form); // idempotent re-import
        Assert.Equal(6, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("updated").GetInt32());
        var status = await f.Anonymous().GetFromJsonAsync<JsonElement>("/api/seamarks/status");
        Assert.Equal(6, status.GetProperty("count").GetInt32());
        var (user, _) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/api/admin/seamarks/import-file", form)).StatusCode);

        // tile server against the same database: tile z12 covering Kiel contains the marks, an ocean tile is empty
        var tile = await f.Tiles.GetAsync("/tiles/seamark/12/2163/1307.mvt");
        tile.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.mapbox-vector-tile", tile.Content.Headers.ContentType?.MediaType);
        var bytes = await tile.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 50, $"tile too small: {bytes.Length} bytes");
        Assert.Contains("seamark", System.Text.Encoding.Latin1.GetString(bytes)); // layer name is encoded in the MVT
        var etag = tile.Headers.ETag!.Tag;
        var req = new HttpRequestMessage(HttpMethod.Get, "/tiles/seamark/12/2163/1307.mvt");
        req.Headers.IfNoneMatch.ParseAdd(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await f.Tiles.SendAsync(req)).StatusCode);
        var empty = await f.Tiles.GetAsync("/tiles/seamark/12/0/0.mvt");
        Assert.Empty(await empty.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await f.Tiles.GetAsync("/tiles/seamark/3/99/99.mvt")).StatusCode);
        var tilejson = await f.Tiles.GetFromJsonAsync<JsonElement>("/tiles/seamark.json");
        Assert.Equal("seamark", tilejson.GetProperty("name").GetString());
    }
}
