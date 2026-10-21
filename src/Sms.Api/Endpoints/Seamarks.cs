using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Sms.Domain;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

/// <summary>
/// Imports OpenSeaMap sea marks (OSM nodes/ways with seamark:type) for a bounding box into the Seamarks table
/// that the tile server renders. Overpass is fine for regions up to a few degrees; Europe-wide imports should
/// use an osmium extract (spec 10 §10.1).
/// </summary>
public static class SeamarkEndpoints
{
    public static void MapSeamarks(this IEndpointRouteBuilder api)
    {
        api.MapGet("/seamarks/status", async (SmsDbContext db, CancellationToken ct) =>
            Results.Ok(new { count = await db.Seamarks.CountAsync(ct), lastImport = await db.Seamarks.MaxAsync(s => (DateTimeOffset?)s.ImportedAt, ct) })).WithTags("Admin");

        api.MapPost("/admin/seamarks/import-osm", async (string bbox, SmsDbContext db, CurrentUser current, IHttpClientFactory http, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var p = bbox.Split(',');
            if (p.Length != 4) return Invalid("bbox", "bbox=south,west,north,east");
            // out geom: ways with their geometry and relations with their members' geometry (lines and area borders); the centre comes from the bounds
            var query = $"[out:json][timeout:300];nwr[\"seamark:type\"]({bbox});out geom;";
            using var doc = await Overpass.QueryAsync(http, cfg, query, ct);
            if (doc is null) return Results.Problem(statusCode: 502, title: "Overpass ist zurzeit nicht erreichbar – später erneut versuchen oder eine Overpass-JSON-Datei hochladen.");
            var r = await ImportAsync(doc, db, ct);
            return Results.Ok(new { r.created, r.updated, r.skipped });
        }).RequireAuthorization().WithTags("Admin");

        // Same import from an uploaded Overpass JSON file (osmium/overpass exports; also used by tests).
        api.MapPost("/admin/seamarks/import-file", async (HttpRequest http, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (!http.HasFormContentType) return Invalid("file", "multipart/form-data erwartet.");
            var form = await http.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null) return Invalid("file", "Overpass-JSON-Datei fehlt.");
            await using var stream = file.OpenReadStream();
            JsonDocument doc;
            try { doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct); }
            catch (JsonException) { return Invalid("file", "Kein Overpass-JSON (Datei ist kein JSON)."); }
            using var owned = doc;
            if (!doc.RootElement.TryGetProperty("elements", out _)) return Invalid("file", "Kein Overpass-JSON (elements fehlt).");
            var r = await ImportAsync(doc, db, ct);
            return Results.Ok(new { r.created, r.updated, r.skipped });
        }).RequireAuthorization().WithTags("Admin");
    }

    /// <summary>Upserts all elements with seamark:type; returns counts.</summary>
    public static async Task<(int created, int updated, int skipped)> ImportAsync(JsonDocument doc, SmsDbContext db, CancellationToken ct)
    {
        {
            var elements = doc.RootElement.GetProperty("elements").EnumerateArray().ToList();
            var ids = elements.Select(e => (type: e.GetProperty("type").GetString()!, id: e.GetProperty("id").GetInt64())).ToList();
            var existing = await db.Seamarks.Where(s => ids.Select(i => i.id).Contains(s.OsmId)).ToDictionaryAsync(s => (s.OsmType, s.OsmId), ct);
            var (created, updated, skipped) = (0, 0, 0);
            foreach (var el in elements)
            {
                var type = el.GetProperty("type").GetString()!;
                var osmId = el.GetProperty("id").GetInt64();
                double lat, lng;
                if (el.TryGetProperty("center", out var c)) { lat = c.GetProperty("lat").GetDouble(); lng = c.GetProperty("lon").GetDouble(); }
                else if (el.TryGetProperty("lat", out var la)) { lat = la.GetDouble(); lng = el.GetProperty("lon").GetDouble(); }
                else if (el.TryGetProperty("bounds", out var bd)) { lat = (bd.GetProperty("minlat").GetDouble() + bd.GetProperty("maxlat").GetDouble()) / 2; lng = (bd.GetProperty("minlon").GetDouble() + bd.GetProperty("maxlon").GetDouble()) / 2; }
                else { skipped++; continue; }
                if (!el.TryGetProperty("tags", out var tags) || !tags.TryGetProperty("seamark:type", out var t)) { skipped++; continue; }
                var seamarkType = t.GetString()!;
                var mark = existing.GetValueOrDefault((type, osmId));
                if (mark is null) { mark = new Seamark { OsmType = type, OsmId = osmId }; db.Seamarks.Add(mark); created++; } else updated++;
                mark.Type = seamarkType;
                mark.Category = Tag(tags, $"seamark:{seamarkType}:category");
                mark.Name = Tag(tags, "seamark:name") ?? Tag(tags, "name");
                mark.Colour = Tag(tags, $"seamark:{seamarkType}:colour");
                mark.Shape = Tag(tags, $"seamark:{seamarkType}:shape");
                var (light, sectors) = Lights.Describe(tags.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? ""));
                mark.Light = light; mark.Sectors = sectors;
                mark.Line = type switch
                {
                    "way" when el.TryGetProperty("geometry", out var geometry) => ToMercatorLine(geometry),
                    "relation" when el.TryGetProperty("members", out var members) => OuterLine(members),
                    _ => null,
                };
                mark.TagsJson = tags.GetRawText();
                mark.Location = ToMercator(lat, lng);
                mark.ImportedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(ct);
            return (created, updated, skipped);
        }
    }

    private static string? Tag(JsonElement tags, string key) => tags.TryGetProperty(key, out var v) ? v.GetString() : null;

    /// <summary>Overpass "geometry" of a way → Mercator line; a closed ring (an area: restricted area, anchorage …) stays a closed line
    /// for its border, its point symbol sits at <see cref="Seamark.Location"/>. Null for degenerate ways.</summary>
    public static LineString? ToMercatorLine(JsonElement geometry)
    {
        var pts = geometry.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object)
            .Select(c => ToMercator(c.GetProperty("lat").GetDouble(), c.GetProperty("lon").GetDouble()).Coordinate).ToArray();
        return pts.Length < 2 ? null : new LineString(pts) { SRID = 3857 };
    }

    /// <summary>Border of a multipolygon relation (Overpass "out geom" members): the outer ways joined, the longest ring or line kept.</summary>
    // ponytail: one ring per row (Line is a LineString); a multipolygon with several outer rings shows only its largest border
    public static LineString? OuterLine(JsonElement members)
    {
        var merger = new NetTopologySuite.Operation.Linemerge.LineMerger();
        foreach (var m in members.EnumerateArray())
            if (m.GetProperty("type").GetString() == "way" && (!m.TryGetProperty("role", out var role) || role.GetString() is "outer" or "")
                && m.TryGetProperty("geometry", out var g) && ToMercatorLine(g) is { } line) merger.Add(line);
        var longest = merger.GetMergedLineStrings().OfType<LineString>().MaxBy(l => l.Length);
        if (longest is not null) longest.SRID = 3857;
        return longest;
    }

    /// <summary>WGS84 → Web Mercator (EPSG:3857), the projection vector tiles are cut in.</summary>
    public static Point ToMercator(double lat, double lng)
    {
        const double r = 6378137.0;
        var x = r * lng * Math.PI / 180;
        var y = r * Math.Log(Math.Tan(Math.PI / 4 + Math.Clamp(lat, -85.05, 85.05) * Math.PI / 360));
        return new Point(x, y) { SRID = 3857 };
    }
}
