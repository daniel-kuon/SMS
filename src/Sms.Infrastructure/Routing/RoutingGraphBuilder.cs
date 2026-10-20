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
    /// <summary>Smoothing: a passage stretch (canal, buoy chain, derived channel) may be joined late / left early by up to this distance along it where the shortcut is navigable; its shape beyond is kept.</summary>
    public double PassageTrimM { get; set; } = 3000;
    public double CoastPenaltyFactor { get; set; } = 0.2;
    /// <summary>Cost multiplier for passage edges (canals, derived fairways): below 1 routes prefer marked channels. Reported distances come from the geometry and are unaffected.</summary>
    public double PassageCostFactor { get; set; } = 1.0; // below 1 makes routes ride cheap passages to their ends (detours); channel preference does the steering
    /// <summary>Extra cost (metres) for passing a lock, roughly the time a mile takes.</summary>
    public double LockPenaltyM { get; set; } = 1852;
    /// <summary>Extra cost (metres) for an opening bridge: waiting for the next opening, about half a lock.</summary>
    public double BridgeOpeningPenaltyM { get; set; } = 926;
    /// <summary>A ship (or request) with a draft but no minimum depth of its own routes with minimum depth = draft + this (DepthBands.MinDepth).</summary>
    public double DepthSafetyMarginM { get; set; } = 0.5;
    /// <summary>Open water within this distance of a derived fairway counts as unmarked/shallow: grid edges there cost ×ChannelPenaltyFactor (0 = off).</summary>
    public double ChannelPreferenceM { get; set; } = 1500;
    /// <summary>Interior passage vertices link to a water node this close, so fairways and canals can be joined along their length (ends use HarbourAccessM).</summary>
    public double PassageLinkM { get; set; } = 600;
    public double ChannelPenaltyFactor { get; set; } = 3;
    /// <summary>Edges inside an Avoid correction (warning area, firing range) cost ×this, by the length inside. Firing ranges are
    /// active on announced days only and yachts cross them otherwise: ×3 sent Marstal→Lippe (between Todendorf and Putlos) 6.6 nm
    /// round, the logged track runs straight through; ×1.3 keeps a slight preference for going round.</summary>
    public double AvoidPenaltyFactor { get; set; } = 1.3;
    /// <summary>A derived channel through water charted shallower than the default yacht (not drying) costs ×this: the depth areas are coarse in narrow sounds.</summary>
    public double ShallowChannelFactor { get; set; } = 1.5;
    /// <summary>A leg up to this length that the coast buffer closes is still added when it clears the land buffered by NarrowBufferM, at ×NarrowLegFactor (sound entrances, harbour mouths narrower than 2 × CoastBufferM). 0 = off.</summary>
    public double NarrowLegM { get; set; } = 1000;
    public double NarrowBufferM { get; set; } = 40;
    public double NarrowLegFactor { get; set; } = 1.5;
    public double MaxRouteNm { get; set; } = 500;
    /// <summary>Smoothing: a pulled or tightened leg keeps this far off the raw coastline, except along the graph path's own corridor
    /// (narrow sounds, channels, passages, harbour approaches – where the path itself runs closer) and near the route ends (0 = off).
    /// The visibility graph keeps CoastBufferM; without it the pull cut corners to a few metres off the beach (Skjoldnæs).</summary>
    public double CoastClearanceM { get; set; } = 100;
    /// <summary>Assumed passages (ferry lines and buoy gaps across the coastline land, notes "Annahme…"): routes may use them and then
    /// carry a warning. Off closes them at query time (the graph keeps them, no rebuild needed).</summary>
    public bool AssumedPassages { get; set; } = true;
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
    /// <summary>Air draft assumed when a route is asked for without one (a typical cruising yacht's mast): fixed bridges with a lower
    /// charted clearance are closed unless no other route exists, and every fixed bridge with a known clearance on the route is
    /// named in a warning. 0 = off (bridges without an air draft are passed silently, the rule before).</summary>
    public double DefaultAirDraftM { get; set; } = 15;
    /// <summary>"Tonnen first": edges in water whose band floor lies below the ship's minimum depth (the band's middle may pass, the
    /// floor does not: not definitely deep enough) cost ×this outside buoyed channels, so the buoyed channel wins wherever one leads. 1 = off.</summary>
    public double UncertainDepthFactor { get; set; } = 3;

    public sealed class RegionBox { public double MinLng { get; set; } = 7.6; public double MinLat { get; set; } = 53.3; public double MaxLng { get; set; } = 12.6; public double MaxLat { get; set; } = 55.4; }
}

public sealed record BuildProgress(string Phase, string Message, DateTimeOffset At);

/// <summary>Where a running build stands: the current phase with its done/total counter, percent of the expected build time and the
/// estimated remaining seconds (from the phase durations of the previous build, the current phase extrapolated from its counter).</summary>
public sealed record BuildEstimate(string Phase, long PhaseDone, long PhaseTotal, double Percent, double ElapsedSeconds, double RemainingSeconds);

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

    // Progress estimate: the build phases in order with their expected seconds. The defaults come from v60 (obstacles in parallel, without
    // the cache); every completed build stores its measured phase seconds in routing_meta 'phase_seconds' for the next one.
    public static readonly (string Phase, double Seconds)[] DefaultPhaseSeconds =
        [("land", 5), ("obstacles", 500), ("nodes", 15), ("visibility", 60), ("land check", 10), ("pricing", 26), ("writing", 60), ("passages", 130), ("links", 62), ("switch", 60)];
    private Dictionary<string, double> _expected = DefaultPhaseSeconds.ToDictionary(p => p.Phase, p => p.Seconds);
    private readonly Dictionary<string, double> _measured = [];
    private readonly Stopwatch _buildClock = new(), _phaseClock = new();
    private string? _phase;
    private long _phaseDone, _phaseTotal;

    /// <summary>Enters the next phase (phases a cache made unnecessary are simply skipped) with the number of work items it has.</summary>
    private void Phase(string phase, long total = 0)
    {
        lock (_measured)
        {
            if (_phase is not null) _measured[_phase] = _phaseClock.Elapsed.TotalSeconds;
            _phase = phase; Interlocked.Exchange(ref _phaseDone, 0); Interlocked.Exchange(ref _phaseTotal, total); _phaseClock.Restart();
        }
    }

    private void Tick(long n = 1) => Interlocked.Add(ref _phaseDone, n);

    /// <summary>Null while no build runs.</summary>
    public BuildEstimate? Estimate()
    {
        if (!IsRunning) return null;
        lock (_measured)
        {
            if (_phase is null) return null;
            var order = DefaultPhaseSeconds.Select(p => p.Phase).ToList();
            var i = order.IndexOf(_phase);
            var done = Interlocked.Read(ref _phaseDone); var total = Interlocked.Read(ref _phaseTotal);
            var spent = _phaseClock.Elapsed.TotalSeconds;
            var f = total > 0 ? Math.Min(1, (double)done / total) : 0;
            // the current phase: extrapolated from its counter once a few percent are done, the previous build's duration before that
            var phaseTotalS = f >= 0.05 ? spent / f : Math.Max(_expected.GetValueOrDefault(_phase), spent);
            var remaining = Math.Max(0, phaseTotalS - spent) + order.Skip(i + 1).Sum(p => _expected.GetValueOrDefault(p));
            var elapsed = _buildClock.Elapsed.TotalSeconds;
            return new BuildEstimate(_phase, done, total, Math.Round(Math.Clamp(elapsed / Math.Max(1, elapsed + remaining) * 100, 0, 99.9), 1), Math.Round(elapsed), Math.Round(remaining));
        }
    }

    public async Task BuildAsync(CancellationToken ct)
    {
        using var span = Activity.StartActivity("routing.build");
        lock (_progress) _progress.Clear();
        lock (_measured) { _measured.Clear(); _phase = null; }
        _buildClock.Restart();
        Phase("land");
        var o = Options;
        var regionEnv = new Envelope(o.Region.MinLng, o.Region.MaxLng, o.Region.MinLat, o.Region.MaxLat);
        var region = Geo.Factory.ToGeometry(regionEnv);
        var sw = Stopwatch.StartNew();
        var shp = await EnsureLandPolygonsAsync(o, ct);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        if (await new NpgsqlCommand("SELECT value FROM routing_meta WHERE key = 'phase_seconds'", conn).ExecuteScalarAsync(ct) is string ps)
            try { foreach (var (k, v) in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(ps)!) lock (_measured) _expected[k] = v; } catch (System.Text.Json.JsonException) { }
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
        // only buoy chains: a ×3 band around the OSM waterway lines too (tried in v50 for the Schlei) covered whole firths such as the
        // Kiel Förde, and as a smoother obstacle it left zigzags and a U-turn off Laboe; the Schlei problem was missing water (water=fjord)
        // (paired lines only: a stretch of single marks in deep skerry water says nothing about shallows beside it, Fairways.Note)
        var fairways = (await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Passage && c.Source == CorrectionSource.Derived && c.Note != null && c.Note.StartsWith("Fahrwasser") && c.Note.Contains(Fairways.PairedNote)).Select(c => c.Geometry).ToListAsync(ct)).OfType<LineString>().ToList();
        var fairwayAreas = (await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Fairway).Select(c => c.Geometry).ToListAsync(ct)).SelectMany(g => g is Polygon p ? [p] : g is MultiPolygon mp ? mp.Geometries.OfType<Polygon>() : []).ToList();
        var avoids = await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Avoid).Select(c => c.Geometry).ToListAsync(ct);
        var depthsByTile = new System.Collections.Concurrent.ConcurrentDictionary<string, List<(Geometry, double)>>();
        static string result_tile_key(Envelope e) => $"{e.MinX:0.####},{e.MinY:0.####}";
        long gridNodeCount, gridEdgeCount, nextId; List<GraphNode>? waterNodes = null;
        var tiler = new WaterGraphTiler(Geo.Factory, regionEnv, o.StartCellM, o.MinCellM, o.CoastPenaltyFactor, o.TileCells);
        var tileNo = 0;
        // the link checks below run in memory (an SQL probe per candidate took an hour); no-go blocks are not land for a link: a buoyed
        // fairway through a Wadden Sea zone-1 area must still hook into the water outside it. The depth limits use it for the coastal strip
        var linkLand = await LandIndexAsync(conn, version, blocks, ct);
        var nearLand = NearLand(linkLand);
        if (o.Mode == "areas")
        {
            (gridNodeCount, gridEdgeCount, nextId, waterNodes) = await BuildAreasAsync(conn, db, version, regionEnv, o, fairways, fairwayAreas, harbours.Select(h => h.Location.Coordinate).ToList(), landKey, sw, nearLand, ct);
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
                await WriteEdgesAsync(conn, version, DepthLimits.Apply(Avoid.Apply(ChannelPreference.Apply(result.Edges, fairways, fairwayAreas, o.ChannelPreferenceM, o.ChannelPenaltyFactor), avoids, o.AvoidPenaltyFactor), depthsByTile.TryRemove(result_tile_key(WaterGraphTiler.WithMargin(result.Tile)), out var tileDepths) ? tileDepths : [], nearLand, fairwayAreas), c);
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
        var channelEnds = new HashSet<long>(); // ends of derived channels: their gap links and relinks run through the same unbuoyed water
        var linkNodes = new HashSet<long>(); // passage vertices that carry a link into the water graph: junctions the contraction keeps
        long NodeAt(Coordinate p)
        {
            var key = ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6));
            if (shared.TryGetValue(key, out var existing)) return existing;
            var id = nextId++;
            shared[key] = id; extraNodes.Add(new GraphNode(id, p.X, p.Y, false));
            return id;
        }
        // nearest-node lookups of this phase run against an in-memory index of the water nodes (area mode); grid mode keeps the SQL path
        var nodeIndex = waterNodes is null ? null : NodeIndex.Build(waterNodes);
        var channelEdgeFrom = new List<int>(); // index ranges of derived channel edges: they run through whatever depth the sound has
        var passageRows = await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Passage).Select(c => new { c.Geometry, c.Note }).ToListAsync(ct);
        var fairwayJumps = 0;
        Phase("passages", passageRows.Count);
        foreach (var pc in passageRows)
        {
            Tick();
            var passage = pc.Geometry;
            if (passage is not LineString ls) continue;
            var isChannel = pc.Note != null && pc.Note.StartsWith("Enge");
            // an assumed passage (ferry line / buoy gap across land) is a guide, not a marked channel: cost ×1, flagged for the warning
            var assumed = pc.Note != null && pc.Note.StartsWith(AssumedPassages.NotePrefix);
            var isFairway = pc.Note != null && pc.Note.StartsWith("Fahrwasser");
            var factor = assumed ? 1 : o.PassageCostFactor;
            var firstEdge = extraEdges.Count;
            long? prev = null; var sinceLink = 0.0;
            for (var i = 0; i < ls.NumPoints; i++)
            {
                var p = ls.GetCoordinateN(i);
                var id = NodeAt(p);
                if (prev is { } pv && pv != id)
                {
                    var q = ls.GetCoordinateN(i - 1); var segM = Haversine(q.Y, q.X, p.Y, p.X); sinceLink += segM;
                    // a buoy chain is chained greedily up to 2 km: a link jumping over an island (Kragerø: 1.5 km across Langøy, the
                    // route crossed it) costs FairwayJumpPenaltyM extra and stays a leg of its own (its ends are kept as junctions);
                    // usable only where nothing else leads on (an inland river the coastline counts as land: buoyed, often no other line)
                    var jump = isFairway && SeaGraph.InteriorLandM(q, p, c => OnLinkLand(linkLand, c)) > FairwayJumpLandM;
                    if (jump) { linkNodes.Add(pv); linkNodes.Add(id); fairwayJumps++; }
                    extraEdges.Add(new GraphEdge(pv, id, segM * factor + (jump ? FairwayJumpPenaltyM : 0), Geo.Factory.CreateLineString([q, p]), Assumed: assumed));
                }
                // ends always hook into the water graph; inner vertices every ~1 km (derived channels have a vertex every few metres)
                var isEnd = i == 0 || i == ls.NumPoints - 1;
                if (isEnd) passageEndIds.Add((id, p));
                if (isEnd && isChannel) channelEnds.Add(id);
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
        List<(Geometry, double)>? depthRows = null;
        if (channelEdgeFrom.Count > 0)
        {
            depthRows = [];
            await using (var dcmd = new NpgsqlCommand("SELECT geom, min_depth_m FROM routing_depth WHERE geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)", conn) { CommandTimeout = 3600 })
            {
                dcmd.Parameters.AddWithValue("x1", regionEnv.MinX); dcmd.Parameters.AddWithValue("y1", regionEnv.MinY); dcmd.Parameters.AddWithValue("x2", regionEnv.MaxX); dcmd.Parameters.AddWithValue("y2", regionEnv.MaxY);
                await using var dr = await dcmd.ExecuteReaderAsync(ct);
                while (await dr.ReadAsync(ct)) depthRows.Add((dr.GetFieldValue<Geometry>(0), dr.GetDouble(1)));
            }
            var channelEdges = new List<GraphEdge>(); var ranges = new List<(int from, int to)>();
            for (var i = 0; i < channelEdgeFrom.Count; i += 2) { ranges.Add((channelEdgeFrom[i], channelEdgeFrom[i + 1])); channelEdges.AddRange(extraEdges.Skip(channelEdgeFrom[i]).Take(channelEdgeFrom[i + 1] - channelEdgeFrom[i])); }
            var limited = DepthLimits.Apply(channelEdges, depthRows, nearLand);
            // a channel through water charted shallower than the default yacht costs ×ShallowChannelFactor but stays open (the depth
            // areas are coarse in the Swedish archipelago and would close every sound); a channel across a drying flat is closed.
            // The floor it runs through stays known as FloorM (as does the band next to a channel in the coastal strip without
            // data): an unbuoyed channel there is not definitely deep enough, a buoyed one wins (SeaGraph.Limits.MinDepthM)
            var shallowM = o.DefaultDraftM + o.DepthSafetyMarginM;
            var k = 0;
            foreach (var (from, to) in ranges) for (var i = from; i < to; i++)
            {
                var e = limited[k++];
                extraEdges[i] = e.MinDepthM is { } d && d > 0 && d < shallowM ? e with { Cost = e.Cost * o.ShallowChannelFactor, MinDepthM = null, FloorM = d }
                    : e.AdjacentDepthM is { } adj ? e with { AdjacentDepthM = null, FloorM = adj } : e;
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
        Report("passages", $"{before} passage edges contracted to {extraEdges.Count}, {extraNodes.Count} nodes kept, {fairwayJumps} fairway links across land penalized ({sw.Elapsed:hh\\:mm\\:ss})");
        await using (var w = await conn.BeginBinaryImportAsync("COPY routing_node (graph_version, id, geom) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var n in extraNodes) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(n.Id, NpgsqlDbType.Bigint, ct); await w.WriteAsync(Geo.Factory.CreatePoint(new Coordinate(n.X, n.Y)), NpgsqlDbType.Geometry, ct); }
            await w.CompleteAsync(ct);
        }
        await WriteEdgesAsync(conn, version, extraEdges, ct);
        if (nodeIndex is not null) nodeIndex = NodeIndex.Build(waterNodes!.Concat(extraNodes).ToList());
        // inland harbours: no water cell nearby (rivers/canals count as land) but maybe a passage node; in area mode every harbour is linked here
        Phase("links", 2L * passageEndIds.Count + harbours.Count);
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
                Tick();
                var own = adjacent.GetValueOrDefault(id);
                var n = await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, p, 200, m => m.Id != id && (own is null || !own.Contains(m.Id)) && !gapSeen.Contains((Math.Min(m.Id, id), Math.Max(m.Id, id))), ct);
                if (n is null) continue;
                gapSeen.Add((Math.Min(n.Id, id), Math.Max(n.Id, id)));
                gapLinks.Add(new GraphEdge(id, n.Id, Haversine(p.Y, p.X, n.Y, n.X), Geo.Factory.CreateLineString([p, new Coordinate(n.X, n.Y)])));
            }
            await WriteEdgesAsync(conn, version, ChannelLinkFloors(gapLinks, channelEnds, depthRows, nearLand), ct);
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
                Tick();
                if (Find(id) == Find(giant)) continue;
                strays++;
                var n = await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, p, o.HarbourAccessM, m => Find(m.Id) != Find(id), ct);
                if (n is null) continue;
                relinks.Add(new GraphEdge(id, n.Id, Haversine(p.Y, p.X, n.Y, n.X), Geo.Factory.CreateLineString([p, new Coordinate(n.X, n.Y)])));
                Union(id, n.Id);
            }
            await WriteEdgesAsync(conn, version, ChannelLinkFloors(relinks, channelEnds, depthRows, nearLand), ct);
            var giantRoot = Find(giant);
            // nodes outside the main component (enclosed pockets, lakes without a passage): the snap of a map click prefers the main
            // component, otherwise a click on a town quay snapped into a pond next to it and the route fell back to a straight line
            await using (var cmd = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS routing_isolated (graph_version int NOT NULL, id bigint NOT NULL, PRIMARY KEY (graph_version, id))", conn)) await cmd.ExecuteNonQueryAsync(ct);
            await using (var w = await conn.BeginBinaryImportAsync("COPY routing_isolated (graph_version, id) FROM STDIN (FORMAT BINARY)", ct))
            {
                foreach (var id in parent.Keys.ToList()) if (Find(id) != giantRoot) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(id, NpgsqlDbType.Bigint, ct); }
                await w.CompleteAsync(ct);
            }
            var reached = access.Select(a => a.harbourId).ToHashSet();
            Tick(reached.Count);
            foreach (var h in harbours.Where(h => !reached.Contains(h.Id)))
            {
                Tick();
                // the access link may not cross land: a harbour on a river or lake the graph does not know stays unreachable rather than
                // being routed across a field (the audit found 35 such routes); a node of the main component is preferred, any other
                // connected node is taken when none is within reach. Only when no link passes, the harbour's own basin and its quay or mole
                // may be crossed (SeaGraph.LinkLandOk harbour: 60 marinas behind a quay lost their access in v64 with the 300 m shortcut)
                var hc = h.Location.Coordinate;
                var n = await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, hc, o.HarbourAccessM, m => Find(m.Id) == giantRoot, ct)
                    ?? await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, hc, o.HarbourAccessM, _ => true, ct)
                    ?? await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, hc, o.HarbourAccessM, m => Find(m.Id) == giantRoot, ct, harbour: true)
                    ?? await NearestWaterLinkAsync(conn, version, nodeIndex, linkLand, hc, o.HarbourAccessM, _ => true, ct, harbour: true);
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
        Phase("switch");
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
                DELETE FROM routing_isolated WHERE graph_version < {version};
                DELETE FROM "RouteCaches" WHERE "GraphVersion" < {version};
                """;
            await new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 3600 }.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        await new NpgsqlCommand("ANALYZE routing_edge; ANALYZE routing_node; ANALYZE routing_land;", conn) { CommandTimeout = 3600 }.ExecuteNonQueryAsync(ct);
        // phase durations for the next build's estimate; phases this build skipped (cache hits) keep their last measured value
        Dictionary<string, double> phaseSeconds;
        lock (_measured) { _measured["switch"] = _phaseClock.Elapsed.TotalSeconds; phaseSeconds = new(_expected); foreach (var (k, v) in _measured) phaseSeconds[k] = Math.Round(v, 1); }
        await new NpgsqlCommand("INSERT INTO routing_meta (key, value) VALUES ('phase_seconds', @p), ('build_seconds', @s) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value", conn)
            { Parameters = { new("p", System.Text.Json.JsonSerializer.Serialize(phaseSeconds)), new("s", Math.Round(sw.Elapsed.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)) } }.ExecuteNonQueryAsync(ct);
        Report("done", $"Graph version {version} active: {nodes} nodes, {edges} edges, {access.Count} harbours ({sw.Elapsed:hh\\:mm\\:ss})");
    }

    /// <summary>Links from a derived channel's end (gap links, relinks) carry no limit, only the floor of the water they cross (FloorM: the
    /// charted band, the band next to them in the coastal strip, or unknown coastal water), as the channel itself.</summary>
    private static List<GraphEdge> ChannelLinkFloors(List<GraphEdge> links, IReadOnlySet<long> channelEnds, IReadOnlyList<(Geometry, double)>? depths, Func<Coordinate, double, bool> nearLand)
    {
        if (depths is null || links.Count == 0) return links;
        var idx = Enumerable.Range(0, links.Count).Where(i => channelEnds.Contains(links[i].Source)).ToList();
        var limited = DepthLimits.Apply(idx.Select(i => links[i]).ToList(), depths, nearLand);
        var result = links.ToList();
        for (var k = 0; k < idx.Count; k++)
            if ((limited[k].MinDepthM ?? limited[k].AdjacentDepthM ?? limited[k].FloorM) is { } f) result[idx[k]] = links[idx[k]] with { FloorM = f };
        return result;
    }

    private static async Task WriteEdgesAsync(NpgsqlConnection conn, int version, IReadOnlyList<GraphEdge> edges, CancellationToken ct)
    {
        if (edges.Count == 0) return;
        await using var w = await conn.BeginBinaryImportAsync("COPY routing_edge (graph_version, source, target, cost, reverse_cost, x1, y1, x2, y2, geom, clearance_m, min_depth_m, assumed, adjacent_depth_m, floor_m, unknown_bridge) FROM STDIN (FORMAT BINARY)", ct);
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
            await w.WriteAsync(e.Assumed, NpgsqlDbType.Boolean, ct);
            if (e.AdjacentDepthM is { } ad) await w.WriteAsync(ad, NpgsqlDbType.Double, ct); else await w.WriteNullAsync(ct);
            if (e.FloorM is { } cf) await w.WriteAsync(cf, NpgsqlDbType.Double, ct); else await w.WriteNullAsync(ct);
            await w.WriteAsync(e.UnknownBridge, NpgsqlDbType.Boolean, ct);
        }
        await w.CompleteAsync(ct);
    }

    /// <summary>
    /// Area mode (VisibilityGraph): buffered, simplified land from routing_land → corner nodes + open-sea lattice + anchors for
    /// harbours and passage ends + corners of shallow banks → visibility edges, decorated like grid edges (channel preference, depth).
    /// </summary>
    private async Task<(long nodes, long edges, long nextId, List<GraphNode> waterNodes)> BuildAreasAsync(NpgsqlConnection conn, SmsDbContext db, int version, Envelope region, RoutingOptions o,
        IReadOnlyList<LineString> fairways, IReadOnlyList<Polygon> fairwayAreas, IReadOnlyList<Coordinate> harbourPoints, string landKey, Stopwatch sw, Func<Coordinate, double, bool> nearLand, CancellationToken ct)
    {
        // a visibility leg through a sound passes under its bridge: clearance, lock and opening costs apply here as on passages;
        // a long bridge with charted spans is passable at the spans only, so the span points become anchors (legs through them)
        var bridgesRaw = (await db.RoutingCorrections.Where(c => c.Active && (c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.Lock || c.Kind == CorrectionKind.OpeningBridge)).Select(c => new { c.Kind, c.Geometry, c.ClearanceM }).ToListAsync(ct))
            .Select(c => (c.Geometry is LineString ? c.Geometry : c.Geometry.Centroid, c.Kind, c.ClearanceM)).ToList();
        var (nodes, edges) = await AreaGraphAsync(conn, db, version, region, o, harbourPoints, bridgesRaw, landKey, ct);

        // 4. pricing: the same decorations as grid edges
        Phase("pricing", 3);
        var depths = new List<(Geometry, double)>();
        await using (var dcmd = new NpgsqlCommand("SELECT geom, min_depth_m FROM routing_depth WHERE geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)", conn) { CommandTimeout = 3600 })
        {
            dcmd.Parameters.AddWithValue("x1", region.MinX); dcmd.Parameters.AddWithValue("y1", region.MinY); dcmd.Parameters.AddWithValue("x2", region.MaxX); dcmd.Parameters.AddWithValue("y2", region.MaxY);
            await using var dr = await dcmd.ExecuteReaderAsync(ct);
            while (await dr.ReadAsync(ct)) depths.Add((dr.GetFieldValue<Geometry>(0), dr.GetDouble(1)));
        }
        var avoids = await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Avoid).Select(c => c.Geometry).ToListAsync(ct);
        edges = ChannelPreference.Apply(edges, fairways, fairwayAreas, o.ChannelPreferenceM, o.ChannelPenaltyFactor);
        Tick(); Report("areas", $"channel preference applied ({sw.Elapsed:hh\\:mm\\:ss})");
        edges = DepthLimits.Apply(Avoid.Apply(edges, avoids, o.AvoidPenaltyFactor), depths, nearLand, fairwayAreas);
        Tick(); Report("areas", $"depth limits applied ({sw.Elapsed:hh\\:mm\\:ss})");
        edges = Obstacles.Apply(edges, Obstacles.WithSpans(bridgesRaw), o.LockPenaltyM, o.BridgeOpeningPenaltyM, o.BridgeMatchAreaM); // a bridge over a sound is a point, the leg may pass a few hundred metres from it
        Tick(); Report("areas", $"bridges and locks applied, writing {nodes.Count} nodes and {edges.Count} edges ({sw.Elapsed:hh\\:mm\\:ss})");
        Phase("writing", 2);
        await using (var w = await conn.BeginBinaryImportAsync("COPY routing_node (graph_version, id, geom) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var n in nodes) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(n.Id, NpgsqlDbType.Bigint, ct); await w.WriteAsync(Geo.Factory.CreatePoint(new Coordinate(n.X, n.Y)), NpgsqlDbType.Geometry, ct); }
            await w.CompleteAsync(ct);
        }
        Tick();
        await WriteEdgesAsync(conn, version, edges, ct);
        Tick();
        return (nodes.Count, edges.Count, nodes.Count + 1, nodes);
    }

    /// <summary>The unpriced area graph (nodes and raw visibility edges, land-checked) of a graph version whose routing_land is written.
    /// Only reads. Public for tools/BuildBench, which times it against the dev database.</summary>
    public async Task<(List<GraphNode> Nodes, List<GraphEdge> Edges)> AreaGraphAsync(NpgsqlConnection conn, SmsDbContext db, int version, Envelope region, RoutingOptions o,
        IReadOnlyList<Coordinate> harbourPoints, IReadOnlyList<(Geometry geom, CorrectionKind kind, double? clearanceM)> bridgesRaw, string landKey, CancellationToken ct)
    {
        var step = Stopwatch.StartNew();
        string Took() { var s = $"{step.Elapsed.TotalSeconds:0.0} s"; step.Restart(); return s; }
        // 1. obstacles: land buffered by the coast distance, unioned and simplified, in 2° chunks (polygons on a chunk border appear twice – harmless for tests, corners are deduplicated);
        //    the same land with a small buffer: short legs through a constriction the coast buffer closes are tested against it
        var (polygons, finePolygons) = await CachedObstaclesAsync(conn, version, region, o, landKey, ct);
        Report("areas", $"{polygons.Count} obstacle polygons, {finePolygons?.Count ?? 0} fine ones ({Took()})");
        var tol = o.SimplifyM / WaterGraphTiler.MPerDegLat;
        var obstacles = VisibilityGraph.Index(polygons);

        // 2. nodes
        var vo = new VisibilityGraph.Options(o.LatticeM, o.LatticeClearM, o.MaxVisibilityM, o.VisibleNeighbours, o.NarrowLegM, o.NarrowLegFactor);
        var corners = VisibilityGraph.Corners(polygons, region);
        var lattice = VisibilityGraph.Lattice(region, obstacles, vo);
        Report("areas", $"index, {corners.Count} corners, {lattice.Count} lattice points ({Took()})");
        var passageEnds = (await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Passage).Select(c => c.Geometry).ToListAsync(ct)).OfType<LineString>()
            .SelectMany(l => new[] { l.StartPoint.Coordinate, l.EndPoint.Coordinate }).ToList();
        var anchorPoints = harbourPoints.Concat(passageEnds).Concat(Obstacles.SpanPoints(bridgesRaw)).ToList();
        Report("areas", $"{anchorPoints.Count} anchor points loaded ({Took()})");
        Phase("nodes", anchorPoints.Count);
        var anchors = VisibilityGraph.Anchors(anchorPoints, polygons, () => Tick());
        Report("areas", $"{anchors.Length} anchors ({Took()})");
        var shallowTol = tol * 2;
        await using var shallowCmd = new NpgsqlCommand("SELECT ST_SimplifyPreserveTopology(geom, @tol) FROM routing_depth WHERE max_depth_m <= @d AND ST_Area(geom) >= @minArea AND geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)", conn) { CommandTimeout = 3600 }; // planar deg²: geography area fails on the invalid Seascape rings
        var midCos = Math.Cos((region.MinY + region.MaxY) / 2 * Math.PI / 180);
        shallowCmd.Parameters.AddWithValue("tol", shallowTol * 2.5); shallowCmd.Parameters.AddWithValue("minArea", o.ShallowNodeMinKm2 * 1e6 / (WaterGraphTiler.MPerDegLat * WaterGraphTiler.MPerDegLat * midCos)); shallowCmd.Parameters.AddWithValue("d", o.ShallowNodeDepthM);
        shallowCmd.Parameters.AddWithValue("x1", region.MinX); shallowCmd.Parameters.AddWithValue("y1", region.MinY); shallowCmd.Parameters.AddWithValue("x2", region.MaxX); shallowCmd.Parameters.AddWithValue("y2", region.MaxY);
        var banks = new List<Polygon>();
        await using (var r = await shallowCmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) if (!r.IsDBNull(0) && r.GetFieldValue<Geometry>(0) is Polygon bp && !bp.IsEmpty) banks.Add(bp);
        var bankCorners = VisibilityGraph.Corners(banks, region).Where(c => !VisibilityGraph.OnTileEdge(c)).ToList(); // the depth areas are cut per z12 tile: corners on tile edges are artefacts
        Report("areas", $"{banks.Count} banks, {bankCorners.Count} bank corners ({Took()})");
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
        Report("areas", $"{corners.Count} coast corners, {lattice.Count} lattice points, {anchors.Length} anchors, {bankCorners.Count} bank corners → {nodes.Count} nodes ({Took()})");

        // 3. visibility edges
        Phase("visibility", nodes.Count);
        var edges = VisibilityGraph.Edges(nodes, polygons, finePolygons, vo, () => Tick());
        Report("areas", $"{edges.Count} visibility edges ({Took()})");
        // safety net against the raw (unbuffered) land: no visibility leg may cross it, whatever the obstacle union did
        Phase("land check", edges.Count);
        var land = new List<Polygon>();
        await using (var lcmd = new NpgsqlCommand("SELECT geom FROM routing_land WHERE graph_version = @v", conn) { CommandTimeout = 3600 })
        {
            lcmd.Parameters.AddWithValue("v", version);
            await using var lr = await lcmd.ExecuteReaderAsync(ct);
            while (await lr.ReadAsync(ct)) { var g = lr.GetFieldValue<Geometry>(0); land.AddRange(g is Polygon lp ? [lp] : g is MultiPolygon lmp ? lmp.Geometries.OfType<Polygon>() : []); }
        }
        var landIndex = VisibilityGraph.Index(land);
        // prepared Intersects is the accelerated predicate (Crosses is not); legs start 150 m off the raw land, so touching it is crossing it
        var crossing = edges.AsParallel().Where(e => { Tick(); return landIndex.Query(e.Geom.EnvelopeInternal).Any(l => l.Intersects(e.Geom)); }).ToList();
        Report("areas", $"land check done ({Took()})");
        if (crossing.Count > 0)
        {
            var sample = crossing[0].Geom.Centroid;
            var covered = obstacles.Query(new Envelope(sample.Coordinate)).Any(ob => ob.Contains(sample));
            Report("areas", $"{crossing.Count} visibility edges crossed land and were dropped (sample {sample.X:0.####},{sample.Y:0.####} inside obstacle union: {covered})");
            var bad = crossing.ToHashSet();
            edges = edges.Where(e => !bad.Contains(e)).ToList();
        }
        return (nodes, edges);
    }

    /// <summary>The obstacle polygons (coast buffer and, with NarrowLegM, the fine buffer) depend on nothing but the land rows and a few
    /// options, so they are kept as a WKB file under CachePath/routing-obstacles: the key is the land key (shapefile, region, Water/Block
    /// corrections, i.e. exactly what routing_land is made of) plus the buffers, simplification and pocket filling. Only the newest file is
    /// kept. A build after passage, bridge, avoid, fairway, depth or harbour changes reads it in seconds instead of building it.</summary>
    private async Task<(List<Polygon> Coast, List<Polygon>? Fine)> CachedObstaclesAsync(NpgsqlConnection conn, int version, Envelope region, RoutingOptions o, string landKey, CancellationToken ct)
    {
        double[] buffers = o.NarrowLegM > 0 ? [o.CoastBufferM, o.NarrowBufferM] : [o.CoastBufferM];
        var key = FormattableString.Invariant($"obstacles-v1|{landKey}|{region}|{string.Join(',', buffers)}|{o.SimplifyM}|{o.FillHoleKm2}");
        var dir = Path.GetFullPath(Path.Combine(o.CachePath, "routing-obstacles"));
        var file = Path.Combine(dir, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16] + ".bin");
        List<Polygon>[] lists;
        if (File.Exists(file))
        {
            try
            {
                lists = ReadPolygonLists(file);
                if (lists.Length == buffers.Length) { Report("areas", $"Obstacle polygons unchanged (land key and buffers): read from {Path.GetFileName(file)}"); return (lists[0], lists.Length > 1 ? lists[1] : null); }
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentException or InvalidCastException or NetTopologySuite.IO.ParseException) { Report("areas", $"Obstacle cache {Path.GetFileName(file)} unreadable ({ex.Message}), building it again"); }
        }
        var chunks = 0;
        for (var x = o.Region.MinLng + Math.Floor((region.MinX - o.Region.MinLng) / 2) * 2; x < region.MaxX; x += 2)
            for (var y = o.Region.MinLat + Math.Floor((region.MinY - o.Region.MinLat) / 2) * 2; y < region.MaxY; y += 2) chunks++;
        Phase("obstacles", chunks * buffers.Length);
        lists = await ObstaclePolygonsAsync(conn, version, region, o, buffers, ct, () => Tick());
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "*.bin")) File.Delete(old);
        WritePolygonLists(file + ".tmp", lists);
        File.Move(file + ".tmp", file, overwrite: true);
        return (lists[0], lists.Length > 1 ? lists[1] : null);
    }

    public static void WritePolygonLists(string file, IReadOnlyList<List<Polygon>> lists)
    {
        using var w = new BinaryWriter(File.Create(file));
        var wkb = new NetTopologySuite.IO.WKBWriter(NetTopologySuite.IO.ByteOrder.LittleEndian, handleSRID: true);
        w.Write(lists.Count);
        foreach (var list in lists) { w.Write(list.Count); foreach (var p in list) { var b = wkb.Write(p); w.Write(b.Length); w.Write(b); } }
    }

    public static List<Polygon>[] ReadPolygonLists(string file)
    {
        using var r = new BinaryReader(File.OpenRead(file));
        var wkb = new NetTopologySuite.IO.WKBReader(NetTopologySuite.NtsGeometryServices.Instance) { HandleSRID = true };
        var lists = new List<Polygon>[r.ReadInt32()];
        for (var l = 0; l < lists.Length; l++)
        {
            var n = r.ReadInt32(); lists[l] = new List<Polygon>(n);
            for (var i = 0; i < n; i++) lists[l].Add((Polygon)wkb.Read(r.ReadBytes(r.ReadInt32())));
        }
        return lists;
    }

    /// <summary>The obstacle polygons of the area build (land buffered by the coast distance, unioned and simplified per 2° chunk,
    /// small enclosed pockets filled) for the chunks that touch <paramref name="region"/>. Public for the admin diagnostics.</summary>
    public static async Task<List<Polygon>> ObstaclePolygonsAsync(NpgsqlConnection conn, int version, Envelope region, RoutingOptions o, CancellationToken ct, double? bufferM = null, Action? tick = null) =>
        (await ObstaclePolygonsAsync(conn, version, region, o, [bufferM ?? o.CoastBufferM], ct, tick))[0];

    /// <summary>The obstacle polygons for several buffers. A chunk is one PostGIS buffer + union, single-threaded in Postgres and minutes
    /// long for an archipelago (28 min for the 2 × 32 chunks one after the other in v60), so the chunks run in parallel on connections
    /// of their own, the ones with the most land vertices first; the result keeps the chunk order (node ids depend on it).</summary>
    private static async Task<List<Polygon>[]> ObstaclePolygonsAsync(NpgsqlConnection conn, int version, Envelope region, RoutingOptions o, double[] buffers, CancellationToken ct, Action? tick)
    {
        var tol = o.SimplifyM / WaterGraphTiler.MPerDegLat;
        var x0 = o.Region.MinLng + Math.Floor((region.MinX - o.Region.MinLng) / 2) * 2; var y0 = o.Region.MinLat + Math.Floor((region.MinY - o.Region.MinLat) / 2) * 2;
        var chunks = new List<Envelope>();
        for (var x = x0; x < region.MaxX; x += 2)
            for (var y = y0; y < region.MaxY; y += 2)
                chunks.Add(new Envelope(x, Math.Min(x + 2, o.Region.MaxLng), y, Math.Min(y + 2, o.Region.MaxLat)));
        var size = new long[chunks.Count];
        for (var i = 0; i < chunks.Count; i++)
        {
            await using var cmd = new NpgsqlCommand("SELECT COALESCE(SUM(ST_NPoints(geom)), 0) FROM routing_land WHERE graph_version = @v AND geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)", conn) { CommandTimeout = 3600 };
            cmd.Parameters.AddWithValue("v", version); cmd.Parameters.AddWithValue("x1", chunks[i].MinX); cmd.Parameters.AddWithValue("y1", chunks[i].MinY); cmd.Parameters.AddWithValue("x2", chunks[i].MaxX); cmd.Parameters.AddWithValue("y2", chunks[i].MaxY);
            size[i] = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }
        var jobs = buffers.SelectMany((_, b) => chunks.Select((_, i) => (b, i))).OrderByDescending(j => size[j.i]).ToList();
        var results = new Geometry?[buffers.Length, chunks.Count];
        // ponytail: fixed degree of parallelism; every job holds a connection and the union of its chunk in Postgres memory
        await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 12), CancellationToken = ct }, async (job, c) =>
        {
            var (b, i) = job; var e = chunks[i];
            await using var cn = (NpgsqlConnection)((ICloneable)conn).Clone(); // keeps the password, which ConnectionString drops after Open
            await cn.OpenAsync(c);
            await using var cmd = new NpgsqlCommand("""
                SELECT ST_SimplifyPreserveTopology(ST_UnaryUnion(ST_Collect(ST_Buffer(geom::geography, @buf)::geometry)), @tol)
                FROM routing_land WHERE graph_version = @v AND geom && ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326)
                """, cn) { CommandTimeout = 3600 };
            cmd.Parameters.AddWithValue("v", version); cmd.Parameters.AddWithValue("buf", buffers[b]); cmd.Parameters.AddWithValue("tol", tol);
            cmd.Parameters.AddWithValue("x1", e.MinX); cmd.Parameters.AddWithValue("y1", e.MinY); cmd.Parameters.AddWithValue("x2", e.MaxX); cmd.Parameters.AddWithValue("y2", e.MaxY);
            results[b, i] = await cmd.ExecuteScalarAsync(c) as Geometry;
            tick?.Invoke();
        });
        var lists = new List<Polygon>[buffers.Length];
        for (var b = 0; b < buffers.Length; b++)
        {
            lists[b] = [];
            for (var i = 0; i < chunks.Count; i++)
            {
                var g = results[b, i];
                if (g is null || g.IsEmpty) continue;
                // pockets of water enclosed by the buffered coast (marina basins behind a narrow entrance) are filled: a node inside
                // would be unreachable; such harbours get their anchor on the outer coast and reach it across the mole, as before
                lists[b].AddRange((g is Polygon p ? [p] : g is MultiPolygon mp ? mp.Geometries.OfType<Polygon>() : []).Select(poly => VisibilityGraph.FillSmallHoles(poly, o.FillHoleKm2)));
            }
        }
        return lists;
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

    /// <summary>The nearest acceptable node whose link from <paramref name="p"/> does not cross land (<see cref="SeaGraph.LinkLandOk"/>:
    /// a harbour point sits on its pier, a channel end on the shore; <paramref name="harbour"/> also lets a harbour's access link leave
    /// its own basin across the quay or mole – never for passage, gap or stray links, which are transit edges).</summary>
    private static async Task<GraphNode?> NearestWaterLinkAsync(NpgsqlConnection conn, int version, NodeIndex? index, STRtree<(IPreparedGeometry prepared, Geometry geom)> landIndex, Coordinate p, double maxM, Func<GraphNode, bool> accept, CancellationToken ct, long maxId = long.MaxValue, bool harbour = false)
    {
        var candidates = index is not null ? index.Nearest(p, maxM, 40, maxId) : await NearestNodesAsync(conn, version, p.X, p.Y, maxM, 40, ct, maxId);
        foreach (var n in candidates)
        {
            if (!accept(n)) continue;
            var lenM = Haversine(p.Y, p.X, n.Y, n.X);
            // no shortcut for short links: a gap link of 172 m between two channel pieces crossed an islet (Marstrand, 57.8218 N)
            if (LinkLandSamples(landIndex, p, new Coordinate(n.X, n.Y)) is not { } land || SeaGraph.LinkLandOk(land, lenM / (land.Length + 1), harbour)) return n;
        }
        return null;
    }

    /// <summary>Land flags of the points every ~25 m along p → q (ends excluded), null when the link touches no land at all. A harbour
    /// point sits on its pier and a channel end on the shore: <see cref="SeaGraph.LinkLandOk"/> decides which crossings are fine.
    /// Sampled with the prepared point-in-polygon test (an exact overlay against a mainland tile took minutes per node).</summary>
    public static bool[]? LinkLandSamples(STRtree<(IPreparedGeometry prepared, Geometry geom)> landIndex, Coordinate p, Coordinate q)
    {
        var seg = Geo.Factory.CreateLineString([p, q]);
        var hits = landIndex.Query(seg.EnvelopeInternal).Where(l => l.prepared.Intersects(seg)).Select(l => l.prepared).ToList();
        if (hits.Count == 0) return null;
        var steps = Math.Max(2, (int)Math.Ceiling(Haversine(p.Y, p.X, q.Y, q.X) / 25));
        var land = new bool[steps - 1];
        for (var k = 1; k < steps; k++)
        {
            var pt = Geo.Factory.CreatePoint(new Coordinate(p.X + (q.X - p.X) * k / steps, p.Y + (q.Y - p.Y) * k / steps));
            land[k - 1] = hits.Any(l => l.Contains(pt));
        }
        return land;
    }

    /// <summary>A fairway link crossing more land than this between water costs FairwayJumpPenaltyM extra (as the router penalizes a link across a mole).</summary>
    private const double FairwayJumpLandM = 300, FairwayJumpPenaltyM = 20_000;
    private static bool OnLinkLand(STRtree<(IPreparedGeometry prepared, Geometry geom)> land, Coordinate c)
    {
        var pt = Geo.Factory.CreatePoint(c);
        return land.Query(new Envelope(c)).Any(l => l.prepared.Contains(pt));
    }

    /// <summary>(point, metres) → land of the index lies within that distance (planar degrees of latitude, see DepthLimits); facet indexes built per polygon on first use.</summary>
    public static Func<Coordinate, double, bool> NearLand(STRtree<(IPreparedGeometry prepared, Geometry geom)> land)
    {
        var facets = new System.Collections.Concurrent.ConcurrentDictionary<Geometry, NetTopologySuite.Operation.Distance.IndexedFacetDistance>(ReferenceEqualityComparer.Instance);
        return (c, m) =>
        {
            var d = m / WaterGraphTiler.MPerDegLat; var pt = Geo.Factory.CreatePoint(c);
            return land.Query(new Envelope(c.X - d, c.X + d, c.Y - d, c.Y + d)).Any(l => facets.GetOrAdd(l.geom, g => new(g)).Distance(pt) <= d);
        };
    }

    /// <summary>The raw land of this graph version (coast tiles with lakes cut out, no-go blocks) as a prepared index for the link checks.</summary>
    public static async Task<STRtree<(IPreparedGeometry prepared, Geometry geom)>> LandIndexAsync(NpgsqlConnection conn, int version, IReadOnlyList<Geometry> blocks, CancellationToken ct)
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
