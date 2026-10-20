using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sms.Domain;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

/// <summary>Points of interest near harbours (PBF import): tiles come from Sms.Tiles, this answers the click with the OSM details.</summary>
public static class PoiEndpoints
{
    private static readonly string[] Keys = ["amenity", "shop", "tourism", "leisure", "craft", "office", "healthcare"];

    public static void MapPois(this IEndpointRouteBuilder api)
    {
        api.MapGet("/pois/{osmType}/{osmId:long}", async (string osmType, long osmId, SmsDbContext db, CancellationToken ct) =>
        {
            var p = await db.Pois.AsNoTracking().FirstOrDefaultAsync(x => x.OsmType == osmType && x.OsmId == osmId, ct);
            return p is null ? Results.NotFound() : Results.Ok(Details(p));
        }).WithTags("Pois");
        // a basemap POI label (no OSM id in the tiles): the same details for the POI of that name within 150 m
        api.MapGet("/pois/lookup", async (string name, double lat, double lng, SmsDbContext db, CancellationToken ct) =>
        {
            // Pois are EPSG:3857: Web Mercator metres, stretched by 1/cos(lat)
            var at = new NetTopologySuite.Geometries.Point(lng * 20037508.34 / 180, Math.Log(Math.Tan((90 + lat) * Math.PI / 360)) * 6378137) { SRID = 3857 };
            var radius = 150 / Math.Cos(lat * Math.PI / 180);
            var p = await db.Pois.AsNoTracking().Where(x => x.Name == name && x.Location.IsWithinDistance(at, radius)).OrderBy(x => x.Location.Distance(at)).FirstOrDefaultAsync(ct);
            return p is null ? Results.NotFound() : Results.Ok(Details(p));
        }).WithTags("Pois");
        // all POIs that provide a harbour service (HarbourServices.PoiKinds, e.g. "toilets") around a point, nearest first: by walking distance
        // over walk_edge (walkM; places too far on foot drop out), straight distance where there is no way nearby (walkM null)
        api.MapGet("/pois/near", async (double lat, double lng, string kind, SmsDbContext db, IMemoryCache cache, CancellationToken ct, int radiusM = HarbourServices.MaxDistanceM) =>
        {
            var tags = HarbourServices.PoiKinds.Where(k => k.Service == kind).ToList();
            if (tags.Count == 0 || Math.Abs(lat) > 85 || Math.Abs(lng) > 180) return Results.BadRequest();
            radiusM = Math.Clamp(radiusM, 1, 5000);   // 5 km: the harbour page highlights everything within walking/cycling distance
            var at = SeamarkEndpoints.ToMercator(lat, lng);
            var k = Math.Cos(lat * Math.PI / 180);   // Pois are EPSG:3857, metres stretched by 1/cos(lat)
            var kinds = tags.Select(t => t.Kind).ToList();
            var rows = (await db.Pois.AsNoTracking().Where(x => kinds.Contains(x.Kind) && x.Location.IsWithinDistance(at, radiusM / k))
                .OrderBy(x => x.Location.Distance(at)).Take(100).Select(x => new { x.Id, x.Name, x.Class, x.Kind, x.OsmType, x.OsmId, x.Location, x.TagsJson }).ToListAsync(ct))
                .Where(x => tags.Any(t => t.Class == x.Class && t.Kind == x.Kind))
                .Select(x => { var (pLat, pLng) = Sms.Infrastructure.Routing.Fairways.FromMercator(x.Location.X, x.Location.Y); return (x, pLat, pLng, distanceM: (int)Math.Round(x.Location.Distance(at) * k)); }).ToList();
            await using var conn = Walking.Connection(db.Database.GetDbConnection());
            await conn.OpenAsync(ct);
            var walk = await Walking.PageSearchAsync(cache, conn, lat, lng, ct) is { } search ? await Walking.DistancesAsync(conn, search, rows.Select(r => (r.x.Id, r.pLat, r.pLng)).ToList(), ct) : null;
            return Results.Ok(rows.Where(r => walk is null || walk.ContainsKey(r.x.Id)).Select(r => (r, walkM: walk?[r.x.Id])).Where(r => (r.walkM ?? r.r.distanceM) <= radiusM)
                .OrderBy(r => r.walkM ?? r.r.distanceM).Take(50).Select(v =>
            {
                var (x, pLat, pLng, distanceM) = v.r;
                var i = Info(x.TagsJson);
                return new { x.Id, x.Name, kind, poiClass = x.Class, poiKind = x.Kind, x.OsmType, x.OsmId, lat = pLat, lng = pLng, distanceM, v.walkM, i.OpeningHours, i.Cuisine, i.Website, i.Phone, i.Address };
            }));
        }).WithTags("Pois").RequireRateLimiting("ingest");
        // the walking path from a point (the harbour) to one POI, for the hover line on the harbour page; 404 = no path (draw it straight)
        api.MapGet("/pois/walk", async (double lat, double lng, long poiId, SmsDbContext db, IMemoryCache cache, CancellationToken ct) =>
        {
            if (Math.Abs(lat) > 85 || Math.Abs(lng) > 180) return Results.BadRequest();
            var result = await cache.GetOrCreateAsync(("walkpath", Math.Round(lat, 5), Math.Round(lng, 5), poiId), async e =>
            {
                e.SlidingExpiration = TimeSpan.FromMinutes(30);
                var poi = await db.Pois.AsNoTracking().Where(p => p.Id == poiId).Select(p => p.Location).FirstOrDefaultAsync(ct);
                if (poi is null) return null;
                var (pLat, pLng) = Sms.Infrastructure.Routing.Fairways.FromMercator(poi.X, poi.Y);
                await using var conn = Walking.Connection(db.Database.GetDbConnection());
                await conn.OpenAsync(ct);
                if (await Walking.PageSearchAsync(cache, conn, lat, lng, ct) is not { } search) return null;
                if (search.PathTo((await Walking.SnapAsync(conn, [(pLat, pLng)], Walking.PlaceSnapM, ct))[0]) is not { } path) return null;
                var line = await Walking.PathGeometryAsync(conn, path, (lat, lng), (pLat, pLng), ct);
                return new { walkM = (int)Math.Round(path.DistanceM), coordinates = line.Coordinates.Select(c => new[] { Math.Round(c.X, 7), Math.Round(c.Y, 7) }).ToArray() };
            });
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).WithTags("Pois").RequireRateLimiting("ingest");
        // public transport only (HarbourServices.TransportKind) near the harbours, without re-importing all POIs; Overpass, so 2°×2° chunks at most
        api.MapPost("/admin/pois/import-transport", async (string bbox, SmsDbContext db, CurrentUser current, IHttpClientFactory http, IConfiguration cfg, ILogger<PbfImportJob> log, CancellationToken ct, double radiusKm = 3) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var b = bbox.Split(',').Select(v => double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
            if (b.Length != 4 || b.Any(double.IsNaN) || b[0] >= b[2] || b[1] >= b[3] || b[2] - b[0] > 2.01 || b[3] - b[1] > 2.01)
                return Invalid("bbox", "bbox=south,west,north,east, at most 2°×2°");
            var near = new PbfImportJob.NearIndex(Math.Clamp(radiusKm, 0.5, 10) * 1000);
            foreach (var h in await db.Harbours.AsNoTracking().Select(h => h.Location).ToListAsync(ct)) near.Add(h.Y, h.X);
            var bb = string.Join(",", b.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var query = $"""[out:json][timeout:300];(nwr["highway"="bus_stop"]({bb});nwr["public_transport"="platform"]["bus"="yes"]({bb});nwr["amenity"~"^(bus_station|ferry_terminal)$"]({bb});nwr["railway"~"^(station|halt|tram_stop)$"]({bb});nwr["public_transport"="station"]["train"="yes"]({bb});nwr["public_transport"="station"]["bus"="yes"]({bb}););out center tags;""";
            using var doc = await Overpass.QueryAsync(http, cfg, query, ct, log);
            if (doc is null) return Results.Problem(statusCode: 502, title: "Overpass ist zurzeit nicht erreichbar – später erneut versuchen.");
            var found = doc.RootElement.GetProperty("elements").GetArrayLength();
            return Results.Ok(new { found, imported = await ImportAsync(doc, near, db, ct) });
        }).RequireAuthorization().WithTags("Admin");
        api.MapGet("/pois/status", async (SmsDbContext db, CancellationToken ct) => Results.Ok(new { count = await db.Pois.CountAsync(ct) })).WithTags("Pois");
    }

    private record Tagged(string? OpeningHours, string? Website, string? Phone, string? Email, string? Address, string? Cuisine);

    /// <summary>The user-facing OSM tags of a POI (opening hours, contact, address, cuisine).</summary>
    private static Tagged Info(string tagsJson)
    {
        using var tags = JsonDocument.Parse(tagsJson);
        string? T(string k) => tags.RootElement.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var addr = string.Join(", ", new[] { string.Join(" ", new[] { T("addr:street"), T("addr:housenumber") }.Where(s => s != null)), string.Join(" ", new[] { T("addr:postcode"), T("addr:city") }.Where(s => s != null)) }.Where(s => s.Length > 0));
        return new Tagged(T("opening_hours"), HttpUrl(T("website") ?? T("contact:website")), T("phone") ?? T("contact:phone"), T("email") ?? T("contact:email"), addr.Length > 0 ? addr : null, T("cuisine"));
    }

    private static object Details(Poi p)
    {
        using var tags = JsonDocument.Parse(p.TagsJson);
        string? T(string k) => tags.RootElement.TryGetProperty(k, out var v) ? v.GetString() : null;
        var i = Info(p.TagsJson);
        return new
        {
            p.OsmType, p.OsmId, p.Name, p.Class, p.Kind, openingHours = i.OpeningHours, website = i.Website, phone = i.Phone, email = i.Email,
            address = i.Address, cuisine = i.Cuisine, wheelchair = T("wheelchair"), description = T("description"), osmUrl = $"https://www.openstreetmap.org/{p.OsmType}/{p.OsmId}",
        };
    }

    private static string? Cut(string? v, int max) => v is null ? null : v.Length <= max ? v : v[..max];

    /// <summary>OSM website tags are free text: only http(s) URLs are handed to the browser as links.</summary>
    public static string? HttpUrl(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var s = v.Contains("://") ? v.Trim() : "https://" + v.Trim();
        return Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) ? u.ToString() : null;
    }

    /// <summary>Upserts the POI elements of an Overpass-style document that lie near a harbour (NearIndex) – ways use their centre.</summary>
    public static async Task<int> ImportAsync(JsonDocument doc, PbfImportJob.NearIndex near, SmsDbContext db, CancellationToken ct)
    {
        var elements = doc.RootElement.GetProperty("elements").EnumerateArray().ToList();
        var ids = elements.Select(e => e.GetProperty("id").GetInt64()).ToList();
        var existing = await db.Pois.Where(p => ids.Contains(p.OsmId)).ToDictionaryAsync(p => (p.OsmType, p.OsmId), ct);
        var n = 0;
        foreach (var el in elements)
        {
            var type = el.GetProperty("type").GetString()!;
            var osmId = el.GetProperty("id").GetInt64();
            double lat, lng;
            if (el.TryGetProperty("center", out var c)) { lat = c.GetProperty("lat").GetDouble(); lng = c.GetProperty("lon").GetDouble(); }
            else if (el.TryGetProperty("lat", out var la)) { lat = la.GetDouble(); lng = el.GetProperty("lon").GetDouble(); }
            else continue;
            if (!near.Near(lat, lng) || !el.TryGetProperty("tags", out var tags)) continue;
            var dict = tags.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            var transport = HarbourServices.TransportKind(dict);   // class public_transport, also for amenity=bus_station/ferry_terminal
            var key = transport is null ? Keys.FirstOrDefault(dict.ContainsKey) : "public_transport";
            if (key is null) continue;
            var poi = existing.GetValueOrDefault((type, osmId));
            if (poi is null) { poi = new Poi { OsmType = type, OsmId = osmId }; db.Pois.Add(poi); }
            poi.Class = key; poi.Kind = transport ?? Cut(dict[key], 200); poi.Name = Cut(dict.GetValueOrDefault("name"), 200); poi.TagsJson = tags.GetRawText();
            poi.Location = SeamarkEndpoints.ToMercator(lat, lng); poi.ImportedAt = DateTimeOffset.UtcNow;
            n++;
        }
        await db.SaveChangesAsync(ct);
        return n;
    }
}
