using System.Diagnostics;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.IO.Esri;
using Npgsql;
using NpgsqlTypes;
using Sms.Domain;

namespace Sms.Infrastructure.Routing;

/// <summary>Builder settings (appsettings "Routing"). Region defaults to the western Baltic; Europe = lon −32…45, lat 27…72 (hours of build time, bounded memory).</summary>
public sealed class RoutingOptions
{
    public string LandPolygonsUrl { get; set; } = "https://osmdata.openstreetmap.de/download/land-polygons-split-4326.zip";
    public string CachePath { get; set; } = "media/cache";
    public RegionBox Region { get; set; } = new();
    public double StartCellM { get; set; } = 20000;
    public double MinCellM { get; set; } = 150;
    /// <summary>Start cells per tile side; a tile (plus its neighbours' border cells) is all that is held in memory.</summary>
    public int TileCells { get; set; } = 10;
    public double HarbourAccessM { get; set; } = 3000;
    public double CoastPenaltyFactor { get; set; } = 0.2;
    /// <summary>Cost multiplier for passage edges (canals, derived fairways): below 1 routes prefer marked channels. Reported distances come from the geometry and are unaffected.</summary>
    public double PassageCostFactor { get; set; } = 1.0; // below 1 makes routes ride cheap passages to their ends (detours); channel preference does the steering
    /// <summary>Extra cost (metres) for passing a lock, roughly the time a mile takes.</summary>
    public double LockPenaltyM { get; set; } = 1852;
    /// <summary>Extra cost (metres) for an opening bridge: waiting for the next opening, about half a lock.</summary>
    public double BridgeOpeningPenaltyM { get; set; } = 926;
    /// <summary>Water under the keel a route keeps: an edge is usable when its shallowest charted depth ≥ draft + this.</summary>
    public double DepthSafetyMarginM { get; set; } = 0.5;
    /// <summary>Open water within this distance of a derived fairway counts as unmarked/shallow: grid edges there cost ×ChannelPenaltyFactor (0 = off).</summary>
    public double ChannelPreferenceM { get; set; } = 1500;
    /// <summary>Interior passage vertices link to a water node this close, so fairways and canals can be joined along their length (ends use HarbourAccessM).</summary>
    public double PassageLinkM { get; set; } = 600;
    public double ChannelPenaltyFactor { get; set; } = 3;
    /// <summary>Edges inside an Avoid correction (warning area, firing range) cost ×this, by the length inside.</summary>
    public double AvoidPenaltyFactor { get; set; } = 3;
    /// <summary>A derived channel through water charted shallower than the default yacht (not drying) costs ×this: the depth areas are coarse in narrow sounds.</summary>
    public double ShallowChannelFactor { get; set; } = 1.5;
    /// <summary>A leg up to this length that the coast buffer closes is still added when it clears the land buffered by NarrowBufferM, at ×NarrowLegFactor (sound entrances, harbour mouths narrower than 2 × CoastBufferM). 0 = off.</summary>
    public double NarrowLegM { get; set; } = 1000;
    public double NarrowBufferM { get; set; } = 40;
    public double NarrowLegFactor { get; set; } = 1.5;
    public double MaxRouteNm { get; set; } = 500;
    /// <summary>Draft assumed when a route is asked for without a ship (a typical cruising yacht): charted shallows below it are avoided by default.</summary>
    public double DefaultDraftM { get; set; } = 1.5;
    /// <summary>"areas": visibility graph over open water (corners of the buffered coast, open-sea lattice, anchors); "grid": the adaptive quadtree.</summary>
    public string Mode { get; set; } = "areas";
    /// <summary>Safety distance kept from the coast: land is buffered by this before the corners are taken.</summary>
    public double CoastBufferM { get; set; } = 150;
    /// <summary>Simplification of the buffered coast (metres): fewer corners, fewer visibility tests.</summary>
    public double SimplifyM { get; set; } = 100;
    public double LatticeM { get; set; } = 20_000;
    public double LatticeClearM { get; set; } = 3_000;
    public double MaxVisibilityM { get; set; } = 40_000;
    public int VisibleNeighbours { get; set; } = 24;
    /// <summary>Charted areas at most this deep get their corners as nodes, so a deep-draft route can bend around a bank.</summary>
    public double ShallowNodeDepthM { get; set; } = 5.5;
    /// <summary>Banks smaller than this get no corner nodes (a route simply crosses or skirts them via the coast corners).</summary>
    public double ShallowNodeMinKm2 { get; set; } = 0.5;
    /// <summary>Water pockets enclosed by the buffered coast up to this size are treated as land for the visibility graph.</summary>
    public double FillHoleKm2 { get; set; } = 3;
    /// <summary>Visibility legs within this distance of a bridge/lock point get its clearance and cost (passages use 60 m).</summary>
    public double BridgeMatchAreaM { get; set; } = 1500;

    public sealed class RegionBox { public double MinLng { get; set; } = 7.6; public double MinLat { get; set; } = 53.3; public double MaxLng { get; set; } = 12.6; public double MaxLat { get; set; } = 55.4; }
}

public sealed record BuildProgress(string Phase, string Message, DateTimeOffset At);

/// <summary>
/// Offline graph build (spec 10 §10.2): OSM land polygons → PostGIS → tile-wise adaptive quadtree over water →
/// nodes/edges streamed into the pgRouting tables (<see cref="WaterGraphTiler"/>). Runs in-process as a background
/// job; progress is exposed to the admin UI. The new graph version becomes active only when the build completes.
/// </summary>
public sealed class RoutingGraphBuilder(IConfiguration config, IServiceScopeFactory scopes, IHttpClientFactory http, ILogger<RoutingGraphBuilder> logger)
{
    private static readonly ActivitySource Activity = new("Sms");
    private readonly List<BuildProgress> _progress = [];
    private int _running;

    public IReadOnlyList<BuildProgress> Progress => _progress;
    public bool IsRunning => _running == 1;

    public RoutingOptions Options => config.GetSection("Routing").Get<RoutingOptions>() ?? new();

    public bool TryStart()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        _ = Task.Run(async () =>
        {
            try { await BuildAsync(CancellationToken.None); }
            catch (Exception ex) { Report("failed", ex.Message); logger.LogError(ex, "Routing graph build failed"); }
            finally { Interlocked.Exchange(ref _running, 0); }
        });
        return true;
    }

    private void Report(string phase, string message)
    {
        lock (_progress) { if (_progress.Count > 200) _progress.RemoveAt(0); _progress.Add(new BuildProgress(phase, message, DateTimeOffset.UtcNow)); }
        logger.LogInformation("Routing build [{Phase}] {Message}", phase, message);
    }

    public async Task BuildAsync(CancellationToken ct)
    {
        using var span = Activity.StartActivity("routing.build");
        lock (_progress) _progress.Clear();
        var o = Options;
        var regionEnv = new Envelope(o.Region.MinLng, o.Region.MaxLng, o.Region.MinLat, o.Region.MaxLat);
        var region = Geo.Factory.ToGeometry(regionEnv);
        var sw = Stopwatch.StartNew();
        var shp = await EnsureLandPolygonsAsync(o, ct);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        var version = Convert.ToInt32(await new NpgsqlCommand("SELECT COALESCE(MAX(graph_version),0)+1 FROM routing_land", conn) { CommandTimeout = 3600 }.ExecuteScalarAsync(ct));

        // 1. land polygons inside the region (streamed, never all in memory) + active Block corrections → routing_land
        var blocks = await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Block).Select(c => c.Geometry).ToListAsync(ct); // before the COPY: same connection
        // the land step (shapefile, lakes cut out, blocks) is the same between builds unless its inputs changed: its rows are
        // copied from the previous version when the key (shapefile, region, lake and block geometries) matches
        var shpInfo = new FileInfo(shp);
        var correctionsHash = await new NpgsqlCommand("SELECT COALESCE(md5(string_agg(md5(ST_AsBinary(\"Geometry\")), ',' ORDER BY \"Id\")), '') FROM \"RoutingCorrections\" WHERE \"Active\" AND \"Kind\" IN ('Water', 'Block')", conn) { CommandTimeout = 3600 }.ExecuteScalarAsync(ct) as string ?? "";
        var landKey = $"{shpInfo.Length}-{shpInfo.LastWriteTimeUtc.Ticks}|{regionEnv}|{correctionsHash}";
        var cachedVersion = await new NpgsqlCommand("SELECT (SELECT value FROM routing_meta WHERE key = 'land_version')::int WHERE (SELECT value FROM routing_meta WHERE key = 'land_key') = @k", conn) { Parameters = { new("k", landKey) } }.ExecuteScalarAsync(ct);
        var landCount = 0;
        if (cachedVersion is int lv)
        {
            Report("land", $"Land polygons unchanged since graph version {lv}: reusing them for version {version}…");
            await using var copy = new NpgsqlCommand("INSERT INTO routing_land (graph_version, geom) SELECT @v, geom FROM routing_land WHERE graph_version = @lv", conn) { CommandTimeout = 3600 };
            copy.Parameters.AddWithValue("v", version); copy.Parameters.AddWithValue("lv", lv);
            landCount = await copy.ExecuteNonQueryAsync(ct);
        }
        else
        {
        Report("land", $"Streaming land polygons inside the region into graph version {version}…");
        // inland water bodies are land in the coastline polygons: cut them out of the land tiles they touch
        var waters = await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Water).Select(c => c.Geometry).ToListAsync(ct);
        var waterIndex = new STRtree<Geometry>(); foreach (var g in waters) waterIndex.Insert(g.EnvelopeInternal, g); waterIndex.Build();
        await using (var w = await conn.BeginBinaryImportAsync("COPY routing_land (graph_version, geom) FROM STDIN (FORMAT BINARY)", ct))
        {
            async Task Write(Geometry g) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(g, NpgsqlDbType.Geometry, ct); landCount++; }
            foreach (var g in Shapefile.ReadAllGeometries(shp))
            {
                ct.ThrowIfCancellationRequested();
                if (!g.EnvelopeInternal.Intersects(regionEnv)) continue;
                var clipped = regionEnv.Contains(g.EnvelopeInternal) ? g : g.Intersection(region);
                foreach (var water in waterIndex.Query(clipped.EnvelopeInternal))
                {
                    if (!water.Intersects(clipped)) continue;
                    try { clipped = clipped.Difference(water); } catch (TopologyException) { clipped = clipped.Buffer(0).Difference(water.Buffer(0)); }
                }
                if (!clipped.IsEmpty) await Write(clipped);
            }
            foreach (var g in blocks) await Write(g);
            await w.CompleteAsync(ct);
        }
        }
        Report("land", $"{landCount} land polygons ({sw.Elapsed:mm\\:ss})");

        // 2. harbours of the region (small) for the access table
        var harbours = await db.Harbours.Where(h => h.IsActive && h.MergedIntoId == null && h.Location.Intersects(region)).Select(h => new { h.Id, h.Location }).ToListAsync(ct);
        var harbourIndex = new STRtree<(Guid Id, double X, double Y)>();
        foreach (var h in harbours) harbourIndex.Insert(new Envelope(h.Location.Coordinate), (h.Id, h.Location.X, h.Location.Y));
        harbourIndex.Build();
        var access = new List<(Guid harbourId, long nodeId, double distance)>();
        var accessDeg = o.HarbourAccessM / WaterGraphTiler.MPerDegLat;

        // 3. tile by tile: land from PostGIS (clipped), quadtree, edges, straight into COPY
        // derived fairways (buoy chains) make the water next to them expensive, spec 10 §10.5 step 3
        var fairways = (await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Passage && c.Source == CorrectionSource.Derived && c.Note != null && c.Note.StartsWith("Fahrwasser")).Select(c => c.Geometry).ToListAsync(ct)).OfType<LineString>().ToList();
        var fairwayAreas = (await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Fairway).Select(c => c.Geometry).ToListAsync(ct)).SelectMany(g => g is Polygon p ? [p] : g is MultiPolygon mp ? mp.Geometries.OfType<Polygon>() : []).ToList();
        var avoids = await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Avoid).Select(c => c.Geometry).ToListAsync(ct);
        var depthsByTile = new System.Collections.Concurrent.ConcurrentDictionary<string, List<(Geometry, double)>>();
        static string result_tile_key(Envelope e) => $"{e.MinX:0.####},{e.MinY:0.####}";
        long gridNodeCount, gridEdgeCount, nextId; List<GraphNode>? waterNodes = null;
        var tiler = new WaterGraphTiler(Geo.Factory, regionEnv, o.StartCellM, o.MinCellM, o.CoastPenaltyFactor, o.TileCells);
        var tileNo = 0;
        if (o.Mode == "areas")
        {
            (gridNodeCount, gridEdgeCount, nextId, waterNodes) = await BuildAreasAsync(conn, db, version, regionEnv, o, fairways, fairwayAreas, harbours.Select(h => h.Location.Coordinate).ToList(), sw, ct);
        }
        else
        {
        Report("grid", $"{tiler.Tiles.Count} tiles of {o.TileCells}×{o.TileCells} start cells");
        await tiler.RunAsync(
            async (tile, c) =>
            {
                var e = WaterGraphTiler.WithMargin(tile);
                await using var cmd = new NpgsqlCommand("""
                    SELECT ST_CollectionExtract(ST_Intersection(geom, ST_MakeEnvelope(@x1,@y1,@x2,@y2,4326)), 3) FROM routing_land
                    WHERE graph_version = @v AND geom && ST_MakeEnvelope(@x1,@y1,@x2,@y2,4326)
                    """, conn) { CommandTimeout = 3600 };
                cmd.Parameters.AddWithValue("v", version);
                cmd.Parameters.AddWithValue("x1", e.MinX); cmd.Parameters.AddWithValue("y1", e.MinY); cmd.Parameters.AddWithValue("x2", e.MaxX); cmd.Parameters.AddWithValue("y2", e.MaxY);
                var land = new List<Geometry>();
                await using (var r = await cmd.ExecuteReaderAsync(c))
                    while (await r.ReadAsync(c)) if (!r.IsDBNull(0)) { var g = r.GetFieldValue<Geometry>(0); if (!g.IsEmpty) land.Add(g); }
                // charted depth areas of the tile (routing_depth, filled by the admin depth import)
                await using var dcmd = new NpgsqlCommand("SELECT geom, min_depth_m FROM routing_depth WHERE geom && ST_MakeEnvelope(@x1,@y1,@x2,@y2,4326)", conn) { CommandTimeout = 3600 };
                dcmd.Parameters.AddWithValue("x1", e.MinX); dcmd.Parameters.AddWithValue("y1", e.MinY); dcmd.Parameters.AddWithValue("x2", e.MaxX); dcmd.Parameters.AddWithValue("y2", e.MaxY);
                var depths = new List<(Geometry, double)>();
                await using (var dr = await dcmd.ExecuteReaderAsync(c))
                    while (await dr.ReadAsync(c)) depths.Add((dr.GetFieldValue<Geometry>(0), dr.GetDouble(1)));
                depthsByTile[result_tile_key(e)] = depths;
                return land;
            },
            async (result, c) =>
            {
                tileNo++;
                await using (var w = await conn.BeginBinaryImportAsync("COPY routing_node (graph_version, id, geom) FROM STDIN (FORMAT BINARY)", c))
                {
                    foreach (var n in result.Nodes) { await w.StartRowAsync(c); await w.WriteAsync(version, NpgsqlDbType.Integer, c); await w.WriteAsync(n.Id, NpgsqlDbType.Bigint, c); await w.WriteAsync(Geo.Factory.CreatePoint(new Coordinate(n.X, n.Y)), NpgsqlDbType.Geometry, c); }
                    await w.CompleteAsync(c);
                }
                await WriteEdgesAsync(conn, version, DepthLimits.Apply(Avoid.Apply(ChannelPreference.Apply(result.Edges, fairways, fairwayAreas, o.ChannelPreferenceM, o.ChannelPenaltyFactor), avoids, o.AvoidPenaltyFactor), depthsByTile.TryRemove(result_tile_key(WaterGraphTiler.WithMargin(result.Tile)), out var tileDepths) ? tileDepths : []), c);
                foreach (var h in harbourIndex.Query(result.Tile.Envelope))
                    if (result.Nearest(h.X, h.Y, accessDeg) is { } near) access.Add((h.Id, near.Id, Haversine(h.Y, h.X, near.Y, near.X)));
                if (result.Nodes.Count > 0 || tileNo % 25 == 0)
                    Report("tile", $"Tile {tileNo}/{tiler.Tiles.Count}: {result.Nodes.Count} cells, {result.Edges.Count} edges – total {tiler.NodeCount} nodes, {tiler.EdgeCount} edges ({sw.Elapsed:hh\\:mm\\:ss})");
            }, ct);
        Report("harbours", $"{access.Count}/{harbours.Count} harbours reachable ({sw.Elapsed:hh\\:mm\\:ss})");
        (gridNodeCount, gridEdgeCount, nextId) = (tiler.NodeCount, tiler.EdgeCount, tiler.NextId);
        }

        // 4. passages (manual lines and imported waterways, spec 10 §10.5) as edge chains; ends hook into the nearest
        //    water node, vertices at the same coordinate are one node so canal networks connect at junctions
        var extraNodes = new List<GraphNode>(); var extraEdges = new List<GraphEdge>();
        var shared = new Dictionary<(long, long), long>();
        var linked = new HashSet<long>(); // one grid link per passage node
        var passageEndIds = new List<(long id, Coordinate p)>();
        var linkNodes = new HashSet<long>(); // passage vertices that carry a link into the water graph: junctions the contraction keeps
        long NodeAt(Coordinate p)
        {
            var key = ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6));
            if (shared.TryGetValue(key, out var existing)) return existing;
            var id = nextId++;
            shared[key] = id; extraNodes.Add(new GraphNode(id, p.X, p.Y, false));
            return id;
        }
        // the link checks below run in memory (an SQL probe per candidate took an hour); no-go blocks are not land for a link: a buoyed
        // fairway through a Wadden Sea zone-1 area must still hook into the water outside it
        var linkLand = await LandIndexAsync(conn, version, blocks, ct);
        // nearest-node lookups of this phase run against an in-memory index of the water nodes (area mode); grid mode keeps the SQL path
        var nodeIndex = waterNodes is null ? null : NodeIndex.Build(waterNodes);
        var channelEdgeFrom = new List<int>(); // index ranges of derived channel edges: they run through whatever depth the sound has
        foreach (var pc in await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Passage).Select(c => new { c.Geometry, c.Note }).ToListAsync(ct))
        {
            var passage = pc.Geometry;
            if (passage is not LineString ls) continue;
            var isChannel = pc.Note != null && pc.Note.StartsWith("Enge");
            var firstEdge = extraEdges.Count;
            long? prev = null; var sinceLink = 0.0;
            for (var i = 0; i < ls.NumPoints; i++)
            {
                var p = ls.GetCoordinateN(i);
                var id = NodeAt(p);
                if (prev is { } pv && pv != id) { var q = ls.GetCoordinateN(i - 1); var segM = Haversine(q.Y, q.X, p.Y, p.X); sinceLink += segM; extraEdges.Add(new GraphEdge(pv, id, segM * o.PassageCostFactor, Geo.Factory.CreateLineString([q, p]))); }
                // ends always hook into the water graph; inner vertices every ~1 km (derived channels have a vertex every few metres)
                var isEnd = i == 0 || i == ls.NumPoints - 1;
                if (isEnd) passageEndIds.Add((id, p));
                var linkM = isEnd ? o.HarbourAccessM : sinceLink >= 1000 ? o.PassageLinkM : 0;
                if (linkM > 0) sinceLink = 0;
                if (linkM > 0 && linked.Add(id))
                {
                    var near = await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, p, linkM, _ => true, ct);
                    if (near is { } n) { extraEdges.Add(new GraphEdge(id, n.Id, Haversine(p.Y, p.X, n.Y, n.X), Geo.Factory.CreateLineString([p, new Coordinate(n.X, n.Y)]))); linkNodes.Add(id); }
                }
                prev = id;
            }
            if (isChannel && extraEdges.Count > firstEdge) { channelEdgeFrom.Add(firstEdge); channelEdgeFrom.Add(extraEdges.Count); }
        }
        if (channelEdgeFrom.Count > 0)
        {
            var depthRows = new List<(Geometry, double)>();
            await using (var dcmd = new NpgsqlCommand("SELECT geom, min_depth_m FROM routing_depth WHERE geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)", conn) { CommandTimeout = 3600 })
            {
                dcmd.Parameters.AddWithValue("x1", regionEnv.MinX); dcmd.Parameters.AddWithValue("y1", regionEnv.MinY); dcmd.Parameters.AddWithValue("x2", regionEnv.MaxX); dcmd.Parameters.AddWithValue("y2", regionEnv.MaxY);
                await using var dr = await dcmd.ExecuteReaderAsync(ct);
                while (await dr.ReadAsync(ct)) depthRows.Add((dr.GetFieldValue<Geometry>(0), dr.GetDouble(1)));
            }
            var channelEdges = new List<GraphEdge>(); var ranges = new List<(int from, int to)>();
            for (var i = 0; i < channelEdgeFrom.Count; i += 2) { ranges.Add((channelEdgeFrom[i], channelEdgeFrom[i + 1])); channelEdges.AddRange(extraEdges.Skip(channelEdgeFrom[i]).Take(channelEdgeFrom[i + 1] - channelEdgeFrom[i])); }
            var limited = DepthLimits.Apply(channelEdges, depthRows);
            // a channel through water charted shallower than the default yacht costs ×AvoidPenaltyFactor but stays open (the depth
            // areas are coarse in the Swedish archipelago and would close every sound); a channel across a drying flat is closed
            var shallowM = o.DefaultDraftM + o.DepthSafetyMarginM;
            var k = 0;
            foreach (var (from, to) in ranges) for (var i = from; i < to; i++)
            {
                var e = limited[k++];
                extraEdges[i] = e.MinDepthM is { } d && d > 0 && d < shallowM ? e with { Cost = e.Cost * o.ShallowChannelFactor, MinDepthM = null } : e;
            }
        }
        var obstacles = Obstacles.WithSpans((await db.RoutingCorrections.Where(c => c.Active && (c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.Lock || c.Kind == CorrectionKind.OpeningBridge)).Select(c => new { c.Kind, c.Geometry, c.ClearanceM }).ToListAsync(ct))
            .Select(c => (c.Geometry is LineString ? c.Geometry : c.Geometry.Centroid, c.Kind, c.ClearanceM)).ToList());
        extraEdges = Obstacles.Apply(extraEdges, obstacles, o.LockPenaltyM, o.BridgeOpeningPenaltyM, Obstacles.MatchM);
        // a canal, river or channel is one leg from junction to junction: vertices with exactly two passage edges and no link are
        // merged away, the leg keeps the full geometry and the strictest depth and clearance (the search walked every vertex before:
        // 722 k passage edges, hundreds for the Eider alone)
        var keep = new HashSet<long>(linkNodes); foreach (var (id, _) in passageEndIds) keep.Add(id);
        // the passage vertex nearest to a harbour stays a junction, so the harbour can hook into the canal or river it lies on
        // (Schülperneuensiel on the Eider lost its access when the river became one leg from Tönning to the Gieselau canal)
        var vertexIndex = new STRtree<GraphNode>();
        foreach (var n in extraNodes) vertexIndex.Insert(new Envelope(n.X, n.X, n.Y, n.Y), n);
        vertexIndex.Build();
        foreach (var h in harbours)
        {
            var hc = h.Location.Coordinate; var cos = Math.Cos(hc.Y * Math.PI / 180);
            var env = new Envelope(hc.X - accessDeg / cos, hc.X + accessDeg / cos, hc.Y - accessDeg, hc.Y + accessDeg);
            var nearest = vertexIndex.Query(env).Select(n => (n, d: Haversine(hc.Y, hc.X, n.Y, n.X))).Where(x => x.d <= o.HarbourAccessM).OrderBy(x => x.d).FirstOrDefault();
            if (nearest.n is not null) keep.Add(nearest.n.Id);
        }
        var before = extraEdges.Count;
        extraEdges = PassageContraction.Contract(extraEdges, keep, nodeId => nodeId <= gridNodeCount);
        var extraNodeIds = extraEdges.SelectMany(e => new[] { e.Source, e.Target }).ToHashSet();
        extraNodes = extraNodes.Where(n => extraNodeIds.Contains(n.Id)).ToList();
        Report("passages", $"{before} passage edges contracted to {extraEdges.Count}, {extraNodes.Count} nodes kept ({sw.Elapsed:hh\\:mm\\:ss})");
        await using (var w = await conn.BeginBinaryImportAsync("COPY routing_node (graph_version, id, geom) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var n in extraNodes) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(n.Id, NpgsqlDbType.Bigint, ct); await w.WriteAsync(Geo.Factory.CreatePoint(new Coordinate(n.X, n.Y)), NpgsqlDbType.Geometry, ct); }
            await w.CompleteAsync(ct);
        }
        await WriteEdgesAsync(conn, version, extraEdges, ct);
        if (nodeIndex is not null) nodeIndex = NodeIndex.Build(waterNodes!.Concat(extraNodes).ToList());
        // inland harbours: no water cell nearby (rivers/canals count as land) but maybe a passage node; in area mode every harbour is linked here
        if (extraNodes.Count > 0 || o.Mode == "areas")
        {
            // the nearest node is not always a connected one: a channel skeleton in an enclosed basin, an anchor without a visible
            // neighbour. Components over everything written so far; stray passage ends get a link into the main component, and a
            // harbour is attached to the nearest node that belongs to it (or to a component that just got linked)
            // skeleton gaps: two channel pieces that end a few metres apart (merger junctions, chunk borders, the mouth of a sound)
            // are joined when the gap is short and land-free – both may already belong to the main component and still force a
            // detour of miles around an island (Dalarö: 6.5 nm around instead of 1 km through the sound)
            var gapLinks = new List<GraphEdge>(); var gapSeen = new HashSet<(long, long)>();
            // nodes already joined to an end by a passage edge are not gap candidates (half a million links to the own line otherwise)
            var adjacent = new Dictionary<long, HashSet<long>>();
            foreach (var e in extraEdges) { (adjacent.TryGetValue(e.Source, out var sa) ? sa : adjacent[e.Source] = []).Add(e.Target); (adjacent.TryGetValue(e.Target, out var ta) ? ta : adjacent[e.Target] = []).Add(e.Source); }
            foreach (var (id, p) in passageEndIds)
            {
                var own = adjacent.GetValueOrDefault(id);
                var n = await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, p, 200, m => m.Id != id && (own is null || !own.Contains(m.Id)) && !gapSeen.Contains((Math.Min(m.Id, id), Math.Max(m.Id, id))), ct);
                if (n is null) continue;
                gapSeen.Add((Math.Min(n.Id, id), Math.Max(n.Id, id)));
                gapLinks.Add(new GraphEdge(id, n.Id, Haversine(p.Y, p.X, n.Y, n.X), Geo.Factory.CreateLineString([p, new Coordinate(n.X, n.Y)])));
            }
            await WriteEdgesAsync(conn, version, gapLinks, ct);
            // live union-find: a stray end links to the nearest node of any other component (land-free), and the two merge; chains
            // of such merges pull archipelago fragments together even where no main-component node is within reach
            var parent = await ComponentsAsync(conn, version, ct);
            long Find(long x) { if (!parent.TryGetValue(x, out var px)) { parent[x] = x; return x; } while (px != x) { var gp = parent[px]; parent[x] = gp; x = px; px = gp; } return x; }
            void Union(long x, long y) { var rx = Find(x); var ry = Find(y); if (rx != ry) parent[rx] = ry; }
            var sizes = parent.Keys.ToList().GroupBy(Find).ToDictionary(g => g.Key, g => g.Count());
            var giant = sizes.OrderByDescending(kv => kv.Value).First().Key;
            var relinks = new List<GraphEdge>(); var strays = 0;
            foreach (var (id, p) in passageEndIds)
            {
                if (Find(id) == Find(giant)) continue;
                strays++;
                var n = await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, p, o.HarbourAccessM, m => Find(m.Id) != Find(id), ct);
                if (n is null) continue;
                relinks.Add(new GraphEdge(id, n.Id, Haversine(p.Y, p.X, n.Y, n.X), Geo.Factory.CreateLineString([p, new Coordinate(n.X, n.Y)])));
                Union(id, n.Id);
            }
            await WriteEdgesAsync(conn, version, relinks, ct);
            var giantRoot = Find(giant);
            var reached = access.Select(a => a.harbourId).ToHashSet();
            foreach (var h in harbours.Where(h => !reached.Contains(h.Id)))
            {
                // the access link may not cross land: a harbour on a river or lake the graph does not know stays unreachable rather than
                // being routed across a field (the audit found 35 such routes); a node of the main component is preferred, any other
                // connected node is taken when none is within reach
                var n = await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, h.Location.Coordinate, o.HarbourAccessM, m => Find(m.Id) == giantRoot, ct)
                    ?? await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, h.Location.Coordinate, o.HarbourAccessM, _ => true, ct);
                if (n is not null) access.Add((h.Id, n.Id, Haversine(h.Location.Y, h.Location.X, n.Y, n.X)));
            }
            Report("passages", $"{extraNodes.Count} passage nodes, {extraEdges.Count} edges; {gapLinks.Count} gap links, {strays} stray passage ends, {relinks.Count} relinked; {access.Count}/{harbours.Count} harbours reachable ({sw.Elapsed:hh\\:mm\\:ss})");
        }
        await using (var w = await conn.BeginBinaryImportAsync("COPY routing_harbour_access (graph_version, harbour_id, node_id, distance_m) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var a in access) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(a.harbourId, NpgsqlDbType.Uuid, ct); await w.WriteAsync(a.nodeId, NpgsqlDbType.Bigint, ct); await w.WriteAsync(a.distance, NpgsqlDbType.Double, ct); }
            await w.CompleteAsync(ct);
        }

        // 5. switch atomically and drop older versions
        var nodes = gridNodeCount + extraNodes.Count; var edges = gridEdgeCount + extraEdges.Count;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            var sql = $"""
                INSERT INTO routing_meta (key, value) VALUES ('land_key', '{landKey.Replace("'", "''")}'), ('land_version', '{version}') ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
                INSERT INTO routing_meta (key, value) VALUES ('active_version', '{version}'), ('built_at', '{DateTimeOffset.UtcNow:O}'),
                  ('region', '{string.Join(',', new[] { o.Region.MinLng, o.Region.MinLat, o.Region.MaxLng, o.Region.MaxLat }.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)))}'), ('nodes', '{nodes}'), ('edges', '{edges}'), ('grid_nodes', '{gridNodeCount}'), ('mode', '{o.Mode}')
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
                DELETE FROM routing_edge WHERE graph_version < {version};
                DELETE FROM routing_node WHERE graph_version < {version};
                DELETE FROM routing_land WHERE graph_version < {version};
                DELETE FROM routing_harbour_access WHERE graph_version < {version};
                DELETE FROM "RouteCaches" WHERE "GraphVersion" < {version};
                """;
            await new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 3600 }.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        await new NpgsqlCommand("ANALYZE routing_edge; ANALYZE routing_node; ANALYZE routing_land;", conn) { CommandTimeout = 3600 }.ExecuteNonQueryAsync(ct);
        Report("done", $"Graph version {version} active: {nodes} nodes, {edges} edges, {access.Count} harbours ({sw.Elapsed:hh\\:mm\\:ss})");
    }

    private static async Task WriteEdgesAsync(NpgsqlConnection conn, int version, IReadOnlyList<GraphEdge> edges, CancellationToken ct)
    {
        if (edges.Count == 0) return;
        await using var w = await conn.BeginBinaryImportAsync("COPY routing_edge (graph_version, source, target, cost, reverse_cost, x1, y1, x2, y2, geom, clearance_m, min_depth_m) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var e in edges)
        {
            await w.StartRowAsync(ct);
            await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(e.Source, NpgsqlDbType.Bigint, ct); await w.WriteAsync(e.Target, NpgsqlDbType.Bigint, ct);
            await w.WriteAsync(e.Cost, NpgsqlDbType.Double, ct); await w.WriteAsync(e.Cost, NpgsqlDbType.Double, ct);
            await w.WriteAsync(e.Geom.StartPoint.X, NpgsqlDbType.Double, ct); await w.WriteAsync(e.Geom.StartPoint.Y, NpgsqlDbType.Double, ct);
            await w.WriteAsync(e.Geom.EndPoint.X, NpgsqlDbType.Double, ct); await w.WriteAsync(e.Geom.EndPoint.Y, NpgsqlDbType.Double, ct);
            await w.WriteAsync(e.Geom, NpgsqlDbType.Geometry, ct);
            if (e.ClearanceM is { } cl) await w.WriteAsync(cl, NpgsqlDbType.Double, ct); else await w.WriteNullAsync(ct);
            if (e.MinDepthM is { } md) await w.WriteAsync(md, NpgsqlDbType.Double, ct); else await w.WriteNullAsync(ct);
        }
        await w.CompleteAsync(ct);
    }

    /// <summary>Nearest node within maxM: bounding box in degrees (uses the GiST index; a geography cast would not), exact distance checked afterwards.</summary>
    /// <summary>
    /// Area mode (VisibilityGraph): buffered, simplified land from routing_land → corner nodes + open-sea lattice + anchors for
    /// harbours and passage ends + corners of shallow banks → visibility edges, decorated like grid edges (channel preference, depth).
    /// </summary>
    private async Task<(long nodes, long edges, long nextId, List<GraphNode> waterNodes)> BuildAreasAsync(NpgsqlConnection conn, SmsDbContext db, int version, Envelope region, RoutingOptions o,
        IReadOnlyList<LineString> fairways, IReadOnlyList<Polygon> fairwayAreas, IReadOnlyList<Coordinate> harbourPoints, Stopwatch sw, CancellationToken ct)
    {
        // 1. obstacles: land buffered by the coast distance, unioned and simplified, in 2° chunks (polygons on a chunk border appear twice – harmless for tests, corners are deduplicated)
        var polygons = await ObstaclePolygonsAsync(conn, version, region, o, ct);
        // the same land with a small buffer: short legs through a constriction the coast buffer closes are tested against it
        var finePolygons = o.NarrowLegM > 0 ? await ObstaclePolygonsAsync(conn, version, region, o, ct, o.NarrowBufferM) : null;
        var tol = o.SimplifyM / WaterGraphTiler.MPerDegLat;
        var obstacles = VisibilityGraph.Index(polygons);
        Report("areas", $"{polygons.Count} obstacle polygons ({sw.Elapsed:hh\\:mm\\:ss})");

        // 2. nodes
        var vo = new VisibilityGraph.Options(o.LatticeM, o.LatticeClearM, o.MaxVisibilityM, o.VisibleNeighbours, o.NarrowLegM, o.NarrowLegFactor);
        var corners = VisibilityGraph.Corners(polygons, region);
        var lattice = VisibilityGraph.Lattice(region, obstacles, vo);
        var passageEnds = (await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Passage).Select(c => c.Geometry).ToListAsync(ct)).OfType<LineString>()
            .SelectMany(l => new[] { l.StartPoint.Coordinate, l.EndPoint.Coordinate }).ToList();
        // a visibility leg through a sound passes under its bridge: clearance, lock and opening costs apply here as on passages;
        // a long bridge with charted spans is passable at the spans only, so the span points become anchors (legs through them)
        var bridgesRaw = (await db.RoutingCorrections.Where(c => c.Active && (c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.Lock || c.Kind == CorrectionKind.OpeningBridge)).Select(c => new { c.Kind, c.Geometry, c.ClearanceM }).ToListAsync(ct))
            .Select(c => (c.Geometry is LineString ? c.Geometry : c.Geometry.Centroid, c.Kind, c.ClearanceM)).ToList();
        var bridges = Obstacles.WithSpans(bridgesRaw);
        var anchors = harbourPoints.Concat(passageEnds).Concat(Obstacles.SpanPoints(bridgesRaw)).Select(p => VisibilityGraph.Anchor(p, obstacles, polygons)).ToList();
        var shallowTol = tol * 2;
        await using var shallowCmd = new NpgsqlCommand("SELECT ST_SimplifyPreserveTopology(geom, @tol) FROM routing_depth WHERE max_depth_m <= @d AND ST_Area(geom) >= @minArea AND geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)", conn) { CommandTimeout = 3600 }; // planar deg²: geography area fails on the invalid Seascape rings
        var midCos = Math.Cos((region.MinY + region.MaxY) / 2 * Math.PI / 180);
        shallowCmd.Parameters.AddWithValue("tol", shallowTol * 2.5); shallowCmd.Parameters.AddWithValue("minArea", o.ShallowNodeMinKm2 * 1e6 / (WaterGraphTiler.MPerDegLat * WaterGraphTiler.MPerDegLat * midCos)); shallowCmd.Parameters.AddWithValue("d", o.ShallowNodeDepthM);
        shallowCmd.Parameters.AddWithValue("x1", region.MinX); shallowCmd.Parameters.AddWithValue("y1", region.MinY); shallowCmd.Parameters.AddWithValue("x2", region.MaxX); shallowCmd.Parameters.AddWithValue("y2", region.MaxY);
        var banks = new List<Polygon>();
        await using (var r = await shallowCmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) if (!r.IsDBNull(0) && r.GetFieldValue<Geometry>(0) is Polygon bp && !bp.IsEmpty) banks.Add(bp);
        var bankCorners = VisibilityGraph.Corners(banks, region).Where(c => !VisibilityGraph.OnTileEdge(c)).ToList(); // the depth areas are cut per z12 tile: corners on tile edges are artefacts
        var seen = new HashSet<(long, long)>();
        var nodes = new List<GraphNode>();
        // nodes closer than ~25 m are one node: a cluster of near-coincident anchors (hundreds of channel ends at one mouth) would
        // otherwise fill each other's nearest-neighbour slots and form a component of its own
        var gridDeg = 25.0 / WaterGraphTiler.MPerDegLat;
        foreach (var c in corners.Concat(lattice).Concat(anchors).Concat(bankCorners))
        {
            if (!seen.Add(((long)Math.Round(c.X * midCos / gridDeg), (long)Math.Round(c.Y / gridDeg)))) continue;
            if (obstacles.Query(new Envelope(c)).Any(ob => ob.ContainsProperly(Geo.Factory.CreatePoint(c)))) continue; // bank corners on land
            nodes.Add(new GraphNode(nodes.Count + 1, c.X, c.Y, false));
        }
        Report("areas", $"{corners.Count} coast corners, {lattice.Count} lattice points, {anchors.Count} anchors, {bankCorners.Count} bank corners → {nodes.Count} nodes ({sw.Elapsed:hh\\:mm\\:ss})");

        // 3. visibility edges, then the same decorations as grid edges
        var edges = VisibilityGraph.Edges(nodes, polygons, finePolygons, vo);
        Report("areas", $"{edges.Count} visibility edges ({sw.Elapsed:hh\\:mm\\:ss})");
        // safety net against the raw (unbuffered) land: no visibility leg may cross it, whatever the obstacle union did
        var land = new List<Polygon>();
        await using (var lcmd = new NpgsqlCommand("SELECT geom FROM routing_land WHERE graph_version = @v", conn) { CommandTimeout = 3600 })
        {
            lcmd.Parameters.AddWithValue("v", version);
            await using var lr = await lcmd.ExecuteReaderAsync(ct);
            while (await lr.ReadAsync(ct)) { var g = lr.GetFieldValue<Geometry>(0); land.AddRange(g is Polygon lp ? [lp] : g is MultiPolygon lmp ? lmp.Geometries.OfType<Polygon>() : []); }
        }
        var landIndex = VisibilityGraph.Index(land);
        // prepared Intersects is the accelerated predicate (Crosses is not); legs start 150 m off the raw land, so touching it is crossing it
        var crossing = edges.AsParallel().Where(e => landIndex.Query(e.Geom.EnvelopeInternal).Any(l => l.Intersects(e.Geom))).ToList();
        Report("areas", $"land check done ({sw.Elapsed:hh\\:mm\\:ss})");
        if (crossing.Count > 0)
        {
            var sample = crossing[0].Geom.Centroid;
            var covered = obstacles.Query(new Envelope(sample.Coordinate)).Any(ob => ob.Contains(sample));
            Report("areas", $"{crossing.Count} visibility edges crossed land and were dropped (sample {sample.X:0.####},{sample.Y:0.####} inside obstacle union: {covered})");
            var bad = crossing.ToHashSet();
            edges = edges.Where(e => !bad.Contains(e)).ToList();
        }
        var depths = new List<(Geometry, double)>();
        await using (var dcmd = new NpgsqlCommand("SELECT geom, min_depth_m FROM routing_depth WHERE geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)", conn) { CommandTimeout = 3600 })
        {
            dcmd.Parameters.AddWithValue("x1", region.MinX); dcmd.Parameters.AddWithValue("y1", region.MinY); dcmd.Parameters.AddWithValue("x2", region.MaxX); dcmd.Parameters.AddWithValue("y2", region.MaxY);
            await using var dr = await dcmd.ExecuteReaderAsync(ct);
            while (await dr.ReadAsync(ct)) depths.Add((dr.GetFieldValue<Geometry>(0), dr.GetDouble(1)));
        }
        var avoids = await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Avoid).Select(c => c.Geometry).ToListAsync(ct);
        edges = ChannelPreference.Apply(edges, fairways, fairwayAreas, o.ChannelPreferenceM, o.ChannelPenaltyFactor);
        Report("areas", $"channel preference applied ({sw.Elapsed:hh\\:mm\\:ss})");
        edges = DepthLimits.Apply(Avoid.Apply(edges, avoids, o.AvoidPenaltyFactor), depths);
        Report("areas", $"depth limits applied ({sw.Elapsed:hh\\:mm\\:ss})");
        edges = Obstacles.Apply(edges, bridges, o.LockPenaltyM, o.BridgeOpeningPenaltyM, o.BridgeMatchAreaM); // a bridge over a sound is a point, the leg may pass a few hundred metres from it
        Report("areas", $"bridges and locks applied, writing {nodes.Count} nodes and {edges.Count} edges ({sw.Elapsed:hh\\:mm\\:ss})");
        await using (var w = await conn.BeginBinaryImportAsync("COPY routing_node (graph_version, id, geom) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var n in nodes) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(n.Id, NpgsqlDbType.Bigint, ct); await w.WriteAsync(Geo.Factory.CreatePoint(new Coordinate(n.X, n.Y)), NpgsqlDbType.Geometry, ct); }
            await w.CompleteAsync(ct);
        }
        await WriteEdgesAsync(conn, version, edges, ct);
        return (nodes.Count, edges.Count, nodes.Count + 1, nodes);
    }

    /// <summary>The obstacle polygons of the area build (land buffered by the coast distance, unioned and simplified per 2° chunk,
    /// small enclosed pockets filled) for the chunks that touch <paramref name="region"/>. Public for the admin diagnostics.</summary>
    public static async Task<List<Polygon>> ObstaclePolygonsAsync(NpgsqlConnection conn, int version, Envelope region, RoutingOptions o, CancellationToken ct, double? bufferM = null)
    {
        var polygons = new List<Polygon>();
        var tol = o.SimplifyM / WaterGraphTiler.MPerDegLat;
        var x0 = o.Region.MinLng + Math.Floor((region.MinX - o.Region.MinLng) / 2) * 2; var y0 = o.Region.MinLat + Math.Floor((region.MinY - o.Region.MinLat) / 2) * 2;
        for (var x = x0; x < region.MaxX; x += 2)
            for (var y = y0; y < region.MaxY; y += 2)
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT ST_SimplifyPreserveTopology(ST_UnaryUnion(ST_Collect(ST_Buffer(geom::geography, @buf)::geometry)), @tol)
                    FROM routing_land WHERE graph_version = @v AND geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)
                    """, conn) { CommandTimeout = 3600 };
                cmd.Parameters.AddWithValue("v", version); cmd.Parameters.AddWithValue("buf", bufferM ?? o.CoastBufferM); cmd.Parameters.AddWithValue("tol", tol);
                cmd.Parameters.AddWithValue("x1", x); cmd.Parameters.AddWithValue("y1", y); cmd.Parameters.AddWithValue("x2", Math.Min(x + 2, o.Region.MaxLng)); cmd.Parameters.AddWithValue("y2", Math.Min(y + 2, o.Region.MaxLat));
                var g = await cmd.ExecuteScalarAsync(ct) as Geometry;
                if (g is null || g.IsEmpty) continue;
                // pockets of water enclosed by the buffered coast (marina basins behind a narrow entrance) are filled: a node inside
                // would be unreachable; such harbours get their anchor on the outer coast and reach it across the mole, as before
                polygons.AddRange((g is Polygon p ? [p] : g is MultiPolygon mp ? mp.Geometries.OfType<Polygon>() : []).Select(poly => VisibilityGraph.FillSmallHoles(poly, o.FillHoleKm2)));
            }
        return polygons;
    }

    /// <summary>Connected components of the graph written so far (union-find over routing_edge): node id → component root.</summary>
    private static async Task<Dictionary<long, long>> ComponentsAsync(NpgsqlConnection conn, int version, CancellationToken ct)
    {
        var parent = new Dictionary<long, long>();
        long Find(long x)
        {
            if (!parent.TryGetValue(x, out var p)) { parent[x] = x; return x; }
            while (p != x) { var gp = parent[p]; parent[x] = gp; x = p; p = gp; }
            return x;
        }
        await using var cmd = new NpgsqlCommand("SELECT source, target FROM routing_edge WHERE graph_version = @v", conn) { CommandTimeout = 3600 };
        cmd.Parameters.AddWithValue("v", version);
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) { var a = Find(r.GetInt64(0)); var b = Find(r.GetInt64(1)); if (a != b) parent[a] = b; }
        return parent.Keys.ToList().ToDictionary(k => k, Find);
    }

    /// <summary>The nearest acceptable node whose link from <paramref name="p"/> does not cross land. The first 150 m at the start are
    /// ignored (a harbour point sits on its pier, a channel end on the shore).</summary>
    private static async Task<GraphNode?> NearestWaterLinkAsync(NpgsqlConnection conn, int version, NodeIndex? index, STRtree<(IPreparedGeometry prepared, Geometry geom)> landIndex, Coordinate p, double maxM, Func<GraphNode, bool> accept, CancellationToken ct, long maxId = long.MaxValue)
    {
        var candidates = index is not null ? index.Nearest(p, maxM, 40, maxId) : await NearestNodesAsync(conn, version, p.X, p.Y, maxM, 40, ct, maxId);
        foreach (var n in candidates)
        {
            if (!accept(n)) continue;
            var lenM = Haversine(p.Y, p.X, n.Y, n.X);
            if (lenM < 300) return n;
            var seg = Geo.Factory.CreateLineString([p, new Coordinate(n.X, n.Y)]);
            // a harbour point sits on its pier and a channel end on the shore: crossing a mole or pier is fine, crossing a field is not.
            // Sampled every 25 m with the prepared point-in-polygon test (an exact overlay against a mainland tile took minutes per node)
            var hits = landIndex.Query(seg.EnvelopeInternal).Where(l => l.prepared.Intersects(seg)).Select(l => l.prepared).ToList();
            if (hits.Count == 0) return n;
            // Two shapes of land crossing are fine: up to 150 m anywhere (a mole), or a leading stretch of up to 1.5 km (the harbour's
            // own basin or creek, which the coastline polygons count as land, plus its mole or dike). Land after water on the way is a field.
            var steps = (int)Math.Ceiling(lenM / 25); var onLand = 0; var leading = 0; var water = false;
            for (var k = 1; k < steps; k++)
            {
                var q = Geo.Factory.CreatePoint(new Coordinate(p.X + (n.X - p.X) * k / steps, p.Y + (n.Y - p.Y) * k / steps));
                if (hits.Any(l => l.Contains(q))) { onLand++; if (!water) leading++; } else water = true;
                if (onLand > 6 && onLand > leading) break; // scattered land beyond 150 m: no need to look further
            }
            if (onLand <= 6 || (onLand == leading && leading * 25 <= 1500)) return n; // a Siel harbour sits behind its dike, 1 km from the river (Schülperneuensiel on the Eider)
        }
        return null;
    }

    /// <summary>The raw land of this graph version (coast tiles with lakes cut out, no-go blocks) as a prepared index for the link checks.</summary>
    private static async Task<STRtree<(IPreparedGeometry prepared, Geometry geom)>> LandIndexAsync(NpgsqlConnection conn, int version, IReadOnlyList<Geometry> blocks, CancellationToken ct)
    {
        var index = new STRtree<(IPreparedGeometry, Geometry)>();
        var blockEnvelopes = blocks.Select(b => b.EnvelopeInternal).ToHashSet();
        await using var cmd = new NpgsqlCommand("SELECT geom FROM routing_land WHERE graph_version = @v", conn) { CommandTimeout = 3600 };
        cmd.Parameters.AddWithValue("v", version);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var g = r.GetFieldValue<Geometry>(0);
            if (blockEnvelopes.Contains(g.EnvelopeInternal)) continue; // a Block correction written into routing_land
            foreach (var poly in g is Polygon lp ? [lp] : g is MultiPolygon lmp ? lmp.Geometries.OfType<Polygon>() : []) index.Insert(poly.EnvelopeInternal, (PreparedGeometryFactory.Prepare(poly), poly));
        }
        index.Build();
        return index;
    }

    /// <param name="maxId">Only nodes up to this id: the water-graph nodes come first, so a stray channel end can look for a water node past the hundreds of vertices of its own skeleton.</param>
    private static async Task<List<GraphNode>> NearestNodesAsync(NpgsqlConnection conn, int version, double x, double y, double maxM, int limit, CancellationToken ct, long maxId = long.MaxValue)
    {
        var dLat = maxM / WaterGraphTiler.MPerDegLat; var dLng = dLat / Math.Max(0.1, Math.Cos(y * Math.PI / 180));
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, ST_X(geom), ST_Y(geom) FROM routing_node
            WHERE graph_version = @v AND id <= @maxId AND geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)
            ORDER BY geom <-> ST_SetSRID(ST_MakePoint(@x, @y), 4326) LIMIT {limit}
            """, conn) { CommandTimeout = 3600 };
        cmd.Parameters.AddWithValue("v", version); cmd.Parameters.AddWithValue("x", x); cmd.Parameters.AddWithValue("y", y); cmd.Parameters.AddWithValue("maxId", maxId);
        cmd.Parameters.AddWithValue("x1", x - dLng); cmd.Parameters.AddWithValue("y1", y - dLat); cmd.Parameters.AddWithValue("x2", x + dLng); cmd.Parameters.AddWithValue("y2", y + dLat);
        var result = new List<GraphNode>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) { var n = new GraphNode(r.GetInt64(0), r.GetDouble(1), r.GetDouble(2), false); if (Haversine(y, x, n.Y, n.X) <= maxM) result.Add(n); }
        return result;
    }

    private static async Task<GraphNode?> NearestNodeAsync(NpgsqlConnection conn, int version, double x, double y, double maxM, CancellationToken ct)
    {
        var dLat = maxM / WaterGraphTiler.MPerDegLat; var dLng = dLat / Math.Max(0.1, Math.Cos(y * Math.PI / 180));
        await using var cmd = new NpgsqlCommand("""
            SELECT id, ST_X(geom), ST_Y(geom) FROM routing_node
            WHERE graph_version = @v AND geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)
            ORDER BY geom <-> ST_SetSRID(ST_MakePoint(@x, @y), 4326) LIMIT 1
            """, conn) { CommandTimeout = 3600 };
        cmd.Parameters.AddWithValue("v", version); cmd.Parameters.AddWithValue("x", x); cmd.Parameters.AddWithValue("y", y);
        cmd.Parameters.AddWithValue("x1", x - dLng); cmd.Parameters.AddWithValue("y1", y - dLat); cmd.Parameters.AddWithValue("x2", x + dLng); cmd.Parameters.AddWithValue("y2", y + dLat);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        var n = new GraphNode(r.GetInt64(0), r.GetDouble(1), r.GetDouble(2), false);
        return Haversine(y, x, n.Y, n.X) <= maxM ? n : null;
    }

    private async Task<string> EnsureLandPolygonsAsync(RoutingOptions o, CancellationToken ct)
    {
        var dir = Path.GetFullPath(Path.Combine(o.CachePath, "land-polygons"));
        Directory.CreateDirectory(dir);
        var existing = Directory.GetFiles(dir, "*.shp", SearchOption.AllDirectories).FirstOrDefault();
        if (existing is not null) { Report("land", $"Using cached land polygons {existing}"); return existing; }
        var zip = Path.Combine(dir, "land-polygons.zip");
        Report("land", $"Downloading {o.LandPolygonsUrl} (≈900 MB, once)…");
        var client = http.CreateClient("routing");
        client.Timeout = TimeSpan.FromMinutes(60);
        await using (var src = await client.GetStreamAsync(o.LandPolygonsUrl, ct))
        await using (var dst = File.Create(zip))
            await src.CopyToAsync(dst, ct);
        Report("land", "Extracting…");
        ZipFile.ExtractToDirectory(zip, dir, overwriteFiles: true);
        File.Delete(zip);
        return Directory.GetFiles(dir, "*.shp", SearchOption.AllDirectories).First();
    }

    public static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6_371_008.8;
        var dLat = (lat2 - lat1) * Math.PI / 180; var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }
}
