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
    Dictionary<string, bool> Facilities, HarbourSource Source, string? OsmType, long? OsmId, IReadOnlyList<WifiDto> Wifis, IReadOnlyList<ReviewDto> Reviews, double? AverageRating, IReadOnlyList<string> PhotoUrls,
    IReadOnlyList<HarbourService> Services);
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

        g.MapPost("", async (HarbourUpsert req, SmsDbContext db, CurrentUser current, HtmlSanitizerService html, SurroundingsJob surroundings, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Invalid("name", "Name fehlt.");
            var h = new Harbour { Source = HarbourSource.User, CreatedById = current.Id };
            Apply(h, req, html);
            db.Harbours.Add(h);
            await db.SaveChangesAsync(ct);
            await ComputeServicesAsync(db, [h.Id], ct);
            surroundings.Enqueue(h.Id);   // POIs around a new harbour: Overpass, in the background
            return Results.Created($"/api/harbours/{h.Id}", new { h.Id });
        }).RequireAuthorization();

        g.MapPut("/{id:guid}", async (Guid id, HarbourUpsert req, SmsDbContext db, CurrentUser current, HtmlSanitizerService html, SurroundingsJob surroundings, CancellationToken ct) =>
        {
            var h = await db.Harbours.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (h is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Name)) return Invalid("name", "Name fehlt.");
            db.HarbourRevisions.Add(new HarbourRevision { HarbourId = id, ChangedById = current.Id, SnapshotJson = Snapshot(h) });
            var moved = Math.Abs(h.Location.Y - req.Lat) > 1e-5 || Math.Abs(h.Location.X - req.Lng) > 1e-5;
            Apply(h, req, html);
            h.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            await ComputeServicesAsync(db, [h.Id], ct);
            if (moved) surroundings.Enqueue(h.Id);
            return Results.NoContent();
        }).RequireAuthorization();

        // Ships that visited (visibility-filtered logbook entries ending or starting here), newest first.
        g.MapGet("/{id:guid}/visits", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var rows = await db.LogbookEntries.AsNoTracking().Include(l => l.Ship).Include(l => l.Trip).Include(l => l.StartHarbour).Include(l => l.EndHarbour)
                .Where(l => l.EndHarbourId == id || l.StartHarbourId == id).Where(viewer.Logbook())
                .Select(l => new { l.ShipId, ShipName = l.Ship.Name, ShipVisibility = l.Ship.Visibility, l.TripId, TripName = l.Trip != null ? l.Trip.Name : null, At = l.EndHarbourId == id ? l.ArrivedAt : l.DepartedAt })
                .ToListAsync(ct);
            var ships = rows.GroupBy(r => r.ShipId).Select(g => new { shipId = g.Key, name = g.First().ShipName, visits = g.Count(), lastVisit = g.Max(r => r.At), lastTripId = g.OrderByDescending(r => r.At).First().TripId, lastTripName = g.OrderByDescending(r => r.At).First().TripName })
                .OrderByDescending(s => s.lastVisit).Take(50).ToList();
            return Results.Ok(ships);
        });

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
        api.MapPost("/admin/harbours/import-osm", async (string bbox, SmsDbContext db, CurrentUser current, IHttpClientFactory http, IConfiguration cfg, SurroundingsJob surroundings, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var p = bbox.Split(',');
            if (p.Length != 4) return Invalid("bbox", "bbox=south,west,north,east");
            var query = $"[out:json][timeout:120];(nwr[\"leisure\"=\"marina\"]({bbox});nwr[\"seamark:type\"=\"harbour\"]({bbox});nwr[\"harbour\"=\"yes\"]({bbox}););out center tags;";
            using var doc = await Overpass.QueryAsync(http, cfg, query, ct);
            if (doc is null) return Results.Problem(statusCode: 502, title: "Overpass ist zurzeit nicht erreichbar – später erneut versuchen.");
            var r = await ImportAsync(doc, db, current.Id, ct);
            var s = p.Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var area = Geo.Factory.ToGeometry(new Envelope(s[1], s[3], s[0], s[2]));
            var ids = await db.Harbours.Where(h => h.IsActive && h.MergedIntoId == null && h.Location.Intersects(area)).Select(h => h.Id).ToListAsync(ct);
            await ComputeServicesAsync(db, ids, ct);
            // harbours without any POI within 1 km (new regions): fetch their surroundings in the background
            var bare = ids.Count == 0 ? [] : await db.Database.SqlQueryRaw<Guid>("""
                SELECT h."Id" AS "Value" FROM "Harbours" h WHERE h."Id" = ANY({0}) AND NOT EXISTS (SELECT 1 FROM "Pois" p
                    WHERE ST_DWithin(p."Location", ST_Transform(h."Location"::geometry, 3857), 1000 / cos(radians(ST_Y(h."Location"::geometry)))))
                """, ids.ToArray()).ToListAsync(ct);
            foreach (var b in bare) surroundings.Enqueue(b);
            return Results.Ok(new { r.created, r.updated, r.skipped, surroundingsQueued = bare.Count });
        }).RequireAuthorization().WithTags("Admin");

        // POIs within 3 km from Overpass and the services of one harbour, in the background (see SurroundingsJob)
        api.MapPost("/admin/harbours/{id:guid}/refresh-surroundings", async (Guid id, SmsDbContext db, CurrentUser current, SurroundingsJob surroundings, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (!await db.Harbours.AnyAsync(h => h.Id == id && h.IsActive, ct)) return Results.NotFound();
            return Results.Accepted(value: new { queued = surroundings.Enqueue(id) });
        }).RequireAuthorization().WithTags("Admin");

        // Nearest services (HarbourServices) for all active harbours: one PostGIS query per batch, ~14 k harbours in seconds.
        api.MapPost("/admin/harbours/compute-services", async (SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var ids = await db.Harbours.AsNoTracking().Where(h => h.IsActive && h.MergedIntoId == null).OrderBy(h => h.Id).Select(h => h.Id).ToListAsync(ct);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var batch in ids.Chunk(1000)) { await ComputeServicesAsync(db, batch, ct); db.ChangeTracker.Clear(); }
            return Results.Ok(new { harbours = ids.Count, seconds = Math.Round(sw.Elapsed.TotalSeconds, 1) });
        }).RequireAuthorization().WithTags("Admin");

        api.MapPost("/admin/harbours/{id:guid}/merge-into/{targetId:guid}", async (Guid id, Guid targetId, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var h = await db.Harbours.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (h is null || !await db.Harbours.AnyAsync(x => x.Id == targetId, ct) || id == targetId) return Results.NotFound();
            await MergeAsync(db, h, targetId, ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Admin");
    }

    /// <summary>Moves logbook entries, wifis, reviews and media to the target and marks the harbour as merged.</summary>
    public static async Task MergeAsync(SmsDbContext db, Harbour h, Guid targetId, CancellationToken ct)
    {
            var id = h.Id;
            await db.LogbookEntries.IgnoreQueryFilters().Where(l => l.StartHarbourId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.StartHarbourId, targetId), ct);
            await db.LogbookEntries.IgnoreQueryFilters().Where(l => l.EndHarbourId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.EndHarbourId, targetId), ct);
            await db.HarbourWifis.Where(w => w.HarbourId == id).ExecuteUpdateAsync(s => s.SetProperty(w => w.HarbourId, targetId), ct);
            await db.HarbourReviews.Where(r => r.HarbourId == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.HarbourId, targetId), ct);
            await db.Media.IgnoreQueryFilters().Where(m => m.HarbourId == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.HarbourId, targetId), ct);
            h.MergedIntoId = targetId; h.IsActive = false;
            await db.SaveChangesAsync(ct);
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

    /// <summary>
    /// Stores the nearest service per kind (HarbourServices) for the given harbours. One statement: candidate POIs within range of each harbour
    /// (Pois are EPSG:3857, so the radius is scaled by 1/cos(lat) and the distance back by cos(lat)), the nearest few per (harbour, service);
    /// then per harbour (in parallel) the walking distances over walk_edge (<see cref="Walking"/>): nearest by walk, places too far on foot drop out,
    /// places without a way nearby and harbours without any network keep the straight distance.
    /// Fuel: boat fuel (boat=yes/only, waterway=fuel) or a station at the harbour is preferred over a nearer road station, which Select then drops.
    /// </summary>
    public static async Task ComputeServicesAsync(SmsDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        // constants from HarbourServices, no user input
        var kinds = string.Join(",", HarbourServices.PoiKinds.Select(k => $"('{k.Service}','{k.Class}','{k.Kind}')"));
        // ponytail: the walking-nearest is taken from the 25 straight-nearest per service; a basin with 25+ places of one kind on the far side would hide a walkable one
        var rows = await db.Database.SqlQueryRaw<ServiceRow>($$"""
            WITH h AS (SELECT "Id", ST_Transform("Location"::geometry, 3857) AS g, cos(radians(ST_Y("Location"::geometry))) AS k FROM "Harbours" WHERE "Id" = ANY({0})),
                 s(svc, cls, kind) AS (VALUES {{kinds}}),
                 c AS (SELECT h."Id" AS "HarbourId", s.svc AS "Kind", p."Id" AS "PoiId", p."Name" AS "Name", ST_Distance(h.g, p."Location") * h.k AS "DistanceM",
                          (p."TagsJson"->>'boat' IN ('yes', 'only') OR p."TagsJson"->>'waterway' = 'fuel') IS TRUE AS "Boat",
                          ST_Y(ST_Transform(p."Location", 4326)) AS "Lat", ST_X(ST_Transform(p."Location", 4326)) AS "Lng"
                       FROM h JOIN "Pois" p ON ST_DWithin(p."Location", h.g, {{HarbourServices.MaxDistanceM}} / h.k)
                              JOIN s ON s.cls = p."Class" AND s.kind = p."Kind")
            SELECT "HarbourId", "Kind", "PoiId", "Name", "DistanceM", "Boat", "Lat", "Lng" FROM (SELECT c.*, row_number() OVER (PARTITION BY "HarbourId", "Kind" ORDER BY ("Kind" = 'fuel' AND NOT "Boat" AND "DistanceM" > {{HarbourServices.FuelAtHarbourM}}), "DistanceM") AS n FROM c) x
            WHERE n <= 25
            """, ids.ToArray()).ToListAsync(ct);
        var byHarbour = rows.ToLookup(r => r.HarbourId);
        var boatFuel = rows.Where(r => r.Boat).Select(r => r.PoiId).ToHashSet();
        var harbours = await db.Harbours.Where(h => ids.Contains(h.Id)).ToListAsync(ct);
        var walks = new System.Collections.Concurrent.ConcurrentDictionary<Guid, Dictionary<long, int?>>();
        var template = db.Database.GetDbConnection();   // the DbContext is not for parallel use: each task clones its connection
        await Parallel.ForEachAsync(harbours.Where(h => byHarbour[h.Id].Any()), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (h, c) =>
        {
            await using var conn = Walking.Connection(template);
            await conn.OpenAsync(c);
            if (await Walking.SearchAsync(conn, h.Location.Y, h.Location.X, HarbourServices.MaxDistanceM, c) is { } search)
                walks[h.Id] = await Walking.DistancesAsync(conn, search, byHarbour[h.Id].DistinctBy(r => r.PoiId).Select(r => (r.PoiId, r.Lat, r.Lng)).ToList(), c);
        });
        foreach (var h in harbours)
        {
            var walk = walks.GetValueOrDefault(h.Id);
            var candidates = byHarbour[h.Id].Where(r => walk is null || walk.ContainsKey(r.PoiId))   // left out = too far on foot
                .Select(r => new HarbourService(r.Kind, (int)Math.Round(r.DistanceM), r.Name, r.PoiId, walk?[r.PoiId]));
            var services = HarbourServices.Select(Facilities(h), candidates, boatFuel);
            h.Services = services.Count > 0 ? JsonSerializer.Serialize(services, JsonOpts) : null;
        }
        await db.SaveChangesAsync(ct);
    }
    private sealed class ServiceRow { public Guid HarbourId { get; set; } public string Kind { get; set; } = ""; public long PoiId { get; set; } public string? Name { get; set; } public double DistanceM { get; set; } public bool Boat { get; set; } public double Lat { get; set; } public double Lng { get; set; } }

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
        h.Reviews.Count > 0 ? Math.Round(h.Reviews.Average(r => r.Rating), 1) : null, photos,
        string.IsNullOrEmpty(h.Services) ? [] : JsonSerializer.Deserialize<List<HarbourService>>(h.Services, JsonOpts) ?? []);
}
