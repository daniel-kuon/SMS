using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;

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
            if (p is null) return Results.NotFound();
            using var tags = JsonDocument.Parse(p.TagsJson);
            string? T(string k) => tags.RootElement.TryGetProperty(k, out var v) ? v.GetString() : null;
            var addr = string.Join(", ", new[] { string.Join(" ", new[] { T("addr:street"), T("addr:housenumber") }.Where(s => s != null)), string.Join(" ", new[] { T("addr:postcode"), T("addr:city") }.Where(s => s != null)) }.Where(s => s.Length > 0));
            return Results.Ok(new
            {
                p.OsmType, p.OsmId, p.Name, p.Class, p.Kind, openingHours = T("opening_hours"), website = T("website") ?? T("contact:website"), phone = T("phone") ?? T("contact:phone"), email = T("email") ?? T("contact:email"),
                address = addr.Length > 0 ? addr : null, cuisine = T("cuisine"), wheelchair = T("wheelchair"), description = T("description"), osmUrl = $"https://www.openstreetmap.org/{p.OsmType}/{p.OsmId}",
            });
        }).WithTags("Pois");
        api.MapGet("/pois/status", async (SmsDbContext db, CancellationToken ct) => Results.Ok(new { count = await db.Pois.CountAsync(ct) })).WithTags("Pois");
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
            var key = Keys.FirstOrDefault(dict.ContainsKey);
            if (key is null) continue;
            var poi = existing.GetValueOrDefault((type, osmId));
            if (poi is null) { poi = new Poi { OsmType = type, OsmId = osmId }; db.Pois.Add(poi); }
            poi.Class = key; poi.Kind = dict[key]; poi.Name = dict.GetValueOrDefault("name"); poi.TagsJson = tags.GetRawText();
            poi.Location = SeamarkEndpoints.ToMercator(lat, lng); poi.ImportedAt = DateTimeOffset.UtcNow;
            n++;
        }
        await db.SaveChangesAsync(ct);
        return n;
    }
}
