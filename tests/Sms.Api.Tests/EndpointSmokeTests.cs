using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sms.Api.Tests;

/// <summary>
/// Calls every registered API route as anonymous, member and admin with plausible ids and an empty JSON body.
/// Nothing may answer 500: binding and validation must turn bad input into 4xx (spec 11 §11.4 "no unhandled
/// exceptions for user input"). Mutating routes run last so the GET sweep sees a complete scenario.
/// </summary>
public class EndpointSmokeTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> IdOf(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }

    // routes that would start long-running or external work
    private static readonly string[] Skip = ["/api/admin/routing/rebuild", "/api/admin/seamarks/import-osm", "/api/admin/harbours/import-osm", "/api/admin/routing/import-waterways"];

    [Fact]
    public async Task No_route_answers_500_for_any_caller()
    {
        var (owner, ownerId) = await f.UserAsync();
        var (admin, _) = await f.AdminAsync();
        var anon = f.Anonymous();
        var ids = await ScenarioAsync(owner, ownerId, admin);

        var routes = f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } t && t.StartsWith("/api/"))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(m => (method: m, template: e.RoutePattern.RawText!)))
            .Distinct().OrderBy(r => r.method == "GET" ? 0 : r.method == "POST" ? 1 : r.method == "PUT" ? 2 : 3).ThenBy(r => r.template).ToList();
        Assert.True(routes.Count > 100, $"only {routes.Count} routes found");

        var failures = new List<string>();
        foreach (var (method, template) in routes)
        {
            if (Skip.Any(s => template.StartsWith(s))) continue;
            var url = Fill(template, ids);
            if (url is null) { failures.Add($"unmapped parameter in {template}"); continue; }
            foreach (var (name, client) in new[] { ("anon", anon), ("owner", owner), ("admin", admin) })
            {
                using var req = new HttpRequestMessage(new HttpMethod(method), url);
                if (method is "POST" or "PUT") req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                HttpResponseMessage resp;
                try { resp = await client.SendAsync(req); }
                catch (Exception ex) { failures.Add($"{method} {url} ({name}): {ex.GetType().Name} {ex.Message}"); continue; }
                if ((int)resp.StatusCode >= 500) failures.Add($"{method} {url} ({name}): {(int)resp.StatusCode}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>A member's ship with trip, logbook entry, post, photo, crew, expense, page, device etc.</summary>
    private async Task<Dictionary<string, string>> ScenarioAsync(HttpClient owner, Guid ownerId, HttpClient admin)
    {
        var ids = new Dictionary<string, string>();
        var harbour = (await owner.GetFromJsonAsync<JsonElement>("/api/harbours?q=Kiel%20D"))[0].GetProperty("id").GetGuid();
        var laboe = (await owner.GetFromJsonAsync<JsonElement>("/api/harbours?q=Laboe"))[0].GetProperty("id").GetGuid();
        var ship = await IdOf(await owner.PostAsJsonAsync("/api/ships", new { name = "Smoke Boat", cruisingSpeedKn = 5, visibility = "Public", mmsi = "211999999" }));
        var trip = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/trips", new { name = "Smoke trip", startDate = "2026-10-10", endDate = "2026-10-20", visibility = "Public", status = "Active", currency = "EUR" }));
        var entry = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/logbook", new { tripId = trip, startHarbourId = harbour, endHarbourId = laboe, departedAt = "2026-10-12T08:00:00Z", arrivedAt = "2026-10-12T10:00:00Z", visibility = "Public", crew = Array.Empty<object>() }));
        var crew = await IdOf(await owner.PostAsJsonAsync($"/api/trips/{trip}/crew", new { guestName = "Gast" }));
        var post = await IdOf(await owner.PostAsJsonAsync($"/api/trips/{trip}/posts", new { title = "Smoke", content = "<p>x</p>", visibility = "Public" }));
        using var img = new Image<Rgba32>(8, 8); using var ms = new MemoryStream(); await img.SaveAsJpegAsync(ms);
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(ms.ToArray()); content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg"); form.Add(content, "files", "s.jpg");
        var media = (await (await owner.PostAsync($"/api/trips/{trip}/media", form)).Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetGuid();
        var expense = await IdOf(await owner.PostAsJsonAsync($"/api/trips/{trip}/expenses", new { amount = 10, currency = "EUR", paidOn = "2026-10-12", category = "Diesel", splitMode = "Equal" }));
        var settlement = await IdOf(await owner.PostAsJsonAsync($"/api/trips/{trip}/settlements", new { toCrewMemberId = crew, amount = 1 }));
        var task = await IdOf(await owner.PostAsJsonAsync($"/api/trips/{trip}/tasks", new { title = "t", done = false }));
        var wifi = await IdOf(await owner.PostAsJsonAsync($"/api/harbours/{harbour}/wifis", new { name = "w", speed = 1, free = true }));
        var page = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/pages", new { title = "Über uns", content = "<p>p</p>" }));
        var domain = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/domains", new { host = "smoke-boat.example.com" }));
        var device = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/tracking-devices", new { name = "Phone", protocol = "OsmAnd" }));
        var zone = await IdOf(await owner.PostAsJsonAsync($"/api/ships/{ship}/privacy-zones", new { name = "z", lat = 54.3, lng = 10.1, radiusM = 500 }));
        var comment = await IdOf(await owner.PostAsJsonAsync($"/api/posts/{post}/comments", new { text = "c" }));
        (await owner.PostAsJsonAsync("/api/reports", new { targetType = "Post", targetId = post, reason = "r" })).EnsureSuccessStatusCode();
        var report = (await admin.GetFromJsonAsync<JsonElement>("/api/admin/reports")).EnumerateArray().First(r => r.GetProperty("targetId").GetGuid() == post).GetProperty("id").GetGuid();
        var correction = await IdOf(await admin.PostAsJsonAsync("/api/admin/routing/corrections", new { kind = "Passage", geometry = new { type = "LineString", coordinates = new[] { new[] { 10.1, 54.3 }, new[] { 10.2, 54.4 } } }, note = "s", active = true }));
        (await admin.PutAsJsonAsync("/api/pages/smoke", new { slug = "smoke", title = "Smoke", content = "<p>s</p>", language = "de", showInFooter = false })).EnsureSuccessStatusCode();
        var notification = (await owner.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("items").EnumerateArray().Select(n => n.GetProperty("id").GetGuid()).FirstOrDefault();

        ids["shipId"] = ship.ToString(); ids["tripId"] = trip.ToString(); ids["harbourId"] = harbour.ToString(); ids["userId"] = ownerId.ToString(); ids["targetId"] = ownerId.ToString();
        ids["ships"] = ship.ToString(); ids["trips"] = trip.ToString(); ids["logbook"] = entry.ToString(); ids["posts"] = post.ToString(); ids["media"] = media.ToString(); ids["harbours"] = harbour.ToString();
        ids["crew"] = crew.ToString(); ids["expenses"] = expense.ToString(); ids["settlements"] = settlement.ToString(); ids["tasks"] = task.ToString(); ids["wifis"] = wifi.ToString();
        ids["ship-pages"] = page.ToString(); ids["domains"] = domain.ToString(); ids["tracking-devices"] = device.ToString(); ids["privacy-zones"] = zone.ToString(); ids["comments"] = comment.ToString();
        ids["reports"] = report.ToString(); ids["corrections"] = correction.ToString(); ids["notifications"] = notification.ToString(); ids["users"] = ownerId.ToString(); ids["members"] = ownerId.ToString();
        ids["revisions"] = Guid.NewGuid().ToString(); ids["revisionId"] = Guid.NewGuid().ToString(); ids["merge-into"] = Guid.NewGuid().ToString();
        return ids;
    }

    /// <summary>Replaces route parameters with scenario ids; the id of "{id}" comes from the segment before it.</summary>
    private static string? Fill(string template, Dictionary<string, string> ids)
    {
        var segments = template.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var m = Regex.Match(segments[i], @"^\{\*?(\w+)(:[^}]*)?\}(\.\w+)?$");
            if (!m.Success) continue;
            var name = m.Groups[1].Value; var suffix = m.Groups[3].Value;
            string? value = name switch
            {
                "id" => i > 0 && ids.TryGetValue(segments[i - 1], out var v) ? v : Guid.NewGuid().ToString(),
                "targetType" => "posts", "variant" => "thumb", "kind" => "users", "action" => template.Contains("costs") ? "lock" : "accept",
                "token" => "no-such-token", "slug" => "smoke", "path" => "ueber-uns", "lang" => "de",
                "z" => "12", "x" => "2163", "y" => "1307", "targetId" => ids["targetId"],
                "fontstack" => "Noto Sans Regular", "range" => "0-255", "file" => "ofm.json", "layer" => "radar",
                _ => ids.GetValueOrDefault(name),
            };
            if (value is null) return null;
            segments[i] = value + suffix;
        }
        return string.Join('/', segments);
    }
}
