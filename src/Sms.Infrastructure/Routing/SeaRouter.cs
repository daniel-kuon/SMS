using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using Npgsql;
using NpgsqlTypes;
using Sms.Domain;

namespace Sms.Infrastructure.Routing;

public sealed record RouteResult(decimal DistanceNm, decimal StraightLineNm, LineString Geometry, int GraphVersion, string Method);
public sealed record NearbyResult(Guid HarbourId, decimal DistanceNm, decimal StraightLineNm);
public sealed record GraphStatus(int ActiveVersion, string? BuiltAt, string? Region, long Nodes, long Edges);

/// <summary>Query side of spec 10 §10.2: snapping, pgRouting A* / driving distance, string-pulling smoothing, cache.</summary>
public sealed class SeaRouter(SmsDbContext db, RoutingGraphBuilder builder, ILogger<SeaRouter> logger)
{
    private static readonly ActivitySource Activity = new("Sms");
    private const double Nm = 1852.0;

    public async Task<GraphStatus> StatusAsync(CancellationToken ct)
    {
        var meta = await db.Database.SqlQueryRaw<MetaRow>("SELECT key AS \"Key\", value AS \"Value\" FROM routing_meta").ToDictionaryAsync(r => r.Key, r => r.Value, ct);
        return new GraphStatus(int.TryParse(meta.GetValueOrDefault("active_version"), out var v) ? v : 0, meta.GetValueOrDefault("built_at"), meta.GetValueOrDefault("region"),
            long.TryParse(meta.GetValueOrDefault("nodes"), out var n) ? n : 0, long.TryParse(meta.GetValueOrDefault("edges"), out var e) ? e : 0);
    }

    public async Task<int> ActiveVersionAsync(CancellationToken ct) => (await StatusAsync(ct)).ActiveVersion;

    /// <summary>Harbour → harbour route; null when either harbour is not connected to the active graph or no path exists.</summary>
    public async Task<RouteResult?> RouteAsync(Guid from, Guid to, CancellationToken ct, double? airDraftM = null)
    {
        var version = await ActiveVersionAsync(ct);
        if (version == 0) return null;
        // the cache holds unrestricted routes only; an air draft may close bridges, so those are computed every time
        var cached = airDraftM is null ? await db.RouteCaches.AsNoTracking().FirstOrDefaultAsync(r => r.FromHarbourId == from && r.ToHarbourId == to && r.GraphVersion == version, ct) : null;
        var a = await db.Harbours.AsNoTracking().Where(h => h.Id == from).Select(h => h.Location).FirstOrDefaultAsync(ct);
        var b = await db.Harbours.AsNoTracking().Where(h => h.Id == to).Select(h => h.Location).FirstOrDefaultAsync(ct);
        if (a is null || b is null) return null;
        var straight = Math.Round((decimal)(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / Nm), 1);
        if (cached is not null) return new RouteResult(cached.DistanceNm, straight, cached.Geometry, version, "sea-graph");
        var start = await AccessNodeAsync(from, version, ct);
        var end = await AccessNodeAsync(to, version, ct);
        if (start is null || end is null) return null;
        var result = await RouteNodesAsync(start.Value, end.Value, a, b, version, ct, airDraftM);
        if (result is null) return null;
        if (airDraftM is not null) return result;
        db.RouteCaches.Add(new RouteCache { FromHarbourId = from, ToHarbourId = to, GraphVersion = version, DistanceNm = result.DistanceNm, Geometry = result.Geometry });
        db.RouteCaches.Add(new RouteCache { FromHarbourId = to, ToHarbourId = from, GraphVersion = version, DistanceNm = result.DistanceNm, Geometry = (LineString)result.Geometry.Reverse() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); } // concurrent insert of the same pair
        return result;
    }

    /// <summary>Point → point route (map clicks). Points are snapped to the nearest water node within 2 km.</summary>
    public async Task<RouteResult?> RouteAsync(Point from, Point to, CancellationToken ct, double? airDraftM = null)
    {
        var version = await ActiveVersionAsync(ct);
        if (version == 0) return null;
        var start = await SnapAsync(from, version, ct);
        var end = await SnapAsync(to, version, ct);
        if (start is null || end is null) return null;
        return await RouteNodesAsync(start.Value, end.Value, from, to, version, ct, airDraftM);
    }

    public async Task<List<NearbyResult>?> NearbyAsync(Guid? harbourId, Point? origin, double maxNm, int limit, CancellationToken ct)
    {
        using var span = Activity.StartActivity("routing.nearby");
        var version = await ActiveVersionAsync(ct);
        if (version == 0) return null;
        long? start = harbourId is { } h ? await AccessNodeAsync(h, version, ct) : origin is not null ? await SnapAsync(origin, version, ct) : null;
        if (start is null) return null;
        var startPoint = origin ?? await db.Harbours.AsNoTracking().Where(x => x.Id == harbourId).Select(x => x.Location).FirstAsync(ct);
        var radiusM = maxNm * Nm;
        var env = Expand(startPoint, startPoint, radiusM * 1.15);
        var rows = await db.Database.SqlQueryRaw<NearbyRow>("""
            SELECT a.harbour_id AS "HarbourId", (d.agg_cost + a.distance_m) AS "Metres"
            FROM pgr_drivingDistance(
                'SELECT id, source, target, cost, reverse_cost FROM routing_edge WHERE graph_version = ' || {0} || ' AND geom && ST_MakeEnvelope(' || {1} || ',' || {2} || ',' || {3} || ',' || {4} || ', 4326)',
                {5}::bigint, {6}::double precision, directed := false) d
            JOIN routing_harbour_access a ON a.node_id = d.node AND a.graph_version = {0}
            """, version, env.MinX, env.MinY, env.MaxX, env.MaxY, start.Value, radiusM).ToListAsync(ct);
        var ids = rows.Select(r => r.HarbourId).Where(id => id != harbourId).ToList();
        var locations = await db.Harbours.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Location }).ToDictionaryAsync(x => x.Id, x => x.Location, ct);
        return rows.Where(r => r.HarbourId != harbourId && locations.ContainsKey(r.HarbourId))
            .Select(r => new NearbyResult(r.HarbourId, Math.Round((decimal)(r.Metres / Nm), 1), Math.Round((decimal)(RoutingGraphBuilder.Haversine(startPoint.Y, startPoint.X, locations[r.HarbourId].Y, locations[r.HarbourId].X) / Nm), 1)))
            .OrderBy(r => r.DistanceNm).Take(limit).ToList();
    }

    private async Task<RouteResult?> RouteNodesAsync(long start, long end, Point a, Point b, int version, CancellationToken ct, double? airDraftM = null)
    {
        // bridges lower than the ship's air draft close their passage edges (clearance_m is null elsewhere)
        var clearance = airDraftM is { } ad ? $" AND (clearance_m IS NULL OR clearance_m >= {ad.ToString(System.Globalization.CultureInfo.InvariantCulture)})" : "";
        using var span = Activity.StartActivity("routing.route");
        span?.SetTag("routing.graph_version", version);
        var straightM = RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X);
        var maxM = builder.Options.MaxRouteNm * Nm;
        if (straightM > maxM) return null;
        List<Coordinate>? path = null;
        for (var pad = 0.3; pad <= 2.5 && path is null; pad *= 2)
        {
            var env = Expand(a, b, Math.Max(straightM * pad, 20_000));
            var rows = await db.Database.SqlQueryRaw<PathRow>("""
                SELECT r.seq AS "Seq", ST_X(n.geom) AS "X", ST_Y(n.geom) AS "Y"
                FROM pgr_aStar(
                    'SELECT id, source, target, cost, reverse_cost, x1, y1, x2, y2 FROM routing_edge WHERE graph_version = ' || {0} || ' AND geom && ST_MakeEnvelope(' || {1} || ',' || {2} || ',' || {3} || ',' || {4} || ', 4326)' || {7},
                    {5}::bigint, {6}::bigint, directed := false, heuristic := 2) r
                JOIN routing_node n ON n.id = r.node AND n.graph_version = {0}
                ORDER BY r.seq
                """, version, env.MinX, env.MinY, env.MaxX, env.MaxY, start, end, clearance).ToListAsync(ct);
            if (rows.Count > 0) path = rows.Select(r => new Coordinate(r.X, r.Y)).ToList();
        }
        if (path is null) { logger.LogInformation("No sea route between nodes {Start} and {End}", start, end); return null; }
        path.Insert(0, a.Coordinate); path.Add(b.Coordinate);
        var smoothed = await SmoothAsync(path, version, ct);
        var metres = 0.0;
        for (var i = 1; i < smoothed.Count; i++) metres += RoutingGraphBuilder.Haversine(smoothed[i - 1].Y, smoothed[i - 1].X, smoothed[i].Y, smoothed[i].X);
        var line = Geo.Factory.CreateLineString(smoothed.ToArray());
        span?.SetTag("routing.distance_nm", metres / Nm);
        return new RouteResult(Math.Round((decimal)(metres / Nm), 1), Math.Round((decimal)(straightM / Nm), 1), line, version, "sea-graph");
    }

    /// <summary>String pulling: connect each point to the farthest later point whose segment does not cross land.</summary>
    private async Task<List<Coordinate>> SmoothAsync(List<Coordinate> path, int version, CancellationToken ct)
    {
        var env = new Envelope(); foreach (var c in path) env.ExpandToInclude(c); env.ExpandBy(0.05);
        var land = await db.Database.SqlQueryRaw<GeomRow>("SELECT geom AS \"Geom\" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)", version, env.MinX, env.MinY, env.MaxX, env.MaxY).ToListAsync(ct);
        var index = new STRtree<IPreparedGeometry>();
        foreach (var g in land) index.Insert(g.Geom.EnvelopeInternal, PreparedGeometryFactory.Prepare(g.Geom));
        index.Build();
        var result = new List<Coordinate> { path[0] };
        var i = 0;
        while (i < path.Count - 1)
        {
            var j = path.Count - 1;
            for (; j > i + 1; j--)
            {
                var seg = Geo.Factory.CreateLineString([path[i], path[j]]);
                var crosses = false;
                foreach (var g in index.Query(seg.EnvelopeInternal)) if (g.Intersects(seg)) { crosses = true; break; }
                if (!crosses) break;
            }
            result.Add(path[j]);
            i = j;
        }
        return result;
    }

    private async Task<long?> AccessNodeAsync(Guid harbourId, int version, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<long>("SELECT node_id AS \"Value\" FROM routing_harbour_access WHERE graph_version = {0} AND harbour_id = {1}", version, harbourId).FirstOrDefaultAsync(ct) is var id && id != 0 ? id : null;

    private async Task<long?> SnapAsync(Point p, int version, CancellationToken ct)
    {
        var row = await db.Database.SqlQueryRaw<SnapRow>("""
            SELECT id AS "Id", ST_DistanceSphere(geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326)) AS "Metres"
            FROM routing_node WHERE graph_version = {0} ORDER BY geom <-> ST_SetSRID(ST_MakePoint({1}, {2}), 4326) LIMIT 1
            """, version, p.X, p.Y).FirstOrDefaultAsync(ct);
        return row is not null && row.Metres <= 2000 ? row.Id : null;
    }

    private static Envelope Expand(Point a, Point b, double metres)
    {
        var env = new Envelope(a.Coordinate); env.ExpandToInclude(b.Coordinate);
        var dLat = metres / 111_320.0; var dLng = metres / (111_320.0 * Math.Cos((a.Y + b.Y) / 2 * Math.PI / 180));
        return new Envelope(env.MinX - dLng, env.MaxX + dLng, env.MinY - dLat, env.MaxY + dLat);
    }

    private sealed class MetaRow { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }
    private sealed class PathRow { public long Seq { get; set; } public double X { get; set; } public double Y { get; set; } }
    private sealed class NearbyRow { public Guid HarbourId { get; set; } public double Metres { get; set; } }
    private sealed class SnapRow { public long Id { get; set; } public double Metres { get; set; } }
    private sealed class GeomRow { public Geometry Geom { get; set; } = null!; }
}
