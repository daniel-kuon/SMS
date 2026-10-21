using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Npgsql;
using NpgsqlTypes;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// The EMODnet Bathymetry DTM 2024 (1/16′ cells, routing_dtm) as "known deep" evidence: it may only lift the "not definitely deep enough"
/// verdict of an edge (SeaGraph.Uncertain), never close, open or downgrade anything. A cell averages a channel with its banks, which only
/// makes it shallower: a cell reading deep enough is trusted on its own (a one-cell-wide sound such as Baggensstäket counts), a shallow
/// reading proves nothing (Bøgestrøm 1.7–1.9 m is sailed with 1.8 m). Land, cells above datum, no data and cells the DTM only
/// interpolated (no soundings, EMODnet interpolation_flag) are stored as short.MinValue and never count. Chunks of 1° × 1°
/// (Cells × Cells, row 0 = north, decimetres below datum), loaded on first touch.
/// </summary>
public sealed class DtmGrid(Func<(int lng, int lat), short[]?> loadChunk, int cells = DtmGrid.Cells)
{
    public const int Cells = 960;
    /// <summary>An edge whose band floor lies at or above this is not sampled (it is uncertain only for a minimum depth above it: drafts over 5 m).</summary>
    public const double MaxFloorM = 5.4864;
    private readonly ConcurrentDictionary<(int, int), short[]?> _chunks = new();
    public static readonly DtmGrid Empty = new(_ => null);
    public int Chunks => _chunks.Count(c => c.Value is not null);

    /// <summary>Depth (m) of the measured water cell at the point; null for land, no data or an interpolated cell.</summary>
    public double? Depth(double lat, double lng)
    {
        long gx = (long)Math.Floor(lng * cells), gy = (long)Math.Floor(lat * cells);
        var key = ((int)Math.Floor((double)gx / cells), (int)Math.Floor((double)gy / cells));
        var chunk = _chunks.GetOrAdd(key, loadChunk);
        if (chunk is null) return null;
        var v = chunk[(cells - 1 - (gy - (long)key.Item2 * cells)) * cells + gx - (long)key.Item1 * cells];
        return v == short.MinValue ? null : v / 10.0;
    }

    /// <summary>The shallowest DTM depth along the edge (samples every ~stepM, segment midpoints), when at least 90 % of the samples
    /// lie in measured water cells; null otherwise.</summary>
    public double? MinDepth(LineString edge, double stepM = 25)
    {
        int n = 0, water = 0; var min = double.PositiveInfinity;
        for (var i = 1; i < edge.NumPoints; i++)
        {
            var a = edge.GetCoordinateN(i - 1); var b = edge.GetCoordinateN(i);
            var k = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / stepM));
            for (var j = 0; j < k; j++)
            {
                n++;
                if (Depth(a.Y + (b.Y - a.Y) * (j + 0.5) / k, a.X + (b.X - a.X) * (j + 0.5) / k) is { } d) { water++; if (d < min) min = d; }
            }
        }
        return n > 0 && water >= 0.9 * n ? min : null;
    }

    /// <summary>Chunks from routing_dtm, each read on first touch over a connection of its own (sync: the callers are the graph build's
    /// edge loop and the smoothing's leg tests).</summary>
    /// <param name="measuredOnly">Cells the DTM only interpolated (no soundings) do not count (Routing:DtmMeasuredOnly).</param>
    public static DtmGrid FromDatabase(NpgsqlConnection template, bool measuredOnly) => new(key =>
    {
        using var cn = (NpgsqlConnection)((ICloneable)template).Clone(); // keeps the password, which ConnectionString drops after Open
        cn.Open();
        using var cmd = new NpgsqlCommand("SELECT cells, water, elevation, interpolated FROM routing_dtm WHERE min_lng = @x AND min_lat = @y", cn);
        cmd.Parameters.AddWithValue("x", (double)key.lng); cmd.Parameters.AddWithValue("y", (double)key.lat);
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.GetInt32(0) != Cells) return null;
        return DtmChunk.Decimetres(Cells * Cells, r.GetFieldValue<byte[]>(1), MemoryMarshal.Cast<byte, float>(r.GetFieldValue<byte[]>(2)).ToArray(), r.GetFieldValue<byte[]>(3), measuredOnly);
    });
}

/// <summary>A DTM subset: the griddap variables per cell (elevation: negative = depth below datum; interpolation_flag, value_count,
/// elevation_min/max, stdev, cdi_index), row 0 = north.</summary>
public sealed record DtmRaster(IReadOnlyDictionary<string, double[]> Vars, int Width, int Height, double West, double North, double StepX, double StepY)
{
    public static DtmRaster FromGeoTiff(GeoTiff t) => new(new Dictionary<string, double[]> { ["elevation"] = Array.ConvertAll(t.ReadAll(), v => (double)v) }, t.Width, t.Height, t.OriginX, t.OriginY, t.PixelW, t.PixelH);

    /// <summary>An ERDDAP griddap NetCDF-3 subset (latitude ascending) of bathymetry_dtm_2024.</summary>
    public static DtmRaster FromNetCdf(byte[] nc)
    {
        var f = NetCdf3.Read(nc);
        var lat = f.Doubles("latitude"); var lng = f.Doubles("longitude");
        int h = lat.Length, w = lng.Length;
        var vars = new Dictionary<string, double[]>();
        foreach (var name in DtmImport.Variables.Where(f.Has))
        {
            var v = f.Doubles(name); var north = new double[w * h];
            for (var r = 0; r < h; r++) Array.Copy(v, (h - 1 - r) * w, north, r * w, w); // north first
            vars[name] = north;
        }
        double sx = w > 1 ? (lng[^1] - lng[0]) / (w - 1) : 1.0 / DtmGrid.Cells, sy = h > 1 ? (lat[^1] - lat[0]) / (h - 1) : 1.0 / DtmGrid.Cells;
        return new DtmRaster(vars, w, h, lng[0] - sx / 2, lat[^1] + sy / 2, sx, sy);
    }
}

/// <summary>One 1° chunk as stored in routing_dtm: the water cells only (elevation &lt; 0), packed in row order (row 0 = north) behind a
/// bit mask of the Cells × Cells grid, with every quality field of the DTM (reprocessable without a new download).</summary>
public sealed record DtmChunk(byte[] Water, float[] Elevation, byte[] Interpolated, int[] ValueCount, float[] ElevationMin, float[] ElevationMax, float[] Stdev, int[] CdiIndex)
{
    public int Count => Elevation.Length;

    /// <summary>Per cell of the grid: decimetres of depth, short.MinValue for land, no data and (measuredOnly) interpolated cells.</summary>
    public static short[] Decimetres(int cellCount, byte[] water, float[] elevation, byte[] interpolated, bool measuredOnly)
    {
        var result = new short[cellCount]; var k = 0;
        for (var i = 0; i < result.Length; i++)
        {
            if ((water[i >> 3] & (1 << (i & 7))) == 0) { result[i] = short.MinValue; continue; }
            result[i] = DtmImport.ToDecimetres(elevation[k], measuredOnly && interpolated[k] == 1); k++;
        }
        return result;
    }
    public short[] Decimetres(bool measuredOnly, int cells = DtmGrid.Cells) => Decimetres(cells * cells, Water, Elevation, Interpolated, measuredOnly);
}

/// <summary>Import of the EMODnet DTM 2024 into routing_dtm, one 1° × 1° chunk per ERDDAP griddap request (POST /api/admin/routing/import-dtm).</summary>
public static class DtmImport
{
    /// <summary>The griddap variables fetched and stored (the dataset's elevation and all its quality fields).</summary>
    public static readonly string[] Variables = ["elevation", "interpolation_flag", "value_count", "elevation_min", "elevation_max", "stdev", "cdi_index"];

    /// <summary>Decimetres of depth; short.MinValue for land, datum or above, no data and an excluded (interpolated) cell.</summary>
    public static short ToDecimetres(double v, bool excluded = false) => excluded || double.IsNaN(v) || v >= 0 ? short.MinValue : (short)Math.Min(short.MaxValue, Math.Round(-v * 10));

    /// <summary>The chunk with south-west corner (lng, lat) from a raster in EPSG:4326 covering it (cells placed by their centres; outside → land).</summary>
    public static DtmChunk Chunk(DtmRaster r, int lng, int lat, int cells = DtmGrid.Cells)
    {
        var src = new List<long>(); var mask = new byte[(cells * cells + 7) / 8];
        var elev = r.Vars["elevation"];
        for (var row = 0; row < cells; row++)
        {
            var sr = (int)Math.Floor((r.North - (lat + 1 - (row + 0.5) / cells)) / r.StepY);
            for (var c = 0; c < cells; c++)
            {
                var sc = (int)Math.Floor((lng + (c + 0.5) / cells - r.West) / r.StepX);
                if (sr < 0 || sc < 0 || sr >= r.Height || sc >= r.Width) continue;
                var i = (long)sr * r.Width + sc;
                if (!(elev[i] < 0)) continue; // land, above datum, no data
                var at = row * cells + c; mask[at >> 3] |= (byte)(1 << (at & 7)); src.Add(i);
            }
        }
        T[] Get<T>(string name, T missing, Func<double, T> conv) => r.Vars.TryGetValue(name, out var v) ? src.Select(i => double.IsNaN(v[i]) ? missing : conv(v[i])).ToArray() : src.Select(_ => missing).ToArray();
        return new DtmChunk(mask, Get("elevation", float.NaN, v => (float)v), Get("interpolation_flag", (byte)127, v => (byte)v), Get("value_count", -1, v => (int)v),
            Get("elevation_min", float.NaN, v => (float)v), Get("elevation_max", float.NaN, v => (float)v), Get("stdev", float.NaN, v => (float)v), Get("cdi_index", -1, v => (int)v));
    }

    /// <summary>The griddap query for a 1° chunk (cell centres, native resolution, stride 1) with every variable.</summary>
    public static string Url(string griddapNc, int lng, int lat)
    {
        string S(double v) => v.ToString("0.0000000", System.Globalization.CultureInfo.InvariantCulture);
        var sub = $"%5B({S(lat + 0.5 / DtmGrid.Cells)}):1:({S(lat + 1 - 0.5 / DtmGrid.Cells)})%5D%5B({S(lng + 0.5 / DtmGrid.Cells)}):1:({S(lng + 1 - 0.5 / DtmGrid.Cells)})%5D";
        return griddapNc + "?" + string.Join(",", Variables.Select(v => v + sub));
    }

    /// <summary>Fetches and stores every 1° chunk whose south-west corner lies in [s, n) × [w, e) (4 requests in parallel); a failed chunk is logged and skipped.</summary>
    public static async Task<(int chunks, long water, long measured, int failed)> RunAsync(HttpClient http, string griddapNc, NpgsqlConnection conn, int s, int w, int n, int e, ILogger logger, CancellationToken ct)
    {
        var keys = Enumerable.Range(s, n - s).SelectMany(lat => Enumerable.Range(w, e - w).Select(lng => (lng, lat))).ToList();
        int chunks = 0, failed = 0; long water = 0, measured = 0;
        using var write = new SemaphoreSlim(1);
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await Parallel.ForEachAsync(keys, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (k, c) =>
        {
            try
            {
                var raster = DtmRaster.FromNetCdf(await http.GetByteArrayAsync(Url(griddapNc, k.lng, k.lat), c));
                if (Math.Abs(raster.StepX * DtmGrid.Cells - 1) > 1e-6 || Math.Abs(raster.StepY * DtmGrid.Cells - 1) > 1e-6) throw new InvalidDataException($"cell size {raster.StepX} × {raster.StepY}, 1/{DtmGrid.Cells}° expected");
                var chunk = Chunk(raster, k.lng, k.lat);
                await write.WaitAsync(c);
                try
                {
                    await WriteAsync(conn, k.lng, k.lat, chunk, c);
                    chunks++; water += chunk.Count; measured += chunk.Interpolated.Count(f => f == 0);
                }
                finally { write.Release(); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Interlocked.Increment(ref failed); logger.LogWarning(ex, "DTM chunk {Lng},{Lat} failed", k.lng, k.lat); }
        });
        return (chunks, water, measured, failed);
    }

    public static async Task WriteAsync(NpgsqlConnection conn, int lng, int lat, DtmChunk chunk, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO routing_dtm (min_lng, min_lat, cells, step, water, elevation, interpolated, value_count, elevation_min, elevation_max, stdev, cdi_index)
            VALUES (@x, @y, @n, @s, @water, @elev, @interp, @count, @min, @max, @stdev, @cdi)
            ON CONFLICT (min_lng, min_lat) DO UPDATE SET cells = EXCLUDED.cells, step = EXCLUDED.step, water = EXCLUDED.water, elevation = EXCLUDED.elevation,
              interpolated = EXCLUDED.interpolated, value_count = EXCLUDED.value_count, elevation_min = EXCLUDED.elevation_min, elevation_max = EXCLUDED.elevation_max,
              stdev = EXCLUDED.stdev, cdi_index = EXCLUDED.cdi_index, imported_at = now()
            """, conn);
        cmd.Parameters.AddWithValue("x", (double)lng); cmd.Parameters.AddWithValue("y", (double)lat); cmd.Parameters.AddWithValue("n", DtmGrid.Cells); cmd.Parameters.AddWithValue("s", 1.0 / DtmGrid.Cells);
        void Bytes<T>(string name, T[] values) where T : struct => cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Bytea) { Value = MemoryMarshal.AsBytes(values.AsSpan()).ToArray() });
        Bytes("water", chunk.Water); Bytes("elev", chunk.Elevation); Bytes("interp", chunk.Interpolated); Bytes("count", chunk.ValueCount);
        Bytes("min", chunk.ElevationMin); Bytes("max", chunk.ElevationMax); Bytes("stdev", chunk.Stdev); Bytes("cdi", chunk.CdiIndex);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>Minimal NetCDF-3 (classic and 64-bit offset) reader for ERDDAP griddap subsets: non-record variables of numeric types.</summary>
public sealed class NetCdf3
{
    private readonly byte[] _b;
    private readonly Dictionary<string, (int type, long count, long begin)> _vars = [];
    private NetCdf3(byte[] b) { _b = b; }

    public static NetCdf3 Read(byte[] b)
    {
        if (b.Length < 8 || b[0] != 'C' || b[1] != 'D' || b[2] != 'F' || b[3] is not (1 or 2)) throw new NotSupportedException("not a NetCDF-3 file (NetCDF-4/HDF5 is not supported)");
        var f = new NetCdf3(b); var wide = b[3] == 2; var at = 8; // magic, numrecs
        int I() { var v = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(at)); at += 4; return v; }
        string Name() { var n = I(); var s = System.Text.Encoding.UTF8.GetString(b, at, n); at += (n + 3) / 4 * 4; return s; }
        int Size(int t) => t switch { 1 or 2 => 1, 3 => 2, 4 or 5 => 4, 6 => 8, _ => throw new NotSupportedException($"NetCDF type {t}") };
        void Attributes() { I(); var n = I(); for (var i = 0; i < n; i++) { Name(); var t = I(); var c = I(); at += (c * Size(t) + 3) / 4 * 4; } }
        I(); var nd = I(); var dims = new List<long>();
        for (var i = 0; i < nd; i++) { Name(); dims.Add(I()); }
        Attributes();
        I(); var nv = I();
        for (var i = 0; i < nv; i++)
        {
            var name = Name(); var rank = I(); long count = 1;
            for (var d = 0; d < rank; d++) count *= dims[I()];
            Attributes();
            var type = I(); I(); // vsize
            long begin = wide ? BinaryPrimitives.ReadInt64BigEndian(b.AsSpan(at)) : (uint)BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(at)); at += wide ? 8 : 4;
            f._vars[name] = (type, count, begin);
        }
        return f;
    }

    public bool Has(string name) => _vars.ContainsKey(name);

    public double[] Doubles(string name)
    {
        var (type, count, begin) = _vars[name];
        var r = new double[count];
        for (var i = 0; i < count; i++)
        {
            var s = _b.AsSpan((int)(begin + i * (type switch { 1 or 2 => 1, 3 => 2, 4 or 5 => 4, _ => 8 })));
            r[i] = type switch { 1 => (sbyte)s[0], 2 => s[0], 3 => BinaryPrimitives.ReadInt16BigEndian(s), 4 => BinaryPrimitives.ReadInt32BigEndian(s), 5 => BinaryPrimitives.ReadSingleBigEndian(s), _ => BinaryPrimitives.ReadDoubleBigEndian(s) };
        }
        return r;
    }

    public float[] Floats(string name) => Array.ConvertAll(Doubles(name), v => (float)v);
}
