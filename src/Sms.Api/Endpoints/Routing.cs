using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record CorrectionUpsert(CorrectionKind Kind, Geometry Geometry, string? Note, bool Active = true);

/// <summary>Spec 10 §10.4. Falls back to straight lines (method "straight-line") while no graph is built.</summary>
public static class RoutingEndpoints
{
    public const string Disclaimer = "Entfernungen sind Schätzwerte und nicht zur Navigation geeignet.";
    public const string Attribution = "© OpenStreetMap-Mitwirkende, OpenSeaMap";

    public static void MapRouting(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/routing").WithTags("Routing");

        g.MapGet("/nearby-ports", async (Guid? harbour, double? lat, double? lng, decimal? maxNm, int? limit, decimal? speedKn, SmsDbContext db, SeaRouter router, CancellationToken ct) =>
        {
            Point origin;
            if (harbour is { } hid)
            {
                var h = await db.Harbours.AsNoTracking().FirstOrDefaultAsync(x => x.Id == hid, ct);
                if (h is null) return Results.NotFound();
                origin = h.Location;
            }
            else if (lat is { } la && lng is { } ln) origin = Geo.Point(la, ln);
            else return Invalid("harbour", "harbour oder lat/lng angeben.");

            var max = Math.Clamp(maxNm ?? 50, 1, 150);
            var speed = speedKn is > 0 ? speedKn.Value : 5;
            var n = Math.Clamp(limit ?? 20, 1, 100);
            var method = "sea-graph";
            var graph = await router.NearbyAsync(harbour, harbour is null ? origin : null, (double)max, n, ct);
            List<NearbyPort> ports;
            if (graph is not null)
            {
                var ids = graph.Select(r => r.HarbourId).ToList();
                var names = await db.Harbours.AsNoTracking().Where(h => ids.Contains(h.Id)).Select(h => new { h.Id, h.Name, h.Location }).ToDictionaryAsync(h => h.Id, ct);
                ports = graph.Select(r => new NearbyPort(r.HarbourId, names[r.HarbourId].Name, names[r.HarbourId].Location.Y, names[r.HarbourId].Location.X, r.DistanceNm, r.StraightLineNm, Math.Round(r.DistanceNm / speed, 1), method)).ToList();
            }
            else
            {
                method = "straight-line";
                var maxMeters = (double)max * 1852;
                var hits = await db.Harbours.AsNoTracking()
                    .Where(h => h.IsActive && h.MergedIntoId == null && h.Id != harbour && h.Location.IsWithinDistance(origin, maxMeters))
                    .OrderBy(h => h.Location.Distance(origin)).Take(n)
                    .Select(h => new { h.Id, h.Name, h.Location, Meters = h.Location.Distance(origin) }).ToListAsync(ct);
                ports = hits.Select(h => { var nm = Math.Round((decimal)h.Meters / 1852m, 1); return new NearbyPort(h.Id, h.Name, h.Location.Y, h.Location.X, nm, nm, Math.Round(nm / speed, 1), method); }).ToList();
            }
            return Results.Ok(new { method, disclaimer = Disclaimer, attribution = Attribution, ports });
        });

        g.MapGet("/route", async (Guid? fromHarbour, Guid? toHarbour, string? from, string? to, SmsDbContext db, SeaRouter router, CancellationToken ct) =>
        {
            RouteResult? r = null;
            Point? a = null, b = null;
            if (fromHarbour is { } f && toHarbour is { } t)
            {
                a = await db.Harbours.AsNoTracking().Where(h => h.Id == f).Select(h => h.Location).FirstOrDefaultAsync(ct);
                b = await db.Harbours.AsNoTracking().Where(h => h.Id == t).Select(h => h.Location).FirstOrDefaultAsync(ct);
                if (a is null || b is null) return Results.NotFound();
                r = await router.RouteAsync(f, t, ct);
            }
            else if (TryParsePoint(from, out var pa) && TryParsePoint(to, out var pb))
            {
                a = pa; b = pb;
                r = await router.RouteAsync(pa, pb, ct);
            }
            else return Invalid("from", "fromHarbour/toHarbour oder from=lat,lng&to=lat,lng angeben.");

            if (r is null)
            {
                var nm = Math.Round((decimal)(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / 1852), 1);
                var line = Geo.Factory.CreateLineString([a.Coordinate, b.Coordinate]);
                return Results.Ok(new { distanceNm = nm, straightLineNm = nm, geometry = line, graphVersion = 0, method = "straight-line", disclaimer = Disclaimer, attribution = Attribution });
            }
            return Results.Ok(new { distanceNm = r.DistanceNm, straightLineNm = r.StraightLineNm, geometry = r.Geometry, graphVersion = r.GraphVersion, method = r.Method, disclaimer = Disclaimer, attribution = Attribution });
        });

        g.MapGet("/status", async (SeaRouter router, RoutingGraphBuilder builder, CancellationToken ct) =>
        {
            var s = await router.StatusAsync(ct);
            return Results.Ok(new { method = s.ActiveVersion > 0 ? "sea-graph" : "straight-line", graphVersion = s.ActiveVersion, s.BuiltAt, s.Region, s.Nodes, s.Edges, building = builder.IsRunning, progress = builder.Progress, options = builder.Options });
        });

        var admin = api.MapGroup("/admin/routing").WithTags("Admin").RequireAuthorization();
        admin.MapPost("/rebuild", async (CurrentUser current, RoutingGraphBuilder builder, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            return builder.TryStart() ? Results.Accepted("/api/routing/status") : Results.Problem(statusCode: 409, title: "Ein Build läuft bereits.");
        });
        admin.MapGet("/corrections", async (CurrentUser current, SmsDbContext db, CancellationToken ct) =>
            await current.IsAdminAsync(ct) ? Results.Ok(await db.RoutingCorrections.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(ct)) : Results.Forbid());
        admin.MapPost("/corrections", async (CorrectionUpsert req, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (req.Kind == CorrectionKind.Block && req.Geometry is not Polygon) return Invalid("geometry", "Block braucht ein Polygon.");
            if (req.Kind == CorrectionKind.Passage && req.Geometry is not LineString) return Invalid("geometry", "Passage braucht eine LineString.");
            req.Geometry.SRID = 4326;
            var c = new RoutingCorrection { Kind = req.Kind, Geometry = req.Geometry, Note = req.Note, Active = req.Active, CreatedById = current.Id };
            db.RoutingCorrections.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/routing/corrections/{c.Id}", new { c.Id });
        });
        admin.MapPut("/corrections/{id:guid}", async (Guid id, CorrectionUpsert req, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var c = await db.RoutingCorrections.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            req.Geometry.SRID = 4326;
            c.Kind = req.Kind; c.Geometry = req.Geometry; c.Note = req.Note; c.Active = req.Active; c.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        admin.MapDelete("/corrections/{id:guid}", async (Guid id, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
            await current.IsAdminAsync(ct) ? (await db.RoutingCorrections.Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : Results.NotFound()) : Results.Forbid());
        admin.MapGet("/unreachable-harbours", async (CurrentUser current, SmsDbContext db, SeaRouter router, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var version = await router.ActiveVersionAsync(ct);
            var reachable = await db.Database.SqlQueryRaw<Guid>("SELECT harbour_id AS \"Value\" FROM routing_harbour_access WHERE graph_version = {0}", version).ToListAsync(ct);
            var region = (await router.StatusAsync(ct)).Region?.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var q = db.Harbours.AsNoTracking().Where(h => h.IsActive && h.MergedIntoId == null && !reachable.Contains(h.Id));
            if (region is { Length: 4 }) { var env = Geo.Factory.ToGeometry(new Envelope(region[0], region[2], region[1], region[3])); q = q.Where(h => h.Location.Intersects(env)); }
            return Results.Ok(await q.Select(h => new { h.Id, h.Name }).ToListAsync(ct));
        });
    }

    private static bool TryParsePoint(string? s, out Point p)
    {
        p = null!;
        var parts = s?.Split(',');
        if (parts is not { Length: 2 } || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lng)) return false;
        p = Geo.Point(lat, lng);
        return true;
    }
}
