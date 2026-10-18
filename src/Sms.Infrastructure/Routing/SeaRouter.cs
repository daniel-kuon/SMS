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

public sealed record RouteResult(decimal DistanceNm, decimal StraightLineNm, LineString Geometry, int GraphVersion, string Method, IReadOnlyDictionary<string, long>? Timings = null);
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
    /// <summary>Routing:DefaultDraftM (null when 0 = off).</summary>
    public double? DefaultDraftM => builder.Options.DefaultDraftM > 0 ? builder.Options.DefaultDraftM : null;

    /// <summary>Harbour → harbour route; null when either harbour is not connected to the active graph or no path exists.</summary>
    public async Task<RouteResult?> RouteAsync(Guid from, Guid to, CancellationToken ct, double? airDraftM = null, double? draftM = null)
    {
        var version = await ActiveVersionAsync(ct);
        if (version == 0) return null;
        // the cache holds unrestricted routes only; an air draft may close bridges and a draft shallow areas, so those are computed every time
        var cached = airDraftM is null && draftM is null ? await db.RouteCaches.AsNoTracking().FirstOrDefaultAsync(r => r.FromHarbourId == from && r.ToHarbourId == to && r.GraphVersion == version, ct) : null;
        var a = await db.Harbours.AsNoTracking().Where(h => h.Id == from).Select(h => h.Location).FirstOrDefaultAsync(ct);
        var b = await db.Harbours.AsNoTracking().Where(h => h.Id == to).Select(h => h.Location).FirstOrDefaultAsync(ct);
        if (a is null || b is null) return null;
        var straight = Math.Round((decimal)(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / Nm), 1);
        if (cached is not null) return new RouteResult(cached.DistanceNm, straight, cached.Geometry, version, "sea-graph");
        var start = await AccessNodeAsync(from, version, ct);
        var end = await AccessNodeAsync(to, version, ct);
        if (start is null || end is null) return null;
        var result = await RouteNodesAsync(start.Value, end.Value, a, b, version, ct, airDraftM, draftM);
        if (result is null) return null;
        if (airDraftM is not null || draftM is not null) return result;
        db.RouteCaches.Add(new RouteCache { FromHarbourId = from, ToHarbourId = to, GraphVersion = version, DistanceNm = result.DistanceNm, Geometry = result.Geometry });
        db.RouteCaches.Add(new RouteCache { FromHarbourId = to, ToHarbourId = from, GraphVersion = version, DistanceNm = result.DistanceNm, Geometry = (LineString)result.Geometry.Reverse() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); } // concurrent insert of the same pair
        return result;
    }

    /// <summary>Point → point route (map clicks). Points are snapped to the nearest water node within 2 km.</summary>
    public async Task<RouteResult?> RouteAsync(Point from, Point to, CancellationToken ct, double? airDraftM = null, double? draftM = null)
    {
        var version = await ActiveVersionAsync(ct);
        if (version == 0) return null;
        var start = await SnapAsync(from, version, ct);
        var end = await SnapAsync(to, version, ct);
        if (start is null || end is null) return null;
        return await RouteNodesAsync(start.Value, end.Value, from, to, version, ct, airDraftM, draftM);
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

    private async Task<RouteResult?> RouteNodesAsync(long start, long end, Point a, Point b, int version, CancellationToken ct, double? airDraftM = null, double? draftM = null)
    {
        // bridges lower than the ship's air draft close their passage edges (clearance_m is null elsewhere); charted depth areas
        // shallower than draft + margin close grid edges (min_depth_m is null on marked passages: a buoyed channel is followed anyway)
        var clearance = airDraftM is { } ad ? $" AND (clearance_m IS NULL OR clearance_m >= {ad.ToString(System.Globalization.CultureInfo.InvariantCulture)})" : "";
        // the edges at the two end nodes are exempt from the depth limit: a harbour basin is charted shallow at this resolution and
        // one has to get in anyway; from there on only deep legs or the buoyed channel (passage edges carry no limit) continue
        // … and so is the harbour approach (HarbourAccessM around both ends): the charted fathom steps call a dredged fjord or marina
        // basin shallow, and one has to get in and out anyway (Nyborg Fjord: 1.8 m charted, 7 m dredged channel)
        // the shallow approach legs that remain cost ×3: the buoyed channel (no depth limit, cost ×1) wins where one exists, the
        // bare crossing of a charted flat is taken only when nothing else leads in
        var costExpr = "cost, reverse_cost";
        if (draftM is { } dm)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var accessDeg = (builder.Options.HarbourAccessM / WaterGraphTiler.MPerDegLat).ToString(inv);
            var limit = (dm + builder.Options.DepthSafetyMarginM).ToString(inv);
            clearance += $" AND (min_depth_m IS NULL OR min_depth_m >= {limit} OR source IN ({start}, {end}) OR target IN ({start}, {end})"
                + $" OR ST_DWithin(geom, ST_SetSRID(ST_MakePoint({a.X.ToString(inv)}, {a.Y.ToString(inv)}), 4326), {accessDeg}) OR ST_DWithin(geom, ST_SetSRID(ST_MakePoint({b.X.ToString(inv)}, {b.Y.ToString(inv)}), 4326), {accessDeg}))";
            costExpr = $"CASE WHEN min_depth_m < {limit} THEN cost * 3 ELSE cost END AS cost, CASE WHEN min_depth_m < {limit} THEN reverse_cost * 3 ELSE reverse_cost END AS reverse_cost";
        }
        using var span = Activity.StartActivity("routing.route");
        span?.SetTag("routing.graph_version", version);
        var straightM = RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X);
        var maxM = builder.Options.MaxRouteNm * Nm;
        if (straightM > maxM) return null;
        var timings = new Dictionary<string, long>(); var clock = Stopwatch.StartNew();
        List<Coordinate>? path = null;
        for (var pad = 0.3; pad <= 2.5 && path is null; pad *= 2)
        {
            var env = Expand(a, b, Math.Max(straightM * pad, 20_000));
            // the path is the chain of edge geometries (a contracted passage edge is a whole canal stretch), each oriented from
            // the node the search left it at; the last row (edge -1) only names the end node
            var rows = await db.Database.SqlQueryRaw<PathRow>("""
                SELECT r.seq AS "Seq", r.node AS "Node", r.edge AS "Edge", e.source AS "Source", e.geom AS "Geom", ST_X(n.geom) AS "X", ST_Y(n.geom) AS "Y"
                FROM pgr_aStar(
                    'SELECT id, source, target, ' || {8} || ', x1, y1, x2, y2 FROM routing_edge WHERE graph_version = ' || {0} || ' AND geom && ST_MakeEnvelope(' || {1} || ',' || {2} || ',' || {3} || ',' || {4} || ', 4326)' || {7},
                    {5}::bigint, {6}::bigint, directed := false, heuristic := 2) r
                JOIN routing_node n ON n.id = r.node AND n.graph_version = {0}
                LEFT JOIN routing_edge e ON e.id = r.edge AND e.graph_version = {0}
                ORDER BY r.seq
                """, version, env.MinX, env.MinY, env.MaxX, env.MaxY, start, end, clearance, costExpr).ToListAsync(ct);
            if (rows.Count > 0)
            {
                path = [];
                foreach (var r in rows)
                {
                    if (r.Geom is null) { if (path.Count == 0 || !path[^1].Equals2D(new Coordinate(r.X, r.Y))) path.Add(new Coordinate(r.X, r.Y)); continue; }
                    var cs = r.Geom.Coordinates; if (r.Source != r.Node) Array.Reverse(cs);
                    foreach (var c in cs) if (path.Count == 0 || !path[^1].Equals2D(c)) path.Add(c);
                }
            }
        }
        timings["astar"] = clock.ElapsedMilliseconds; clock.Restart();
        if (path is null) { logger.LogInformation("No sea route between nodes {Start} and {End}", start, end); return null; }
        path.Insert(0, a.Coordinate); path.Add(b.Coordinate);
        var smoothed = await SmoothAsync(path, version, ct, airDraftM, draftM, timings);
        timings["smooth"] = clock.ElapsedMilliseconds;
        var metres = 0.0;
        for (var i = 1; i < smoothed.Count; i++) metres += RoutingGraphBuilder.Haversine(smoothed[i - 1].Y, smoothed[i - 1].X, smoothed[i].Y, smoothed[i].X);
        var line = Geo.Factory.CreateLineString(smoothed.ToArray());
        span?.SetTag("routing.distance_nm", metres / Nm);
        return new RouteResult(Math.Round((decimal)(metres / Nm), 1), Math.Round((decimal)(straightM / Nm), 1), line, version, "sea-graph", timings);
    }

    /// <summary>
    /// String pulling: connect each point to the farthest later point whose segment does not cross land. Offshore grid edges
    /// are up to 20 km long, so the path is densified first (every ≤ 500 m) – otherwise the pulled string could only bend at
    /// grid nodes and kept a dog-leg when the direct line to the next node grazed the coast.
    /// </summary>
    private sealed record SmoothStatics(STRtree<(Geometry geom, double? clearance)> Bridges, STRtree<Geometry> Zone);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Lazy<Task<SmoothStatics>>> _statics = new();

    /// <summary>Per graph version: the bridge pieces after chains and charted spans, and the channel zone pieces; built on first use.</summary>
    private Task<SmoothStatics> StaticsAsync(int version, CancellationToken ct)
    {
        foreach (var old in _statics.Keys.Where(k => k != version).ToList()) _statics.TryRemove(old, out _);
        return _statics.GetOrAdd(version, v => new Lazy<Task<SmoothStatics>>(() => BuildStaticsAsync(v))).Value;
    }

    private async Task<SmoothStatics> BuildStaticsAsync(int version)
    {
        var sdb = db; // the first route of a version builds the cache; the caller awaits it, so its scope outlives the build
        var bridges = new STRtree<(Geometry, double?)>();
        var raw = (await sdb.RoutingCorrections.AsNoTracking().Where(c => c.Active && (c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.OpeningBridge)).Select(c => new { c.Kind, c.Geometry, c.ClearanceM }).ToListAsync())
            .Select(c => (c.Geometry is LineString ? c.Geometry : c.Geometry.Centroid, c.Kind, c.ClearanceM)).ToList();
        foreach (var (g, _, cl) in Obstacles.WithSpans(raw)) bridges.Insert(g.EnvelopeInternal, (g, cl));
        bridges.Build();
        var zone = new STRtree<Geometry>();
        var o = builder.Options;
        if (o.ChannelPreferenceM > 0)
        {
            var fairways = (await sdb.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == CorrectionKind.Passage && c.Source == CorrectionSource.Derived && c.Note != null && c.Note.StartsWith("Fahrwasser")).Select(c => c.Geometry).ToListAsync()).OfType<LineString>().ToList(); // same set as the builder's channel zone
            if (fairways.Count > 0)
            {
                var corridorDeg = 150 / WaterGraphTiler.MPerDegLat;
                var corridor = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(fairways.Select(f => f.Buffer(corridorDeg)).ToList());
                var corridorPrepared = PreparedGeometryFactory.Prepare(corridor);
                foreach (var z in ChannelPreference.Zone(fairways, [], o.ChannelPreferenceM))
                {
                    var g = corridorPrepared.Intersects(z) ? z.Difference(corridor) : z;
                    if (!g.IsEmpty) zone.Insert(g.EnvelopeInternal, g);
                }
            }
        }
        zone.Build();
        return new SmoothStatics(bridges, zone);
    }

    private async Task<List<Coordinate>> SmoothAsync(List<Coordinate> path, int version, CancellationToken ct, double? airDraftM = null, double? draftM = null, Dictionary<string, long>? timings = null)
    {
        var clock = Stopwatch.StartNew();
        void Lap(string k) { if (timings is not null) timings[k] = clock.ElapsedMilliseconds; clock.Restart(); }
        path = Densify(path, 500);
        var env = new Envelope(); foreach (var c in path) env.ExpandToInclude(c); env.ExpandBy(0.05);
        var land = await db.Database.SqlQueryRaw<GeomRow>("SELECT geom AS \"Geom\" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)", version, env.MinX, env.MinY, env.MaxX, env.MaxY).ToListAsync(ct);
        var index = new STRtree<IPreparedGeometry>();
        foreach (var g in land) index.Insert(g.Geom.EnvelopeInternal, PreparedGeometryFactory.Prepare(g.Geom));
        Lap("smooth-land");
        // what A* avoided must stay avoided when the string is pulled: too-low bridges (a circle around the point, a band along a
        // line), the impassable parts of bridges with charted spans, and too-shallow areas count as land
        {
            // bridge pieces (chains, charted spans) are the same for every route of a graph version: computed once, queried by envelope
            var statics = await StaticsAsync(version, ct);
            var radiusDeg = builder.Options.BridgeMatchAreaM / WaterGraphTiler.MPerDegLat;
            foreach (var (g, clearance) in statics.Bridges.Query(env))
            {
                if (clearance is not { } cl || (cl > 0 && (airDraftM is not { } ad || cl >= ad))) continue;
                var b = g is Point ? g.Buffer(radiusDeg) : g.Buffer(300 / WaterGraphTiler.MPerDegLat);
                if (b.EnvelopeInternal.Intersects(env)) index.Insert(b.EnvelopeInternal, PreparedGeometryFactory.Prepare(b));
            }
        }
        Lap("smooth-bridges");
        if (draftM is { } dm)
        {
            var shallow = await db.Database.SqlQueryRaw<GeomRow>("SELECT geom AS \"Geom\" FROM routing_depth WHERE min_depth_m < {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)", dm + builder.Options.DepthSafetyMarginM, env.MinX, env.MinY, env.MaxX, env.MaxY).ToListAsync(ct);
            foreach (var g in shallow) index.Insert(g.Geom.EnvelopeInternal, PreparedGeometryFactory.Prepare(g.Geom));
        }
        Lap("smooth-depth");
        // the channel zone (water next to a buoy chain, minus the chain's own corridor) counts as an obstacle too: where the chart
        // has no depth data (Wadden Sea gaps) nothing else stops the string from cutting straight across the flats the chain avoids
        var o = builder.Options;
        if (o.ChannelPreferenceM > 0)
        {
            // the channel zone (band next to every buoy chain minus the chains' corridors) likewise: once per version
            foreach (var z in (await StaticsAsync(version, ct)).Zone.Query(env)) index.Insert(z.EnvelopeInternal, PreparedGeometryFactory.Prepare(z));
        }
        Lap("smooth-zone");
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
        Lap("smooth-pull");
        return result;
    }

    /// <summary>Intermediate points every ≤ stepM along the path (bend candidates for the string pulling).</summary>
    public static List<Coordinate> Densify(IReadOnlyList<Coordinate> path, double stepM)
    {
        var result = new List<Coordinate> { path[0] };
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1]; var b = path[i];
            var n = (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / stepM);
            for (var k = 1; k < n; k++) result.Add(new Coordinate(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n));
            result.Add(b);
        }
        return result;
    }

    private async Task<long?> AccessNodeAsync(Guid harbourId, int version, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<long>("SELECT node_id AS \"Value\" FROM routing_harbour_access WHERE graph_version = {0} AND harbour_id = {1}", version, harbourId).FirstOrDefaultAsync(ct) is var id && id != 0 ? id : null;

    /// <summary>Nearest node within 2 km; grid (water-cell) nodes win over passage vertices, which sit on a line and would send the route to the line's end.</summary>
    /// <summary>
    /// Nearest graph node for a map click. On water the grid is coarse offshore (cells up to StartCellM wide), so the search
    /// reaches a whole cell; on land (coast, harbour basins counted as land) only 2 km, so inland clicks stay unroutable.
    /// </summary>
    private async Task<long?> SnapAsync(Point p, int version, CancellationToken ct)
    {
        var onLand = await db.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM routing_land WHERE graph_version = {0} AND geom && ST_SetSRID(ST_MakePoint({1}, {2}), 4326) AND ST_Intersects(geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326))", version, p.X, p.Y).AnyAsync(ct);
        var maxM = onLand ? 2000 : Math.Max(2000, builder.Options.StartCellM);
        var degLat = maxM / WaterGraphTiler.MPerDegLat; var degLng = degLat / Math.Max(0.2, Math.Cos(p.Y * Math.PI / 180));
        // first choice: a node the point can see (the first leg is drawn straight), then the plain nearest
        var row = onLand ? null : await db.Database.SqlQueryRaw<SnapRow>("""
            SELECT n.id AS "Id", ST_DistanceSphere(n.geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326)) AS "Metres"
            FROM routing_node n WHERE n.graph_version = {0} AND n.geom && ST_MakeEnvelope({1} - {3}, {2} - {4}, {1} + {3}, {2} + {4}, 4326)
              AND NOT EXISTS (SELECT 1 FROM routing_land l WHERE l.graph_version = {0} AND l.geom && ST_MakeLine(ST_SetSRID(ST_MakePoint({1}, {2}), 4326), n.geom) AND ST_Intersects(l.geom, ST_MakeLine(ST_SetSRID(ST_MakePoint({1}, {2}), 4326), n.geom)))
            ORDER BY n.geom <-> ST_SetSRID(ST_MakePoint({1}, {2}), 4326) LIMIT 1
            """, version, p.X, p.Y, degLng, degLat).FirstOrDefaultAsync(ct);
        row ??= await db.Database.SqlQueryRaw<SnapRow>("""
            SELECT id AS "Id", ST_DistanceSphere(geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326)) AS "Metres"
            FROM routing_node WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1} - {3}, {2} - {4}, {1} + {3}, {2} + {4}, 4326)
            ORDER BY CASE WHEN id <= (SELECT COALESCE(MAX(value::bigint), 0) FROM routing_meta WHERE key = 'grid_nodes') THEN 0 ELSE 1 END, geom <-> ST_SetSRID(ST_MakePoint({1}, {2}), 4326) LIMIT 1
            """, version, p.X, p.Y, degLng, degLat).FirstOrDefaultAsync(ct);
        return row is not null && row.Metres <= maxM ? row.Id : null;
    }

    private static Envelope Expand(Point a, Point b, double metres)
    {
        var env = new Envelope(a.Coordinate); env.ExpandToInclude(b.Coordinate);
        var dLat = metres / 111_320.0; var dLng = metres / (111_320.0 * Math.Cos((a.Y + b.Y) / 2 * Math.PI / 180));
        return new Envelope(env.MinX - dLng, env.MaxX + dLng, env.MinY - dLat, env.MaxY + dLat);
    }

    private sealed class MetaRow { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }
    private sealed class PathRow { public long Seq { get; set; } public long Node { get; set; } public long Edge { get; set; } public long? Source { get; set; } public Geometry? Geom { get; set; } public double X { get; set; } public double Y { get; set; } }
    private sealed class NearbyRow { public Guid HarbourId { get; set; } public double Metres { get; set; } }
    private sealed class SnapRow { public long Id { get; set; } public double Metres { get; set; } }
    private sealed class GeomRow { public Geometry Geom { get; set; } = null!; }
}
