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

/// <summary>Builder settings (appsettings "Routing"). Region defaults to the western Baltic; Europe = lon −32…45, lat 27…72 (hours of build time).</summary>
public sealed class RoutingOptions
{
    public string LandPolygonsUrl { get; set; } = "https://osmdata.openstreetmap.de/download/land-polygons-split-4326.zip";
    public string CachePath { get; set; } = "media/cache";
    public RegionBox Region { get; set; } = new();
    public double StartCellM { get; set; } = 20000;
    public double MinCellM { get; set; } = 150;
    public double HarbourAccessM { get; set; } = 3000;
    public double CoastPenaltyFactor { get; set; } = 0.2;
    public double MaxRouteNm { get; set; } = 500;

    public sealed class RegionBox { public double MinLng { get; set; } = 9.3; public double MinLat { get; set; } = 53.8; public double MaxLng { get; set; } = 12.6; public double MaxLat { get; set; } = 55.4; }
}

public sealed record BuildProgress(string Phase, string Message, DateTimeOffset At);

/// <summary>
/// Offline graph build (spec 10 §10.2): OSM land polygons → adaptive quadtree over water → nodes/edges →
/// PostgreSQL tables for pgRouting. Runs in-process as a background job; progress is exposed to the admin UI.
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
        var region = Geo.Factory.ToGeometry(new Envelope(o.Region.MinLng, o.Region.MaxLng, o.Region.MinLat, o.Region.MaxLat));
        var sw = Stopwatch.StartNew();

        // 1. land polygons (downloaded once, ~900 MB) + active Block corrections
        var shp = await EnsureLandPolygonsAsync(o, ct);
        Report("land", "Reading land polygons inside the region…");
        var land = new List<Geometry>();
        foreach (var g in Shapefile.ReadAllGeometries(shp))
        {
            if (!g.EnvelopeInternal.Intersects(region.EnvelopeInternal)) continue;
            var clipped = g.Intersection(region);
            if (!clipped.IsEmpty) land.Add(clipped);
        }
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
            land.AddRange(await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Block).Select(c => c.Geometry).ToListAsync(ct));
        }
        Report("land", $"{land.Count} land polygons ({sw.Elapsed:mm\\:ss})");
        var landIndex = new STRtree<IPreparedGeometry>();
        foreach (var g in land) landIndex.Insert(g.EnvelopeInternal, PreparedGeometryFactory.Prepare(g));
        landIndex.Build();

        // 2. adaptive quadtree over water in a local equirectangular frame (metres)
        var lat0 = (o.Region.MinLat + o.Region.MaxLat) / 2;
        var mPerDegLat = 111_320.0;
        var mPerDegLng = 111_320.0 * Math.Cos(lat0 * Math.PI / 180);
        var leaves = new List<Cell>();
        var startDegLat = o.StartCellM / mPerDegLat; var startDegLng = o.StartCellM / mPerDegLng;
        var minDeg = o.MinCellM / mPerDegLat;
        for (var y = o.Region.MinLat; y < o.Region.MaxLat; y += startDegLat)
            for (var x = o.Region.MinLng; x < o.Region.MaxLng; x += startDegLng)
                Split(new Cell(x, y, Math.Min(startDegLng, o.Region.MaxLng - x), Math.Min(startDegLat, o.Region.MaxLat - y), 0), landIndex, minDeg, mPerDegLng / mPerDegLat, leaves, ct);
        Report("grid", $"{leaves.Count} water cells ({sw.Elapsed:mm\\:ss})");

        // 3. nodes + edges between neighbouring cells whose connecting segment stays on water
        for (var i = 0; i < leaves.Count; i++) leaves[i].Id = i + 1;
        var cellIndex = new STRtree<Cell>();
        foreach (var c in leaves) cellIndex.Insert(c.Envelope, c);
        cellIndex.Build();
        var edges = new List<Edge>();
        var eps = minDeg / 10;
        foreach (var a in leaves)
        {
            ct.ThrowIfCancellationRequested();
            var env = new Envelope(a.Envelope); env.ExpandBy(eps);
            foreach (var b in cellIndex.Query(env))
            {
                if (b.Id <= a.Id) continue;
                var line = Geo.Factory.CreateLineString([new Coordinate(a.Cx, a.Cy), new Coordinate(b.Cx, b.Cy)]);
                if (CrossesLand(line, landIndex)) continue;
                var metres = Haversine(a.Cy, a.Cx, b.Cy, b.Cx);
                if (a.IsMin || b.IsMin) metres *= 1 + o.CoastPenaltyFactor; // keep routes off the coast
                edges.Add(new Edge(a.Id, b.Id, metres, line));
            }
        }
        Report("edges", $"{edges.Count} edges ({sw.Elapsed:mm\\:ss})");

        // 4. passages (manual lines) as extra edge chains, 5. harbour access nodes
        var nextId = leaves.Count + 1;
        var nodes = leaves.Select(c => new Node(c.Id, c.Cx, c.Cy)).ToList();
        List<(Guid harbourId, long nodeId, double distance)> access = [];
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
            foreach (var passage in await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Passage).Select(c => c.Geometry).ToListAsync(ct))
            {
                if (passage is not LineString ls) continue;
                long? prev = null;
                for (var i = 0; i < ls.NumPoints; i++)
                {
                    var p = ls.GetCoordinateN(i);
                    var id = nextId++;
                    nodes.Add(new Node(id, p.X, p.Y));
                    if (prev is { } pv) edges.Add(new Edge(pv, id, Haversine(ls.GetCoordinateN(i - 1).Y, ls.GetCoordinateN(i - 1).X, p.Y, p.X), Geo.Factory.CreateLineString([ls.GetCoordinateN(i - 1), p])));
                    if (i == 0 || i == ls.NumPoints - 1)
                    {
                        var near = Nearest(cellIndex, p.X, p.Y, o.HarbourAccessM / mPerDegLat);
                        if (near is not null) edges.Add(new Edge(id, near.Id, Haversine(p.Y, p.X, near.Cy, near.Cx), Geo.Factory.CreateLineString([p, new Coordinate(near.Cx, near.Cy)])));
                    }
                    prev = id;
                }
            }
            var harbours = await db.Harbours.Where(h => h.IsActive && h.MergedIntoId == null && h.Location.Intersects(region)).Select(h => new { h.Id, h.Location }).ToListAsync(ct);
            foreach (var h in harbours)
            {
                var near = Nearest(cellIndex, h.Location.X, h.Location.Y, o.HarbourAccessM / mPerDegLat);
                if (near is not null) access.Add((h.Id, near.Id, Haversine(h.Location.Y, h.Location.X, near.Cy, near.Cx)));
            }
            Report("harbours", $"{access.Count}/{harbours.Count} harbours reachable ({sw.Elapsed:mm\\:ss})");
        }

        // 6. write a new graph version and switch atomically
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await conn.OpenAsync(ct);
            var version = Convert.ToInt32(await new NpgsqlCommand("SELECT COALESCE(MAX(graph_version),0)+1 FROM routing_node", conn).ExecuteScalarAsync(ct));
            Report("write", $"Writing graph version {version}…");
            await using (var w = await conn.BeginBinaryImportAsync("COPY routing_land (graph_version, geom) FROM STDIN (FORMAT BINARY)", ct))
            {
                foreach (var g in land) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(g, NpgsqlDbType.Geometry, ct); }
                await w.CompleteAsync(ct);
            }
            await using (var w = await conn.BeginBinaryImportAsync("COPY routing_node (graph_version, id, geom) FROM STDIN (FORMAT BINARY)", ct))
            {
                foreach (var n in nodes) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(n.Id, NpgsqlDbType.Bigint, ct); await w.WriteAsync(Geo.Factory.CreatePoint(new Coordinate(n.X, n.Y)), NpgsqlDbType.Geometry, ct); }
                await w.CompleteAsync(ct);
            }
            await using (var w = await conn.BeginBinaryImportAsync("COPY routing_edge (graph_version, source, target, cost, reverse_cost, x1, y1, x2, y2, geom) FROM STDIN (FORMAT BINARY)", ct))
            {
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
            await using (var w = await conn.BeginBinaryImportAsync("COPY routing_harbour_access (graph_version, harbour_id, node_id, distance_m) FROM STDIN (FORMAT BINARY)", ct))
            {
                foreach (var a in access) { await w.StartRowAsync(ct); await w.WriteAsync(version, NpgsqlDbType.Integer, ct); await w.WriteAsync(a.harbourId, NpgsqlDbType.Uuid, ct); await w.WriteAsync(a.nodeId, NpgsqlDbType.Bigint, ct); await w.WriteAsync(a.distance, NpgsqlDbType.Double, ct); }
                await w.CompleteAsync(ct);
            }
            await using (var tx = await conn.BeginTransactionAsync(ct))
            {
                var sql = $"""
                    INSERT INTO routing_meta (key, value) VALUES ('active_version', '{version}'), ('built_at', '{DateTimeOffset.UtcNow:O}'),
                      ('region', '{string.Join(',', new[] { o.Region.MinLng, o.Region.MinLat, o.Region.MaxLng, o.Region.MaxLat }.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)))}'), ('nodes', '{nodes.Count}'), ('edges', '{edges.Count}')
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
            await new NpgsqlCommand("ANALYZE routing_edge; ANALYZE routing_node;", conn).ExecuteNonQueryAsync(ct);
            Report("done", $"Graph version {version} active: {nodes.Count} nodes, {edges.Count} edges, {access.Count} harbours ({sw.Elapsed:mm\\:ss})");
        }
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

    private sealed class Cell(double x, double y, double w, double h, int depth)
    {
        public double X = x, Y = y, W = w, H = h; public int Depth = depth; public long Id; public bool IsMin;
        public double Cx => X + W / 2; public double Cy => Y + H / 2;
        public Envelope Envelope => new(X, X + W, Y, Y + H);
    }
    private sealed record Node(long Id, double X, double Y);
    private sealed record Edge(long Source, long Target, double Cost, LineString Geom);

    private static void Split(Cell c, STRtree<IPreparedGeometry> land, double minDeg, double lngFactor, List<Cell> leaves, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var poly = Geo.Factory.ToGeometry(c.Envelope);
        var hits = land.Query(c.Envelope);
        var touchesLand = false;
        foreach (var g in hits) if (g.Intersects(poly)) { touchesLand = true; break; }
        if (!touchesLand) { leaves.Add(c); return; }
        if (c.H > minDeg)
        {
            var hw = c.W / 2; var hh = c.H / 2;
            Split(new Cell(c.X, c.Y, hw, hh, c.Depth + 1), land, minDeg, lngFactor, leaves, ct);
            Split(new Cell(c.X + hw, c.Y, hw, hh, c.Depth + 1), land, minDeg, lngFactor, leaves, ct);
            Split(new Cell(c.X, c.Y + hh, hw, hh, c.Depth + 1), land, minDeg, lngFactor, leaves, ct);
            Split(new Cell(c.X + hw, c.Y + hh, hw, hh, c.Depth + 1), land, minDeg, lngFactor, leaves, ct);
            return;
        }
        // minimum size: keep as water node if the centre is not on land
        var centre = Geo.Factory.CreatePoint(new Coordinate(c.Cx, c.Cy));
        foreach (var g in hits) if (g.Contains(centre)) return;
        c.IsMin = true;
        leaves.Add(c);
    }

    private static bool CrossesLand(LineString line, STRtree<IPreparedGeometry> land)
    {
        foreach (var g in land.Query(line.EnvelopeInternal)) if (g.Intersects(line)) return true;
        return false;
    }

    private static Cell? Nearest(STRtree<Cell> cells, double x, double y, double maxDeg)
    {
        var env = new Envelope(x - maxDeg, x + maxDeg, y - maxDeg, y + maxDeg);
        Cell? best = null; var bestD = double.MaxValue;
        foreach (var c in cells.Query(env))
        {
            var d = (c.Cx - x) * (c.Cx - x) + (c.Cy - y) * (c.Cy - y);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best is not null && Math.Sqrt(bestD) <= maxDeg ? best : null;
    }

    public static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6_371_008.8;
        var dLat = (lat2 - lat1) * Math.PI / 180; var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }
}
