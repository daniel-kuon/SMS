using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.IO.VectorTiles.Mapbox;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using Sms.Infrastructure;

namespace Sms.Api;

public sealed record DepthArea(Polygon Area, double MinDepthM, double MaxDepthM, string Source);

/// <summary>
/// Depth areas (DEPARE: "between drval1 and drval2 metres") for the routing graph. Provider behind an interface like the
/// weather; today the Open Waters Seascape vector tiles (EMODnet/GEBCO/NOAA bathymetry, per-source licences, chart datum).
/// </summary>
public interface IDepthProvider
{
    string Name { get; }
    Task<List<DepthArea>> AreasAsync(double minLng, double minLat, double maxLng, double maxLat, CancellationToken ct);
}

public sealed class SeascapeDepthProvider(IHttpClientFactory http, IConfiguration cfg, ILogger<SeascapeDepthProvider> logger) : IDepthProvider
{
    public string Name => "OpenWaters Seascape";
    /// <summary>z10 carries only the 10-fathom contours; z12 has the shallow DEPARE steps that matter for yachts (16× the tiles).</summary>
    private int Zoom => cfg.GetValue("Depths:Zoom", 12);

    public async Task<List<DepthArea>> AreasAsync(double minLng, double minLat, double maxLng, double maxLat, CancellationToken ct)
    {
        var baseUrl = (cfg["Depths:SeascapeUrl"] ?? "https://tiles.openwaters.io/seascape").TrimEnd('/');
        var client = http.CreateClient("weather");
        var result = new List<DepthArea>();
        var z = Zoom;
        var (x1, y1) = TileXY(minLng, maxLat, z); var (x2, y2) = TileXY(maxLng, minLat, z);
        var failed = 0;
        var tiles = Enumerable.Range(x1, x2 - x1 + 1).SelectMany(x => Enumerable.Range(y1, y2 - y1 + 1).Select(y => (x, y))).ToList();
        logger.LogInformation("Seascape: fetching {Tiles} z{Z} tiles", tiles.Count, z);
        // a region-wide import is ~20k tiles: fetched 8 at a time (the tile server is a CDN), decoded into one list under a lock
        await Parallel.ForEachAsync(tiles, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (t, token) =>
        {
            var (x, y) = t;
            try
            {
                using var resp = await client.GetAsync($"{baseUrl}/{z}/{x}/{y}.pbf", token);
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return;
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(token);
                var areas = Decode(stream, x, y, z);
                lock (result) result.AddRange(areas);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Interlocked.Increment(ref failed); logger.LogWarning(ex, "Seascape tile {Z}/{X}/{Y} failed", z, x, y); }
        });
        if (failed > 0) logger.LogWarning("Seascape: {Failed} tiles failed", failed);
        return result;
    }

    /// <summary>DEPARE polygons of one Seascape tile as WGS84 geometries.</summary>
    public static List<DepthArea> Decode(Stream pbf, int x, int y, int z)
    {
        var tile = new MapboxTileReader().Read(pbf, new NetTopologySuite.IO.VectorTiles.Tiles.Tile(x, y, z));
        var layer = tile.Layers.FirstOrDefault(l => l.Name == "depare");
        if (layer is null) return [];
        var result = new List<DepthArea>();
        foreach (var f in layer.Features)
        {
            var d1 = f.Attributes.Exists("drval1") ? Num(f.Attributes["drval1"]) : null; var d2 = f.Attributes.Exists("drval2") ? Num(f.Attributes["drval2"]) : null;
            if (d1 is null) continue;
            var src = f.Attributes.Exists("sys") ? f.Attributes["sys"]?.ToString() ?? "" : "";
            var polys = f.Geometry switch { Polygon p => [p], MultiPolygon mp => mp.Geometries.OfType<Polygon>().ToList(), _ => new List<Polygon>() };
            foreach (var p in polys) { p.SRID = 4326; result.Add(new DepthArea(p, d1.Value, d2 ?? d1.Value, src)); }
        }
        return result;
    }

    private static double? Num(object? v) => v switch { null => null, double d => d, float f => f, long l => l, int i => i, string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d, _ => null };

    private static (int x, int y) TileXY(double lng, double lat, int zoom)
    {
        var n = 1 << zoom;
        var x = (int)Math.Floor((lng + 180) / 360 * n);
        var latR = lat * Math.PI / 180;
        var y = (int)Math.Floor((1 - Math.Log(Math.Tan(latR) + 1 / Math.Cos(latR)) / Math.PI) / 2 * n);
        return (Math.Clamp(x, 0, n - 1), Math.Clamp(y, 0, n - 1));
    }
}

/// <summary>Replaces the depth areas inside a bounding box with the provider's; the next graph build reads them. Kartverket rows
/// (KartverketDepths) stay and win: the new areas lose the parts they cover.</summary>
public static class DepthImport
{
    /// <summary>Pieces smaller than this (deg², ≈ 1 m²) left by a cut are dropped.</summary>
    public const double MinPieceDeg2 = 1e-10;

    public static async Task<(int areas, int removed)> RunAsync(IDepthProvider provider, SmsDbContext db, double minLng, double minLat, double maxLng, double maxLat, CancellationToken ct)
    {
        var areas = await provider.AreasAsync(minLng, minLat, maxLng, maxLat, ct);
        var removed = await db.Database.ExecuteSqlRawAsync("DELETE FROM routing_depth WHERE geom && ST_MakeEnvelope({0},{1},{2},{3},4326) AND source NOT IN ({4}, {5})", [minLng, minLat, maxLng, maxLat, KartverketDepths.Source, KartverketDepths.UnsurveyedSource], ct);
        var conn = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var cover = new List<Geometry>();
        await using (var cmd = new Npgsql.NpgsqlCommand("SELECT geom FROM routing_depth WHERE source IN (@k, @u) AND geom && ST_Expand(ST_MakeEnvelope(@x1, @y1, @x2, @y2, 4326), 0.2)", conn) { CommandTimeout = 3600 })
        {
            cmd.Parameters.AddWithValue("k", KartverketDepths.Source); cmd.Parameters.AddWithValue("u", KartverketDepths.UnsurveyedSource);
            cmd.Parameters.AddWithValue("x1", minLng); cmd.Parameters.AddWithValue("y1", minLat); cmd.Parameters.AddWithValue("x2", maxLng); cmd.Parameters.AddWithValue("y2", maxLat);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) cover.Add(r.GetFieldValue<Geometry>(0));
        }
        if (cover.Count > 0) areas = Outside(areas, CoverIndex(cover));
        await KartverketDepths.CopyAsync(conn, areas, ct);
        return (areas.Count, removed);
    }

    public static STRtree<Geometry> CoverIndex(IEnumerable<Geometry> cover)
    {
        var index = new STRtree<Geometry>();
        foreach (var c in cover) if (!c.IsEmpty) index.Insert(c.EnvelopeInternal, c);
        index.Build();
        return index;
    }

    /// <summary>Precedence: the areas with the parts the cover (kartverket rows) overlaps cut away.</summary>
    public static List<DepthArea> Outside(IEnumerable<DepthArea> areas, STRtree<Geometry> cover) =>
        areas.SelectMany(a => Outside(a.Area, cover) is { } parts ? parts.Select(p => a with { Area = p }) : [a]).ToList();

    /// <summary>The parts of an area outside every cover polygon; null = untouched (no cover polygon overlaps it, touching does not count).</summary>
    public static List<Polygon>? Outside(Polygon area, STRtree<Geometry> cover)
    {
        var hits = cover.Query(area.EnvelopeInternal).Where(c => c.Intersects(area) && !c.Touches(area)).ToList();
        if (hits.Count == 0) return null;
        Geometry rest;
        try { rest = OverlayNGRobust.Overlay(area, OverlayNGRobust.Union(Geo.Factory.BuildGeometry(hits)), SpatialFunction.Difference); }
        catch (TopologyException) { rest = OverlayNGRobust.Overlay(area.Buffer(0), OverlayNGRobust.Union(Geo.Factory.BuildGeometry(hits.Select(h => h.Buffer(0)).ToList())), SpatialFunction.Difference); }
        return Polygons(rest);
    }

    /// <summary>The polygons of an overlay result (SRID 4326), slivers below <see cref="MinPieceDeg2"/> dropped.</summary>
    public static List<Polygon> Polygons(Geometry g)
    {
        var result = new List<Polygon>();
        for (var i = 0; i < g.NumGeometries; i++)
            if (g.GetGeometryN(i) is Polygon { IsEmpty: false } p && p.Area >= MinPieceDeg2) { p.SRID = 4326; result.Add(p); }
        return result;
    }
}
