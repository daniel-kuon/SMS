using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Sms.Api;
using Sms.Domain;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class KartverketDepthsTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static List<DepthArea> Fixture()
    {
        using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "kartverket-dybdedata.xml"));
        return KartverketDepths.Parse(s);
    }
    private static Polygon Box(double x1, double y1, double x2, double y2) => F.CreatePolygon([new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2), new(x1, y1)]);
    private static GraphEdge Through(Polygon p)
    {
        var c = p.InteriorPoint.Coordinate;
        return new GraphEdge(1, 2, 1, F.CreateLineString([new Coordinate(c.X - 1e-7, c.Y), new Coordinate(c.X + 1e-7, c.Y)]));
    }

    [Fact]
    public void Parses_areas_shoals_rocks_drying_and_unsurveyed_areas_in_lat_lon_order()
    {
        var areas = Fixture();
        Assert.Equal(8, areas.Count);
        Assert.All(areas, a => { Assert.InRange(a.Area.Centroid.X, 10.5, 10.8); Assert.InRange(a.Area.Centroid.Y, 59.6, 59.9); Assert.True(a.Area.IsValid); Assert.Equal(4326, a.Area.SRID); });
        var bands = areas.Where(a => a.MaxDepthM > a.MinDepthM).Select(a => (a.MinDepthM, a.MaxDepthM)).ToList();
        Assert.Equal([(5.0, 10.0), (0.5, 2.0), (2.0, 5.0), (2.0, 5.0)], bands);
        Assert.Contains(areas, a => a.Area.NumInteriorRings == 1);
        Assert.Contains(areas, a => a.MinDepthM == 0.1 && a.MaxDepthM == 0.1 && a.Area.NumPoints == 9); // Grunne 0.1 m: a 20 m disc
        Assert.Single(areas, a => a.Source == KartverketDepths.UnsurveyedSource);
        Assert.Equal(7, areas.Count(a => a.Source == KartverketDepths.Source));
        Assert.Equal(3, areas.Count(a => a.MinDepthM == 0)); // Skjær, Tørrfall, unsurveyed
        var disc = areas.Single(a => a.MinDepthM == 0.1).Area; // ≈ 40 m across
        Assert.InRange(disc.EnvelopeInternal.Height * WaterGraphTiler.MPerDegLat, 35, 45);
    }

    [Fact]
    public void A_wfs_exception_report_throws()
    {
        using var s = new MemoryStream("""<?xml version="1.0"?><ows:ExceptionReport xmlns:ows="http://www.opengis.net/ows/1.1"><ows:Exception><ows:ExceptionText>bad bbox</ows:ExceptionText></ows:Exception></ows:ExceptionReport>"""u8.ToArray());
        Assert.Contains("bad bbox", Assert.Throws<InvalidDataException>(() => KartverketDepths.Parse(s)).Message);
    }

    [Fact]
    public void A_5_to_10_m_area_blocks_a_12_m_draft_and_passes_2_3_m()
    {
        var areas = Fixture();
        var area = areas.Single(a => a.MinDepthM == 5).Area;
        var edge = DepthLimits.Apply([Through(area)], areas.Select(a => ((Geometry)a.Area, a.MinDepthM)).ToList())[0];
        Assert.Equal(5, edge.MinDepthM);
        // the bands the router sees: Kartverket's metric areas next to the fathom bands elsewhere
        (double, double)[] fathoms = [(1.8288, 3.6576), (3.6576, 5.4864), (5.4864, 9.144), (9.144, 18.288), (18.288, 36.576)];
        var bands = areas.Select(a => (a.MinDepthM, a.MaxDepthM)).Concat(fathoms).Append((10, 15)).Append((15, 20)).ToList();
        bool Passes(double draft) => edge.MinDepthM >= DepthBands.EffectiveFloor(DepthBands.MinDepth(draft, null)!.Value, bands);
        Assert.False(Passes(12));
        Assert.True(Passes(2.3));
        Assert.True(edge.MinDepthM >= DepthBands.MinDepth(2.3, null)); // and definitely deep enough: not uncertain
        // the 0.5-2 m area stays closed for 2.3 m although a 0.5-10 m area elsewhere shares its floor
        var shallow = DepthLimits.Apply([Through(areas.Single(a => a.MinDepthM == 0.5).Area)], areas.Select(a => ((Geometry)a.Area, a.MinDepthM)).ToList())[0];
        Assert.False(shallow.MinDepthM >= DepthBands.EffectiveFloor(2.8, bands.Append((0.5, 10)).ToList()));
    }

    [Fact]
    public void Unsurveyed_water_is_uncertain_never_known()
    {
        var unsurveyed = Box(10.0, 59.0, 10.01, 59.01); var deep = Box(10.0, 59.0, 10.02, 59.01);
        var alone = DepthLimits.Apply([Through(Box(10.0, 59.0, 10.01, 59.01))], [(unsurveyed, double.NaN)])[0];
        Assert.Null(alone.MinDepthM); Assert.Equal(DepthLimits.UnknownCoastalFloorM, alone.FloorM);
        var inDeep = DepthLimits.Apply([Through(unsurveyed)], [(unsurveyed, double.NaN), (deep, 20)])[0];
        Assert.Equal(20, inDeep.MinDepthM); Assert.Equal(DepthLimits.UnknownCoastalFloorM, inDeep.FloorM); // the floor makes it uncertain (SeaGraph.Floor)
        var outside = DepthLimits.Apply([Through(Box(10.015, 59.0, 10.02, 59.01))], [(unsurveyed, double.NaN), (deep, 20)])[0];
        Assert.Equal(20, outside.MinDepthM); Assert.Null(outside.FloorM);
        Assert.Contains("'NaN'", DepthLimits.AreasSql);
    }

    [Fact]
    public void Kartverket_areas_cut_the_fathom_bands_they_cover()
    {
        var kv = Fixture().Single(a => a.MinDepthM == 5).Area;
        var env = kv.EnvelopeInternal;
        var band = new DepthArea(Box(env.MinX - 0.001, env.MinY - 0.001, env.MaxX + 0.001, env.MaxY + 0.001), 1.8288, 3.6576, "ft");
        var far = new DepthArea(Box(11, 59, 11.01, 59.01), 1.8288, 3.6576, "ft");
        var result = DepthImport.Outside([band, far], DepthImport.CoverIndex([kv]));
        Assert.Contains(far, result); // untouched: the same row
        var cut = result.Where(a => a != far).ToList();
        Assert.NotEmpty(cut);
        Assert.All(cut, a => { Assert.Equal(1.8288, a.MinDepthM); Assert.Equal("ft", a.Source); Assert.True(a.Area.Intersection(kv).Area < 1e-12); });
        Assert.Equal(band.Area.Area - kv.Area, cut.Sum(a => a.Area.Area), 10);
        Assert.Null(DepthImport.Outside(Box(env.MaxX, env.MinY, env.MaxX + 0.01, env.MaxY), DepthImport.CoverIndex([Box(env.MinX, env.MinY, env.MaxX, env.MaxY)]))); // touching only
    }

    [Fact]
    public void Chunk_rows_are_grid_pieces_inside_the_chunk_and_discs_by_their_centre()
    {
        var chunk = new Envelope(10, 11, 59, 60);
        var big = new DepthArea(Box(9.98, 59.02, 10.12, 59.04), 5, 10, KartverketDepths.Source); // crosses the chunk's west edge and two cell lines
        var rows = KartverketDepths.Rows(new() { ["Dybdeareal"] = [big], ["Grunne"] = Fixture().Where(a => a.MinDepthM == 0.1).ToList(), ["Skjær"] = [] }, new Envelope(10.6, 10.7, 59.6, 59.7));
        Assert.Single(rows); // the Drøbak disc only (the big area lies outside that chunk)
        var disc = rows[0].Area.EnvelopeInternal;
        var dry = new DepthArea(Box(disc.MinX - 0.001, disc.MinY - 0.001, disc.MaxX + 0.001, disc.MaxY + 0.001), 0, 0, KartverketDepths.Source);
        Assert.DoesNotContain(KartverketDepths.Rows(new() { ["Tørrfall"] = [dry], ["Grunne"] = rows }, new Envelope(10.6, 10.7, 59.6, 59.7)), r => r.MinDepthM == 0.1); // inside a drying area: adds nothing
        Assert.Contains(KartverketDepths.Rows(new() { ["Dybdeareal"] = [dry with { MinDepthM = 2, MaxDepthM = 5 }], ["Grunne"] = rows }, new Envelope(10.6, 10.7, 59.6, 59.7)), r => r.MinDepthM == 0.1); // inside 2-5 m: kept
        var pieces = KartverketDepths.Grid([big], chunk);
        Assert.Equal(3, pieces.Count); // 10.00-10.05, 10.05-10.10, 10.10-10.12
        Assert.All(pieces, p => Assert.True(chunk.Contains(p.Area.EnvelopeInternal)));
        Assert.Equal(0.12 * 0.02, pieces.Sum(p => p.Area.Area), 10);
    }

    /// <summary>Optional: SMS_LIVE_KARTVERKET=&lt;file&gt; fetches Drøbak and Blindleia from the live WFS and writes the counts to the file.</summary>
    [Fact]
    public async Task Fetches_live_chunks_when_configured()
    {
        var path = Environment.GetEnvironmentVariable("SMS_LIVE_KARTVERKET");
        if (string.IsNullOrEmpty(path)) return;
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("sailing-portal/0.1 (+https://github.com/daniel-kuon/SMS)");
        var lines = new List<string>();
        var boxes = new List<(string, Envelope)> { ("Drøbak", new Envelope(10.6, 10.7, 59.6, 59.7)), ("Blindleia", new Envelope(8.3, 8.5, 58.15, 58.25)) };
        if (Environment.GetEnvironmentVariable("SMS_LIVE_KARTVERKET_BBOX") is { Length: > 0 } extra && extra.Split(',').Select(double.Parse).ToArray() is [var s, var w, var n, var e])
            boxes = [($"{extra}", new Envelope(w, e, s, n))];
        foreach (var (name, box) in boxes)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var fetched = await KartverketDepths.FetchAsync(client, box, TestContext.Current.CancellationToken);
            var fetchS = clock.Elapsed.TotalSeconds;
            var rows = KartverketDepths.Rows(fetched, box);
            var pieces = KartverketDepths.Grid(fetched.Where(kv => !KartverketDepths.PointTypes.Contains(kv.Key)).SelectMany(kv => kv.Value), box).Count;
            var pairs = fetched["Dybdeareal"].GroupBy(a => (a.MinDepthM, a.MaxDepthM)).OrderBy(g => g.Key).Select(g => $"{g.Key.MinDepthM}-{g.Key.MaxDepthM}:{g.Count()}");
            lines.Add($"{name}: {string.Join(", ", fetched.Select(kv => $"{kv.Key} {kv.Value.Count}"))} -> {rows.Count} rows ({pieces} area pieces, {rows.Count - pieces} shoal/rock discs), fetch {fetchS:0} s, rows {clock.Elapsed.TotalSeconds - fetchS:0} s; bands {string.Join(" ", pairs)}");
            Assert.NotEmpty(fetched["Dybdeareal"]);
            Assert.All(rows, r => Assert.True(box.Intersects(r.Area.EnvelopeInternal)));
        }
        await File.WriteAllLinesAsync(path, lines);
    }
}

/// <summary>The import against the test database, the WFS replaced by the fixture (no network).</summary>
public class KartverketImportTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private sealed class FakeWfs : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var body = request.RequestUri!.Query.Contains("typeNames=app:Dybdeareal")
                ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "kartverket-dybdedata.xml"))
                : """<?xml version="1.0"?><wfs:FeatureCollection xmlns:wfs="http://www.opengis.net/wfs/2.0"/>"""u8.ToArray();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }
    private sealed class FixedDepths(List<DepthArea> areas) : IDepthProvider
    {
        public string Name => "fixed";
        public Task<List<DepthArea>> AreasAsync(double minLng, double minLat, double maxLng, double maxLat, CancellationToken ct) => Task.FromResult(areas.Select(a => a with { Area = (Polygon)a.Area.Copy() }).ToList());
    }

    [Fact]
    public async Task Replaces_the_chunk_cuts_the_fathom_bands_and_keeps_them_cut_on_a_seascape_reimport()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Sms.Infrastructure.SmsDbContext>();
        var conn = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync(ct);
        async Task<double> Scalar(string sql) { await using var c = new Npgsql.NpgsqlCommand(sql, conn); return Convert.ToDouble(await c.ExecuteScalarAsync(ct) ?? 0.0); }
        using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "kartverket-dybdedata.xml"));
        var kv = KartverketDepths.Parse(s).Single(a => a.MinDepthM == 5).Area;
        var e = kv.EnvelopeInternal;
        var gf = new GeometryFactory(new PrecisionModel(), 4326);
        Polygon Box(double x1, double y1, double x2, double y2) => gf.CreatePolygon([new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2), new(x1, y1)]);
        var band = new DepthArea(Box(e.MinX - 0.001, e.MinY - 0.001, e.MaxX + 0.001, e.MaxY + 0.001), 1.8288, 3.6576, "ft");
        var far = new DepthArea(Box(10.2, 59.2, 10.21, 59.21), 1.8288, 3.6576, "ft");
        await KartverketDepths.CopyAsync(conn, [band, far], ct);
        var farId = await Scalar("SELECT id FROM routing_depth WHERE source = 'ft' AND ST_X(ST_Centroid(geom)) < 10.3");

        var wfs = new FakeWfs();
        var first = (await KartverketDepths.RunAsync(new HttpClient(wfs), conn, 59, 10, 60, 11, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, ct)).Single();
        Assert.Equal(KartverketDepths.Types.Length, wfs.Calls); // one page per type
        Assert.Equal(8, first.Features["Dybdeareal"]);
        Assert.Equal(first.Rows, await Scalar("SELECT count(*) FROM routing_depth WHERE source IN ('kartverket', 'kartverket-unsurveyed')"));
        Assert.Equal(1, first.Clipped);
        Assert.Equal(farId, await Scalar("SELECT id FROM routing_depth WHERE source = 'ft' AND ST_X(ST_Centroid(geom)) < 10.3")); // untouched
        var cutArea = await Scalar("SELECT sum(ST_Area(geom)) FROM routing_depth WHERE source = 'ft' AND ST_X(ST_Centroid(geom)) > 10.3");
        Assert.Equal(band.Area.Area - kv.Area, cutArea, 10);
        Assert.Equal(0, await Scalar($"SELECT count(*) FROM routing_depth WHERE source = 'ft' AND ST_Area(ST_Intersection(geom, ST_GeomFromText('{kv.AsText()}', 4326))) > 1e-12"));

        var again = (await KartverketDepths.RunAsync(new HttpClient(new FakeWfs()), conn, 59, 10, 60, 11, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, ct)).Single();
        Assert.Equal(first.Rows, again.Removed); // idempotent: the chunk's rows replaced
        Assert.Equal(first.Rows, await Scalar("SELECT count(*) FROM routing_depth WHERE source IN ('kartverket', 'kartverket-unsurveyed')"));
        Assert.Equal(cutArea, await Scalar("SELECT sum(ST_Area(geom)) FROM routing_depth WHERE source = 'ft' AND ST_X(ST_Centroid(geom)) > 10.3"), 10);

        // a Seascape re-import keeps the kartverket rows and cuts the new bands again
        var (areas, removed) = await DepthImport.RunAsync(new FixedDepths([band]), db, e.MinX - 0.01, e.MinY - 0.01, e.MaxX + 0.01, e.MaxY + 0.01, ct);
        Assert.True(removed >= 1);
        Assert.Equal(first.Rows, await Scalar("SELECT count(*) FROM routing_depth WHERE source IN ('kartverket', 'kartverket-unsurveyed')"));
        Assert.Equal(cutArea, await Scalar("SELECT sum(ST_Area(geom)) FROM routing_depth WHERE source = 'ft' AND ST_X(ST_Centroid(geom)) > 10.3"), 10);

        // the graph build reads unsurveyed rows as NaN (DepthLimits), everything else with its depth
        await using var cmd = new Npgsql.NpgsqlCommand(DepthLimits.AreasSql + " WHERE source = 'kartverket-unsurveyed'", conn);
        await using (var r = await cmd.ExecuteReaderAsync(ct)) { Assert.True(await r.ReadAsync(ct)); Assert.True(double.IsNaN(r.GetDouble(1))); }
    }

    [Fact]
    public async Task The_endpoint_is_admin_only_and_checks_the_bbox()
    {
        var (user, _) = await f.UserAsync();
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await user.PostAsync("/api/admin/routing/import-no-depths?bbox=59,10,60,11", null, TestContext.Current.CancellationToken)).StatusCode);
        var (admin, _) = await f.AdminAsync();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/routing/import-no-depths?bbox=56,4,61,12", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/routing/import-no-depths", null, TestContext.Current.CancellationToken)).StatusCode);
    }
}
