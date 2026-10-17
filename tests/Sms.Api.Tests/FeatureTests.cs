using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Sms.Api.Tests;

/// <summary>End-to-end coverage of the remaining endpoint groups: harbours, trips/crew, logbook, posts/media, costs, social, tracking, routing, calendar, profile.</summary>
public class FeatureTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> IdOf(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }

    private async Task<(HttpClient skipper, Guid ship, Guid trip, Guid kiel, Guid laboe)> Trip(string name = "Feature trip")
    {
        var (skipper, _) = await f.UserAsync();
        var kiel = (await skipper.GetFromJsonAsync<JsonElement>("/api/harbours?q=Kiel%20D"))[0].GetProperty("id").GetGuid();
        var laboe = (await skipper.GetFromJsonAsync<JsonElement>("/api/harbours?q=Laboe"))[0].GetProperty("id").GetGuid();
        var ship = await IdOf(await skipper.PostAsJsonAsync("/api/ships", new { name = "Feature boat", cruisingSpeedKn = 5, visibility = "Public" }));
        var trip = await IdOf(await skipper.PostAsJsonAsync($"/api/ships/{ship}/trips", new { name, startDate = "2026-10-10", endDate = "2026-10-20", visibility = "Public", status = "Active", currency = "EUR" }));
        return (skipper, ship, trip, kiel, laboe);
    }

    [Fact]
    public async Task Harbours_search_bbox_wifi_reviews_revisions_and_nearby()
    {
        var (user, _) = await f.UserAsync();
        var anon = f.Anonymous();
        var all = await anon.GetFromJsonAsync<JsonElement>("/api/harbours?bbox=9.5,54.0,11.5,55.0");
        Assert.True(all.GetArrayLength() >= 10);
        var h = await IdOf(await user.PostAsJsonAsync("/api/harbours", new { name = "Testhafen", lat = 54.5, lng = 10.3, country = "DE", category = "marina", description = "<p>ok</p><img src=x onerror=alert(1)>" }));
        var detail = await anon.GetFromJsonAsync<JsonElement>($"/api/harbours/{h}");
        Assert.DoesNotContain("onerror", detail.GetProperty("description").GetString());
        (await user.PutAsJsonAsync($"/api/harbours/{h}", new { name = "Testhafen 2", lat = 54.5, lng = 10.3 })).EnsureSuccessStatusCode();
        var revs = await anon.GetFromJsonAsync<JsonElement>($"/api/harbours/{h}/revisions");
        Assert.Single(revs.EnumerateArray());
        (await user.PostAsync($"/api/harbours/{h}/revisions/{revs[0].GetProperty("id").GetGuid()}/revert", null)).EnsureSuccessStatusCode();
        Assert.Equal("Testhafen", (await anon.GetFromJsonAsync<JsonElement>($"/api/harbours/{h}")).GetProperty("name").GetString());

        var wifi = await IdOf(await user.PostAsJsonAsync($"/api/harbours/{h}/wifis", new { name = "Hafen-WLAN", password = "secret", speed = 2, free = true }));
        Assert.Null((await anon.GetFromJsonAsync<JsonElement>($"/api/harbours/{h}")).GetProperty("wifis")[0].GetProperty("password").GetString()); // anonymous: no password
        Assert.Equal("secret", (await user.GetFromJsonAsync<JsonElement>($"/api/harbours/{h}")).GetProperty("wifis")[0].GetProperty("password").GetString());
        (await user.DeleteAsync($"/api/wifis/{wifi}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync($"/api/harbours/{h}/reviews", new { rating = 5 })).StatusCode);
        (await user.PostAsJsonAsync($"/api/harbours/{h}/reviews", new { rating = 4, text = "nett" })).EnsureSuccessStatusCode();
        Assert.Equal(4, (await anon.GetFromJsonAsync<JsonElement>($"/api/harbours/{h}")).GetProperty("averageRating").GetDouble());

        var nearby = await anon.GetFromJsonAsync<JsonElement>($"/api/routing/nearby-ports?harbour={h}&maxNm=40&limit=5");
        Assert.True(nearby.GetProperty("ports").GetArrayLength() > 0);
        Assert.Contains(nearby.GetProperty("method").GetString(), new[] { "straight-line", "sea-graph" });
        var route = await anon.GetFromJsonAsync<JsonElement>("/api/routing/route?from=54.45,10.25&to=54.55,10.03");
        Assert.True(route.GetProperty("distanceNm").GetDecimal() > 5);
        Assert.Equal("LineString", route.GetProperty("geometry").GetProperty("type").GetString());
        var status = await anon.GetFromJsonAsync<JsonElement>("/api/routing/status");
        Assert.True(status.TryGetProperty("method", out _));
    }

    [Fact]
    public async Task Trips_crew_guests_invitations_planner_and_ical()
    {
        var (skipper, ship, trip, kiel, laboe) = await Trip();
        var guest = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{trip}/crew", new { guestName = "Anna", canWriteLogbook = true }));
        var (friend, friendId) = await f.UserAsync();
        var invited = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{trip}/crew", new { userId = friendId }));
        var mine = await friend.GetFromJsonAsync<JsonElement>("/api/trips");
        Assert.Equal("Invited", mine[0].GetProperty("myStatus").GetString());
        (await friend.PostAsync($"/api/crew/{invited}/accept", null)).EnsureSuccessStatusCode();
        var dto = await friend.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}");
        Assert.True(dto.GetProperty("isCrew").GetBoolean());
        Assert.False(dto.GetProperty("canEdit").GetBoolean());
        Assert.Equal(3, dto.GetProperty("crew").GetArrayLength());
        // link the guest to the friend's account is refused while the friend already is crew? -> only a free user
        (await skipper.PutAsJsonAsync($"/api/crew/{guest}", new { guestName = "Anna B.", canWriteLogbook = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await friend.DeleteAsync($"/api/crew/{guest}")).StatusCode);
        // invitation link
        var link = await skipper.PostAsJsonAsync($"/api/trips/{trip}/invitations", new { });
        var token = (await link.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var (newcomer, _) = await f.UserAsync();
        Assert.True((await newcomer.GetAsync($"/api/trip-invitations/{token}")).IsSuccessStatusCode);
        (await newcomer.PostAsync($"/api/trip-invitations/{token}/accept", null)).EnsureSuccessStatusCode();
        Assert.Equal(4, (await skipper.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/crew")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Gone, (await newcomer.PostAsync("/api/trip-invitations/nope/accept", null)).StatusCode);
        // planner legs
        var legs = await skipper.PutAsJsonAsync($"/api/trips/{trip}/legs", new[] { new { fromHarbourId = kiel, toHarbourId = laboe }, new { fromHarbourId = laboe, toHarbourId = kiel } });
        legs.EnsureSuccessStatusCode();
        var legDto = await legs.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, legDto.GetArrayLength());
        Assert.True(legDto[0].GetProperty("distanceNm").GetDecimal() > 1);
        Assert.True(legDto[0].GetProperty("etaHours").GetDecimal() > 0);
        // ical feeds
        var feeds = await skipper.GetFromJsonAsync<JsonElement>("/api/calendar/my-feeds");
        var ics = await f.Anonymous().GetStringAsync(feeds.GetProperty("user").GetString()!.Replace("http://localhost", ""));
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("Feature trip", ics);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Anonymous().GetAsync("/api/calendar/users/invalid.ics")).StatusCode);
        _ = ship;
    }

    [Fact]
    public async Task Logbook_defaults_timeline_posts_and_photo_upload()
    {
        var (skipper, ship, trip, kiel, laboe) = await Trip("Photo trip");
        var defaults = await skipper.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/logbook/new-defaults?trip={trip}");
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("startHarbourId").ValueKind);
        var entry = await IdOf(await skipper.PostAsJsonAsync($"/api/ships/{ship}/logbook", new { tripId = trip, startHarbourId = kiel, endHarbourId = laboe, departedAt = "2026-10-12T08:00:00Z", arrivedAt = "2026-10-12T12:30:00Z", logStart = 100, logEnd = 108.5, engineHoursStart = 10, engineHoursEnd = 11.5, windSpeed = "4 Bft", windDirection = "SW", visibility = "Public", crew = Array.Empty<object>() }));
        defaults = await skipper.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/logbook/new-defaults?trip={trip}");
        Assert.Equal(laboe, defaults.GetProperty("startHarbourId").GetGuid());
        Assert.Equal(108.5m, defaults.GetProperty("logStart").GetDecimal());
        Assert.Equal(11.5m, defaults.GetProperty("engineHoursStart").GetDecimal());

        // photo upload (generated JPEG with no EXIF -> day = today or trip end)
        using var img = new Image<Rgba32>(64, 48);
        using var ms = new MemoryStream();
        await img.SaveAsJpegAsync(ms);
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(content, "files", "test.jpg");
        form.Add(new StringContent("Public"), "visibility");
        form.Add(new StringContent("2026-10-12"), "day");
        var up = await skipper.PostAsync($"/api/trips/{trip}/media", form);
        up.EnsureSuccessStatusCode();
        var media = await up.Content.ReadFromJsonAsync<JsonElement>();
        var mediaId = media[0].GetProperty("id").GetGuid();
        Assert.Equal(64, media[0].GetProperty("width").GetInt32());
        Assert.Equal("Harbour", media[0].GetProperty("locationSource").GetString()); // placed at the day's end harbour
        var thumb = await f.Anonymous().GetAsync($"/api/media/{mediaId}/thumb");
        Assert.Equal("image/jpeg", thumb.Content.Headers.ContentType?.MediaType);
        using var bad = new MultipartFormDataContent();
        var txt = new ByteArrayContent("hello"u8.ToArray()); txt.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        bad.Add(txt, "files", "x.txt");
        Assert.Equal(HttpStatusCode.BadRequest, (await skipper.PostAsync($"/api/trips/{trip}/media", bad)).StatusCode);

        var post = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{trip}/posts", new { title = "Tag 3", content = "<p>Moin</p>", day = "2026-10-12", visibility = "Public", mediaIds = new[] { mediaId } }));
        var timeline = await f.Anonymous().GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/timeline");
        var day = timeline.EnumerateArray().Single(d => d.GetProperty("date").GetString() == "2026-10-12");
        Assert.Single(day.GetProperty("logbook").EnumerateArray());
        Assert.Single(day.GetProperty("posts").EnumerateArray());
        Assert.Single(day.GetProperty("posts")[0].GetProperty("media").EnumerateArray());
        Assert.Empty(day.GetProperty("photos").EnumerateArray()); // attached to the post, not loose
        var detail = await f.Anonymous().GetFromJsonAsync<JsonElement>($"/api/logbook/{entry}");
        Assert.Equal("4:30", detail.GetProperty("entry").GetProperty("duration").GetString());
        Assert.Single(detail.GetProperty("days").EnumerateArray());
        (await skipper.DeleteAsync($"/api/posts/{post}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await f.Anonymous().GetAsync($"/api/posts/{post}")).StatusCode);
        var list = await f.Anonymous().GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/logbook?sort=departedAt");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var overview = await f.Anonymous().GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/overview");
        Assert.Equal(8.5m, overview.GetProperty("stats").GetProperty("distanceNm").GetDecimal());
    }

    [Fact]
    public async Task Costs_split_modes_currencies_settlements_lock_and_export()
    {
        var (skipper, _, trip, _, _) = await Trip("Cost trip");
        var anna = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{trip}/crew", new { guestName = "Anna" }));
        var ben = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{trip}/crew", new { guestName = "Ben" }));
        var me = (await skipper.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}")).GetProperty("me").GetProperty("id").GetGuid();
        (await skipper.PostAsJsonAsync($"/api/trips/{trip}/expenses", new { amount = 36, currency = "EUR", paidOn = "2026-10-12", category = "Hafengeld", splitMode = "Equal" })).EnsureSuccessStatusCode();
        (await skipper.PostAsJsonAsync($"/api/trips/{trip}/expenses", new { amount = 90, currency = "EUR", paidOn = "2026-10-12", category = "Diesel", paidByCrewMemberId = ben, splitMode = "Equal" })).EnsureSuccessStatusCode();
        (await skipper.PostAsJsonAsync($"/api/trips/{trip}/expenses", new { amount = 50, currency = "EUR", paidOn = "2026-10-13", category = "Restaurant", paidByCrewMemberId = anna, splitMode = "Equal", participants = new[] { new { crewMemberId = me }, new { crewMemberId = ben } } })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await skipper.PostAsJsonAsync($"/api/trips/{trip}/expenses", new { amount = 10, currency = "EUR", paidOn = "2026-10-12", category = "x", splitMode = "Exact", participants = new[] { new { crewMemberId = me, exactAmount = 4 }, new { crewMemberId = ben, exactAmount = 5 } } })).StatusCode);
        // manual rate for a currency the ECB does not list (or before the rate job ran)
        (await skipper.PostAsJsonAsync($"/api/trips/{trip}/expenses", new { amount = 1000, currency = "XYZ", exchangeRate = 0.1, paidOn = "2026-10-12", category = "Sonstiges", splitMode = "Weights", participants = new[] { new { crewMemberId = me, weight = 2 }, new { crewMemberId = anna, weight = 1 }, new { crewMemberId = ben, weight = 1 } } })).EnsureSuccessStatusCode();
        var balances = await skipper.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/balances");
        Assert.Equal(276m, balances.GetProperty("total").GetDecimal()); // 36 + 90 + 50 + 100
        Assert.Equal(0m, balances.GetProperty("balances").EnumerateArray().Sum(b => b.GetProperty("balance").GetDecimal()));
        var transfers = balances.GetProperty("suggestedTransfers").EnumerateArray().ToList();
        Assert.True(transfers.Count is >= 1 and <= 2);
        var tr = transfers[0];
        var settlement = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{trip}/settlements", new { toCrewMemberId = tr.GetProperty("toCrewMemberId").GetGuid(), amount = tr.GetProperty("amount").GetDecimal(), fromCrewMemberId = tr.GetProperty("fromCrewMemberId").GetGuid() }));
        (await skipper.PostAsync($"/api/settlements/{settlement}/confirm", null)).EnsureSuccessStatusCode();
        balances = await skipper.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/balances");
        Assert.Equal("Confirmed", balances.GetProperty("settlements")[0].GetProperty("status").GetString());
        var csv = await skipper.GetStringAsync($"/api/trips/{trip}/costs/export");
        Assert.Contains("Hafengeld", csv);
        Assert.Contains("XYZ", csv);
        var pdfResponse = await skipper.GetAsync($"/api/trips/{trip}/costs/export.pdf");
        pdfResponse.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", pdfResponse.Content.Headers.ContentType?.MediaType);
        var pdf = System.Text.Encoding.Latin1.GetString(await pdfResponse.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("Hafengeld", pdf); // uncompressed content streams: text is searchable
        Assert.Contains("Confirmed", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Anonymous().GetAsync($"/api/trips/{trip}/costs/export.pdf")).StatusCode);
        (await skipper.PostAsync($"/api/trips/{trip}/costs/lock", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await skipper.PostAsJsonAsync($"/api/trips/{trip}/expenses", new { amount = 1, currency = "EUR", paidOn = "2026-10-12", category = "x", splitMode = "Equal" })).StatusCode);
        (await skipper.PostAsync($"/api/trips/{trip}/costs/unlock", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Social_follow_comment_like_feed_notifications_report()
    {
        var (skipper, ship, trip, _, _) = await Trip("Social trip");
        var skipperId = (await skipper.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetGuid();
        var post = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{trip}/posts", new { title = "Hallo", content = "<p>Welt</p>", visibility = "Public" }));
        var secret = await IdOf(await skipper.PostAsJsonAsync($"/api/trips/{trip}/posts", new { title = "Geheim", content = "<p>x</p>", visibility = "Crew" }));
        var (fan, _) = await f.UserAsync();
        (await fan.PostAsJsonAsync("/api/follows", new { targetType = "User", targetId = skipperId })).EnsureSuccessStatusCode();
        (await fan.PostAsJsonAsync("/api/follows", new { targetType = "Ship", targetId = ship })).EnsureSuccessStatusCode();
        (await fan.PostAsJsonAsync($"/api/posts/{post}/comments", new { text = "Toll!" })).EnsureSuccessStatusCode();
        (await fan.PutAsync($"/api/posts/{post}/reactions", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await fan.PostAsJsonAsync($"/api/posts/{secret}/comments", new { text = "hack" })).StatusCode);
        var social = await f.Anonymous().GetFromJsonAsync<JsonElement>($"/api/posts/{post}/social");
        Assert.Equal(1, social.GetProperty("likes").GetInt32());
        Assert.Single(social.GetProperty("comments").EnumerateArray());
        var notes = await skipper.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(2, notes.GetProperty("unread").GetInt32()); // follow + comment
        (await skipper.PostAsync("/api/notifications/read-all", null)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await skipper.GetFromJsonAsync<JsonElement>("/api/notifications")).GetProperty("unread").GetInt32());
        var feed = await fan.GetFromJsonAsync<JsonElement>("/api/feed");
        Assert.Contains(feed.EnumerateArray(), i => i.GetProperty("id").GetGuid() == post);
        Assert.DoesNotContain(feed.EnumerateArray(), i => i.GetProperty("id").GetGuid() == secret);
        (await fan.DeleteAsync($"/api/follows/User/{skipperId}")).EnsureSuccessStatusCode();
        Assert.Single((await fan.GetFromJsonAsync<JsonElement>("/api/me/follows")).EnumerateArray());
        (await fan.PostAsJsonAsync("/api/reports", new { targetType = "Post", targetId = post, reason = "Spam" })).EnsureSuccessStatusCode();
        var (admin, _) = await f.AdminAsync();
        var reports = await admin.GetFromJsonAsync<JsonElement>("/api/admin/reports");
        var report = reports.EnumerateArray().First(r => r.GetProperty("targetId").GetGuid() == post);
        (await admin.PostAsJsonAsync($"/api/admin/reports/{report.GetProperty("id").GetGuid()}/resolve", new { hide = true, resolution = "spam" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await f.Anonymous().GetAsync($"/api/posts/{post}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fan.GetAsync("/api/admin/reports")).StatusCode);
    }

    [Fact]
    public async Task Tracking_devices_ingest_import_segments_position_and_privacy()
    {
        var (skipper, ship, trip, kiel, laboe) = await Trip("Track trip");
        (await skipper.PutAsJsonAsync($"/api/trips/{trip}", new { name = "Track trip", startDate = "2026-10-10", endDate = "2026-10-20", visibility = "Public", status = "Active", trackVisibility = "Public", publicPositionDelayMinutes = 0 })).EnsureSuccessStatusCode();
        (await skipper.PostAsJsonAsync($"/api/ships/{ship}/logbook", new { tripId = trip, startHarbourId = kiel, endHarbourId = laboe, departedAt = "2026-10-12T08:00:00Z", arrivedAt = "2026-10-12T10:00:00Z", visibility = "Public", crew = Array.Empty<object>() })).EnsureSuccessStatusCode();
        var created = await skipper.PostAsJsonAsync($"/api/ships/{ship}/tracking-devices", new { name = "Phone", protocol = "OsmAnd" });
        created.EnsureSuccessStatusCode();
        var token = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var anon = f.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/tracking/osmand?id=wrong&lat=54&lon=10")).StatusCode);
        var t0 = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        for (var i = 0; i <= 12; i++)
            (await anon.GetAsync($"/api/tracking/osmand?id={token}&lat={(54.34 + i * 0.006).ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={(10.16 + i * 0.005).ToString(System.Globalization.CultureInfo.InvariantCulture)}&timestamp={t0 + i * 600}&speed=5&bearing=45&accuracy=6")).EnsureSuccessStatusCode();
        (await anon.PostAsJsonAsync($"/api/tracking/points", new { deviceToken = token, points = new[] { new { t = "2026-10-12T10:10:00Z", lat = 54.42, lon = 10.23, sog = 4.0, cog = 40.0, acc = 5.0 } } })).EnsureSuccessStatusCode();
        using var form = new MultipartFormDataContent();
        var gpx = new StringContent("""<?xml version="1.0"?><gpx xmlns="http://www.topografix.com/GPX/1/1" version="1.1"><trk><trkseg><trkpt lat="54.42" lon="10.24"><time>2026-10-13T07:00:00Z</time></trkpt><trkpt lat="54.44" lon="10.30"><time>2026-10-13T07:30:00Z</time></trkpt><trkpt lat="54.45" lon="10.38"><time>2026-10-13T08:00:00Z</time></trkpt></trkseg></trk></gpx>""");
        gpx.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        form.Add(gpx, "files", "track.gpx"); form.Add(new StringContent(trip.ToString()), "tripId");
        var import = await skipper.PostAsync($"/api/ships/{ship}/track-imports", form);
        import.EnsureSuccessStatusCode();
        Assert.Equal(3, (await import.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("added").GetInt32());
        (await skipper.PostAsync($"/api/trips/{trip}/track/reprocess", null)).EnsureSuccessStatusCode();

        var track = await anon.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/track");
        var features = track.GetProperty("features").EnumerateArray().ToList();
        Assert.Equal(2, features.Count);
        Assert.Contains(features, x => x.GetProperty("properties").GetProperty("source").GetString() == "GpsLive");
        Assert.Contains(features, x => x.GetProperty("properties").GetProperty("source").GetString() == "GpsFile");
        var pos = await anon.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/position");
        Assert.Equal(54.45, pos.GetProperty("lat").GetDouble(), 3); // latest fix is the last GPX point
        var suggestions = await skipper.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/track/suggestions");
        Assert.Equal(JsonValueKind.Array, suggestions.ValueKind);
        // privacy zone around the start hides those points for the public
        (await skipper.PostAsJsonAsync($"/api/ships/{ship}/privacy-zones", new { name = "Home", lat = 54.34, lng = 10.16, radiusM = 1500 })).EnsureSuccessStatusCode();
        var publicTrack = await anon.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/track");
        var live = publicTrack.GetProperty("features").EnumerateArray().First(x => x.GetProperty("properties").GetProperty("source").GetString() == "GpsLive");
        var crewTrack = await skipper.GetFromJsonAsync<JsonElement>($"/api/trips/{trip}/track");
        var liveCrew = crewTrack.GetProperty("features").EnumerateArray().First(x => x.GetProperty("properties").GetProperty("source").GetString() == "GpsLive");
        Assert.True(live.GetProperty("geometry").GetProperty("coordinates").GetArrayLength() < liveCrew.GetProperty("geometry").GetProperty("coordinates").GetArrayLength());
        // delayed public position
        (await skipper.PutAsJsonAsync($"/api/trips/{trip}", new { name = "Track trip", startDate = "2026-10-10", endDate = "2026-10-20", visibility = "Public", status = "Active", trackVisibility = "Public", publicPositionDelayMinutes = 100000 })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await anon.GetAsync($"/api/trips/{trip}/position")).StatusCode);
        Assert.True((await skipper.GetAsync($"/api/trips/{trip}/position")).IsSuccessStatusCode);
        // revoke device
        var devices = await skipper.GetFromJsonAsync<JsonElement>($"/api/ships/{ship}/tracking-devices");
        (await skipper.DeleteAsync($"/api/ships/{ship}/tracking-devices/{devices[0].GetProperty("id").GetGuid()}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/tracking/osmand?id={token}&lat=54&lon=10")).StatusCode);
    }

    [Fact]
    public async Task Profile_settings_export_search_and_account_deletion()
    {
        var (user, id) = await f.UserAsync("deleteme");
        (await user.PutAsJsonAsync("/api/me/profile", new { displayName = "Delete Me", bio = "<p>bio</p>", profileVisibility = "Public", defaultContentVisibility = "Public", language = "en", emailNotifications = false })).EnsureSuccessStatusCode();
        var me = await user.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("en", me.GetProperty("language").GetString());
        var (other, _) = await f.UserAsync();
        var found = await other.GetFromJsonAsync<JsonElement>("/api/users?q=Delete");
        Assert.Contains(found.EnumerateArray(), u => u.GetProperty("userId").GetGuid() == id);
        Assert.False(found[0].TryGetProperty("email", out _));
        var profile = await f.Anonymous().GetFromJsonAsync<JsonElement>($"/api/users/{id}");
        Assert.Equal("Delete Me", profile.GetProperty("displayName").GetString());
        var export = await user.GetFromJsonAsync<JsonElement>("/api/me/export");
        Assert.True(export.TryGetProperty("profile", out _));
        var ship = await IdOf(await user.PostAsJsonAsync("/api/ships", new { name = "Lonely", cruisingSpeedKn = 5, visibility = "Public" }));
        Assert.Equal(HttpStatusCode.Conflict, (await user.DeleteAsync("/api/me")).StatusCode); // sole owner
        (await user.DeleteAsync($"/api/ships/{ship}")).EnsureSuccessStatusCode();
        (await user.DeleteAsync("/api/me")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await f.Anonymous().GetAsync($"/api/users/{id}")).StatusCode);
    }

    [Fact]
    public async Task Auth_flow_registration_confirmation_and_rate_limit_shape()
    {
        var anon = f.Anonymous();
        var email = $"flow{Guid.NewGuid():N}@test.local";
        (await anon.PostAsJsonAsync("/api/auth/register", new { email, password = "Passw0rd123", displayName = "Flow" })).EnsureSuccessStatusCode();
        var login = await anon.PostAsJsonAsync("/api/auth/login", new { email, password = "Passw0rd123" });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode); // not confirmed
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/auth/login", new { email = "nobody@test.local", password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsJsonAsync("/api/auth/register", new { email, password = "short", displayName = "" })).StatusCode);
        var cfg = await anon.GetFromJsonAsync<JsonElement>("/api/config");
        Assert.StartsWith("http", cfg.GetProperty("mapStyleUrl").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/ships/not-a-guid/trips")).StatusCode); // guid route constraint
    }
}
