using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Sms.Domain;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record HarbourMarker(Guid Id, string Name, double Lat, double Lng, string? Category, string? Country);
public record HarbourUpsert(string Name, double Lat, double Lng, string? Country, string? Website, string? Description, string? Category, string? Phone, string? VhfChannel, Dictionary<string, bool>? Facilities);
public record WifiDto(Guid Id, string Name, string? Password, int Speed, bool Free);
public record WifiUpsert(string Name, string? Password, int Speed, bool Free);
public record ReviewDto(Guid Id, Guid AuthorId, string Author, int Rating, string? Text, DateOnly? VisitedOn, DateTimeOffset CreatedAt);
public record ReviewUpsert(int Rating, string? Text, DateOnly? VisitedOn);
public record HarbourDto(Guid Id, string Name, double Lat, double Lng, string? Country, string? Website, string? Description, string? Category, string? Phone, string? VhfChannel,
    Dictionary<string, bool> Facilities, HarbourSource Source, string? OsmType, long? OsmId, IReadOnlyList<WifiDto> Wifis, IReadOnlyList<ReviewDto> Reviews, double? AverageRating, IReadOnlyList<string> PhotoUrls);
public record NearbyPort(Guid HarbourId, string Name, double Lat, double Lng, decimal DistanceNm, decimal StraightLineNm, decimal EtaHours, string Method);

public static class HarbourEndpoints
{
    public const string Attribution = RoutingEndpoints.Attribution;

    public static void MapHarbours(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/harbours").WithTags("Harbours");

        g.MapGet("", async (string? bbox, string? q, int? limit, SmsDbContext db, CancellationToken ct) =>
        {
            var query = db.Harbours.AsNoTracking().Where(h => h.IsActive && h.MergedIntoId == null);
            if (!string.IsNullOrWhiteSpace(bbox))
            {
                var p = bbox.Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (p.Length != 4) return Invalid("bbox", "bbox=minLng,minLat,maxLng,maxLat");
                var env = Geo.Factory.ToGeometry(new Envelope(p[0], p[2], p[1], p[3]));
                query = query.Where(h => h.Location.Intersects(env));
            }
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(h => EF.Functions.ILike(h.Name, $"%{q}%"));
            // geography columns have no ST_X/ST_Y: project the point and read coordinates in memory
            var items = await query.OrderBy(h => h.Name).Take(Math.Clamp(limit ?? 2000, 1, 5000))
                .Select(h => new { h.Id, h.Name, h.Location, h.Category, h.Country }).ToListAsync(ct);
            return Results.Ok(items.Select(h => new HarbourMarker(h.Id, h.Name, h.Location.Y, h.Location.X, h.Category, h.Country)));
        });

        g.MapGet("/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var h = await db.Harbours.AsNoTracking().Include(x => x.Wifis).Include(x => x.Reviews).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (h is null) return Results.NotFound();
            if (h.MergedIntoId is { } target) return Results.Redirect($"/api/harbours/{target}");
            var names = await ShipEndpoints.DisplayNames(db, h.Reviews.Select(r => r.AuthorId), ct);
            var photos = await db.Media.AsNoTracking().Where(m => m.HarbourId == id && m.Visibility == Visibility.Public && m.Kind == MediaKind.Photo)
                .OrderByDescending(m => m.CreatedAt).Take(20).Select(m => $"/api/media/{m.Id}/thumb").ToListAsync(ct);
            return Results.Ok(ToDto(h, names, current.IsAuthenticated, photos));
        });

        g.MapPost("", async (HarbourUpsert req, SmsDbContext db, CurrentUser current, HtmlSanitizerService html, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Invalid("name", "Name fehlt.");
            var h = new Harbour { Source = HarbourSource.User, CreatedById = current.Id };
            Apply(h, req, html);
            db.Harbours.Add(h);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/harbours/{h.Id}", new { h.Id });
        }).RequireAuthorization();

        g.MapPut("/{id:guid}", async (Guid id, HarbourUpsert req, SmsDbContext db, CurrentUser current, HtmlSanitizerService html, CancellationToken ct) =>
        {
            var h = await db.Harbours.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (h is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Name)) return Invalid("name", "Name fehlt.");
            db.HarbourRevisions.Add(new HarbourRevision { HarbourId = id, ChangedById = current.Id, SnapshotJson = Snapshot(h) });
            Apply(h, req, html);
            h.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapGet("/{id:guid}/revisions", async (Guid id, SmsDbContext db, CancellationToken ct) =>
        {
            var revs = await db.HarbourRevisions.AsNoTracking().Where(r => r.HarbourId == id).OrderByDescending(r => r.ChangedAt).Take(50).ToListAsync(ct);
            var names = await ShipEndpoints.DisplayNames(db, revs.Where(r => r.ChangedById != null).Select(r => r.ChangedById!.Value), ct);
            return Results.Ok(revs.Select(r => new { r.Id, r.ChangedAt, ChangedBy = r.ChangedById is { } u ? names.GetValueOrDefault(u) : null, Snapshot = JsonDocument.Parse(r.SnapshotJson).RootElement }));
        });

        g.MapPost("/{id:guid}/revisions/{revisionId:guid}/revert", async (Guid id, Guid revisionId, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var h = await db.Harbours.FirstOrDefaultAsync(x => x.Id == id, ct);
            var rev = await db.HarbourRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == revisionId && r.HarbourId == id, ct);
            if (h is null || rev is null) return Results.NotFound();
            db.HarbourRevisions.Add(new HarbourRevision { HarbourId = id, ChangedById = current.Id, SnapshotJson = Snapshot(h) });
            var snap = JsonSerializer.Deserialize<HarbourUpsert>(rev.SnapshotJson, JsonOpts)!;
            Apply(h, snap, null);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // --- wifi (passwords for registered users only, spec 08 §8.3)
        g.MapPost("/{id:guid}/wifis", async (Guid id, WifiUpsert req, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (!await db.Harbours.AnyAsync(h => h.Id == id, ct)) return Results.NotFound();
            var w = new HarbourWifi { HarbourId = id, Name = req.Name, Password = req.Password, Speed = Math.Clamp(req.Speed, 0, 3), Free = req.Free, CreatedById = current.Id };
            db.HarbourWifis.Add(w);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/wifis/{w.Id}", new { w.Id });
        }).RequireAuthorization();

        api.MapPut("/wifis/{id:guid}", async (Guid id, WifiUpsert req, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var w = await db.HarbourWifis.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w is null) return Results.NotFound();
            // spec 08 §8.4: every registered user may edit harbour data (wiki-like); the revision keeps it auditable
            db.HarbourRevisions.Add(new HarbourRevision { HarbourId = w.HarbourId, ChangedById = current.Id, SnapshotJson = JsonSerializer.Serialize(new { wifi = new WifiDto(w.Id, w.Name, null, w.Speed, w.Free) }, JsonOpts) }); // revisions are public: never snapshot the password
            w.Name = req.Name; w.Password = req.Password; w.Speed = Math.Clamp(req.Speed, 0, 3); w.Free = req.Free; w.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Harbours");

        api.MapDelete("/wifis/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var w = await db.HarbourWifis.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (w is null) return Results.NotFound();
            db.HarbourRevisions.Add(new HarbourRevision { HarbourId = w.HarbourId, ChangedById = current.Id, SnapshotJson = JsonSerializer.Serialize(new { deletedWifi = new WifiDto(w.Id, w.Name, null, w.Speed, w.Free) }, JsonOpts) });
            db.HarbourWifis.Remove(w);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Harbours");

        // --- reviews
        g.MapPost("/{id:guid}/reviews", async (Guid id, ReviewUpsert req, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (req.Rating is < 1 or > 5) return Invalid("rating", "1 bis 5 Sterne.");
            if (!await db.Harbours.AnyAsync(h => h.Id == id, ct)) return Results.NotFound();
            var r = await db.HarbourReviews.FirstOrDefaultAsync(x => x.HarbourId == id && x.AuthorId == current.RequiredId, ct)
                    ?? db.HarbourReviews.Add(new HarbourReview { HarbourId = id, AuthorId = current.RequiredId }).Entity;
            r.Rating = req.Rating; r.Text = req.Text; r.VisitedOn = req.VisitedOn;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id });
        }).RequireAuthorization();

        // --- admin: OSM import (Overpass, small areas; spec 10.3) and merge
        api.MapPost("/admin/harbours/import-osm", async (string bbox, SmsDbContext db, CurrentUser current, IHttpClientFactory http, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var p = bbox.Split(',');
            if (p.Length != 4) return Invalid("bbox", "bbox=south,west,north,east");
            var query = $"[out:json][timeout:120];(nwr[\"leisure\"=\"marina\"]({bbox});nwr[\"seamark:type\"=\"harbour\"]({bbox});nwr[\"harbour\"=\"yes\"]({bbox}););out center tags;";
            using var doc = await Overpass.QueryAsync(http, cfg, query, ct);
            if (doc is null) return Results.Problem(statusCode: 502, title: "Overpass ist zurzeit nicht erreichbar – später erneut versuchen.");
            var r = await ImportAsync(doc, db, current.Id, ct);
            return Results.Ok(new { r.created, r.updated, r.skipped });
        }).RequireAuthorization().WithTags("Admin");

        api.MapPost("/admin/harbours/{id:guid}/merge-into/{targetId:guid}", async (Guid id, Guid targetId, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var h = await db.Harbours.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (h is null || !await db.Harbours.AnyAsync(x => x.Id == targetId, ct) || id == targetId) return Results.NotFound();
            await db.LogbookEntries.IgnoreQueryFilters().Where(l => l.StartHarbourId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.StartHarbourId, targetId), ct);
            await db.LogbookEntries.IgnoreQueryFilters().Where(l => l.EndHarbourId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.EndHarbourId, targetId), ct);
            await db.HarbourWifis.Where(w => w.HarbourId == id).ExecuteUpdateAsync(s => s.SetProperty(w => w.HarbourId, targetId), ct);
            await db.HarbourReviews.Where(r => r.HarbourId == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.HarbourId, targetId), ct);
            await db.Media.IgnoreQueryFilters().Where(m => m.HarbourId == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.HarbourId, targetId), ct);
            h.MergedIntoId = targetId; h.IsActive = false;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Admin");
    }

    /// <summary>Upserts OSM harbours (Overpass-style elements); OSM fills empty fields only, user text wins (spec 10.3).</summary>
    public static async Task<(int created, int updated, int skipped)> ImportAsync(JsonDocument doc, SmsDbContext db, Guid? userId, CancellationToken ct)
    {
        var (created, updated, skipped) = (0, 0, 0);
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            var type = el.GetProperty("type").GetString()!;
            var osmId = el.GetProperty("id").GetInt64();
            double lat, lng;
            if (el.TryGetProperty("center", out var c)) { lat = c.GetProperty("lat").GetDouble(); lng = c.GetProperty("lon").GetDouble(); }
            else if (el.TryGetProperty("lat", out var la)) { lat = la.GetDouble(); lng = el.GetProperty("lon").GetDouble(); }
            else { skipped++; continue; }
            if (!el.TryGetProperty("tags", out var tags) || !tags.TryGetProperty("name", out var nameEl)) { skipped++; continue; }
            var name = nameEl.GetString()!;
            var location = Geo.Point(lat, lng);
            var existing = await db.Harbours.FirstOrDefaultAsync(h => h.OsmType == type && h.OsmId == osmId, ct)
                           ?? await db.Harbours.FirstOrDefaultAsync(h => h.Location.IsWithinDistance(location, 300) && EF.Functions.ILike(h.Name, name), ct);
            var facilities = new Dictionary<string, bool>();
            foreach (var tag in new[] { "fuel", "drinking_water", "power_supply", "toilets", "shower", "slipway", "sanitary_dump_station", "internet_access" })
                if (tags.TryGetProperty(tag, out var v) && v.GetString() is { } sv && sv != "no") facilities[tag] = true;
            var upsert = new HarbourUpsert(name, lat, lng, Tag(tags, "addr:country"), Tag(tags, "website") ?? Tag(tags, "contact:website"), null,
                Tag(tags, "seamark:harbour:category") ?? Tag(tags, "leisure"), Tag(tags, "phone") ?? Tag(tags, "contact:phone"),
                Tag(tags, "seamark:communication:channel") ?? Tag(tags, "vhf"), facilities);
            if (existing is null)
            {
                var h = new Harbour { Source = HarbourSource.Osm, OsmType = type, OsmId = osmId, CreatedById = userId };
                Apply(h, upsert, null);
                db.Harbours.Add(h); created++;
            }
            else
            {
                // OSM fills empty fields only; user text wins (spec 10.3).
                existing.OsmType ??= type; existing.OsmId ??= osmId;
                existing.Website ??= upsert.Website; existing.Country ??= upsert.Country; existing.Category ??= upsert.Category;
                existing.Phone ??= upsert.Phone; existing.VhfChannel ??= upsert.VhfChannel;
                if (string.IsNullOrEmpty(existing.Facilities) && facilities.Count > 0) existing.Facilities = JsonSerializer.Serialize(facilities);
                updated++;
            }
        }
        await db.SaveChangesAsync(ct);
        return (created, updated, skipped);
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static string? Tag(JsonElement tags, string key) => tags.TryGetProperty(key, out var v) ? v.GetString() : null;

    private static string Snapshot(Harbour h) => JsonSerializer.Serialize(new HarbourUpsert(h.Name, h.Location.Y, h.Location.X, h.Country, h.Website, h.Description, h.Category, h.Phone, h.VhfChannel, Facilities(h)), JsonOpts);

    private static Dictionary<string, bool> Facilities(Harbour h) =>
        string.IsNullOrEmpty(h.Facilities) ? [] : JsonSerializer.Deserialize<Dictionary<string, bool>>(h.Facilities) ?? [];

    private static void Apply(Harbour h, HarbourUpsert r, HtmlSanitizerService? html)
    {
        h.Name = r.Name.Trim(); h.Location = Geo.Point(r.Lat, r.Lng); h.Country = r.Country; h.Website = r.Website;
        h.Description = html is null ? r.Description : html.Sanitize(r.Description);
        h.Category = r.Category; h.Phone = r.Phone; h.VhfChannel = r.VhfChannel;
        h.Facilities = r.Facilities is { Count: > 0 } ? JsonSerializer.Serialize(r.Facilities) : null;
    }

    private static HarbourDto ToDto(Harbour h, Dictionary<Guid, string> names, bool registered, IReadOnlyList<string> photos) => new(
        h.Id, h.Name, h.Location.Y, h.Location.X, h.Country, h.Website, h.Description, h.Category, h.Phone, h.VhfChannel, Facilities(h), h.Source, h.OsmType, h.OsmId,
        h.Wifis.Select(w => new WifiDto(w.Id, w.Name, registered ? w.Password : null, w.Speed, w.Free)).ToList(),
        h.Reviews.OrderByDescending(r => r.CreatedAt).Select(r => new ReviewDto(r.Id, r.AuthorId, names.GetValueOrDefault(r.AuthorId, "?"), r.Rating, r.Text, r.VisitedOn, r.CreatedAt)).ToList(),
        h.Reviews.Count > 0 ? Math.Round(h.Reviews.Average(r => r.Rating), 1) : null, photos);
}
