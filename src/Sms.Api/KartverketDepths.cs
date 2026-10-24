using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using Npgsql;
using NpgsqlTypes;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;

namespace Sms.Api;

/// <summary>
/// Kartverket "Sjøkart – Dybdedata" (WFS 2.0, GML 3.2, CC BY 4.0, "ikke godkjent for navigasjon") into routing_depth, per 1° chunk:
/// Dybdeareal (min/max depth in metres), Tørrfall (drying, 0 m), Grunne (shoal with its depth) and Skjær (rock, 0 m) as small discs,
/// IkkeKartlagtSjømåltOmr as "kartverket-unsurveyed" (0 m in the table; the graph build makes it uncertain, DepthLimits.UnsurveyedSource).
/// Areas are cut to a <see cref="CellDeg"/> grid inside the chunk (pieces like the Seascape tiles, no duplicates across chunks); the
/// chunk's kartverket rows are replaced, and other sources' areas lose the parts the kartverket rows cover (DepthImport.Outside).
/// Dybdepunkt (spot soundings) are not imported: inside the areas they only repeat the floor, and the DTM grid averages its cells.
/// </summary>
public static class KartverketDepths
{
    public const string Source = "kartverket", UnsurveyedSource = DepthLimits.UnsurveyedSource;
    public const string Attribution = "Dybdedata © Kartverket (CC BY 4.0), ikke for navigasjon";
    public const string WfsUrl = "https://wfs.geonorge.no/skwms1/wfs.dybdedata";
    public const double CellDeg = 0.05, ShoalRadiusM = 20;
    public const int PageSize = 5000;
    /// <summary>The feature types read; the points (<see cref="PointTypes"/>) become discs of <see cref="ShoalRadiusM"/>.</summary>
    public static readonly string[] Types = ["Dybdeareal", "Tørrfall", "IkkeKartlagtSjømåltOmr", "Grunne", "Skjær"], PointTypes = ["Grunne", "Skjær"];
    private static readonly XNamespace Gml = "http://www.opengis.net/gml/3.2";
    private const string Wfs = "http://www.opengis.net/wfs/2.0";

    /// <summary>The depth areas of a GetFeature response (srsName EPSG::4326: lat,lon order); an ExceptionReport throws.</summary>
    public static List<DepthArea> Parse(Stream gml) => Page(gml).areas;

    /// <summary>The areas of a response and its number of features (a page is full when that reaches the count).</summary>
    private static (List<DepthArea> areas, int features) Page(Stream gml)
    {
        var result = new List<DepthArea>(); var features = 0;
        using var r = XmlReader.Create(gml, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit });
        r.MoveToContent();
        if (r.LocalName == "ExceptionReport") throw new InvalidDataException("Kartverket WFS: " + XElement.Load(r).Value.Trim());
        while (!r.EOF)
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "member" && r.NamespaceURI == Wfs) { features++; if (((XElement)XNode.ReadFrom(r)).Elements().FirstOrDefault() is { } f) Feature(f, result); }
            else r.Read();
        return (result, features);
    }

    private static void Feature(XElement f, List<DepthArea> into)
    {
        double? Num(string name) => double.TryParse((string?)f.Element(f.Name.Namespace + name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        Coordinate? At() => f.Descendants(Gml + "pos").FirstOrDefault() is { } p && Coords(p.Value) is [var c] ? c : null;
        switch (f.Name.LocalName)
        {
            case "Dybdeareal" when Num("minimumsdybde") is { } min:
                foreach (var p in Polygons(f)) into.Add(new DepthArea(p, min, Num("maksimumsdybde") ?? min, Source)); break;
            case "Tørrfall": foreach (var p in Polygons(f)) into.Add(new DepthArea(p, 0, 0, Source)); break;
            case "IkkeKartlagtSjømåltOmr": foreach (var p in Polygons(f)) into.Add(new DepthArea(p, 0, 0, UnsurveyedSource)); break;
            case "Grunne" when At() is { } c: var d = Num("dybde") ?? 0; into.Add(new DepthArea(Disc(c), d, d, Source)); break;
            case "Skjær" when At() is { } c: into.Add(new DepthArea(Disc(c), 0, 0, Source)); break;
        }
    }

    /// <summary>gml:Polygon and gml:PolygonPatch (MultiSurface/Surface) with their rings; a ring's posLists in order (Ring/curveMember too).</summary>
    private static IEnumerable<Polygon> Polygons(XElement f)
    {
        LinearRing? Ring(XElement? e)
        {
            var cs = e?.Descendants(Gml + "posList").SelectMany(p => Coords(p.Value)).ToList();
            if (cs is not { Count: >= 3 }) return null;
            if (!cs[0].Equals2D(cs[^1])) cs.Add(cs[0].Copy());
            return cs.Count >= 4 ? Geo.Factory.CreateLinearRing(cs.ToArray()) : null;
        }
        foreach (var p in f.Descendants().Where(e => e.Name == Gml + "Polygon" || e.Name == Gml + "PolygonPatch"))
            if (Ring(p.Element(Gml + "exterior")) is { } shell && Geo.Factory.CreatePolygon(shell, p.Elements(Gml + "interior").Select(Ring).OfType<LinearRing>().ToArray()) is var poly)
            {
                // ~1 % of the Dybdeareal rings touch themselves (434 in 58-59 N 8-9 E): fixed here, an invalid polygon fails the overlays later
                if (poly.IsValid) yield return poly;
                else foreach (var part in DepthImport.Polygons(GeometryFixer.Fix(poly))) yield return part;
            }
    }

    private static List<Coordinate> Coords(string posList)
    {
        var v = posList.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        var result = new List<Coordinate>(v.Length / 2);
        for (var i = 0; i + 1 < v.Length; i += 2) result.Add(new Coordinate(v[i + 1], v[i])); // lat lon → x = lon
        return result;
    }

    private static Polygon Disc(Coordinate c)
    {
        var dy = ShoalRadiusM / WaterGraphTiler.MPerDegLat; var dx = dy / Math.Cos(c.Y * Math.PI / 180);
        var ring = Enumerable.Range(0, 9).Select(i => new Coordinate(c.X + dx * Math.Cos(i * Math.PI / 4), c.Y + dy * Math.Sin(i * Math.PI / 4))).ToArray();
        ring[8] = ring[0].Copy();
        return Geo.Factory.CreatePolygon(ring);
    }

    /// <summary>Every type of the chunk, paged by <see cref="PageSize"/> (count/startIndex; the bbox is lat,lon).</summary>
    public static async Task<Dictionary<string, List<DepthArea>>> FetchAsync(HttpClient client, Envelope chunk, CancellationToken ct, string baseUrl = WfsUrl)
    {
        var inv = CultureInfo.InvariantCulture;
        var result = new Dictionary<string, List<DepthArea>>();
        foreach (var type in Types)
        {
            var all = new List<DepthArea>();
            for (var start = 0; ; start += PageSize)
            {
                var url = string.Create(inv, $"{baseUrl}?service=WFS&version=2.0.0&request=GetFeature&typeNames=app:{Uri.EscapeDataString(type)}&srsName=urn:ogc:def:crs:EPSG::4326&bbox={chunk.MinY},{chunk.MinX},{chunk.MaxY},{chunk.MaxX},urn:ogc:def:crs:EPSG::4326&count={PageSize}&startIndex={start}");
                using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var (areas, features) = Page(stream);
                all.AddRange(areas);
                if (features < PageSize) break;
            }
            result[type] = all;
        }
        return result;
    }

    /// <summary>The chunk's rows of a fetch: areas cut to the grid cells inside the chunk, point discs whole when their centre lies in
    /// it (half-open, so a point on a chunk line goes to one chunk) and in no fetched area as shallow or shallower (a shoal inside a
    /// 2-5 m area at 3 m changes no edge's floor: most of them).</summary>
    public static List<DepthArea> Rows(Dictionary<string, List<DepthArea>> fetched, Envelope chunk)
    {
        var areas = new STRtree<(IPreparedGeometry prepared, double depth)>();
        foreach (var (type, list) in fetched)
            if (!PointTypes.Contains(type)) foreach (var a in list.Where(a => a.Source == Source)) areas.Insert(a.Area.EnvelopeInternal, (PreparedGeometryFactory.Prepare(a.Area), a.MinDepthM));
        areas.Build();
        bool Keep(DepthArea d)
        {
            var c = d.Area.EnvelopeInternal.Centre;
            if (c.X < chunk.MinX || c.X >= chunk.MaxX || c.Y < chunk.MinY || c.Y >= chunk.MaxY) return false;
            var pt = Geo.Factory.CreatePoint(c);
            return !areas.Query(pt.EnvelopeInternal).Any(a => a.depth <= d.MinDepthM && a.prepared.Contains(pt));
        }
        return fetched.SelectMany(kv => PointTypes.Contains(kv.Key) ? kv.Value.Where(Keep) : Grid(kv.Value, chunk)).ToList();
    }

    /// <summary>Areas cut to the <see cref="CellDeg"/> grid cells inside the chunk (an area inside one cell stays whole).</summary>
    public static List<DepthArea> Grid(IEnumerable<DepthArea> areas, Envelope chunk)
    {
        var result = new List<DepthArea>();
        foreach (var a in areas)
        {
            var env = a.Area.EnvelopeInternal;
            int x0 = (int)Math.Floor((Math.Max(env.MinX, chunk.MinX) - chunk.MinX) / CellDeg), x1 = (int)Math.Ceiling((Math.Min(env.MaxX, chunk.MaxX) - chunk.MinX) / CellDeg);
            int y0 = (int)Math.Floor((Math.Max(env.MinY, chunk.MinY) - chunk.MinY) / CellDeg), y1 = (int)Math.Ceiling((Math.Min(env.MaxY, chunk.MaxY) - chunk.MinY) / CellDeg);
            for (var x = x0; x < x1; x++)
                for (var y = y0; y < y1; y++)
                {
                    var cell = new Envelope(chunk.MinX + x * CellDeg, Math.Min(chunk.MinX + (x + 1) * CellDeg, chunk.MaxX), chunk.MinY + y * CellDeg, Math.Min(chunk.MinY + (y + 1) * CellDeg, chunk.MaxY));
                    if (!cell.Intersects(env)) continue;
                    if (cell.Contains(env)) { result.Add(a); continue; }
                    Geometry piece;
                    try { piece = OverlayNGRobust.Overlay(a.Area, Geo.Factory.ToGeometry(cell), SpatialFunction.Intersection); }
                    catch (TopologyException) { piece = OverlayNGRobust.Overlay(GeometryFixer.Fix(a.Area), Geo.Factory.ToGeometry(cell), SpatialFunction.Intersection); }
                    foreach (var p in DepthImport.Polygons(piece)) result.Add(a with { Area = p });
                }
        }
        return result;
    }

    public sealed record ChunkResult(string Chunk, Dictionary<string, int> Features, int Rows, int Removed, int Clipped);

    /// <summary>Imports the 1° chunks of s,w,n,e (clamped to the bbox) one after another, each in one transaction.</summary>
    public static async Task<List<ChunkResult>> RunAsync(HttpClient client, NpgsqlConnection conn, double s, double w, double n, double e, ILogger log, CancellationToken ct)
    {
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        var results = new List<ChunkResult>();
        for (var lat = Math.Floor(s); lat < n; lat++)
            for (var lng = Math.Floor(w); lng < e; lng++)
            {
                var chunk = new Envelope(Math.Max(lng, w), Math.Min(lng + 1, e), Math.Max(lat, s), Math.Min(lat + 1, n));
                if (chunk.Width <= 0 || chunk.Height <= 0) continue;
                var fetched = await FetchAsync(client, chunk, ct);
                var rows = Rows(fetched, chunk);
                var r = await WriteAsync(conn, chunk, rows, ct);
                var name = string.Create(CultureInfo.InvariantCulture, $"{chunk.MinY},{chunk.MinX},{chunk.MaxY},{chunk.MaxX}");
                results.Add(new ChunkResult(name, fetched.ToDictionary(kv => kv.Key, kv => kv.Value.Count), rows.Count, r.removed, r.clipped));
                log.LogInformation("Kartverket depths {Chunk}: {Features} → {Rows} rows, {Removed} replaced, {Clipped} other areas clipped", name, string.Join(", ", fetched.Select(kv => $"{kv.Key} {kv.Value.Count}")), rows.Count, r.removed, r.clipped);
            }
        return results;
    }

    private static readonly string[] Sources = [Source, UnsurveyedSource];

    /// <summary>Replaces the chunk's kartverket rows (by a point on their surface: the grid pieces and discs of this chunk) and cuts the
    /// other sources' areas that meet kartverket rows (precedence: metric and finer than the fathom bands).</summary>
    private static async Task<(int removed, int clipped)> WriteAsync(NpgsqlConnection conn, Envelope chunk, List<DepthArea> rows, CancellationToken ct)
    {
        await using var tx = await conn.BeginTransactionAsync(ct);
        int removed;
        await using (var del = new NpgsqlCommand("DELETE FROM routing_depth WHERE source = ANY(@src) AND geom && ST_MakeEnvelope(@x1,@y1,@x2,@y2,4326) AND ST_Covers(ST_MakeEnvelope(@x1,@y1,@x2,@y2,4326), ST_PointOnSurface(geom))", conn, tx) { CommandTimeout = 3600 })
        {
            del.Parameters.AddWithValue("src", Sources); Box(del, chunk);
            removed = await del.ExecuteNonQueryAsync(ct);
        }
        await CopyAsync(conn, rows, ct);
        // the other sources' areas in the chunk and the kartverket rows around them
        var others = new List<(long id, DepthArea area)>();
        await using (var cmd = new NpgsqlCommand("SELECT id, geom, min_depth_m, max_depth_m, source FROM routing_depth WHERE source <> ALL(@src) AND geom && ST_MakeEnvelope(@x1,@y1,@x2,@y2,4326)", conn, tx) { CommandTimeout = 3600 })
        {
            cmd.Parameters.AddWithValue("src", Sources); Box(cmd, chunk);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) if (r.GetFieldValue<Geometry>(1) is Polygon p) others.Add((r.GetInt64(0), new DepthArea(p, r.GetDouble(2), r.GetDouble(3), r.GetString(4))));
        }
        if (others.Count == 0) { await tx.CommitAsync(ct); return (removed, 0); }
        var around = new Envelope(); foreach (var (_, a) in others) around.ExpandToInclude(a.Area.EnvelopeInternal);
        var cover = new List<Geometry>();
        await using (var cmd = new NpgsqlCommand("SELECT geom FROM routing_depth WHERE source = ANY(@src) AND geom && ST_MakeEnvelope(@x1,@y1,@x2,@y2,4326)", conn, tx) { CommandTimeout = 3600 })
        {
            cmd.Parameters.AddWithValue("src", Sources); Box(cmd, around);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) cover.Add(r.GetFieldValue<Geometry>(0));
        }
        var index = DepthImport.CoverIndex(cover);
        var cut = new List<long>(); var rest = new List<DepthArea>();
        foreach (var (id, a) in others)
            if (DepthImport.Outside(a.Area, index) is { } parts) { cut.Add(id); rest.AddRange(parts.Select(p => a with { Area = p })); }
        if (cut.Count > 0)
        {
            await using (var del = new NpgsqlCommand("DELETE FROM routing_depth WHERE id = ANY(@ids)", conn, tx) { CommandTimeout = 3600 })
            {
                del.Parameters.AddWithValue("ids", cut.ToArray());
                await del.ExecuteNonQueryAsync(ct);
            }
            await CopyAsync(conn, rest, ct);
        }
        await tx.CommitAsync(ct);
        return (removed, cut.Count);
    }

    private static void Box(NpgsqlCommand cmd, Envelope e)
    {
        cmd.Parameters.AddWithValue("x1", e.MinX); cmd.Parameters.AddWithValue("y1", e.MinY); cmd.Parameters.AddWithValue("x2", e.MaxX); cmd.Parameters.AddWithValue("y2", e.MaxY);
    }

    public static async Task CopyAsync(NpgsqlConnection conn, IEnumerable<DepthArea> areas, CancellationToken ct)
    {
        await using var w = await conn.BeginBinaryImportAsync("COPY routing_depth (geom, min_depth_m, max_depth_m, source) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var a in areas)
        {
            await w.StartRowAsync(ct);
            await w.WriteAsync(a.Area, NpgsqlDbType.Geometry, ct); await w.WriteAsync(a.MinDepthM, NpgsqlDbType.Double, ct);
            await w.WriteAsync(a.MaxDepthM, NpgsqlDbType.Double, ct); await w.WriteAsync(a.Source, NpgsqlDbType.Text, ct);
        }
        await w.CompleteAsync(ct);
    }
}
