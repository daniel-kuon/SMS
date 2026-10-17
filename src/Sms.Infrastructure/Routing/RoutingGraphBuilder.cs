using System.Diagnostics;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
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
    public double PassageCostFactor { get; set; } = 0.8;
    public double MaxRouteNm { get; set; } = 500;

    public sealed class RegionBox { public double MinLng { get; set; } = 9.3; public double MinLat { get; set; } = 53.8; public double MaxLng { get; set; } = 12.6; public double MaxLat { get; set; } = 55.4; }
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
        var version = Convert.ToInt32(await new NpgsqlCommand("SELECT COALESCE(MAX(graph_version),0)+1 FROM routing_land", conn).ExecuteScalarAsync(ct));

        // 1. land polygons inside the region (streamed, never all in memory) + active Block corrections → routing_land
        Report("land", $"Streaming land polygons inside the region into graph version {version}…");
        var blocks = await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Block).Select(c => c.Geometry).ToListAsync(ct); // before the COPY: same connection
        var landCount = 0;
        await using (var w = await conn.BeginBinaryImportAsync("COPY routing_land (graph_version, geom) FROM STDIN (FORMAT BINARY)", ct))
        {
            async Task Write(Geometry g) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(g, NpgsqlDbType.Geometry, ct); landCount++; }
            foreach (var g in Shapefile.ReadAllGeometries(shp))
            {
                ct.ThrowIfCancellationRequested();
                if (!g.EnvelopeInternal.Intersects(regionEnv)) continue;
                var clipped = regionEnv.Contains(g.EnvelopeInternal) ? g : g.Intersection(region);
                if (!clipped.IsEmpty) await Write(clipped);
            }
            foreach (var g in blocks) await Write(g);
            await w.CompleteAsync(ct);
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
        var tiler = new WaterGraphTiler(Geo.Factory, regionEnv, o.StartCellM, o.MinCellM, o.CoastPenaltyFactor, o.TileCells);
        var tileNo = 0;
        Report("grid", $"{tiler.Tiles.Count} tiles of {o.TileCells}×{o.TileCells} start cells");
        await tiler.RunAsync(
            async (tile, c) =>
            {
                var e = WaterGraphTiler.WithMargin(tile);
                await using var cmd = new NpgsqlCommand("""
                    SELECT ST_CollectionExtract(ST_Intersection(geom, ST_MakeEnvelope(@x1,@y1,@x2,@y2,4326)), 3) FROM routing_land
                    WHERE graph_version = @v AND geom && ST_MakeEnvelope(@x1,@y1,@x2,@y2,4326)
                    """, conn);
                cmd.Parameters.AddWithValue("v", version);
                cmd.Parameters.AddWithValue("x1", e.MinX); cmd.Parameters.AddWithValue("y1", e.MinY); cmd.Parameters.AddWithValue("x2", e.MaxX); cmd.Parameters.AddWithValue("y2", e.MaxY);
                var land = new List<Geometry>();
                await using var r = await cmd.ExecuteReaderAsync(c);
                while (await r.ReadAsync(c)) if (!r.IsDBNull(0)) { var g = r.GetFieldValue<Geometry>(0); if (!g.IsEmpty) land.Add(g); }
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
                await WriteEdgesAsync(conn, version, result.Edges, c);
                foreach (var h in harbourIndex.Query(result.Tile.Envelope))
                    if (result.Nearest(h.X, h.Y, accessDeg) is { } near) access.Add((h.Id, near.Id, Haversine(h.Y, h.X, near.Y, near.X)));
                if (result.Nodes.Count > 0 || tileNo % 25 == 0)
                    Report("tile", $"Tile {tileNo}/{tiler.Tiles.Count}: {result.Nodes.Count} cells, {result.Edges.Count} edges – total {tiler.NodeCount} nodes, {tiler.EdgeCount} edges ({sw.Elapsed:hh\\:mm\\:ss})");
            }, ct);
        Report("harbours", $"{access.Count}/{harbours.Count} harbours reachable ({sw.Elapsed:hh\\:mm\\:ss})");

        // 4. passages (manual lines and imported waterways, spec 10 §10.5) as edge chains; ends hook into the nearest
        //    water node, vertices at the same coordinate are one node so canal networks connect at junctions
        var nextId = tiler.NextId;
        var extraNodes = new List<GraphNode>(); var extraEdges = new List<GraphEdge>();
        var shared = new Dictionary<(long, long), long>();
        long NodeAt(Coordinate p)
        {
            var key = ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6));
            if (shared.TryGetValue(key, out var existing)) return existing;
            var id = nextId++;
            shared[key] = id; extraNodes.Add(new GraphNode(id, p.X, p.Y, false));
            return id;
        }
        foreach (var passage in await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Passage).Select(c => c.Geometry).ToListAsync(ct))
        {
            if (passage is not LineString ls) continue;
            long? prev = null;
            for (var i = 0; i < ls.NumPoints; i++)
            {
                var p = ls.GetCoordinateN(i);
                var id = NodeAt(p);
                if (prev is { } pv && pv != id) { var q = ls.GetCoordinateN(i - 1); extraEdges.Add(new GraphEdge(pv, id, Haversine(q.Y, q.X, p.Y, p.X) * o.PassageCostFactor, Geo.Factory.CreateLineString([q, p]))); }
                if (i == 0 || i == ls.NumPoints - 1)
                {
                    var near = await NearestNodeAsync(conn, version, p.X, p.Y, o.HarbourAccessM, ct);
                    if (near is { } n) extraEdges.Add(new GraphEdge(id, n.Id, Haversine(p.Y, p.X, n.Y, n.X), Geo.Factory.CreateLineString([p, new Coordinate(n.X, n.Y)])));
                }
                prev = id;
            }
        }
        await using (var w = await conn.BeginBinaryImportAsync("COPY routing_node (graph_version, id, geom) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var n in extraNodes) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(n.Id, NpgsqlDbType.Bigint, ct); await w.WriteAsync(Geo.Factory.CreatePoint(new Coordinate(n.X, n.Y)), NpgsqlDbType.Geometry, ct); }
            await w.CompleteAsync(ct);
        }
        await WriteEdgesAsync(conn, version, extraEdges, ct);
        // inland harbours: no water cell nearby (rivers/canals count as land) but maybe a passage node
        if (extraNodes.Count > 0)
        {
            var reached = access.Select(a => a.harbourId).ToHashSet();
            foreach (var h in harbours.Where(h => !reached.Contains(h.Id)))
                if (await NearestNodeAsync(conn, version, h.Location.X, h.Location.Y, o.HarbourAccessM, ct) is { } n)
                    access.Add((h.Id, n.Id, Haversine(h.Location.Y, h.Location.X, n.Y, n.X)));
            Report("passages", $"{extraNodes.Count} passage nodes, {extraEdges.Count} edges; {access.Count}/{harbours.Count} harbours reachable ({sw.Elapsed:hh\\:mm\\:ss})");
        }
        await using (var w = await conn.BeginBinaryImportAsync("COPY routing_harbour_access (graph_version, harbour_id, node_id, distance_m) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var a in access) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(a.harbourId, NpgsqlDbType.Uuid, ct); await w.WriteAsync(a.nodeId, NpgsqlDbType.Bigint, ct); await w.WriteAsync(a.distance, NpgsqlDbType.Double, ct); }
            await w.CompleteAsync(ct);
        }

        // 5. switch atomically and drop older versions
        var nodes = tiler.NodeCount + extraNodes.Count; var edges = tiler.EdgeCount + extraEdges.Count;
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            var sql = $"""
                INSERT INTO routing_meta (key, value) VALUES ('active_version', '{version}'), ('built_at', '{DateTimeOffset.UtcNow:O}'),
                  ('region', '{string.Join(',', new[] { o.Region.MinLng, o.Region.MinLat, o.Region.MaxLng, o.Region.MaxLat }.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)))}'), ('nodes', '{nodes}'), ('edges', '{edges}')
                ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value;
                DELETE FROM routing_edge WHERE graph_version < {version};
                DELETE FROM routing_node WHERE graph_version < {version};
                DELETE FROM routing_land WHERE graph_version < {version};
                DELETE FROM routing_harbour_access WHERE graph_version < {version};
                DELETE FROM "RouteCaches" WHERE "GraphVersion" < {version};
                """;
            await new NpgsqlCommand(sql, conn, tx).ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        await new NpgsqlCommand("ANALYZE routing_edge; ANALYZE routing_node; ANALYZE routing_land;", conn).ExecuteNonQueryAsync(ct);
        Report("done", $"Graph version {version} active: {nodes} nodes, {edges} edges, {access.Count} harbours ({sw.Elapsed:hh\\:mm\\:ss})");
    }

    private static async Task WriteEdgesAsync(NpgsqlConnection conn, int version, IReadOnlyList<GraphEdge> edges, CancellationToken ct)
    {
        if (edges.Count == 0) return;
        await using var w = await conn.BeginBinaryImportAsync("COPY routing_edge (graph_version, source, target, cost, reverse_cost, x1, y1, x2, y2, geom) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var e in edges)
        {
            await w.StartRowAsync(ct);
            await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(e.Source, NpgsqlDbType.Bigint, ct); await w.WriteAsync(e.Target, NpgsqlDbType.Bigint, ct);
            await w.WriteAsync(e.Cost, NpgsqlDbType.Double, ct); await w.WriteAsync(e.Cost, NpgsqlDbType.Double, ct);
            await w.WriteAsync(e.Geom.StartPoint.X, NpgsqlDbType.Double, ct); await w.WriteAsync(e.Geom.StartPoint.Y, NpgsqlDbType.Double, ct);
            await w.WriteAsync(e.Geom.EndPoint.X, NpgsqlDbType.Double, ct); await w.WriteAsync(e.Geom.EndPoint.Y, NpgsqlDbType.Double, ct);
            await w.WriteAsync(e.Geom, NpgsqlDbType.Geometry, ct);
        }
        await w.CompleteAsync(ct);
    }

    private static async Task<GraphNode?> NearestNodeAsync(NpgsqlConnection conn, int version, double x, double y, double maxM, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id, ST_X(geom), ST_Y(geom) FROM routing_node
            WHERE graph_version = @v AND ST_DWithin(geom::geography, ST_SetSRID(ST_MakePoint(@x, @y), 4326)::geography, @m)
            ORDER BY geom <-> ST_SetSRID(ST_MakePoint(@x, @y), 4326) LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("v", version); cmd.Parameters.AddWithValue("x", x); cmd.Parameters.AddWithValue("y", y); cmd.Parameters.AddWithValue("m", maxM);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new GraphNode(r.GetInt64(0), r.GetDouble(1), r.GetDouble(2), false) : null;
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
