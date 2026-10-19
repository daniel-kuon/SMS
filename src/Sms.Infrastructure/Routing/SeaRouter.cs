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

public sealed record RouteResult(decimal DistanceNm, decimal StraightLineNm, LineString Geometry, int GraphVersion, string Method, IReadOnlyDictionary<string, long>? Timings = null, IReadOnlyList<RouteWarning>? Warnings = null);
/// <summary>Something the route assumes that the chart data does not show; Kind "assumedPassage": a passage through a strait the coastline closes.</summary>
public sealed record RouteWarning(string Kind, double Lat, double Lng, string Note);
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

    /// <summary>Harbour → harbour route; null when either harbour is not connected to the active graph or no path exists.</summary>
    public async Task<RouteResult?> RouteAsync(Guid from, Guid to, CancellationToken ct, double? airDraftM = null, double? draftM = null)
    {
        var version = await ActiveVersionAsync(ct);
        if (version == 0) return null;
        // the cache holds unrestricted routes only; an air draft may close bridges and a draft shallow areas, so those are computed every time
        // with assumed passages switched off the cached routes (which may use them) are bypassed
        var useCache = airDraftM is null && draftM is null && builder.Options.AssumedPassages;
        var cached = useCache ? await db.RouteCaches.AsNoTracking().FirstOrDefaultAsync(r => r.FromHarbourId == from && r.ToHarbourId == to && r.GraphVersion == version, ct) : null;
        var a = await db.Harbours.AsNoTracking().Where(h => h.Id == from).Select(h => h.Location).FirstOrDefaultAsync(ct);
        var b = await db.Harbours.AsNoTracking().Where(h => h.Id == to).Select(h => h.Location).FirstOrDefaultAsync(ct);
        if (a is null || b is null) return null;
        var straight = Math.Round((decimal)(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / Nm), 1);
        if (cached is not null) return new RouteResult(cached.DistanceNm, straight, cached.Geometry, version, "sea-graph", Warnings: (await WarningsAlongAsync([cached.Geometry], ct))[0]);
        var start = await AccessNodeAsync(from, version, ct);
        var end = await AccessNodeAsync(to, version, ct);
        if (start is null || end is null) return null;
        var graph = await GraphAsync(version);
        var result = await RouteEndsAsync(graph, new SeaGraph.End(a.Coordinate, (int)start.Value), new SeaGraph.End(b.Coordinate, (int)end.Value), version, ct, airDraftM, draftM);
        if (result is null) return null;
        if (!useCache) return result;
        db.RouteCaches.Add(new RouteCache { FromHarbourId = from, ToHarbourId = to, GraphVersion = version, DistanceNm = result.DistanceNm, Geometry = result.Geometry });
        db.RouteCaches.Add(new RouteCache { FromHarbourId = to, ToHarbourId = from, GraphVersion = version, DistanceNm = result.DistanceNm, Geometry = (LineString)result.Geometry.Reverse() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ChangeTracker.Clear(); } // concurrent insert of the same pair
        return result;
    }

    /// <summary>Point → point route (map clicks). Each point snaps to a linked harbour next to it, a passage edge or the nearest node (EndAsync).</summary>
    public async Task<RouteResult?> RouteAsync(Point from, Point to, CancellationToken ct, double? airDraftM = null, double? draftM = null)
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
        return await RouteEndsAsync(graph, start, end, version, ct, airDraftM, draftM, timings);
    }

    /// <summary>A click within this distance of a harbour the builder linked snaps to the harbour's access node.</summary>
    private const double HarbourSnapM = 150;

    /// <summary>
    /// Route end for a map click: (1) a linked harbour within HarbourSnapM – its access node, even when the harbour lies behind
    /// a dike beyond the snap distance (the builder links up to HarbourAccessM, the final leg draws the link); (2) otherwise
    /// the nearest node (SnapAsync), unless a passage edge is closer – a contracted canal stretch has no node inside, so the
    /// end is projected onto it and the edge split there (the leg to the projection must not cross land while a node is at hand).
    /// </summary>
    private async Task<SeaGraph.End?> EndAsync(Point p, int version, SeaGraph graph, CancellationToken ct)
    {
        var access = await db.Database.SqlQueryRaw<long>("""
            SELECT a.node_id AS "Value" FROM "Harbours" h JOIN routing_harbour_access a ON a.harbour_id = h."Id" AND a.graph_version = {0}
            WHERE ST_DWithin(h."Location", ST_SetSRID(ST_MakePoint({1}, {2}), 4326)::geography, {3})
            ORDER BY ST_Distance(h."Location", ST_SetSRID(ST_MakePoint({1}, {2}), 4326)::geography) LIMIT 1
            """, version, p.X, p.Y, HarbourSnapM).ToListAsync(ct);
        if (access.Count > 0) return new SeaGraph.End(p.Coordinate, (int)access[0]);
        var (node, metres, maxM) = await SnapAsync(p, version, ct);
        var onEdge = graph.SnapToPassage(p.Coordinate, node is null ? maxM : metres);
        if (onEdge is not null && (node is null || !await CrossesLandAsync(p.Coordinate, onEdge.Point, version, ct))) return new SeaGraph.End(p.Coordinate, OnEdge: onEdge);
        return node is { } n ? new SeaGraph.End(p.Coordinate, (int)n) : null;
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
        var src = new int[edgeCount]; var tgt = new int[edgeCount]; var cost = new float[edgeCount]; var depth = new float[edgeCount]; var clear = new float[edgeCount]; var assumed = new bool[edgeCount];
        var geomStart = new int[edgeCount + 1]; var geom = new List<double>();
        var m = 0;
        await using (var c = new NpgsqlCommand("SELECT source, target, cost, min_depth_m, clearance_m, CASE WHEN ST_NPoints(geom) > 2 THEN ST_AsBinary(geom) END, assumed FROM routing_edge WHERE graph_version = @v", conn) { CommandTimeout = 600 })
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
                m++;
            }
        }
        geomStart[m] = geom.Count / 2;
        if (m < edgeCount) { Array.Resize(ref src, m); Array.Resize(ref tgt, m); Array.Resize(ref cost, m); Array.Resize(ref depth, m); Array.Resize(ref clear, m); Array.Resize(ref assumed, m); Array.Resize(ref geomStart, m + 1); }
        var isolated = new bool[maxNode + 1];
        await using (var c = new NpgsqlCommand("SELECT id FROM routing_isolated WHERE graph_version = @v", conn))
        {
            c.Parameters.AddWithValue("v", version);
            await using var r = await c.ExecuteReaderAsync();
            while (await r.ReadAsync()) { var id = r.GetInt64(0); if (id <= maxNode) isolated[id] = true; }
        }
        var loaded = sw.ElapsedMilliseconds;
        var graph = new SeaGraph(x, y, src, tgt, cost, depth, clear, geomStart, geom.ToArray(), (int)gridNodes + 1, isolated, assumed: assumed);
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
    /// <summary>The lowest charted depth band floor a ship with this draft may cross (see <see cref="DepthBands"/>).</summary>
    private async Task<double> DepthFloorAsync(double draftM, CancellationToken ct)
    {
        if (_depthBands is null)
        {
            var rows = await db.Database.SqlQueryRaw<BandRow>("SELECT DISTINCT min_depth_m AS \"Min\", max_depth_m AS \"Max\" FROM routing_depth").ToListAsync(ct);
            _depthBands = (rows.Count, rows.Select(r => (r.Min, r.Max)).ToArray());
        }
        return DepthBands.EffectiveFloor(draftM + builder.Options.DepthSafetyMarginM, _depthBands.Value.bands);
    }
    private sealed class BandRow { public double Min { get; set; } public double Max { get; set; } }

    private async Task<RouteResult?> RouteEndsAsync(SeaGraph graph, SeaGraph.End from, SeaGraph.End to, int version, CancellationToken ct, double? airDraftM = null, double? draftM = null, Dictionary<string, long>? timings = null)
    {
        // bridges lower than the ship's air draft close their passage edges (clearance_m is null elsewhere); charted depth areas
        // shallower than draft + margin close grid edges (min_depth_m is null on marked passages: a buoyed channel is followed anyway)
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
        var limits = new SeaGraph.Limits(airDraftM, draftM is { } dm ? await DepthFloorAsync(dm, ct) : null, o.HarbourAccessM / WaterGraphTiler.MPerDegLat, NoAssumed: !o.AssumedPassages);
        var path = graph.Route(from, to, limits);
        timings["astar"] = clock.ElapsedMilliseconds; clock.Restart();
        if (path is null) { logger.LogInformation("No sea route between {From} and {To}", from, to); return null; }
        span?.SetTag("routing.settled", path.Settled);
        var smoothed = await SmoothAsync(path.Points, path.Fixed, version, ct, airDraftM, draftM, timings);
        timings["smooth"] = clock.ElapsedMilliseconds;
        var metres = 0.0;
        for (var i = 1; i < smoothed.Count; i++) metres += RoutingGraphBuilder.Haversine(smoothed[i - 1].Y, smoothed[i - 1].X, smoothed[i].Y, smoothed[i].X);
        var line = Geo.Factory.CreateLineString(smoothed.ToArray());
        span?.SetTag("routing.distance_nm", metres / Nm);
        return new RouteResult(Math.Round((decimal)(metres / Nm), 1), Math.Round((decimal)(straightM / Nm), 1), line, version, "sea-graph", timings, await WarningsAtAsync(path.Assumed ?? [], ct));
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
        return result;
    }

    private static RouteWarning Warning(Coordinate at, string? note) =>
        new("assumedPassage", Math.Round(at.Y, 5), Math.Round(at.X, 5), $"Durchfahrt angenommen ({(note is null ? "Fährlinie oder Tonnenlücke" : note[AssumedPassages.NotePrefix.Length..].TrimStart(':', ' '))}) – nicht in den Kartendaten, vor Ort prüfen");

    /// <summary>
    /// Warnings for stored routes (route cache, planned legs) that carry no graph flags: every active "Annahme" passage a route runs
    /// along (<see cref="AssumedPassages.Follows"/>). One list per route; empty lists while Routing:AssumedPassages is off.
    /// </summary>
    public async Task<List<RouteWarning>[]> WarningsAlongAsync(IReadOnlyList<LineString?> routes, CancellationToken ct)
    {
        var result = routes.Select(_ => new List<RouteWarning>()).ToArray();
        if (!builder.Options.AssumedPassages || routes.All(r => r is null)) return result;
        var env = new Envelope(); foreach (var r in routes) if (r is not null) env.ExpandToInclude(r.EnvelopeInternal);
        var box = Geo.Factory.ToGeometry(env); box.SRID = 4326;
        var passages = await db.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == CorrectionKind.Passage && c.Note != null && c.Note.StartsWith(AssumedPassages.NotePrefix) && c.Geometry.Intersects(box))
            .Select(c => new { c.Geometry, c.Note }).ToListAsync(ct);
        for (var i = 0; i < routes.Count; i++)
            if (routes[i] is { } route)
                foreach (var p in passages)
                    if (AssumedPassages.Follows(route, p.Geometry.Coordinates)) result[i].Add(Warning(AssumedPassages.Midpoint(p.Geometry.Coordinates), p.Note));
        return result;
    }

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

    private async Task<List<Coordinate>> SmoothAsync(List<Coordinate> path, List<bool> fixedSeg, int version, CancellationToken ct, double? airDraftM = null, double? draftM = null, Dictionary<string, long>? timings = null)
    {
        var clock = Stopwatch.StartNew();
        void Lap(string k) { if (timings is not null) timings[k] = clock.ElapsedMilliseconds; clock.Restart(); }
        (path, fixedSeg) = StringPull.Densify(path, fixedSeg, 1000); // passage geometry stays as it is
        var env = new Envelope(); foreach (var c in path) env.ExpandToInclude(c); env.ExpandBy(0.05);
        var land = await db.Database.SqlQueryRaw<GeomRow>("SELECT geom AS \"Geom\" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)", version, env.MinX, env.MinY, env.MaxX, env.MaxY).ToListAsync(ct);
        var index = new STRtree<IPreparedGeometry>();
        foreach (var g in land) index.Insert(g.Geom.EnvelopeInternal, PreparedGeometryFactory.Prepare(g.Geom));
        Lap("smooth-land");
        // what A* avoided must stay avoided when the string is pulled: too-low bridges (a circle around the point, a band along a
        // line), the impassable parts of bridges with charted spans, and too-shallow areas count as land
        var statics = await StaticsAsync(version, ct);
        {
            // bridge pieces (chains, charted spans) are the same for every route of a graph version: computed once, queried by envelope
            var radiusDeg = builder.Options.BridgeMatchAreaM / WaterGraphTiler.MPerDegLat;
            foreach (var (g, clearance) in statics.Bridges.Query(env))
            {
                if (clearance is not { } cl || (cl > 0 && (airDraftM is not { } ad || cl >= ad))) continue;
                var b = g is Point ? g.Buffer(radiusDeg) : g.Buffer(300 / WaterGraphTiler.MPerDegLat);
                if (b.EnvelopeInternal.Intersects(env)) index.Insert(b.EnvelopeInternal, PreparedGeometryFactory.Prepare(b));
            }
        }
        Lap("smooth-bridges");
        var limit = draftM is { } dm ? await DepthFloorAsync(dm, ct) : double.MinValue;
        if (draftM is not null)
        {
            var shallow = await db.Database.SqlQueryRaw<GeomRow>("SELECT geom AS \"Geom\" FROM routing_depth WHERE min_depth_m < {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)", limit, env.MinX, env.MinY, env.MaxX, env.MaxY).ToListAsync(ct);
            foreach (var g in shallow) index.Insert(g.Geom.EnvelopeInternal, PreparedGeometryFactory.Prepare(g.Geom));
        }
        index.Build();
        Lap("smooth-depth");
        // the channel zone (water next to a buoy chain, minus the chain's own corridor) blocks where the chart has no depth deep
        // enough: in the Wadden Sea gaps nothing else stops the string from cutting across the flats the chain avoids. Where the
        // chart says deep (all depth areas the string crosses inside the zone ≥ draft + margin), a corner next to a chain is cut
        // like anywhere else (it was an obstacle everywhere and kept the route on the chain: Fehmarnsund, Kiel Förde)
        var zone = new STRtree<IPreparedGeometry>(); var deep = new STRtree<IPreparedGeometry>();
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
        if (boxes.Count > 0)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var values = string.Join(", ", boxes.Select(e => string.Create(inv, $"({e.MinX}, {e.MinY}, {e.MaxX}, {e.MaxY})")));
            var rows = await db.Database.SqlQueryRaw<GeomRow>($$"""
                SELECT geom AS "Geom" FROM routing_depth WHERE id IN (
                  SELECT d.id FROM (VALUES {{values}}) b(x1, y1, x2, y2) JOIN routing_depth d ON d.geom && ST_MakeEnvelope(b.x1, b.y1, b.x2, b.y2, 4326) WHERE d.min_depth_m >= {0})
                """, limit).ToListAsync(ct);
            foreach (var g in rows) deep.Insert(g.Geom.EnvelopeInternal, PreparedGeometryFactory.Prepare(g.Geom));
        }
        deep.Build();
        // passage geometry keeps its shape where it matters – canals, rivers, channels through shallow or uncharted water; a
        // passage segment that lies in water charted deep enough all along (a buoyed fairway across the open Kiel Bay) is pulled
        // like any other leg (fixed, the route rode the Fehmarnsund–Kiel lighthouse chain up to 54.49 N instead of heading for the Förde)
        for (var k = 0; k < fixedSeg.Count; k++) if (fixedSeg[k] && Charted(path[k], path[k + 1], deep)) fixedSeg[k] = false;
        (path, fixedSeg) = StringPull.Densify(path, fixedSeg, 1000);
        Lap("smooth-zone");
        var tests = 0L;
        bool Blocked(Coordinate p, Coordinate q)
        {
            tests++;
            var seg = Geo.Factory.CreateLineString([p, q]);
            foreach (var g in index.Query(seg.EnvelopeInternal)) if (g.Intersects(seg)) return true;
            return InUnchartedZone(seg, zone, deep);
        }
        var result = StringPull.Pull(path, fixedSeg, Blocked, builder.Options.PassageTrimM);
        Lap("smooth-pull");
        if (timings is not null) { timings["smooth-points"] = path.Count; timings["smooth-tests"] = tests; }
        // piers, jetties, breakwaters are not in the land: legs that cross one are rerouted round it locally, the blockers above still blocking
        var piers = await PiersAsync();
        if (piers.Count > 0)
        {
            try { result = PierAvoidance.Apply(result, piers, Blocked); }
            catch (Exception ex) when (ex is NetTopologySuite.Geometries.TopologyException or ArgumentException) { logger.LogWarning(ex, "Pier avoidance skipped"); } // a bad OSM pier must never cost the route
        }
        Lap("smooth-piers");
        return result;
    }

    /// <summary>True when every sample of the leg (every ~100 m) lies in a deep-enough depth area.</summary>
    private static bool Charted(Coordinate a, Coordinate b, STRtree<IPreparedGeometry> deep)
    {
        var n = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / 100));
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
    /// reaches a whole cell; on land (coast, harbour basins counted as land) only 2 km, so inland clicks stay unroutable.
    /// </summary>
    private async Task<(long? Id, double Metres, double MaxM)> SnapAsync(Point p, int version, CancellationToken ct)
    {
        var onLand = await db.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM routing_land WHERE graph_version = {0} AND geom && ST_SetSRID(ST_MakePoint({1}, {2}), 4326) AND ST_Intersects(geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326))", version, p.X, p.Y).AnyAsync(ct);
        var maxM = onLand ? 2000 : Math.Max(2000, builder.Options.StartCellM);
        var degLat = maxM / WaterGraphTiler.MPerDegLat; var degLng = degLat / Math.Max(0.2, Math.Cos(p.Y * Math.PI / 180));
        // first choice: a node the point can see (the first leg is drawn straight), then the plain nearest. The sight test runs on
        // the 64 nearest nodes only, against the land clipped to their box, in memory (in SQL, on all nodes of the 40 km box and
        // against the whole 146 000-point Dutch land polygon, it took 8 s next to the IJsselmeer)
        SnapRow? row = null;
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
            }
        }
        row ??= await db.Database.SqlQueryRaw<SnapRow>("""
            SELECT id AS "Id", ST_DistanceSphere(geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326)) AS "Metres", ST_X(geom) AS "X", ST_Y(geom) AS "Y", false AS "Isolated"
            FROM routing_node WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1} - {3}, {2} - {4}, {1} + {3}, {2} + {4}, 4326)
              AND ST_DistanceSphere(geom, ST_SetSRID(ST_MakePoint({1}, {2}), 4326)) <= {5} -- the box corners are farther: a grid node there must not beat a passage node next to the point
            ORDER BY EXISTS (SELECT 1 FROM routing_isolated i WHERE i.graph_version = {0} AND i.id = routing_node.id), CASE WHEN id <= (SELECT COALESCE(MAX(value::bigint), 0) FROM routing_meta WHERE key = 'grid_nodes') THEN 0 ELSE 1 END, geom <-> ST_SetSRID(ST_MakePoint({1}, {2}), 4326) LIMIT 1
            """, version, p.X, p.Y, degLng, degLat, maxM).FirstOrDefaultAsync(ct);
        return row is not null && row.Metres <= maxM ? (row.Id, row.Metres, maxM) : (null, 0, maxM);
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
