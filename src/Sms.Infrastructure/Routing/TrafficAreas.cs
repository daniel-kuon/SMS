using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using Npgsql;
using NpgsqlTypes;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// EMODnet Human Activities vessel density cells (routing_traffic, 1 km, hours per km² per month) with sailing traffic of at least
/// Routing:TrafficKnownSailing: water sailed that much is known-navigable (SeaGraph.Traffic), like a ferry track – but a 1 km cell says
/// nothing about where in it, so it only lifts the "not definitely deep enough" verdict; the charted band block stays.
/// </summary>
public sealed class TrafficAreas
{
    private readonly STRtree<IPreparedGeometry> _cells = new();
    public int Count { get; }
    public static readonly TrafficAreas Empty = new([]);

    public TrafficAreas(IReadOnlyList<Polygon> cells)
    {
        foreach (var c in cells) _cells.Insert(c.EnvelopeInternal, PreparedGeometryFactory.Prepare(c));
        _cells.Build();
        Count = cells.Count;
    }

    /// <summary>At least half of the edge's 25 m samples lie in dense cells (the polygons are trapezoids in WGS84: tested, not bboxed).</summary>
    public bool Along(LineString edge) => Count > 0 && _cells.Query(edge.EnvelopeInternal) is { Count: > 0 } hits && ChannelPreference.ShareInside(edge, hits, 25) >= 0.5;

    public bool Contains(Coordinate p)
    {
        if (Count == 0) return false;
        var pt = Geo.Factory.CreatePoint(p);
        foreach (var c in _cells.Query(new Envelope(p))) if (c.Contains(pt)) return true;
        return false;
    }

    /// <summary>The cells with sailing ≥ minSailing (0 = off: none) from routing_traffic; none before the TrafficAndDtm migration.</summary>
    public static async Task<TrafficAreas> LoadAsync(NpgsqlConnection conn, double minSailing, CancellationToken ct = default)
    {
        if (minSailing <= 0) return Empty;
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        if (await new NpgsqlCommand("SELECT to_regclass('routing_traffic') IS NULL", conn).ExecuteScalarAsync(ct) is true) return Empty;
        var cells = new List<Polygon>();
        await using var cmd = new NpgsqlCommand("SELECT geom FROM routing_traffic WHERE sailing >= @s", conn) { CommandTimeout = 3600 };
        cmd.Parameters.AddWithValue("s", (float)minSailing);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) if (r.GetFieldValue<Geometry>(0) is Polygon p) cells.Add(p);
        return new TrafficAreas(cells);
    }
}

/// <summary>One vessel density cell: the 1 km EPSG:3035 square as a WGS84 polygon (a slight trapezoid), sailing and pleasure craft hours per km² per month.</summary>
public sealed record TrafficCell(Polygon Cell, double Sailing, double Pleasure);

/// <summary>Import of the EMODnet vessel density GeoTIFFs (ship types 04 sailing, 05 pleasure craft; POST /api/admin/routing/import-traffic-file).</summary>
public static partial class TrafficImport
{
    /// <summary>Cells below this in both kinds are not stored (84 % of Europe's cells are 0).</summary>
    public const double MinDensity = 0.1;

    [GeneratedRegex(@"^vesseldensity_(04|05)_(\d{4})(_[^.]*)?\.tiff?$", RegexOptions.IgnoreCase)]
    private static partial Regex NamePattern();

    /// <summary>Ship type ("04"/"05") and year from an EMODnet file name (vesseldensity_04_2023.tif), null for anything else.</summary>
    public static (string kind, int year)? ParseName(string fileName) =>
        NamePattern().Match(Path.GetFileName(fileName)) is { Success: true } m ? (m.Groups[1].Value, int.Parse(m.Groups[2].Value)) : null;

    /// <summary>The cells whose centre lies in the region (expanded by 1 km), averaged per kind over its files (a cell missing in a file counts 0),
    /// kept when either kind reaches MinDensity. The files must share one grid (the EMODnet files do).</summary>
    public static List<TrafficCell> Cells(IEnumerable<(string kind, GeoTiff tif)> files, Envelope region)
    {
        var sums = new Dictionary<(int row, int col), double[]>();
        var perKind = new Dictionary<string, int>();
        GeoTiff? grid = null;
        var r = region.Copy(); r.ExpandBy(1000 / (WaterGraphTiler.MPerDegLat * Math.Cos(Math.Max(Math.Abs(r.MinY), Math.Abs(r.MaxY)) * Math.PI / 180)), 1000 / WaterGraphTiler.MPerDegLat);
        foreach (var (kind, tif) in files)
        {
            if (grid is not null && (grid.OriginX != tif.OriginX || grid.OriginY != tif.OriginY || grid.PixelW != tif.PixelW || grid.Width != tif.Width)) throw new InvalidDataException("the density files do not share one grid");
            grid ??= tif;
            var k = kind == "04" ? 0 : 1; perKind[kind] = perKind.GetValueOrDefault(kind) + 1;
            foreach (var (row, values) in tif.Rows())
                for (var col = 0; col < values.Length; col++)
                {
                    var v = values[col];
                    if (!(v > 0) || v == tif.NoData || float.IsInfinity(v)) continue;
                    if (!sums.TryGetValue((row, col), out var s))
                    {
                        var (lng, lat) = Laea.Inverse(tif.OriginX + (col + 0.5) * tif.PixelW, tif.OriginY - (row + 0.5) * tif.PixelH);
                        if (!r.Contains(lng, lat)) continue;
                        sums[(row, col)] = s = new double[2];
                    }
                    s[k] += v;
                }
        }
        var result = new List<TrafficCell>();
        if (grid is null) return result;
        foreach (var ((row, col), s) in sums)
        {
            double sailing = s[0] / Math.Max(1, perKind.GetValueOrDefault("04")), pleasure = s[1] / Math.Max(1, perKind.GetValueOrDefault("05"));
            if (sailing < MinDensity && pleasure < MinDensity) continue;
            double x0 = grid.OriginX + col * grid.PixelW, y0 = grid.OriginY - row * grid.PixelH, x1 = x0 + grid.PixelW, y1 = y0 - grid.PixelH;
            Coordinate C(double x, double y) { var (lng, lat) = Laea.Inverse(x, y); return new Coordinate(lng, lat); }
            var c0 = C(x0, y0);
            var poly = Geo.Factory.CreatePolygon([c0, C(x1, y0), C(x1, y1), C(x0, y1), c0.Copy()]);
            poly.SRID = 4326;
            result.Add(new TrafficCell(poly, sailing, pleasure));
        }
        return result;
    }

    /// <summary>Replaces routing_traffic with the cells (the upload always covers the whole region).</summary>
    public static async Task WriteAsync(NpgsqlConnection conn, IReadOnlyList<TrafficCell> cells, string years, CancellationToken ct)
    {
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await new NpgsqlCommand("DELETE FROM routing_traffic", conn, tx).ExecuteNonQueryAsync(ct);
        await using (var w = await conn.BeginBinaryImportAsync("COPY routing_traffic (geom, sailing, pleasure, years) FROM STDIN (FORMAT BINARY)", ct))
        {
            foreach (var c in cells)
            {
                await w.StartRowAsync(ct);
                await w.WriteAsync(c.Cell, NpgsqlDbType.Geometry, ct); await w.WriteAsync((float)c.Sailing, NpgsqlDbType.Real, ct);
                await w.WriteAsync((float)c.Pleasure, NpgsqlDbType.Real, ct); await w.WriteAsync(years, NpgsqlDbType.Text, ct);
            }
            await w.CompleteAsync(ct);
        }
        await tx.CommitAsync(ct);
    }
}
