using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Distance;
using Npgsql;
using NpgsqlTypes;
using Sms.Domain;

namespace Sms.Infrastructure.Routing;

public sealed record RouteResult(decimal DistanceNm, decimal StraightLineNm, LineString Geometry, int GraphVersion, string Method, IReadOnlyDictionary<string, long>? Timings = null, IReadOnlyList<RouteWarning>? Warnings = null);
/// <summary>Something the route assumes that the chart data does not show; Kind "assumedPassage": a passage through a strait the coastline closes.
/// Kind "startOnLand"/"endOnLand": the clicked point (Lat/Lng) lay on land, the route starts/ends at the nearest navigable water (Snapped*, DistanceM away).
/// Kind "checkDepth": the route runs through water charted (or, without data in the coastal strip, next to a band) shallower than the ship's warning depth at Lat/Lng, DistanceM the length of that stretch.
/// Kind "uncertainDepth": no route leads through definitely deep water and buoyed channels only; this one crosses water whose band floor lies below the minimum depth at Lat/Lng (DistanceM: length).
/// Kind "bridge": a fixed bridge with a known clearance (DistanceM) on a route asked for without an air draft; "bridgeUnknown": a fixed bridge without a charted clearance.</summary>
public sealed record RouteWarning(string Kind, double Lat, double Lng, string Note, double? SnappedLat = null, double? SnappedLng = null, double? DistanceM = null);
public sealed record NearbyResult(Guid HarbourId, decimal DistanceNm, decimal StraightLineNm);
public sealed record GraphStatus(int ActiveVersion, string? BuiltAt, string? Region, long Nodes, long Edges);

/// <summary>Query side of spec 10 §10.2: snapping, in-memory A* (SeaGraph, one per graph version), pgRouting driving distance, string-pulling smoothing, cache.</summary>
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
    /// <summary>The minimum depth for a route: the ship's own value as given, else draft + Routing:DepthSafetyMarginM (DepthBands.MinDepth).</summary>
    public double? MinDepth(double? draftM, double? minDepthM) => DepthBands.MinDepth(draftM, minDepthM, builder.Options.DepthSafetyMarginM);

    /// <summary>Harbour → harbour route; null when either harbour is not connected to the active graph or no path exists.</summary>
    /// <param name="minDepthM">Routes avoid bands whose middle lies below it (null = no depth limit, see <see cref="MinDepth"/>).</param>
    /// <param name="warnDepthM">"checkDepth" warnings where the route crosses a band whose floor lies below it (null = none).</param>
    /// <param name="avoidUnknownBridges">Bridges without a charted clearance are closed unless no other route exists (else passed with a "bridgeUnknown" warning).</param>
    public async Task<RouteResult?> RouteAsync(Guid from, Guid to, CancellationToken ct, double? airDraftM = null, double? minDepthM = null, double? warnDepthM = null, bool avoidUnknownBridges = false)
    {
        var version = await ActiveVersionAsync(ct);
        if (version == 0) return null;
        // the cache holds unrestricted routes only; an air draft may close bridges and a minimum depth shallow areas, so those are computed every time
        // with assumed passages switched off the cached routes (which may use them) are bypassed
        var useCache = airDraftM is null && minDepthM is null && !avoidUnknownBridges && builder.Options.AssumedPassages;
        var cached = useCache ? await db.RouteCaches.AsNoTracking().FirstOrDefaultAsync(r => r.FromHarbourId == from && r.ToHarbourId == to && r.GraphVersion == version, ct) : null;
        var a = await db.Harbours.AsNoTracking().Where(h => h.Id == from).Select(h => h.Location).FirstOrDefaultAsync(ct);
        var b = await db.Harbours.AsNoTracking().Where(h => h.Id == to).Select(h => h.Location).FirstOrDefaultAsync(ct);
        if (a is null || b is null) return null;
        var straight = Math.Round((decimal)(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / Nm), 1);
        if (cached is not null) return new RouteResult(cached.DistanceNm, straight, cached.Geometry, version, "sea-graph", Warnings: [.. (await WarningsAlongAsync([cached.Geometry], ct, warnDepthM))[0], .. await BridgeWarningsAsync(cached.Geometry, version, ct, withKnown: true)]);
        var start = await AccessNodeAsync(from, version, ct);
        var end = await AccessNodeAsync(to, version, ct);
        if (start is null || end is null) return null;
        var graph = await GraphAsync(version);
        var result = await RouteEndsAsync(graph, new SeaGraph.End(a.Coordinate, (int)start.Value), new SeaGraph.End(b.Coordinate, (int)end.Value), version, ct, airDraftM, minDepthM, warnDepthM, avoidUnknownBridges: avoidUnknownBridges);
        if (result is null) return null;
        if (!useCache) return result;
        // the unrestricted route only: a later hit with a warning depth gets its depth warnings along the stored geometry
        db.RouteCaches.Add(new RouteCache { FromHarbourId = from, ToHarbourId = to, GraphVersion = version, DistanceNm = result.DistanceNm, Geometry = result.Geometry });
        db.RouteCaches.Add(new RouteCache { FromHarbourId = to, ToHarbourId = from, GraphVersion = version, DistanceNm = result.DistanceNm, Geometry = (LineString)result.Geometry.Reverse() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); } // concurrent insert of the same pair
        return result;
    }

    /// <summary>Point → point route (map clicks). Each point snaps to a linked harbour next to it, a passage edge or the nearest node (EndAsync).</summary>
    public async Task<RouteResult?> RouteAsync(Point from, Point to, CancellationToken ct, double? airDraftM = null, double? minDepthM = null, double? warnDepthM = null, bool avoidUnknownBridges = false)
    {
        var version = await ActiveVersionAsync(ct);
        if (version == 0) return null;
        var clock = Stopwatch.StartNew();
        var graph = await GraphAsync(version);
        var timings = new Dictionary<string, long> { ["graph"] = clock.ElapsedMilliseconds }; clock.Restart();
        var start = await EndAsync(from, version, graph, ct);
        var end = await EndAsync(to, version, graph, ct);
        timings["snap"] = clock.ElapsedMilliseconds;
        if (start is null || end is null) return null;
        return await RouteEndsAsync(graph, start, end, version, ct, airDraftM, minDepthM, warnDepthM, timings, avoidUnknownBridges);
    }

    /// <summary>A click within this distance of a harbour the builder linked snaps to the harbour's access node.</summary>
    private const double HarbourSnapM = 150;

    /// <summary>
    /// Route end for a map click: (1) a linked harbour within HarbourSnapM – its access node, even when the harbour lies behind
    /// a dike beyond the snap distance (the builder links up to HarbourAccessM, the final leg draws the link); (2) otherwise
    /// the nearest node (SnapAsync), unless a passage edge is closer – a contracted canal stretch has no node inside, so the
    /// end is projected onto it and the edge split there (the leg to the projection must not cross land while a node is at hand).
    /// </summary>
    private async Task<SeaGraph.End?> EndAsync(Point p, int version, SeaGraph graph, CancellationToken ct, bool toWater = true)
    {
        var access = await db.Database.SqlQueryRaw<long>("""
            SELECT a.node_id AS "Value" FROM "Harbours" h JOIN routing_harbour_access a ON a.harbour_id = h."Id" AND a.graph_version = {0}
            WHERE ST_DWithin(h."Location", ST_SetSRID(ST_MakePoint({1}, {2}), 4326)::geography, {3})
            ORDER BY ST_Distance(h."Location", ST_SetSRID(ST_MakePoint({1}, {2}), 4326)::geography) LIMIT 1
            """, version, p.X, p.Y, HarbourSnapM).ToListAsync(ct);
        if (access.Count > 0) return new SeaGraph.End(p.Coordinate, (int)access[0]);
        var (node, metres, maxM, onLand, visible) = await SnapAsync(p, version, ct);
        var onEdge = graph.SnapToPassage(p.Coordinate, node is null ? maxM : metres);
        // on the edge, the node in sight stays a second way in (SeaGraph.Anchors): the search takes the cheaper
        var end = onEdge is not null && (node is null || !await CrossesLandAsync(p.Coordinate, onEdge.Point, version, ct)) ? new SeaGraph.End(p.Coordinate, visible && node is { } vn ? (int)vn : -1, onEdge)
            : node is { } n ? new SeaGraph.End(p.Coordinate, (int)n) : null;
        return onLand && toWater ? await LandEndAsync(p, end, version, graph, ct) : end;
    }

    /// <summary>A click on land keeps the snap above when its leg crosses at most this much land (a harbour basin or canal the coastline counts as land);
    /// the route then starts where that leg leaves the land, with the start/endOnLand warning.</summary>
    private const double ShortLandM = 300;
    /// <summary>How far from a click on land the nearest navigable water is looked for (the route then carries a startOnLand/endOnLand
    /// warning with the distance; beyond it the route endpoint answers "no sea route" – an inland logbook harbour 2.5 km from the
    /// Strelasund got a straight line across land with 2 km).</summary>
    public const double WaterSearchM = 5000;
    public double MaxRouteNm => builder.Options.MaxRouteNm;

    /// <summary>
    /// A map click on land: the snap (node or passage edge) stays when its first leg crosses no more than ShortLandM of land (the route
    /// starting where the leg reaches the water); otherwise
    /// the route ends at the nearest point of navigable water - water with a node of the main component in it, or of a component that
    /// holds a harbour's access node (a lake with a harbour; an isolated pond is skipped) - and the click is kept as End.OnLand, so the
    /// route draws no line across land and the response says how far the click lies from the water. Null when no such water is within WaterSearchM.
    /// </summary>
    private async Task<SeaGraph.End?> LandEndAsync(Point p, SeaGraph.End? snapped, int version, SeaGraph graph, CancellationToken ct)
    {
        var c = p.Coordinate; var cos = Math.Max(0.2, Math.Cos(c.Y * Math.PI / 180));
        var dLat = WaterSearchM / WaterGraphTiler.MPerDegLat; var dLng = dLat / cos;
        var box = new Envelope(c.X - dLng, c.X + dLng, c.Y - dLat, c.Y + dLat);
        var rows = await db.Database.SqlQueryRaw<GeomRow>("SELECT ST_ClipByBox2D(geom, ST_MakeEnvelope({1},{2},{3},{4},4326)) AS \"Geom\" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)",
            version, box.MinX, box.MinY, box.MaxX, box.MaxY).ToListAsync(ct);
        var pieces = rows.Select(r => r.Geom).Where(g => !g.IsEmpty).ToList();
        Geometry land;
        try { land = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(pieces); }
        catch (TopologyException) { land = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(pieces.Select(g => g.Buffer(0)).ToList()); }
        if (land is null || land.IsEmpty) return snapped;
        var landPrepared = PreparedGeometryFactory.Prepare(land);
        bool OnLand(Coordinate q) => landPrepared.Contains(Geo.Factory.CreatePoint(q));
        if (snapped is not null)
        {
            var target = snapped.OnEdge?.Point ?? graph.Node(snapped.Node);
            var legM = RoutingGraphBuilder.Haversine(c.Y, c.X, target.Y, target.X);
            var steps = Math.Max(1, (int)Math.Ceiling(legM / 10));
            var landM = 0.0; var lastLand = 0;
            for (var k = 1; k <= steps; k++) if (OnLand(new Coordinate(c.X + (target.X - c.X) * k / steps, c.Y + (target.Y - c.Y) * k / steps))) { landM += legM / steps; lastLand = k; }
            // the snap stays, but the route starts where its leg leaves the land (the click is kept as End.OnLand: warning and dotted
            // connector) - no drawn leg across land from a click (Ærøskøbing: a click 13 m inside an islet's tip got a silent land leg)
            if (landM <= ShortLandM)
            {
                var t = Math.Min(1.0, (lastLand + 1.0) / steps);
                return snapped with { Point = new Coordinate(c.X + (target.X - c.X) * t, c.Y + (target.Y - c.Y) * t), OnLand = c };
            }
        }
        var water = Geo.Factory.ToGeometry(box).Difference(land);
        var nodes = await db.Database.SqlQueryRaw<SnapRow>("""
            SELECT id AS "Id", 0::float8 AS "Metres", ST_X(geom) AS "X", ST_Y(geom) AS "Y", false AS "Isolated" FROM routing_node
            WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)
            """, version, box.MinX, box.MinY, box.MaxX, box.MaxY).ToListAsync(ct);
        var nodeIndex = new STRtree<SnapRow>(); foreach (var n in nodes) nodeIndex.Insert(new Envelope(n.X, n.X, n.Y, n.Y), n);
        nodeIndex.Build();
        // nearest points in a metric frame (longitude scaled by cos φ): in plain degrees a point east or west looks 1.7 times farther than it is
        var toM = NetTopologySuite.Geometries.Utilities.AffineTransformation.ScaleInstance(cos, 1);
        var pt = toM.Transform(Geo.Factory.CreatePoint(c));
        var candidates = Enumerable.Range(0, water.NumGeometries).Select(i => water.GetGeometryN(i)).Where(g => g is Polygon && !g.IsEmpty)
            .Select(g => (g, near: NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(toM.Transform(g), pt)[0]))
            .Select(x => (x.g, near: new Coordinate(x.near.X / cos, x.near.Y)))
            .Select(x => (x.g, x.near, m: RoutingGraphBuilder.Haversine(c.Y, c.X, x.near.Y, x.near.X))).OrderBy(x => x.m).ToList();
        var components = new Dictionary<int, bool>(); // node → its isolated component holds a harbour access node
        foreach (var (g, near, m) in candidates)
        {
            if (m > WaterSearchM) break;
            var prepared = PreparedGeometryFactory.Prepare(g);
            var inside = nodeIndex.Query(g.EnvelopeInternal).Where(n => prepared.Contains(Geo.Factory.CreatePoint(new Coordinate(n.X, n.Y)))).Select(n => (int)n.Id).ToList();
            var navigable = inside.Any(id => !graph.IsIsolated(id));
            foreach (var id in inside.Take(5)) // isolated water: navigable when a harbour hooks into its component
            {
                if (navigable) break;
                if (!components.TryGetValue(id, out var ok))
                {
                    var comp = graph.Component(id, 200_000);
                    ok = comp is null || await db.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM routing_harbour_access WHERE graph_version = {0} AND node_id = ANY({1})", version, comp.Select(x => (long)x).ToArray()).AnyAsync(ct);
                    foreach (var x in comp ?? [id]) components[x] = ok;
                }
                navigable = ok;
            }
            if (!navigable) continue;
            // a step past the shore into the water, so the water end does not lie on the land boundary itself
            var dx = (near.X - c.X) * cos; var dy = near.Y - c.Y; var len = Math.Sqrt(dx * dx + dy * dy);
            var step = 2 / WaterGraphTiler.MPerDegLat;
            var w = len > 0 ? new Coordinate(near.X + dx / len * step / cos, near.Y + dy / len * step) : near;
            if (OnLand(w)) w = near;
            var end = await EndAsync(Geo.Point(w.Y, w.X), version, graph, ct, toWater: false);
            if (end is not null) return end with { OnLand = c };
        }
        return null;
    }

    private async Task<bool> CrossesLandAsync(Coordinate a, Coordinate b, int version, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<int>("""
            SELECT 1 AS "Value" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope(LEAST({1}, {3}), LEAST({2}, {4}), GREATEST({1}, {3}), GREATEST({2}, {4}), 4326)
              AND ST_Intersects(geom, ST_SetSRID(ST_MakeLine(ST_MakePoint({1}, {2}), ST_MakePoint({3}, {4})), 4326))
            """, version, a.X, a.Y, b.X, b.Y).AnyAsync(ct);

    // the graph of the active version, loaded once per process on first use (the old version is dropped when a new one is active)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Lazy<Task<SeaGraph>>> _graphs = new();

    private async Task<SeaGraph> GraphAsync(int version)
    {
        foreach (var old in _graphs.Keys.Where(k => k != version).ToList()) _graphs.TryRemove(old, out _);
        var lazy = _graphs.GetOrAdd(version, v => new Lazy<Task<SeaGraph>>(() => LoadGraphAsync((NpgsqlConnection)((ICloneable)db.Database.GetDbConnection()).Clone(), v, logger))); // a clone keeps the password an opened connection string has lost
        try { return await lazy.Value; }
        catch { _graphs.TryRemove(new KeyValuePair<int, Lazy<Task<SeaGraph>>>(version, lazy)); throw; } // the next request retries
    }

    /// <summary>Nodes, edges (cost, depth, clearance, interior points of multi-point edges) and isolated nodes of a graph version.</summary>
    public static async Task<SeaGraph> LoadGraphAsync(NpgsqlConnection connection, int version, ILogger logger)
    {
        var sw = Stopwatch.StartNew(); var before = GC.GetTotalMemory(false);
        await using var conn = connection;
        await conn.OpenAsync();
        async Task<long> Scalar(string sql)
        {
            await using var c = new NpgsqlCommand(sql, conn); c.Parameters.AddWithValue("v", version);
            return Convert.ToInt64(await c.ExecuteScalarAsync() ?? 0L);
        }
        var maxNode = (int)await Scalar("SELECT COALESCE(MAX(id), 0) FROM routing_node WHERE graph_version = @v");
        var edgeCount = (int)await Scalar("SELECT COUNT(*) FROM routing_edge WHERE graph_version = @v");
        var gridNodes = await Scalar("SELECT COALESCE(MAX(value::bigint), 0) FROM routing_meta WHERE key = 'grid_nodes'");
        var x = new double[maxNode + 1]; var y = new double[maxNode + 1];
        Array.Fill(x, double.NaN); Array.Fill(y, double.NaN);
        await using (var c = new NpgsqlCommand("SELECT id, ST_X(geom), ST_Y(geom) FROM routing_node WHERE graph_version = @v", conn) { CommandTimeout = 600 })
        {
            c.Parameters.AddWithValue("v", version);
            await using var r = await c.ExecuteReaderAsync();
            while (await r.ReadAsync()) { var id = (int)r.GetInt64(0); x[id] = r.GetDouble(1); y[id] = r.GetDouble(2); }
        }
        var src = new int[edgeCount]; var tgt = new int[edgeCount]; var cost = new float[edgeCount]; var depth = new float[edgeCount]; var clear = new float[edgeCount]; var assumed = new bool[edgeCount]; var adjacent = new float[edgeCount]; var floor = new float[edgeCount]; var unknownBridge = new bool[edgeCount]; var ferry = new bool[edgeCount];
        var geomStart = new int[edgeCount + 1]; var geom = new List<double>();
        var m = 0;
        await using (var c = new NpgsqlCommand("SELECT source, target, cost, min_depth_m, clearance_m, CASE WHEN ST_NPoints(geom) > 2 THEN ST_AsBinary(geom) END, assumed, adjacent_depth_m, floor_m, unknown_bridge, ferry FROM routing_edge WHERE graph_version = @v", conn) { CommandTimeout = 600 })
        {
            c.Parameters.AddWithValue("v", version);
            await using var r = await c.ExecuteReaderAsync();
            while (await r.ReadAsync() && m < edgeCount)
            {
                src[m] = (int)r.GetInt64(0); tgt[m] = (int)r.GetInt64(1); cost[m] = (float)r.GetDouble(2);
                depth[m] = r.IsDBNull(3) ? float.NaN : (float)r.GetDouble(3);
                clear[m] = r.IsDBNull(4) ? float.NaN : (float)r.GetDouble(4);
                geomStart[m] = geom.Count / 2;
                if (!r.IsDBNull(5)) AppendInterior(r.GetFieldValue<byte[]>(5), geom);
                assumed[m] = r.GetBoolean(6);
                adjacent[m] = r.IsDBNull(7) ? float.NaN : (float)r.GetDouble(7);
                floor[m] = r.IsDBNull(8) ? float.NaN : (float)r.GetDouble(8);
                unknownBridge[m] = r.GetBoolean(9);
                ferry[m] = r.GetBoolean(10);
                m++;
            }
        }
        geomStart[m] = geom.Count / 2;
        if (m < edgeCount) { Array.Resize(ref src, m); Array.Resize(ref tgt, m); Array.Resize(ref cost, m); Array.Resize(ref depth, m); Array.Resize(ref clear, m); Array.Resize(ref assumed, m); Array.Resize(ref adjacent, m); Array.Resize(ref floor, m); Array.Resize(ref unknownBridge, m); Array.Resize(ref ferry, m); Array.Resize(ref geomStart, m + 1); }
        var isolated = new bool[maxNode + 1];
        await using (var c = new NpgsqlCommand("SELECT id FROM routing_isolated WHERE graph_version = @v", conn))
        {
            c.Parameters.AddWithValue("v", version);
            await using var r = await c.ExecuteReaderAsync();
            while (await r.ReadAsync()) { var id = r.GetInt64(0); if (id <= maxNode) isolated[id] = true; }
        }
        var loaded = sw.ElapsedMilliseconds;
        var graph = new SeaGraph(x, y, src, tgt, cost, depth, clear, geomStart, geom.ToArray(), (int)gridNodes + 1, isolated, assumed: assumed, adjacent: adjacent, floor: floor, unknownBridge: unknownBridge, ferry: ferry);
        logger.LogInformation("Routing graph v{Version} in memory: {Nodes} node slots, {Edges} edges, {Snap} passage edges for snapping, heuristic x{Factor:F6}, ~{Mb} MB (GC +{GcMb} MB); read {Read} ms, indexed {Index} ms",
            version, graph.NodeSlots, graph.EdgeCount, graph.SnapEdgeCount, graph.HeuristicFactor, graph.ApproxBytes >> 20, (GC.GetTotalMemory(false) - before) >> 20, loaded, sw.ElapsedMilliseconds - loaded);
        return graph;
    }

    /// <summary>Interior points of a WKB linestring (2D) appended as x,y pairs.</summary>
    private static void AppendInterior(byte[] wkb, List<double> into)
    {
        var le = wkb[0] == 1;
        double D(int at) => le ? System.Buffers.Binary.BinaryPrimitives.ReadDoubleLittleEndian(wkb.AsSpan(at)) : System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(wkb.AsSpan(at));
        var n = le ? System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(wkb.AsSpan(5)) : System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(wkb.AsSpan(5));
        for (var k = 1; k < n - 1; k++) { into.Add(D(9 + 16 * k)); into.Add(D(9 + 16 * k + 8)); }
    }

    public async Task<List<NearbyResult>?> NearbyAsync(Guid? harbourId, Point? origin, double maxNm, int limit, CancellationToken ct)
    {
        using var span = Activity.StartActivity("routing.nearby");
        var version = await ActiveVersionAsync(ct);
        if (version == 0) return null;
        long? start = harbourId is { } h ? await AccessNodeAsync(h, version, ct) : origin is not null ? (await SnapAsync(origin, version, ct)).Id : null;
        if (start is null) return null;
        var startPoint = origin ?? await db.Harbours.AsNoTracking().Where(x => x.Id == harbourId).Select(x => x.Location).FirstAsync(ct);
        var radiusM = maxNm * Nm;
        var env = Expand(startPoint, startPoint, radiusM * 1.15);
        var rows = await db.Database.SqlQueryRaw<NearbyRow>("""
            SELECT a.harbour_id AS "HarbourId", (d.agg_cost + a.distance_m) AS "Metres"
            FROM pgr_drivingDistance(
                'SELECT id, source, target, cost, reverse_cost FROM routing_edge WHERE graph_version = ' || {0} || ' AND geom && ST_MakeEnvelope(' || {1} || ',' || {2} || ',' || {3} || ',' || {4} || ', 4326)' || {7},
                {5}::bigint, {6}::double precision, directed := false) d
            JOIN routing_harbour_access a ON a.node_id = d.node AND a.graph_version = {0}
            """, version, env.MinX, env.MinY, env.MaxX, env.MaxY, start.Value, radiusM, builder.Options.AssumedPassages ? "" : " AND NOT assumed").ToListAsync(ct);
        var ids = rows.Select(r => r.HarbourId).Where(id => id != harbourId).ToList();
        var locations = await db.Harbours.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Location }).ToDictionaryAsync(x => x.Id, x => x.Location, ct);
        return rows.Where(r => r.HarbourId != harbourId && locations.ContainsKey(r.HarbourId))
            .Select(r => new NearbyResult(r.HarbourId, Math.Round((decimal)(r.Metres / Nm), 1), Math.Round((decimal)(RoutingGraphBuilder.Haversine(startPoint.Y, startPoint.X, locations[r.HarbourId].Y, locations[r.HarbourId].X) / Nm), 1)))
            .OrderBy(r => r.DistanceNm).Take(limit).ToList();
    }

    private static (int count, (double, double)[] bands)? _depthBands;
    /// <summary>The lowest charted depth band floor a ship with this minimum depth may cross (see <see cref="DepthBands"/>).</summary>
    private async Task<double> DepthFloorAsync(double minDepthM, CancellationToken ct)
    {
        if (_depthBands is null)
        {
            var rows = await db.Database.SqlQueryRaw<BandRow>("SELECT DISTINCT min_depth_m AS \"Min\", max_depth_m AS \"Max\" FROM routing_depth").ToListAsync(ct);
            _depthBands = (rows.Count, rows.Select(r => (r.Min, r.Max)).ToArray());
        }
        return DepthBands.EffectiveFloor(minDepthM, _depthBands.Value.bands);
    }
    private sealed class BandRow { public double Min { get; set; } public double Max { get; set; } }

    private async Task<RouteResult?> RouteEndsAsync(SeaGraph graph, SeaGraph.End from, SeaGraph.End to, int version, CancellationToken ct, double? airDraftM = null, double? minDepthM = null, double? warnDepthM = null, Dictionary<string, long>? timings = null, bool avoidUnknownBridges = false)
    {
        // bridges lower than the ship's air draft close their passage edges (clearance_m is null elsewhere); charted depth bands whose
        // middle lies below the minimum depth close grid edges (min_depth_m is null on marked passages: a buoyed channel is followed anyway),
        // and so does coastal water without depth data next to such a band (adjacent_depth_m, DepthBands.Effective)
        // the edges at the two end nodes are exempt from the depth limit: a harbour basin is charted shallow at this resolution and
        // one has to get in anyway; from there on only deep legs or the buoyed channel (passage edges carry no limit) continue
        // … and so is the harbour approach (HarbourAccessM around both ends): the charted fathom steps call a dredged fjord or marina
        // basin shallow, and one has to get in and out anyway (Nyborg Fjord: 1.8 m charted, 7 m dredged channel)
        // the shallow approach legs that remain cost ×3: the buoyed channel (no depth limit, cost ×1) wins where one exists, the
        // bare crossing of a charted flat is taken only when nothing else leads in
        using var span = Activity.StartActivity("routing.route");
        span?.SetTag("routing.graph_version", version);
        var a = from.Point; var b = to.Point;
        var straightM = RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X);
        if (straightM > builder.Options.MaxRouteNm * Nm) return null;
        timings ??= []; var clock = Stopwatch.StartNew();
        var o = builder.Options;
        // no air draft given: a typical yacht's mast (Routing:DefaultAirDraftM) - fixed bridges charted lower are closed unless no other route exists
        var air = airDraftM ?? (o.DefaultAirDraftM > 0 ? o.DefaultAirDraftM : null);
        // "Tonnen first", strict: water whose band floor lies below the minimum depth (not a buoyed channel) is closed outside the harbour
        // approach; only when no route exists that way the search runs again with it at x UncertainDepthFactor, and the route warns there
        // (Oknö: the route came up through the unbuoyed 1.83-3.66 m skerries to the Öknöström bridge)
        var strict = minDepthM is not null && o.UncertainDepthFactor != 1;
        var limits = new SeaGraph.Limits(air, minDepthM is { } dm ? await DepthFloorAsync(dm, ct) : null, o.HarbourAccessM / WaterGraphTiler.MPerDegLat, NoAssumed: !o.AssumedPassages,
            MinDepthM: minDepthM, UncertainFactor: o.UncertainDepthFactor, StrictDepth: strict, AvoidUnknownBridges: avoidUnknownBridges);
        // the builder lets a passage end's link into the water graph cross up to 50 m of land (a harbour point on its pier): from a marina
        // channel that is a link straight across the mole (Großenbrode). Links on the path that cross land with water on both sides
        // cost Limits.PenaltyM (20 km) extra and the search runs again: they stay usable where nothing else leads on, as before
        var penalized = new HashSet<int>(); var checkedLinks = new HashSet<int>(); var rounds = 0;
        async Task<SeaGraph.Path?> SearchAsync()
        {
            while (true)
            {
                var p = graph.Route(from, to, limits with { Penalized = penalized });
                if (p?.Edges is null || rounds++ == 8) return p;
                // … and so do straight edges between passage vertices across an island (gap links between channel pieces: Marstrand, Koön)
                var bad = await LandLinksAsync(graph, p.Edges.Where(e => (graph.IsLink(e) || graph.IsStraightPassage(e)) && checkedLinks.Add(e)).ToList(), version, ct);
                if (bad.Count == 0) return p;
                // the other links at both ends of a bad one go with it: a water node beyond a mole collects the links of a whole marina
                var near = bad.SelectMany(e => graph.EdgesAt(graph.Source(e)).Concat(graph.EdgesAt(graph.Target(e)))).Where(e => graph.IsLink(e) && checkedLinks.Add(e)).ToList();
                bad.AddRange(await LandLinksAsync(graph, near, version, ct));
                penalized.UnionWith(bad);
            }
        }
        // the strict search, and the cost rule only when it finds nothing
        async Task<SeaGraph.Path?> DepthFallbackAsync()
        {
            limits = limits with { StrictDepth = strict }; rounds = 0;
            var p = await SearchAsync();
            if (p is not null || !strict) return p;
            limits = limits with { StrictDepth = false }; rounds = 0;
            timings["depth-fallback"] = 1;
            return await SearchAsync();
        }
        var path = await DepthFallbackAsync();
        if (path is null && (avoidUnknownBridges || (airDraftM is null && air is not null)))
        {
            // only under a bridge lower than the assumed mast, or under one of unknown clearance the ship avoids: that route, with the
            // bridge named in a warning (a bridge lower than the ship's own air draft stays closed); strict again first - it was the bridge
            // that closed the way, not necessarily the depth
            limits = limits with { AirDraftM = airDraftM, AvoidUnknownBridges = false }; air = airDraftM;
            timings.Remove("depth-fallback"); timings["bridge-fallback"] = 1;
            path = await DepthFallbackAsync();
        }
        timings["astar"] = clock.ElapsedMilliseconds; timings["astar-rounds"] = rounds; timings["penalized-links"] = penalized.Count; timings["cost"] = (long)(path?.Cost ?? -1); clock.Restart();
        if (path is null) { logger.LogInformation("No sea route between {From} and {To}", from, to); return null; }
        span?.SetTag("routing.settled", path.Settled);
        Stage("astar", path.Points);
        // the points of assumed edges: their legs cross the coastline land by design and are not rerouted round it
        var assumedPoints = new HashSet<Coordinate>();
        foreach (var e in path.Edges ?? []) if (graph.IsAssumed(e)) assumedPoints.UnionWith(graph.Coords(e));
        // the pull keeps what the search kept: no shortcut through uncertain water after a strict search, none under an avoided bridge
        var avoidedBridges = limits.AvoidUnknownBridges && !(path.Edges ?? []).Any(graph.UnknownBridge);
        var smoothed = await SmoothAsync(path.Points, path.Fixed, version, ct, air, minDepthM, timings, assumedPoints, limits.StrictDepth, avoidedBridges);
        timings["smooth"] = clock.ElapsedMilliseconds;
        var metres = 0.0;
        for (var i = 1; i < smoothed.Count; i++) metres += RoutingGraphBuilder.Haversine(smoothed[i - 1].Y, smoothed[i - 1].X, smoothed[i].Y, smoothed[i].X);
        var line = Geo.Factory.CreateLineString(smoothed.ToArray());
        span?.SetTag("routing.distance_nm", metres / Nm);
        var warnings = await WarningsAtAsync(path.Assumed ?? [], ct);
        if (from.OnLand is { } fl) warnings.Insert(0, LandWarning("startOnLand", fl, a, "Start liegt an Land – Route beginnt am nächsten Wasser"));
        if (to.OnLand is { } tl) warnings.Add(LandWarning("endOnLand", tl, b, "Ziel liegt an Land – Route endet am nächsten Wasser"));
        if (strict && !limits.StrictDepth)
        {
            var pieces = graph.UncertainEdges(path.Edges ?? [], limits, from, to)
                .Select(e => { var l = Geo.Factory.CreateLineString(graph.Coords(e)); return new DepthBands.Piece(l.Centroid.Coordinate, graph.Floor(e), double.NaN, LengthM(l)); }).ToList();
            warnings.AddRange(DepthBands.Stretches(line, pieces).Select(p => new RouteWarning("uncertainDepth", Math.Round(p.At.Y, 5), Math.Round(p.At.X, 5), UncertainDepthNote, DistanceM: Math.Round(p.LengthM))));
        }
        warnings.AddRange(await BridgeWarningsAsync(line, version, ct, withKnown: airDraftM is null, (path.Edges ?? []).Where(graph.UnknownBridge).Select(e => Geo.Factory.CreateLineString(graph.Coords(e))).ToList()));
        if (warnDepthM is { } wd)
        {
            // graph edges without charted depth that count as the band next to them (the smoothed line runs along them)
            var inferred = (path.Edges ?? []).Distinct().Where(e => graph.DepthInferred(e) && DepthBands.Warns(graph.Depth(e), wd))
                .Select(e => { var l = Geo.Factory.CreateLineString(graph.Coords(e)); return new DepthBands.Piece(l.Centroid.Coordinate, graph.Depth(e), double.NaN, LengthM(l), true); }).ToList();
            clock.Restart();
            warnings.AddRange(await DepthWarningsAsync(line, wd, inferred, ct));
            timings["depth-warnings"] = clock.ElapsedMilliseconds;
        }
        return new RouteResult(Math.Round((decimal)(metres / Nm), 1), Math.Round((decimal)(straightM / Nm), 1), line, version, "sea-graph", timings, warnings);
    }

    /// <summary>The link edges among these that cross land with water on both sides (SeaGraph.CrossesInteriorLand), against the land clipped
    /// around each; a straight passage edge only with more than StraightPassageLandM of such land (a canal line a few metres off its water polygon stays).</summary>
    private async Task<List<int>> LandLinksAsync(SeaGraph graph, List<int> links, int version, CancellationToken ct)
    {
        var bad = new List<int>();
        // links close together (in one 0.1° cell) share one land query; links far apart (both ends of a long route) get their own
        var groups = links.GroupBy(e => { var c = graph.Coords(e)[0]; return (Math.Floor(c.X * 10), Math.Floor(c.Y * 10)); }).Select(g => g.ToList()).ToList();
        foreach (var group in groups)
        {
            var env = new Envelope(); foreach (var e in group) { var cs = graph.Coords(e); env.ExpandToInclude(cs[0]); env.ExpandToInclude(cs[^1]); }
            env.ExpandBy(0.0005);
            var land = await db.Database.SqlQueryRaw<GeomRow>("SELECT ST_ClipByBox2D(geom, ST_MakeEnvelope({1},{2},{3},{4},4326)) AS \"Geom\" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)",
                version, env.MinX, env.MinY, env.MaxX, env.MaxY).ToListAsync(ct);
            var index = new STRtree<IPreparedGeometry>();
            foreach (var l in land) if (!l.Geom.IsEmpty) index.Insert(l.Geom.EnvelopeInternal, PreparedGeometryFactory.Prepare(l.Geom));
            if (index.Count == 0) continue;
            bool OnLand(Coordinate c) { var pt = Geo.Factory.CreatePoint(c); return index.Query(new Envelope(c)).Any(g => g.Contains(pt)); }
            foreach (var e in group)
            {
                var cs = graph.Coords(e);
                if (graph.IsLink(e) ? SeaGraph.CrossesInteriorLand(cs[0], cs[^1], OnLand) : SeaGraph.InteriorLandM(cs[0], cs[^1], OnLand, 5) > StraightPassageLandM) bad.Add(e);
            }
        }
        return bad;
    }

    /// <summary>A point bridge (a charted span) this close to the route counts as passed under.</summary>
    private const double BridgeWarnM = 300;

    public const string UncertainDepthNote = "Route führt durch Wasser, das möglicherweise flacher ist als die Mindesttiefe";
    public const string UnknownBridgeNote = "Brücke mit unbekannter Durchfahrtshöhe – vor Ort prüfen";

    /// <summary>
    /// Bridge warnings, one per bridge, in route order: "bridgeUnknown" for every fixed bridge without a charted clearance the route passes
    /// under (a bridge line it crosses, a bridge point within BridgeWarnM); with <paramref name="withKnown"/> (a route asked for without an air
    /// draft) also "bridge" for every fixed bridge with a known clearance - the mast height is the skipper's to check; DistanceM carries the clearance.
    /// </summary>
    /// <param name="unknownEdges">The path's edges marked UnknownBridge: an unknown bridge is named only where one of them passes under it, so the
    /// warnings name what "avoid" avoids (a stored route without edges: by geometry alone).</param>
    private async Task<List<RouteWarning>> BridgeWarningsAsync(LineString route, int version, CancellationToken ct, bool withKnown, IReadOnlyList<LineString>? unknownEdges = null)
    {
        var defaultAir = builder.Options.DefaultAirDraftM;
        withKnown &= defaultAir > 0;
        var statics = await StaticsAsync(version, ct);
        var near = route.EnvelopeInternal.Copy(); near.ExpandBy(2 * BridgeWarnM / WaterGraphTiler.MPerDegLat);
        var line = new NetTopologySuite.LinearReferencing.LengthIndexedLine(route);
        var hits = new List<(double at, Coordinate where, double? clearance)>();
        foreach (var (g, clearance, kind) in statics.Bridges.Query(near))
        {
            if (kind != CorrectionKind.Bridge || clearance is <= 0) continue;
            Coordinate? where = null;
            if (g is Point pt)
            {
                var q = line.ExtractPoint(line.Project(pt.Coordinate));
                if (RoutingGraphBuilder.Haversine(q.Y, q.X, pt.Y, pt.X) <= BridgeWarnM) where = pt.Coordinate;
            }
            else if (g.Intersects(route)) where = g.Intersection(route).Coordinate;
            if (where is null) continue;
            if (clearance is null && unknownEdges is not null && !unknownEdges.Any(e => g is Point ? e.IsWithinDistance(g, 2 * Obstacles.MatchM / WaterGraphTiler.MPerDegLat) : e.Intersects(g))) continue;
            hits.Add((line.Project(where), where, clearance));
        }
        // a bridge line and its span point (or the pieces of one bridge) name one bridge: one warning per 500 m of route. A bridge without a
        // clearance within BridgeMatchAreaM of a known one on the route is the same crossing drawn twice (Rügendamm, Fehmarnsund: the OSM
        // road lines next to the charted point) - as for the edges (Obstacles.Apply), the known clearance counts
        var known = hits.Where(h => h.clearance is not null).ToList();
        var result = new List<RouteWarning>(); var lastAt = double.NegativeInfinity;
        foreach (var h in hits.Where(h => h.clearance is not null || !known.Any(k => RoutingGraphBuilder.Haversine(k.where.Y, k.where.X, h.where.Y, h.where.X) <= builder.Options.BridgeMatchAreaM)).OrderBy(h => h.at))
        {
            if ((h.at - lastAt) * WaterGraphTiler.MPerDegLat < 500) continue; // ponytail: planar degrees along the line, fine for "the same bridge"
            lastAt = h.at;
            if (h.clearance is not { } cl) { result.Add(new RouteWarning("bridgeUnknown", Math.Round(h.where.Y, 5), Math.Round(h.where.X, 5), UnknownBridgeNote)); continue; }
            if (!withKnown) continue;
            var low = cl < defaultAir ? string.Format(De, ", niedriger als die angenommene Masthöhe von {0:0} m: die Route führt nur hier durch", defaultAir) : "";
            result.Add(new RouteWarning("bridge", Math.Round(h.where.Y, 5), Math.Round(h.where.X, 5), string.Format(De, "Brücke mit {0:0.0} m Durchfahrtshöhe{1} – Durchfahrtshöhe des Schiffs prüfen (nicht angegeben)", cl, low), DistanceM: cl));
        }
        return result;
    }

    /// <summary>Warnings for the assumed edges a path used (their midpoints), each with the note of the nearest "Annahme" correction.</summary>
    private async Task<List<RouteWarning>> WarningsAtAsync(IReadOnlyList<Coordinate> points, CancellationToken ct)
    {
        var result = new List<RouteWarning>();
        foreach (var c in points)
        {
            var pt = Geo.Factory.CreatePoint(c); pt.SRID = 4326;
            var note = await db.RoutingCorrections.AsNoTracking().Where(x => x.Kind == CorrectionKind.Passage && x.Note != null && x.Note.StartsWith(AssumedPassages.NotePrefix) && x.Geometry.IsWithinDistance(pt, 200 / WaterGraphTiler.MPerDegLat))
                .OrderBy(x => x.Geometry.Distance(pt)).Select(x => x.Note).FirstOrDefaultAsync(ct);
            result.Add(Warning(c, note));
        }
        return Dedupe(result);
    }

    /// <summary>One warning per assumed passage: a passage is cut into several edges (and pieces "(x sm)"), and a Stockholm route
    /// listed the same ferry line 9–16 times.</summary>
    internal static List<RouteWarning> Dedupe(IEnumerable<RouteWarning> warnings) =>
        warnings.GroupBy(w => w.Kind == "assumedPassage" ? System.Text.RegularExpressions.Regex.Replace(w.Note, @"\s*\(\d+[,.]?\d* sm\)", "") : w.Kind + w.Lat + w.Lng)
            .Select(g => g.First() with { Note = System.Text.RegularExpressions.Regex.Replace(g.First().Note, @"\s*\(\d+[,.]?\d* sm\)", "") }).ToList();

    private static RouteWarning LandWarning(string kind, Coordinate land, Coordinate water, string text)
    {
        var m = Math.Round(RoutingGraphBuilder.Haversine(land.Y, land.X, water.Y, water.X));
        return new RouteWarning(kind, Math.Round(land.Y, 6), Math.Round(land.X, 6), $"{text} ({m:0} m)", Math.Round(water.Y, 6), Math.Round(water.X, 6), m);
    }

    private static RouteWarning Warning(Coordinate at, string? note) =>
        new("assumedPassage", Math.Round(at.Y, 5), Math.Round(at.X, 5), $"Durchfahrt angenommen ({(note is null ? "Fährlinie oder Tonnenlücke" : note[AssumedPassages.NotePrefix.Length..].TrimStart(':', ' '))}) – nicht in den Kartendaten, vor Ort prüfen");

    /// <summary>
    /// Warnings for stored routes (route cache, planned legs) that carry no graph flags: every active "Annahme" passage a route runs
    /// along (<see cref="AssumedPassages.Follows"/>). One list per route; empty lists while Routing:AssumedPassages is off.
    /// </summary>
    /// <param name="warnDepthM">Also "checkDepth" warnings where a route crosses charted bands with a floor below it (water without data is not judged here: no graph edges).</param>
    public async Task<List<RouteWarning>[]> WarningsAlongAsync(IReadOnlyList<LineString?> routes, CancellationToken ct, double? warnDepthM = null)
    {
        var result = routes.Select(_ => new List<RouteWarning>()).ToArray();
        if (warnDepthM is { } wd)
            for (var i = 0; i < routes.Count; i++)
                if (routes[i] is { } r) result[i].AddRange(await DepthWarningsAsync(r, wd, [], ct));
        if (!builder.Options.AssumedPassages || routes.All(r => r is null)) return result;
        var env = new Envelope(); foreach (var r in routes) if (r is not null) env.ExpandToInclude(r.EnvelopeInternal);
        var box = Geo.Factory.ToGeometry(env); box.SRID = 4326;
        var passages = await db.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == CorrectionKind.Passage && c.Note != null && c.Note.StartsWith(AssumedPassages.NotePrefix) && c.Geometry.Intersects(box))
            .Select(c => new { c.Geometry, c.Note }).ToListAsync(ct);
        for (var i = 0; i < routes.Count; i++)
            if (routes[i] is { } route)
                foreach (var p in passages)
                    if (AssumedPassages.Follows(route, p.Geometry.Coordinates)) result[i].Add(Warning(AssumedPassages.Midpoint(p.Geometry.Coordinates), p.Note));
        for (var i = 0; i < result.Length; i++) result[i] = Dedupe(result[i]);
        return result;
    }

    /// <summary>"checkDepth" warnings for a route: its pieces inside depth areas with a floor below the warning depth (PostGIS, per route
    /// segment so the index sees small boxes) plus the given inferred pieces, merged into stretches (<see cref="DepthBands.Stretches"/>).</summary>
    private async Task<List<RouteWarning>> DepthWarningsAsync(LineString route, double warnDepthM, IReadOnlyList<DepthBands.Piece> inferred, CancellationToken ct)
    {
        var g = (LineString)route.Copy(); g.SRID = 4326;
        var rows = await db.Database.SqlQueryRaw<DepthPieceRow>("""
            WITH r AS (SELECT {0}::geometry AS g),
                 s AS (SELECT ST_MakeLine(ST_PointN(r.g, i), ST_PointN(r.g, i + 1)) AS seg FROM r, generate_series(1, ST_NPoints(r.g) - 1) i)
            SELECT ST_X(p) AS "X", ST_Y(p) AS "Y", d.min_depth_m AS "Floor", d.max_depth_m AS "Max", ST_Length(x.geom::geography) AS "M"
            FROM s JOIN routing_depth d ON d.geom && s.seg AND d.min_depth_m < {1} AND ST_Intersects(d.geom, s.seg),
                 LATERAL ST_Dump(ST_CollectionExtract(ST_Intersection(d.geom, s.seg), 2)) x, LATERAL ST_LineInterpolatePoint(x.geom, 0.5) p
            """, g, warnDepthM).ToListAsync(ct);
        var pieces = rows.Select(r => new DepthBands.Piece(new Coordinate(r.X, r.Y), r.Floor, r.Max, r.M)).Concat(inferred).ToList();
        return DepthBands.Stretches(route, pieces).Select(p => new RouteWarning("checkDepth", Math.Round(p.At.Y, 5), Math.Round(p.At.X, 5),
            $"Tiefe prüfen – möglicherweise zu flach ({(p.Inferred ? "keine Tiefendaten, daneben " : "Karte ")}{Band(p.FloorM, p.MaxM)})", DistanceM: Math.Round(p.LengthM))).ToList();
    }

    private static readonly System.Globalization.CultureInfo De = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
    private static string Band(double floor, double max) => floor < 0 ? "trockenfallend" : double.IsNaN(max) || max <= floor ? string.Format(De, "ab {0:0.0} m", floor) : string.Format(De, "{0:0.0}–{1:0.0} m", floor, max);

    private static double LengthM(LineString l)
    {
        var m = 0.0; var cs = l.Coordinates;
        for (var i = 1; i < cs.Length; i++) m += RoutingGraphBuilder.Haversine(cs[i - 1].Y, cs[i - 1].X, cs[i].Y, cs[i].X);
        return m;
    }
    private sealed class DepthPieceRow { public double X { get; set; } public double Y { get; set; } public double Floor { get; set; } public double Max { get; set; } public double M { get; set; } }

    /// <summary>
    /// Length (metres, unsmoothed) of the graph route between two points with assumed passages closed, null when there is none: the
    /// derive endpoint asks whether the graph already connects two buoys or the ends of a guide-line stretch.
    /// </summary>
    public async Task<double?> GraphDistanceAsync(Coordinate a, Coordinate b, CancellationToken ct)
    {
        var version = await ActiveVersionAsync(ct);
        if (version == 0) return null;
        var graph = await GraphAsync(version);
        var from = await EndAsync(Geo.Point(a.Y, a.X), version, graph, ct);
        var to = await EndAsync(Geo.Point(b.Y, b.X), version, graph, ct);
        if (from is null || to is null) return null;
        var path = graph.Route(from, to, new SeaGraph.Limits(NoAssumed: true));
        return path is null ? null : StringPull.Length(path.Points);
    }

    /// <summary>
    /// Smoothing (SmoothAsync): string pulling over the path's own points (StringPull) with land, low bridges, shallow areas and
    /// the uncharted channel zone as obstacles. Offshore grid edges are up to 20 km long, so the free legs are densified first
    /// (every ≤ 1 km) – otherwise the pulled string could only bend at grid nodes and kept a dog-leg when the direct line to the
    /// next node grazed the coast. Passage geometry outside charted deep water keeps its shape.
    /// </summary>
    private sealed record SmoothStatics(STRtree<(Geometry geom, double? clearance, CorrectionKind kind)> Bridges, STRtree<Geometry> Zone);
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
        var bridges = new STRtree<(Geometry, double?, CorrectionKind)>();
        var raw = (await sdb.RoutingCorrections.AsNoTracking().Where(c => c.Active && (c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.OpeningBridge)).Select(c => new { c.Kind, c.Geometry, c.ClearanceM }).ToListAsync())
            .Select(c => (c.Geometry is LineString ? c.Geometry : c.Geometry.Centroid, c.Kind, c.ClearanceM)).ToList();
        foreach (var (g, kind, cl) in Obstacles.WithSpans(raw)) bridges.Insert(g.EnvelopeInternal, (g, cl, kind));
        bridges.Build();
        var zone = new STRtree<Geometry>();
        var o = builder.Options;
        if (o.ChannelPreferenceM > 0)
        {
            var fairways = (await sdb.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == CorrectionKind.Passage && c.Source == CorrectionSource.Derived && c.Note != null && c.Note.StartsWith("Fahrwasser") && c.Note.Contains(Fairways.PairedNote) && !c.Note.Contains(Fairways.WideNote)).Select(c => c.Geometry).ToListAsync()).OfType<LineString>().ToList(); // same set as the builder's channel zone
            var fairwayAreas = (await sdb.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == CorrectionKind.Fairway && !(c.Note != null && c.Note.Contains(Fairways.WideNote))).Select(c => c.Geometry).ToListAsync()).SelectMany(g => g is Polygon p ? [p] : g is MultiPolygon mp ? mp.Geometries.OfType<Polygon>() : []).ToList();
            if (fairways.Count > 0)
            {
                var corridorDeg = 150 / WaterGraphTiler.MPerDegLat;
                var corridor = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(fairways.Select(f => f.Buffer(corridorDeg)).ToList());
                var corridorPrepared = PreparedGeometryFactory.Prepare(corridor);
                foreach (var z in ChannelPreference.Zone(fairways, fairwayAreas, o.ChannelPreferenceM))
                {
                    var g = corridorPrepared.Intersects(z) ? z.Difference(corridor) : z;
                    if (!g.IsEmpty) zone.Insert(g.EnvelopeInternal, g);
                }
            }
        }
        zone.Build();
        return new SmoothStatics(bridges, zone);
    }

    /// <summary>Pier corrections, indexed once per process; the pier import resets it.</summary>
    private static volatile Lazy<Task<STRtree<Geometry>>>? _piers;
    public static void InvalidatePiers() => _piers = null;
    private Task<STRtree<Geometry>> PiersAsync()
    {
        var piers = _piers;
        if (piers is null) _piers = piers = new Lazy<Task<STRtree<Geometry>>>(LoadPiersAsync); // a racing second load is harmless
        return piers.Value;
    }

    private async Task<STRtree<Geometry>> LoadPiersAsync()
    {
        var tree = new STRtree<Geometry>();
        foreach (var g in await db.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == CorrectionKind.Pier).Select(c => c.Geometry).ToListAsync()) tree.Insert(g.EnvelopeInternal, g);
        tree.Build();
        return tree;
    }

    /// <param name="strictDepth">The search closed uncertain water (band floor below minDepthM) outside the harbour approach: the pull does too.</param>
    /// <param name="avoidUnknownBridges">The search kept clear of bridges without a charted clearance: their lines block the pull.</param>
    private async Task<List<Coordinate>> SmoothAsync(List<Coordinate> path, List<bool> fixedSeg, int version, CancellationToken ct, double? airDraftM = null, double? minDepthM = null, Dictionary<string, long>? timings = null, IReadOnlySet<Coordinate>? assumedPoints = null, bool strictDepth = false, bool avoidUnknownBridges = false)
    {
        var clock = Stopwatch.StartNew();
        void Lap(string k) { if (timings is not null) timings[k] = clock.ElapsedMilliseconds; clock.Restart(); }
        (path, fixedSeg) = StringPull.Densify(path, fixedSeg, 1000); // passage geometry stays as it is
        var env = new Envelope(); foreach (var c in path) env.ExpandToInclude(c); env.ExpandBy(0.05);
        var land = await db.Database.SqlQueryRaw<GeomRow>("SELECT geom AS \"Geom\" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)", version, env.MinX, env.MinY, env.MaxX, env.MaxY).ToListAsync(ct);
        var index = new STRtree<IPreparedGeometry>();
        var landOnly = new STRtree<IPreparedGeometry>();
        foreach (var g in land) { var pg = PreparedGeometryFactory.Prepare(g.Geom); index.Insert(g.Geom.EnvelopeInternal, pg); landOnly.Insert(g.Geom.EnvelopeInternal, pg); }
        landOnly.Build();
        Lap("smooth-land");
        // what A* avoided must stay avoided when the string is pulled: too-low bridges (a circle around the point, a band along a
        // line), the impassable parts of bridges with charted spans, and too-shallow areas count as land
        var statics = await StaticsAsync(version, ct);
        {
            // bridge pieces (chains, charted spans) are the same for every route of a graph version: computed once, queried by envelope
            var radiusDeg = builder.Options.BridgeMatchAreaM / WaterGraphTiler.MPerDegLat;
            foreach (var (g, clearance, kind) in statics.Bridges.Query(env))
            {
                if (clearance is null && avoidUnknownBridges && kind == CorrectionKind.Bridge)
                {
                    var u = g.Buffer((g is Point ? Obstacles.MatchM : 20) / WaterGraphTiler.MPerDegLat);
                    if (u.EnvelopeInternal.Intersects(env)) index.Insert(u.EnvelopeInternal, PreparedGeometryFactory.Prepare(u));
                }
                if (clearance is not { } cl || (cl > 0 && (airDraftM is not { } ad || cl >= ad))) continue;
                var b = g is Point ? g.Buffer(radiusDeg) : g.Buffer(300 / WaterGraphTiler.MPerDegLat);
                if (b.EnvelopeInternal.Intersects(env)) index.Insert(b.EnvelopeInternal, PreparedGeometryFactory.Prepare(b));
            }
        }
        Lap("smooth-bridges");
        var limit = minDepthM is { } dm ? await DepthFloorAsync(dm, ct) : double.MinValue;
        if (minDepthM is not null)
        {
            var shallow = await db.Database.SqlQueryRaw<GeomRow>("SELECT geom AS \"Geom\" FROM routing_depth WHERE min_depth_m < {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)", limit, env.MinX, env.MinY, env.MaxX, env.MaxY).ToListAsync(ct);
            foreach (var g in shallow) index.Insert(g.Geom.EnvelopeInternal, PreparedGeometryFactory.Prepare(g.Geom));
        }
        index.Build();
        // after a strict search: water whose band floor lies below the minimum depth blocks the pull outside the harbour approach (the
        // search closed it there), so no shortcut leaves the buoyed channel or the definitely deep water across it
        var uncertain = new STRtree<IPreparedGeometry>();
        if (strictDepth && minDepthM is { } md && md > limit)
        {
            var rows = await db.Database.SqlQueryRaw<GeomRow>("SELECT geom AS \"Geom\" FROM routing_depth WHERE min_depth_m < {0} AND min_depth_m >= {1} AND geom && ST_MakeEnvelope({2},{3},{4},{5},4326)", md, limit, env.MinX, env.MinY, env.MaxX, env.MaxY).ToListAsync(ct);
            foreach (var g in rows) uncertain.Insert(g.Geom.EnvelopeInternal, PreparedGeometryFactory.Prepare(g.Geom));
        }
        uncertain.Build();
        Coordinate start = path[0], goal = path[^1]; var approachM = builder.Options.HarbourAccessM;
        bool InUncertain(LineString seg)
        {
            var hits = uncertain.Query(seg.EnvelopeInternal).Where(u => u.Intersects(seg)).ToList();
            if (hits.Count == 0) return false;
            var a = seg.GetCoordinateN(0); var b = seg.GetCoordinateN(1);
            // ponytail: sampled every ~25 m, a sliver of uncertain water narrower than that slips through
            var n = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / 25));
            for (var k = 0; k <= n; k++)
            {
                var c = new Coordinate(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n);
                if (RoutingGraphBuilder.Haversine(c.Y, c.X, start.Y, start.X) <= approachM || RoutingGraphBuilder.Haversine(c.Y, c.X, goal.Y, goal.X) <= approachM) continue;
                var pt = Geo.Factory.CreatePoint(c);
                if (hits.Any(u => u.Contains(pt))) return true;
            }
            return false;
        }
        Lap("smooth-depth");
        // the channel zone (water next to a buoy chain, minus the chain's own corridor) blocks where the chart has no depth deep
        // enough: in the Wadden Sea gaps nothing else stops the string from cutting across the flats the chain avoids. Where the
        // chart says deep (all depth areas the string crosses inside the zone ≥ draft + margin), a corner next to a chain is cut
        // like anywhere else (it was an obstacle everywhere and kept the route on the chain: Fehmarnsund, Kiel Förde)
        var zone = new STRtree<IPreparedGeometry>(); STRtree<IPreparedGeometry> deep;
        foreach (var z in statics.Zone.Query(env)) zone.Insert(z.EnvelopeInternal, PreparedGeometryFactory.Prepare(z));
        zone.Build();
        // depth areas around the route (boxes of ≤ 20 points) where it passes the zone (0.1° ≈ 7–11 km margin: the corners the
        // pulling cuts; legs through the zone farther from the route stay blocked) or follows a passage (0.01°)
        var boxes = new List<Envelope>();
        for (var k = 0; k < path.Count; k += 20)
        {
            var box = new Envelope(); var passage = false;
            for (var q = k; q < Math.Min(path.Count, k + 21); q++) { box.ExpandToInclude(path[q]); passage |= q < fixedSeg.Count && fixedSeg[q]; }
            var wide = new Envelope(box); wide.ExpandBy(0.1);
            if (zone.Query(wide).Count > 0) boxes.Add(wide);
            else if (passage) { box.ExpandBy(0.01); boxes.Add(box); }
        }
        async Task<STRtree<IPreparedGeometry>> DeepAsync(List<Envelope> boxes, double floor)
        {
            var tree = new STRtree<IPreparedGeometry>();
            if (boxes.Count > 0)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var values = string.Join(", ", boxes.Select(e => string.Create(inv, $"({e.MinX}, {e.MinY}, {e.MaxX}, {e.MaxY})")));
                var rows = await db.Database.SqlQueryRaw<GeomRow>($$"""
                    SELECT geom AS "Geom" FROM routing_depth WHERE id IN (
                      SELECT d.id FROM (VALUES {{values}}) b(x1, y1, x2, y2) JOIN routing_depth d ON d.geom && ST_MakeEnvelope(b.x1, b.y1, b.x2, b.y2, 4326) WHERE d.min_depth_m >= {0})
                    """, floor).ToListAsync(ct);
                foreach (var g in rows) tree.Insert(g.Geom.EnvelopeInternal, PreparedGeometryFactory.Prepare(g.Geom));
            }
            tree.Build();
            return tree;
        }
        // "Tonnen first": next to a buoy chain and along a passage only water definitely deep enough counts - every band floor at or above
        // the minimum depth; a band that passes by its middle only is the water a channel avoids (Oknö: the pull cut from the fairway
        // across the 1.8-3.7 m band into the skerries)
        deep = await DeepAsync(boxes, minDepthM ?? limit);
        // passage geometry keeps its shape where it matters – canals, rivers, channels through shallow or uncharted water; a
        // passage segment that lies in water charted deep enough all along (a buoyed fairway across the open Kiel Bay) is pulled
        // like any other leg (fixed, the route rode the Fehmarnsund–Kiel lighthouse chain up to 54.49 N instead of heading for the Förde)
        for (var k = 0; k < fixedSeg.Count; k++) if (fixedSeg[k] && Charted(path[k], path[k + 1], deep)) fixedSeg[k] = false;
        (path, fixedSeg) = StringPull.Densify(path, fixedSeg, 1000);
        Lap("smooth-zone");
        // legs of the graph path across land with water on both sides - a buoy-chain chord over an island or spit (Vaxholm, the
        // Albrektsund canal), a short gap link over an islet: the pull keeps passage geometry as it is, so they are rerouted first,
        // locally round the land pieces they cross (PierAvoidance over the land clipped around the leg), the other blockers still
        // blocking. Assumed passages cross the coastline land by design and stay; a leg without a way round stays as it was
        var cuts = LandCuts(path, landOnly, assumedPoints);
        if (cuts.Count > 0)
        {
            try { (path, fixedSeg) = PierAvoidance.Apply(path, fixedSeg, cuts, Obstructed, clearM: 5, windowM: 300); }
            catch (Exception ex) when (ex is TopologyException or ArgumentException) { logger.LogWarning(ex, "Land detour skipped"); }
            if (timings is not null) timings["land-cuts"] = cuts.Count;
            Stage("landcut", path);
        }
        // coast clearance (Routing:CoastClearanceM): a pulled leg keeps it, except within ClearCorridorM of the graph path itself - in a
        // narrow sound, channel, passage or harbour approach the path runs closer and the pull may not stray from it - and within
        // ClearEndM of the route ends; the visibility graph keeps CoastBufferM, the pull and Tighten cut that down to the beach (Skjoldnæs)
        var clearM = builder.Options.CoastClearanceM;
        var coast = new STRtree<Lazy<IndexedFacetDistance>>();
        foreach (var g in land) coast.Insert(g.Geom.EnvelopeInternal, new Lazy<IndexedFacetDistance>(() => new IndexedFacetDistance(g.Geom)));
        coast.Build();
        // where the graph path itself crosses land (a link across a mole, a channel skeleton over a spit), the pull must stay free to
        // get rid of it: no clearance within ClearEndM of such a segment (Langedrag→Marstrand kept a 45 m land crossing otherwise)
        var corridor = new STRtree<LineSegment>(); var rough = new STRtree<LineSegment>();
        for (var k = 1; k < path.Count; k++)
        {
            var sg = new LineSegment(path[k - 1], path[k]); var se = new Envelope(path[k - 1], path[k]);
            corridor.Insert(se, sg);
            var line = Geo.Factory.CreateLineString([path[k - 1], path[k]]);
            if (index.Query(se).Any(g => g.Intersects(line))) rough.Insert(se, sg);
        }
        corridor.Build(); rough.Build();
        Coordinate first = path[0], last = path[^1];
        static Envelope Around(Coordinate x, double m) { var d = m / WaterGraphTiler.MPerDegLat; var dx = d / Math.Cos(x.Y * Math.PI / 180); return new Envelope(x.X - dx, x.X + dx, x.Y - d, x.Y + d); }
        bool NearCoast(Coordinate x, double m)
        {
            var pt = Geo.Factory.CreatePoint(x); var cos = Math.Cos(x.Y * Math.PI / 180);
            foreach (var f in coast.Query(Around(x, m)))
            {
                if (f.Value.Distance(pt) * WaterGraphTiler.MPerDegLat * cos > m) continue; // planar degrees: a lower bound of the metres
                var near = f.Value.NearestPoints(pt)[0];
                if (RoutingGraphBuilder.Haversine(x.Y, x.X, near.Y, near.X) <= m) return true;
            }
            return false;
        }
        static bool Within(STRtree<LineSegment> tree, Coordinate x, double m)
        {
            var cos = Math.Cos(x.Y * Math.PI / 180); var xs = new Coordinate(x.X * cos, x.Y);
            return tree.Query(Around(x, m)).Any(sg => new LineSegment(sg.P0.X * cos, sg.P0.Y, sg.P1.X * cos, sg.P1.Y).Distance(xs) * WaterGraphTiler.MPerDegLat <= m);
        }
        bool TooClose(Coordinate p, Coordinate q, bool exempt)
        {
            if (clearM <= 0) return false;
            var env = new Envelope(p, q); env.ExpandToInclude(Around(p, clearM)); env.ExpandToInclude(Around(q, clearM));
            var near = coast.Query(env);
            if (near.Count == 0) return false;
            var seg = Geo.Factory.CreateLineString([p, q]); var cos = Math.Cos(Math.Max(Math.Abs(p.Y), Math.Abs(q.Y)) * Math.PI / 180);
            if (near.All(f => f.Value.Distance(seg) * WaterGraphTiler.MPerDegLat * cos > clearM)) return false;
            // ponytail: sampled every ~20 m (a headland between two samples is passed at >= clearance - 10 m); exact buffers per route are slower
            var n = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(p.Y, p.X, q.Y, q.X) / 20));
            for (var k = 0; k <= n; k++)
            {
                var x = new Coordinate(p.X + (q.X - p.X) * k / n, p.Y + (q.Y - p.Y) * k / n);
                if (exempt && (RoutingGraphBuilder.Haversine(x.Y, x.X, first.Y, first.X) < ClearEndM || RoutingGraphBuilder.Haversine(x.Y, x.X, last.Y, last.X) < ClearEndM || Within(corridor, x, ClearCorridorM) || Within(rough, x, ClearEndM))) continue;
                if (NearCoast(x, clearM)) return true;
            }
            return false;
        }
        bool Obstructed(Coordinate p, Coordinate q)
        {
            var seg = Geo.Factory.CreateLineString([p, q]);
            foreach (var g in index.Query(seg.EnvelopeInternal)) if (g.Intersects(seg)) return true;
            return InUncertain(seg) || InUnchartedZone(seg, zone, deep);
        }
        var tests = 0L;
        bool Blocked(Coordinate p, Coordinate q) { tests++; return Obstructed(p, q) || TooClose(p, q, exempt: true); }
        var (result, resultFixed) = StringPull.PullFlags(path, fixedSeg, Blocked, builder.Options.PassageTrimM);
        Lap("smooth-pull");
        if (timings is not null) { timings["smooth-points"] = path.Count; timings["smooth-tests"] = tests; timings["pulled-points"] = result.Count; }
        Stage("pulled", result);
        // piers, jetties, breakwaters are not in the land: legs that cross one are rerouted round it locally, the blockers above still blocking
        var piers = await PiersAsync();
        Func<Coordinate, Coordinate, bool> finalBlocked = Blocked;
        if (piers.Count > 0)
        {
            try
            {
                // detour legs round a pier head lie next to the pier by nature: land, bridges, depth and zone block them, the clearance does not
                var (avoided, avoidedFixed) = PierAvoidance.Apply(result, resultFixed, piers, Obstructed);
                Stage("piers", avoided);
                Lap("smooth-piers");
                // the detours are local: a detour round a pier head can make the next route point an out-and-back (the harbour access
                // node behind a jetty, a grid node beyond a mole's head – Großenbrode). A second pull over the finished points, piers
                // blocking now too, drops every point the route can do without (it never gets longer, passage shapes stay)
                var crossesPier = PierAvoidance.Crossing(piers);
                finalBlocked = (p, q) => crossesPier(p, q) || Blocked(p, q);
                (result, resultFixed) = StringPull.PullFlags(avoided, avoidedFixed, finalBlocked, builder.Options.PassageTrimM);
                if (timings is not null) { timings["pier-points"] = avoided.Count; timings["repulled-points"] = result.Count; }
                Stage("repulled", result);
            }
            catch (Exception ex) when (ex is NetTopologySuite.Geometries.TopologyException or ArgumentException) { logger.LogWarning(ex, "Pier avoidance skipped"); } // a bad OSM pier must never cost the route
        }
        Lap("smooth-repull");
        // corners the pull could not cut because the path has no point near the obstacle (StringPull.Tighten). A chamfer only rounds a
        // turn: it keeps the clearance everywhere (no corridor, no end exemption) and lies all along in charted water deep enough for the
        // draft - water without depth data next to the coast is the < 1.83 m strip the depth raster leaves out (Skjoldnæs: the chamfer
        // ran across it a few metres off the beach). Corners at passage geometry stay (Tighten skips fixed segments)
        var tightBoxes = new List<Envelope>();
        for (var k = 1; k < result.Count - 1; k++) if (!resultFixed[k - 1] && !resultFixed[k]) { var e = new Envelope(result[k - 1], result[k + 1]); e.ExpandToInclude(result[k]); tightBoxes.Add(e); }
        var tightDeep = await DeepAsync(tightBoxes, Math.Max(limit, 0));
        var chamferBlocked = finalBlocked;
        (result, _) = StringPull.Tighten(result, resultFixed, (p, q) => chamferBlocked(p, q) || TooClose(p, q, exempt: false) || !Charted(p, q, tightDeep, 25));
        if (timings is not null) timings["final-points"] = result.Count;
        Stage("tightened", result);
        Lap("smooth-tighten");
        return result;
    }

    // SMS_ROUTE_STAGES=1 logs the polyline after each smoothing stage (RouteBench, debugging)
    private static readonly bool LogStages = Environment.GetEnvironmentVariable("SMS_ROUTE_STAGES") == "1";
    private void Stage(string name, List<Coordinate> points)
    {
        if (LogStages) logger.LogInformation("stage {Stage} ({Count}): {Points}", name, points.Count, string.Join(" ", points.Select(c => FormattableString.Invariant($"{c.Y:F6},{c.X:F6}"))));
    }

    /// <summary>The land pieces (clipped to the leg's box + 300 m) that legs of the path cross with water before and after them; assumed legs skipped.</summary>
    private static STRtree<Geometry> LandCuts(List<Coordinate> path, STRtree<IPreparedGeometry> land, IReadOnlySet<Coordinate>? assumedPoints)
    {
        var cuts = new STRtree<Geometry>();
        for (var k = 1; k < path.Count; k++)
        {
            Coordinate p = path[k - 1], q = path[k];
            if (assumedPoints is not null && assumedPoints.Contains(p) && assumedPoints.Contains(q)) continue;
            var line = Geo.Factory.CreateLineString([p, q]);
            var hits = land.Query(line.EnvelopeInternal).Where(g => g.Intersects(line)).ToList();
            if (hits.Count == 0 || !SeaGraph.CrossesInteriorLand(p, q, c => { var pt = Geo.Factory.CreatePoint(c); return hits.Any(h => h.Contains(pt)); })) continue;
            var box = new Envelope(p, q); var d = 300 / WaterGraphTiler.MPerDegLat; box.ExpandBy(d / Math.Cos(p.Y * Math.PI / 180), d);
            var boxGeom = Geo.Factory.ToGeometry(box);
            foreach (var h in hits)
            {
                Geometry clip;
                try { clip = h.Geometry.Intersection(boxGeom); } catch (TopologyException) { continue; }
                for (var i = 0; i < clip.NumGeometries; i++) if (clip.GetGeometryN(i) is Polygon poly && !poly.IsEmpty) cuts.Insert(poly.EnvelopeInternal, poly);
            }
        }
        cuts.Build();
        return cuts;
    }

    private const double StraightPassageLandM = 20;

    /// <summary>A pulled leg within this of the graph path or of a route end may come closer to the coast than CoastClearanceM.</summary>
    private const double ClearCorridorM = 40, ClearEndM = 500;

    /// <summary>True when every sample of the leg (every ~stepM) lies in a deep-enough depth area.</summary>
    private static bool Charted(Coordinate a, Coordinate b, STRtree<IPreparedGeometry> deep, double stepM = 100)
    {
        var n = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / stepM));
        for (var k = 0; k <= n; k++)
        {
            var c = new Coordinate(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n);
            var pt = Geo.Factory.CreatePoint(c);
            if (!deep.Query(new Envelope(c)).Any(d => d.Contains(pt))) return false;
        }
        return true;
    }

    /// <summary>True when a sample of the leg (every ~100 m) lies in the channel zone outside every deep-enough depth area.</summary>
    private static bool InUnchartedZone(LineString seg, STRtree<IPreparedGeometry> zone, STRtree<IPreparedGeometry> deep)
    {
        var hits = zone.Query(seg.EnvelopeInternal);
        if (hits.Count == 0 || !hits.Any(z => z.Intersects(seg))) return false;
        var a = seg.GetCoordinateN(0); var b = seg.GetCoordinateN(1);
        // ponytail: sampled, a no-data pocket narrower than the step slips through; an exact overlay per leg is far slower
        var n = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / 100));
        for (var k = 0; k <= n; k++)
        {
            var c = new Coordinate(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n);
            var pt = Geo.Factory.CreatePoint(c);
            if (!hits.Any(z => z.Contains(pt))) continue;
            if (!deep.Query(new Envelope(c)).Any(d => d.Contains(pt))) return true;
        }
        return false;
    }

    private async Task<long?> AccessNodeAsync(Guid harbourId, int version, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<long>("SELECT node_id AS \"Value\" FROM routing_harbour_access WHERE graph_version = {0} AND harbour_id = {1}", version, harbourId).FirstOrDefaultAsync(ct) is var id && id != 0 ? id : null;

    /// <summary>
    /// Nearest graph node for a map click. On water the grid is coarse offshore (cells up to StartCellM wide), so the search
    /// reaches a whole cell; on land (coast, harbour basins counted as land) only 2 km (beyond that LandEndAsync looks for the nearest water).
    /// </summary>
    private async Task<(long? Id, double Metres, double MaxM, bool OnLand, bool Visible)> SnapAsync(Point p, int version, CancellationToken ct)
    {
        var onLand = await db.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM routing_land WHERE graph_version = {0} AND geom && ST_SetSRID(ST_MakePoint({1}, {2}), 4326) AND ST_Intersects(geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326))", version, p.X, p.Y).AnyAsync(ct);
        var maxM = onLand ? 2000 : Math.Max(2000, builder.Options.StartCellM);
        var degLat = maxM / WaterGraphTiler.MPerDegLat; var degLng = degLat / Math.Max(0.2, Math.Cos(p.Y * Math.PI / 180));
        // first choice: a node the point can see (the first leg is drawn straight), then the plain nearest. The sight test runs on
        // the 64 nearest nodes only, against the land clipped to their box, in memory (in SQL, on all nodes of the 40 km box and
        // against the whole 146 000-point Dutch land polygon, it took 8 s next to the IJsselmeer)
        SnapRow? row = null;
        var visible = false;
        if (!onLand)
        {
            var near = await db.Database.SqlQueryRaw<SnapRow>("""
                SELECT n.id AS "Id", ST_DistanceSphere(n.geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326)) AS "Metres", ST_X(n.geom) AS "X", ST_Y(n.geom) AS "Y",
                       EXISTS (SELECT 1 FROM routing_isolated i WHERE i.graph_version = {0} AND i.id = n.id) AS "Isolated"
                FROM (SELECT id, geom FROM routing_node WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1} - {3}, {2} - {4}, {1} + {3}, {2} + {4}, 4326)
                      ORDER BY geom <-> ST_SetSRID(ST_MakePoint({1}, {2}), 4326) LIMIT 64) n
                """, version, p.X, p.Y, degLng, degLat).ToListAsync(ct);
            if (near.Count > 0)
            {
                var box = new Envelope(p.Coordinate); foreach (var n in near) box.ExpandToInclude(n.X, n.Y);
                var land = await db.Database.SqlQueryRaw<GeomRow>("SELECT ST_ClipByBox2D(geom, ST_MakeEnvelope({1},{2},{3},{4},4326)) AS \"Geom\" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)",
                    version, box.MinX, box.MinY, box.MaxX, box.MaxY).ToListAsync(ct);
                var prepared = land.Where(l => !l.Geom.IsEmpty).Select(l => PreparedGeometryFactory.Prepare(l.Geom)).ToList();
                row = near.OrderBy(n => n.Isolated).ThenBy(n => n.Metres)
                    .FirstOrDefault(n => { var leg = Geo.Factory.CreateLineString([p.Coordinate, new Coordinate(n.X, n.Y)]); return !prepared.Any(g => g.Intersects(leg)); });
                visible = row is not null;
                // none in sight (a harbour basin round a bend): the node whose leg crosses the least land, not the nearest grid node
                // (Glückstadt: a grid node north of the mouth, 414 m of land, beat the fairway's end inside the basin, 65 m)
                if (row is null)
                {
                    var cos = Math.Cos(p.Y * Math.PI / 180);
                    double LandM(SnapRow n)
                    {
                        var leg = Geo.Factory.CreateLineString([p.Coordinate, new Coordinate(n.X, n.Y)]);
                        return land.Where(l => !l.Geom.IsEmpty && l.Geom.EnvelopeInternal.Intersects(leg.EnvelopeInternal)).Sum(l =>
                        {
                            try { var x = l.Geom.Intersection(leg); return x.IsEmpty ? 0 : Math.Sqrt(Math.Pow(x.EnvelopeInternal.Width * cos, 2) + Math.Pow(x.EnvelopeInternal.Height, 2)) * WaterGraphTiler.MPerDegLat; }
                            catch (TopologyException) { return double.MaxValue / 4; }
                        });
                    }
                    var least = near.Where(n => !n.Isolated).Select(n => (n, m: LandM(n))).OrderBy(x => x.m).ThenBy(x => x.n.Metres).FirstOrDefault();
                    if (least.n is not null && least.m <= ShortLandM) row = least.n;
                }
            }
        }
        row ??= await db.Database.SqlQueryRaw<SnapRow>("""
            SELECT id AS "Id", ST_DistanceSphere(geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326)) AS "Metres", ST_X(geom) AS "X", ST_Y(geom) AS "Y", false AS "Isolated"
            FROM routing_node WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1} - {3}, {2} - {4}, {1} + {3}, {2} + {4}, 4326)
              AND ST_DistanceSphere(geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326)) <= {5} -- the box corners are farther: a grid node there must not beat a passage node next to the point
            ORDER BY EXISTS (SELECT 1 FROM routing_isolated i WHERE i.graph_version = {0} AND i.id = routing_node.id), CASE WHEN id <= (SELECT COALESCE(MAX(value::bigint), 0) FROM routing_meta WHERE key = 'grid_nodes') THEN 0 ELSE 1 END, geom <-> ST_SetSRID(ST_MakePoint({1}, {2}), 4326) LIMIT 1
            """, version, p.X, p.Y, degLng, degLat, maxM).FirstOrDefaultAsync(ct);
        return row is not null && row.Metres <= maxM ? (row.Id, row.Metres, maxM, onLand, visible) : (null, 0, maxM, onLand, false);
    }

    private static Envelope Expand(Point a, Point b, double metres)
    {
        var env = new Envelope(a.Coordinate); env.ExpandToInclude(b.Coordinate);
        var dLat = metres / 111_320.0; var dLng = metres / (111_320.0 * Math.Cos((a.Y + b.Y) / 2 * Math.PI / 180));
        return new Envelope(env.MinX - dLng, env.MaxX + dLng, env.MinY - dLat, env.MaxY + dLat);
    }

    private sealed class MetaRow { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }
    private sealed class NearbyRow { public Guid HarbourId { get; set; } public double Metres { get; set; } }
    private sealed class SnapRow { public long Id { get; set; } public double Metres { get; set; } public double X { get; set; } public double Y { get; set; } public bool Isolated { get; set; } }
    private sealed class GeomRow { public Geometry Geom { get; set; } = null!; }
}
