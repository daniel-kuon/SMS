using System.Buffers.Binary;
using System.IO.Compression;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Minimal reader for single-band float32 GeoTIFFs (the EMODnet vessel density files: little-endian deflate strips, EPSG:3035; the
/// EMODnet Bathymetry WCS tiffs: big-endian uncompressed tiles, EPSG:4326). Classic TIFF only (no BigTIFF), compression none or deflate,
/// predictor 1, one sample per pixel; anything else throws <see cref="NotSupportedException"/> naming the tag. The geotransform comes
/// from ModelPixelScale (33550) and ModelTiepoint (33922) or ModelTransformation (34264), shifted by half a pixel for PixelIsPoint rasters (as GDAL does).
/// </summary>
public sealed class GeoTiff
{
    private readonly byte[] _b;
    private readonly bool _le;
    private readonly Dictionary<int, (int type, long count, int at)> _tags = [];
    public int Width { get; }
    public int Height { get; }
    /// <summary>Upper-left corner of the upper-left pixel and the pixel size (PixelH positive, y grows upwards).</summary>
    public double OriginX { get; }
    public double OriginY { get; }
    public double PixelW { get; }
    public double PixelH { get; }
    /// <summary>GDAL_NODATA (42113), NaN when absent.</summary>
    public float NoData { get; } = float.NaN;
    private readonly bool _deflate, _tiled;

    public static GeoTiff Open(Stream s)
    {
        if (s is MemoryStream ms && ms.TryGetBuffer(out var seg) && seg.Offset == 0 && seg.Count == seg.Array!.Length) return new GeoTiff(seg.Array);
        using var copy = new MemoryStream(); s.CopyTo(copy); return new GeoTiff(copy.ToArray());
    }

    /// <summary>The whole raster, row-major from the top row.</summary>
    public static (float[] values, int width, int height, double originX, double originY, double pixelW, double pixelH) ReadFloat32(Stream s)
    {
        var t = Open(s);
        return (t.ReadAll(), t.Width, t.Height, t.OriginX, t.OriginY, t.PixelW, t.PixelH);
    }

    public GeoTiff(byte[] bytes)
    {
        _b = bytes;
        _le = bytes.Length > 8 && bytes[0] == (byte)'I' && bytes[1] == (byte)'I';
        if (!_le && !(bytes.Length > 8 && bytes[0] == (byte)'M' && bytes[1] == (byte)'M')) throw new NotSupportedException("not a TIFF (byte order)");
        if (U16(2) != 42) throw new NotSupportedException($"TIFF magic {U16(2)} (BigTIFF is not supported)");
        var ifd = (int)U32(4); var n = U16(ifd);
        for (var i = 0; i < n; i++)
        {
            var at = ifd + 2 + 12 * i;
            int tag = U16(at), type = U16(at + 2); long count = U32(at + 4);
            var size = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 => 4, 5 or 10 or 12 => 8, _ => 0 };
            _tags[tag] = (type, count, size * count <= 4 ? at + 8 : (int)U32(at + 8));
        }
        Width = (int)Int(256); Height = (int)Int(257);
        if (Int(258, 32) != 32) throw new NotSupportedException($"tag 258 BitsPerSample {Int(258)}");
        if (Int(339, 1) != 3) throw new NotSupportedException($"tag 339 SampleFormat {Int(339, 1)} (float expected)");
        if (Int(277, 1) != 1) throw new NotSupportedException($"tag 277 SamplesPerPixel {Int(277)}");
        if (Int(317, 1) != 1) throw new NotSupportedException($"tag 317 Predictor {Int(317)}");
        var comp = Int(259, 1);
        if (comp is not (1 or 8 or 32946)) throw new NotSupportedException($"tag 259 Compression {comp}");
        _deflate = comp != 1;
        _tiled = _tags.ContainsKey(322);
        if (_tags.ContainsKey(33550) && _tags.ContainsKey(33922))
        {
            var scale = Doubles(33550); var tie = Doubles(33922);
            PixelW = scale[0]; PixelH = scale[1];
            OriginX = tie[3] - tie[0] * PixelW; OriginY = tie[4] + tie[1] * PixelH;
        }
        else if (_tags.ContainsKey(34264)) // ModelTransformation (the WCS tiffs): x = a·i + d, y = f·j + h, no rotation
        {
            var m = Doubles(34264);
            if (m[1] != 0 || m[4] != 0) throw new NotSupportedException("tag 34264 ModelTransformation with rotation");
            PixelW = m[0]; PixelH = -m[5]; OriginX = m[3]; OriginY = m[7];
        }
        else throw new NotSupportedException("tags 33550/33922 or 34264 (geotransform) missing");
        // GeoKeyDirectory: GTRasterTypeGeoKey (1025) = 2 → PixelIsPoint, the tiepoint is a pixel centre
        if (_tags.ContainsKey(34735))
        {
            var keys = Ints(34735);
            for (var k = 4; k + 3 < keys.Length; k += 4)
                if (keys[k] == 1025 && keys[k + 1] == 0 && keys[k + 3] == 2) { OriginX -= PixelW / 2; OriginY += PixelH / 2; }
        }
        if (_tags.TryGetValue(42113, out var nd))
        {
            var text = System.Text.Encoding.ASCII.GetString(_b, nd.at, (int)nd.count).TrimEnd('\0', ' ');
            if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) NoData = v;
        }
    }

    /// <summary>The raster, row-major from the top row (Width × Height floats).</summary>
    public float[] ReadAll()
    {
        var result = new float[(long)Width * Height];
        if (!_tiled)
        {
            foreach (var (row, values) in Rows()) values.CopyTo(result, (long)row * Width);
            return result;
        }
        int tw = (int)Int(322), th = (int)Int(323); var offsets = Ints(324); var counts = Ints(325);
        var across = (Width + tw - 1) / tw;
        var tile = new float[tw * th];
        for (var t = 0; t < offsets.Length; t++)
        {
            int r0 = t / across * th, c0 = t % across * tw;
            if (r0 >= Height) break;
            Decode(offsets[t], counts[t], tile);
            var w = Math.Min(tw, Width - c0);
            for (var r = 0; r < th && r0 + r < Height; r++) Array.Copy(tile, r * tw, result, (long)(r0 + r) * Width + c0, w);
        }
        return result;
    }

    /// <summary>Row by row (stripped rasters only): a 7508 × 6431 Europe file is read a few kB at a time.</summary>
    public IEnumerable<(int row, float[] values)> Rows()
    {
        if (_tiled) throw new NotSupportedException("tag 322 TileWidth: row access needs a stripped TIFF");
        var rps = (int)Math.Min(Int(278, Height), Height); var offsets = Ints(273); var counts = Ints(279);
        var strip = new float[(long)rps * Width];
        for (var s = 0; s < offsets.Length; s++)
        {
            var r0 = s * rps; var rows = Math.Min(rps, Height - r0);
            if (rows <= 0) yield break;
            Decode(offsets[s], counts[s], strip.AsSpan(0, rows * Width));
            for (var r = 0; r < rows; r++) yield return (r0 + r, strip.AsSpan(r * Width, Width).ToArray());
        }
    }

    private void Decode(long offset, long count, Span<float> into)
    {
        var bytes = into.Length * 4;
        byte[] raw;
        if (_deflate)
        {
            raw = new byte[bytes];
            using var z = new ZLibStream(new MemoryStream(_b, (int)offset, (int)count), CompressionMode.Decompress);
            var read = 0; int got;
            while (read < bytes && (got = z.Read(raw, read, bytes - read)) > 0) read += got;
        }
        else raw = _b.AsSpan((int)offset, (int)Math.Min(count, bytes)).ToArray();
        var n = Math.Min(into.Length, raw.Length / 4);
        for (var i = 0; i < n; i++) into[i] = _le ? BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(4 * i)) : BinaryPrimitives.ReadSingleBigEndian(raw.AsSpan(4 * i));
    }

    private int U16(int at) => _le ? BinaryPrimitives.ReadUInt16LittleEndian(_b.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(_b.AsSpan(at));
    private uint U32(int at) => _le ? BinaryPrimitives.ReadUInt32LittleEndian(_b.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(_b.AsSpan(at));

    private long Int(int tag, long? fallback = null) =>
        _tags.ContainsKey(tag) ? Ints(tag)[0] : fallback ?? throw new NotSupportedException($"tag {tag} missing");

    private long[] Ints(int tag)
    {
        var (type, count, at) = _tags[tag];
        var r = new long[count];
        for (var i = 0; i < count; i++) r[i] = type switch { 3 => U16(at + 2 * i), 4 => U32(at + 4 * i), 1 => _b[at + i], _ => throw new NotSupportedException($"tag {tag} type {type}") };
        return r;
    }

    private double[] Doubles(int tag)
    {
        var (type, count, at) = _tags[tag];
        if (type != 12) throw new NotSupportedException($"tag {tag} type {type} (double expected)");
        var r = new double[count];
        for (var i = 0; i < count; i++) r[i] = _le ? BinaryPrimitives.ReadDoubleLittleEndian(_b.AsSpan(at + 8 * i)) : BinaryPrimitives.ReadDoubleBigEndian(_b.AsSpan(at + 8 * i));
        return r;
    }
}

/// <summary>ETRS89 Lambert azimuthal equal-area (EPSG:3035) → WGS84 longitude/latitude: Snyder's ellipsoidal inverse (1987, pp. 187–190)
/// with the authalic latitude series; GRS80, φ0 = 52°, λ0 = 10°, false origin 4 321 000 / 3 210 000.</summary>
public static class Laea
{
    private const double A = 6_378_137, F = 1 / 298.257222101, Lat0 = 52 * Math.PI / 180, Lng0 = 10, X0 = 4_321_000, Y0 = 3_210_000;
    private static readonly double E2 = 2 * F - F * F, E = Math.Sqrt(E2);
    private static double Q(double sinPhi) => (1 - E2) * (sinPhi / (1 - E2 * sinPhi * sinPhi) - 1 / (2 * E) * Math.Log((1 - E * sinPhi) / (1 + E * sinPhi)));
    private static readonly double Qp = Q(1), Rq = A * Math.Sqrt(Qp / 2), Beta1 = Math.Asin(Q(Math.Sin(Lat0)) / Qp),
        D = A * Math.Cos(Lat0) / Math.Sqrt(1 - E2 * Math.Sin(Lat0) * Math.Sin(Lat0)) / (Rq * Math.Cos(Beta1));

    public static (double lng, double lat) Inverse(double easting, double northing)
    {
        double x = easting - X0, y = northing - Y0;
        var rho = Math.Sqrt(x / D * (x / D) + D * y * (D * y));
        if (rho < 1e-9) return (Lng0, Lat0 * 180 / Math.PI);
        var ce = 2 * Math.Asin(rho / (2 * Rq));
        double sinCe = Math.Sin(ce), cosCe = Math.Cos(ce);
        var beta = Math.Asin(cosCe * Math.Sin(Beta1) + D * y * sinCe * Math.Cos(Beta1) / rho);
        var lam = Math.Atan2(x * sinCe, D * rho * Math.Cos(Beta1) * cosCe - D * D * y * Math.Sin(Beta1) * sinCe);
        double e4 = E2 * E2, e6 = e4 * E2;
        var phi = beta + (E2 / 3 + 31 * e4 / 180 + 517 * e6 / 5040) * Math.Sin(2 * beta) + (23 * e4 / 360 + 251 * e6 / 3780) * Math.Sin(4 * beta) + 761 * e6 / 45360 * Math.Sin(6 * beta);
        return (Lng0 + lam * 180 / Math.PI, phi * 180 / Math.PI);
    }
}
