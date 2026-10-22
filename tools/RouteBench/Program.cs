// Times SeaRouter point → point routes against the dev database without the API: graph load (first route), then warm runs.
// usage: dotnet run --project tools/RouteBench -c Release -- "Host=localhost;Port=…;Database=sms;Username=postgres;Password=…" [geojson-out-dir]
//        [routes "lat1,lng1,lat2,lng2,draft[,minDepth[,warnDepth[,airDraft[,avoidUnknownBridges]]]]" …]   (0 = none; minDepth replaces draft + margin; 1 = avoid)
//        or as third argument "backfill-adjacent": DepthLimits' coastal no-data rule (adjacent_depth_m) for the active graph version, without a rebuild
//        or "derive-fairways" s,w,n,e ["derived-only"]: dry run of the buoy-chain derivation (stored vs derived lines), in 2° chunks like the admin runs it
//        env SMS_BENCH_PASSAGES=file: routes on the active graph plus the passage lines in the file ("lat,lng lat,lng …" per line), a rebuild simulated
//        or "backfill-floor": floor_m (coastal water without data, derived channels "Enge" and their links) for the active graph version, without a rebuild
//        or "import-bridges" s,w,n,e: road/railway bridges without a clearance over routing water from Overpass (as the admin endpoint import-bridges)
//        or "backfill-bridges": unknown_bridge (edges under a fixed bridge without a clearance, Obstacles.Apply) for the active graph version, without a rebuild
//        or "backfill-ferry": ferry (edges along a stored ferry track, kind Ferry, within Routing:FerryCorridorM; their adjacent_depth_m/floor_m dropped) for the active graph version, without a rebuild
//        env SMS_BENCH_CARDINALS=factor|hard: routes on the active graph with grid edges through a cardinal's danger wedge priced x factor in memory (CardinalPenaltyFactor simulated, passage edges not)
//        env SMS_BENCH_FERRY=file: routes on the active graph with the edges along the ferry lines in the file ("lat,lng lat,lng …" per line) flagged in memory, a backfill-ferry simulated
//        env SMS_BENCH_ZONE="shoal=<ids file>[;dump=s,w,n,e]": routes on the active graph with the buoyed-zone rule simulated in memory (shoal single-mark lines join the channel zone, the gaps' x3 cut, known-water evidence blocked in the zone); the ids file is computed when missing; dump writes the gap chords and zone outlines in the box to <out>/zone-dump.geojson
//        env SMS_BENCH_KNOWN="dtm=<dir of ERDDAP dtm_<lat>_<lng>.nc>;traffic=<vesseldensity_04_<year>.tif>,…[;sailing=10][;isolated=1][;noflag=1][;nohazard=1][;nopull=1]": the TrafficAndDtm build in memory (traffic flag, DtmMinM, isolated-danger pricing) on the active graph
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;

var cs = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("SMS_BENCH_CS") ?? throw new ArgumentException("connection string missing");
var outDir = args.Length > 1 ? args[1] : null;
var root = AppContext.BaseDirectory;
while (!File.Exists(Path.Combine(root, "Sms.slnx"))) root = Path.GetDirectoryName(root)!;
var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(root, "src/Sms.Api/appsettings.json")).AddEnvironmentVariables("SMS_").Build(); // e.g. SMS_Routing__PassageTrimM=10000
var services = new ServiceCollection()
    .AddLogging(l => l.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information).AddFilter("Microsoft", LogLevel.Warning))
    .AddSingleton<IConfiguration>(config)
    .AddDbContext<SmsDbContext>(o => o.UseNpgsql(cs, n => n.UseNetTopologySuite()))
    .AddSingleton<RoutingGraphBuilder>()
    .AddScoped<SeaRouter>();
services.AddHttpClient();
await using var sp = services.BuildServiceProvider();

if (args.Length > 2 && args[2] == "backfill-adjacent")
{
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
    var conn = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync();
    async Task<long> Scalar(string sql) => Convert.ToInt64(await new Npgsql.NpgsqlCommand(sql, conn) { CommandTimeout = 3600 }.ExecuteScalarAsync());
    var version = (int)await Scalar("SELECT value::int FROM routing_meta WHERE key = 'active_version'");
    var gridNodes = await Scalar("SELECT value::bigint FROM routing_meta WHERE key = 'grid_nodes'");
    var clock = Stopwatch.StartNew();
    var blocks = await db.RoutingCorrections.Where(c => c.Active && c.Kind == Sms.Domain.CorrectionKind.Block).Select(c => c.Geometry).ToListAsync();
    var nearLand = RoutingGraphBuilder.NearLand(await RoutingGraphBuilder.LandIndexAsync(conn, version, blocks, default));
    var depths = new List<(NetTopologySuite.Geometries.Geometry, double)>();
    await using (var r = await new Npgsql.NpgsqlCommand("SELECT geom, min_depth_m FROM routing_depth", conn) { CommandTimeout = 3600 }.ExecuteReaderAsync())
        while (await r.ReadAsync()) depths.Add((r.GetFieldValue<NetTopologySuite.Geometries.Geometry>(0), r.GetDouble(1)));
    var ids = new List<long>(); var edges = new List<GraphEdge>();
    await using (var r = await new Npgsql.NpgsqlCommand($"SELECT id, source, target, geom FROM routing_edge WHERE graph_version = {version} AND min_depth_m IS NULL AND source <= {gridNodes} AND target <= {gridNodes}", conn) { CommandTimeout = 3600 }.ExecuteReaderAsync())
        while (await r.ReadAsync()) { ids.Add(r.GetInt64(0)); edges.Add(new GraphEdge(r.GetInt64(1), r.GetInt64(2), 0, r.GetFieldValue<NetTopologySuite.Geometries.LineString>(3))); }
    Console.WriteLine($"v{version}: {edges.Count} grid edges without charted depth, {depths.Count} depth areas loaded ({clock.Elapsed.TotalSeconds:0} s)");
    var limited = DepthLimits.Apply(edges, depths, nearLand);
    var found = limited.Select((e, i) => (id: ids[i], d: e.AdjacentDepthM)).Where(x => x.d is not null).ToList();
    Console.WriteLine($"{found.Count} coastal edges next to a band ({clock.Elapsed.TotalSeconds:0} s): " + string.Join(", ", found.GroupBy(x => x.d).OrderBy(g => g.Key).Select(g => $"{g.Key} m × {g.Count()}")));
    await new Npgsql.NpgsqlCommand($"ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS adjacent_depth_m float8 NULL; UPDATE routing_edge SET adjacent_depth_m = NULL WHERE graph_version = {version} AND adjacent_depth_m IS NOT NULL; CREATE TEMP TABLE adj (id bigint, d float8)", conn) { CommandTimeout = 3600 }.ExecuteNonQueryAsync();
    await using (var w = await conn.BeginBinaryImportAsync("COPY adj (id, d) FROM STDIN (FORMAT BINARY)"))
    {
        foreach (var (id, d) in found) { await w.StartRowAsync(); await w.WriteAsync(id, NpgsqlTypes.NpgsqlDbType.Bigint); await w.WriteAsync(d!.Value, NpgsqlTypes.NpgsqlDbType.Double); }
        await w.CompleteAsync();
    }
    var updated = await new Npgsql.NpgsqlCommand($"UPDATE routing_edge e SET adjacent_depth_m = adj.d FROM adj WHERE e.graph_version = {version} AND e.id = adj.id", conn) { CommandTimeout = 3600 }.ExecuteNonQueryAsync();
    Console.WriteLine($"{updated} edges updated ({clock.Elapsed.TotalSeconds:0} s)");
    return;
}

if (args.Length > 2 && args[2] == "backfill-floor")
{
    // the builder's FloorM rule without a rebuild: coastal water edges without data and without a band in reach get the unknown
    // coastal floor; derived channels ("Enge") and their links get the floor of the water they cross. After contraction a passage
    // edge is a channel when its first segment lies on an "Enge" line, a straight link when one of its ends does (and none lies
    // on a fairway or waterway line)
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
    var conn = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync();
    async Task<long> Scalar(string sql) => Convert.ToInt64(await new Npgsql.NpgsqlCommand(sql, conn) { CommandTimeout = 3600 }.ExecuteScalarAsync());
    var version = (int)await Scalar("SELECT value::int FROM routing_meta WHERE key = 'active_version'");
    var gridNodes = await Scalar("SELECT value::bigint FROM routing_meta WHERE key = 'grid_nodes'");
    var clock = Stopwatch.StartNew();
    var blocks = await db.RoutingCorrections.Where(c => c.Active && c.Kind == Sms.Domain.CorrectionKind.Block).Select(c => c.Geometry).ToListAsync();
    var nearLand = RoutingGraphBuilder.NearLand(await RoutingGraphBuilder.LandIndexAsync(conn, version, blocks, default));
    var depths = new List<(NetTopologySuite.Geometries.Geometry, double)>();
    await using (var r = await new Npgsql.NpgsqlCommand("SELECT geom, min_depth_m FROM routing_depth", conn) { CommandTimeout = 3600 }.ExecuteReaderAsync())
        while (await r.ReadAsync()) depths.Add((r.GetFieldValue<NetTopologySuite.Geometries.Geometry>(0), r.GetDouble(1)));
    var enge = new NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Geometry>();
    var other = new NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Geometry>();
    foreach (var c in await db.RoutingCorrections.Where(c => c.Active && c.Kind == Sms.Domain.CorrectionKind.Passage).Select(c => new { c.Geometry, c.Note }).ToListAsync())
        (c.Note != null && c.Note.StartsWith("Enge") ? enge : other).Insert(c.Geometry.EnvelopeInternal, c.Geometry);
    enge.Build(); other.Build();
    static bool On(NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Geometry> tree, NetTopologySuite.Geometries.Coordinate c)
    {
        var pt = Geo.Factory.CreatePoint(c);
        return tree.Query(pt.EnvelopeInternal).Any(g => g.IsWithinDistance(pt, 1e-6));
    }
    var ids = new List<long>(); var edges = new List<GraphEdge>(); int grid = 0, channels = 0;
    await using (var r = await new Npgsql.NpgsqlCommand($"SELECT id, source, target, geom FROM routing_edge WHERE graph_version = {version} AND NOT assumed AND (source > {gridNodes} OR target > {gridNodes} OR (min_depth_m IS NULL AND adjacent_depth_m IS NULL))", conn) { CommandTimeout = 3600 }.ExecuteReaderAsync())
        while (await r.ReadAsync())
        {
            long s = r.GetInt64(1), t = r.GetInt64(2);
            var line = r.GetFieldValue<NetTopologySuite.Geometries.LineString>(3);
            if (s > gridNodes || t > gridNodes)
            {
                var c0 = line.GetCoordinateN(0); var c1 = line.GetCoordinateN(1); var cn = line.GetCoordinateN(line.NumPoints - 1);
                if (On(other, c0) || On(other, cn)) continue;
                var channel = On(enge, new NetTopologySuite.Geometries.Coordinate((c0.X + c1.X) / 2, (c0.Y + c1.Y) / 2)) || (line.NumPoints == 2 && (On(enge, c0) || On(enge, cn)));
                if (!channel) continue;
                channels++;
            }
            else grid++;
            ids.Add(r.GetInt64(0)); edges.Add(new GraphEdge(s, t, 0, line));
        }
    Console.WriteLine($"v{version}: {grid} water-graph edges without depth data, {channels} channel edges and links, {depths.Count} depth areas loaded ({clock.Elapsed.TotalSeconds:0} s)");
    var limited = DepthLimits.Apply(edges, depths, nearLand);
    var found = limited.Select((e, i) => (id: ids[i], d: edges[i].Source > gridNodes || edges[i].Target > gridNodes ? e.MinDepthM ?? e.AdjacentDepthM ?? e.FloorM : e.FloorM)).Where(x => x.d is not null).ToList();
    Console.WriteLine($"{found.Count} edges with a floor ({clock.Elapsed.TotalSeconds:0} s): " + string.Join(", ", found.GroupBy(x => x.d).OrderBy(g => g.Key).Select(g => $"{g.Key} m × {g.Count()}")));
    await new Npgsql.NpgsqlCommand($"ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS floor_m float8 NULL; UPDATE routing_edge SET floor_m = NULL WHERE graph_version = {version} AND floor_m IS NOT NULL; CREATE TEMP TABLE cf (id bigint, d float8)", conn) { CommandTimeout = 3600 }.ExecuteNonQueryAsync();
    await using (var w = await conn.BeginBinaryImportAsync("COPY cf (id, d) FROM STDIN (FORMAT BINARY)"))
    {
        foreach (var (id, d) in found) { await w.StartRowAsync(); await w.WriteAsync(id, NpgsqlTypes.NpgsqlDbType.Bigint); await w.WriteAsync(d!.Value, NpgsqlTypes.NpgsqlDbType.Double); }
        await w.CompleteAsync();
    }
    var updated = await new Npgsql.NpgsqlCommand($"UPDATE routing_edge e SET floor_m = cf.d FROM cf WHERE e.graph_version = {version} AND e.id = cf.id", conn) { CommandTimeout = 3600 }.ExecuteNonQueryAsync();
    Console.WriteLine($"{updated} edges updated ({clock.Elapsed.TotalSeconds:0} s)");
    return;
}

if (args.Length > 3 && args[2] == "import-bridges")
{
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
    var b = args[3].Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("SMS-RouteBench/1.0");
    HttpResponseMessage? resp = null;
    foreach (var url in new[] { "https://overpass-api.de/api/interpreter", "https://maps.mail.ru/osm/tools/overpass/api/interpreter", "https://overpass.kumi.systems/api/interpreter" })
    {
        resp = await http.PostAsync(url, new FormUrlEncodedContent([new("data", string.Format(Waterways.OverpassBridgeQuery, args[3]))]));
        if (resp.IsSuccessStatusCode) break;
        Console.WriteLine($"{url}: {(int)resp.StatusCode}");
    }
    resp!.EnsureSuccessStatusCode();
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
    var (candidates, created, removed) = await Waterways.ImportUnknownBridgesAsync(doc, db, null, new NetTopologySuite.Geometries.Envelope(b[1], b[3], b[0], b[2]), default);
    Console.WriteLine($"{candidates} plain bridges in the bbox, {created} over routing water imported, {removed} replaced");
    return;
}

if (args.Length > 2 && args[2] == "backfill-bridges")
{
    // Obstacles.Apply's UnknownBridge rule on the active version: edges crossing an unknown-clearance bridge piece (after chains and charted
    // spans), or within Obstacles.MatchM of such a point
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
    var conn = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync();
    var version = Convert.ToInt32(await new Npgsql.NpgsqlCommand("SELECT value::int FROM routing_meta WHERE key = 'active_version'", conn).ExecuteScalarAsync());
    var raw = (await db.RoutingCorrections.Where(c => c.Active && (c.Kind == Sms.Domain.CorrectionKind.Bridge || c.Kind == Sms.Domain.CorrectionKind.OpeningBridge)).Select(c => new { c.Kind, c.Geometry, c.ClearanceM }).ToListAsync())
        .Select(c => (c.Geometry is NetTopologySuite.Geometries.LineString ? c.Geometry : c.Geometry.Centroid, c.Kind, c.ClearanceM)).ToList();
    var pieces = Obstacles.WithSpans(raw).Where(p => p.kind == Sms.Domain.CorrectionKind.Bridge && p.clearanceM is null).Select(p => { p.geom.SRID = 4326; return p.geom.AsBinary(); }).ToArray();
    await new Npgsql.NpgsqlCommand($"ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS unknown_bridge boolean NOT NULL DEFAULT false; UPDATE routing_edge SET unknown_bridge = false WHERE graph_version = {version} AND unknown_bridge", conn) { CommandTimeout = 3600 }.ExecuteNonQueryAsync();
    var cmd = new Npgsql.NpgsqlCommand($@"UPDATE routing_edge e SET unknown_bridge = true FROM (SELECT ST_SetSRID(ST_GeomFromWKB(w), 4326) AS g FROM unnest(@w::bytea[]) w) p
        WHERE e.graph_version = {version} AND e.geom && ST_Expand(p.g, 0.001)
          AND (CASE WHEN GeometryType(p.g) = 'POINT' THEN ST_DWithin(e.geom::geography, p.g::geography, {Obstacles.MatchM}) ELSE ST_Intersects(e.geom, p.g) END)", conn) { CommandTimeout = 3600 };
    cmd.Parameters.AddWithValue("w", pieces);
    var marked = await cmd.ExecuteNonQueryAsync();
    var known = await new Npgsql.NpgsqlCommand($"UPDATE routing_edge SET unknown_bridge = false WHERE graph_version = {version} AND unknown_bridge AND clearance_m IS NOT NULL", conn) { CommandTimeout = 3600 }.ExecuteNonQueryAsync();
    Console.WriteLine($"v{version}: {pieces.Length} unknown-clearance bridge pieces, {marked} edges marked, {known} of them unmarked again (a known clearance on the edge)");
    return;
}

if (args.Length > 2 && args[2] == "backfill-ferry")
{
    // DepthLimits' ferry rule on the active version: an edge along a ferry track is known-navigable (no adjacent band, no floor; the charted band stays)
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
    var conn = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync();
    var version = Convert.ToInt32(await new Npgsql.NpgsqlCommand("SELECT value::int FROM routing_meta WHERE key = 'active_version'", conn).ExecuteScalarAsync());
    var clock = Stopwatch.StartNew();
    var lines = (await db.RoutingCorrections.Where(c => c.Active && c.Kind == Sms.Domain.CorrectionKind.Ferry).Select(c => c.Geometry).ToListAsync()).OfType<NetTopologySuite.Geometries.LineString>().ToList();
    var ferries = new FerryCorridors(lines, sp.GetRequiredService<RoutingGraphBuilder>().Options.FerryCorridorM);
    var flagged = new List<long>(); var total = 0;
    await using (var r = await new Npgsql.NpgsqlCommand($"SELECT id, geom FROM routing_edge WHERE graph_version = {version}", conn) { CommandTimeout = 3600 }.ExecuteReaderAsync())
        while (await r.ReadAsync()) { total++; if (ferries.Along(r.GetFieldValue<NetTopologySuite.Geometries.LineString>(1))) flagged.Add(r.GetInt64(0)); }
    Console.WriteLine($"v{version}: {lines.Count} ferry tracks, {flagged.Count} of {total} edges along one ({clock.Elapsed.TotalSeconds:0} s)");
    await new Npgsql.NpgsqlCommand($"ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS ferry boolean NOT NULL DEFAULT false; UPDATE routing_edge SET ferry = false WHERE graph_version = {version} AND ferry; CREATE TEMP TABLE fe (id bigint)", conn) { CommandTimeout = 3600 }.ExecuteNonQueryAsync();
    await using (var w = await conn.BeginBinaryImportAsync("COPY fe (id) FROM STDIN (FORMAT BINARY)"))
    {
        foreach (var id in flagged) { await w.StartRowAsync(); await w.WriteAsync(id, NpgsqlTypes.NpgsqlDbType.Bigint); }
        await w.CompleteAsync();
    }
    var updated = await new Npgsql.NpgsqlCommand($"UPDATE routing_edge e SET ferry = true, adjacent_depth_m = NULL, floor_m = NULL FROM fe WHERE e.graph_version = {version} AND e.id = fe.id", conn) { CommandTimeout = 3600 }.ExecuteNonQueryAsync();
    Console.WriteLine($"{updated} edges flagged ({clock.Elapsed.TotalSeconds:0} s)");
    return;
}

if (args.Length > 3 && args[2] == "derive-fairways")
{
    // dry run of the derive-fairways endpoint for a bbox s,w,n,e: the derived lines next to the stored "Fahrwasser" passages, nothing written
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
    // a bbox larger than 2° is cut into 2° chunks, each read with the endpoint's margin and keeping the lines whose centroid it owns
    var b = args[3].Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    var opts = sp.GetRequiredService<RoutingGraphBuilder>().Options;
    if (args.Length <= 4 || args[4] != "derived-only")
    {
        var stored = await db.RoutingCorrections.Where(c => c.Active && c.Kind == Sms.Domain.CorrectionKind.Passage && c.Note != null && c.Note.StartsWith("Fahrwasser")).Select(c => new { c.Geometry, c.Note }).ToListAsync();
        var box = new NetTopologySuite.Geometries.Envelope(b[1], b[3], b[0], b[2]);
        foreach (var s in stored.Where(s => box.Contains(s.Geometry.EnvelopeInternal))) Console.WriteLine($"stored {s.Note}: " + string.Join(" ", s.Geometry.Coordinates.Select(c => FormattableString.Invariant($"{c.Y:F6},{c.X:F6}"))));
    }
    for (var s0 = b[0]; s0 < b[2]; s0 += 2)
        for (var w0 = b[1]; w0 < b[3]; w0 += 2)
        {
            double s = s0, w = w0, n = Math.Min(s0 + 2, b[2]), e = Math.Min(w0 + 2, b[3]);
            var mLat = 0.15; var mLng = mLat / Math.Max(0.2, Math.Cos((s + n) / 2 * Math.PI / 180));
            await db.Database.OpenConnectionAsync();
            var (marks, water) = await Fairways.InputsAsync((Npgsql.NpgsqlConnection)db.Database.GetDbConnection(), new NetTopologySuite.Geometries.Envelope(w - mLng, e + mLng, s - mLat, n + mLat), default);
            var extent = Geo.Factory.ToGeometry(new NetTopologySuite.Geometries.Envelope(w, e, s, n));
            foreach (var f in Fairways.Derive(marks, Geo.Factory, water).Where(f => extent.Intersects(f.Line.Centroid)))
            {
                var shoal = f.Pairs == 0 && !f.Wide ? FormattableString.Invariant($" shoal {Fairways.ShoalShare(f.Line, water, opts.ChannelPreferenceM, opts.ZoneShoalFloorM):0.000}") : "";
                Console.WriteLine($"derived {f.Pairs} pairs {f.Singles} singles{shoal}: " + string.Join(" ", f.Line.Coordinates.Select(c => FormattableString.Invariant($"{c.Y:F6},{c.X:F6}"))));
                if (f.Area is { } area) Console.WriteLine("area: " + area.AsText());
            }
        }
    return;
}
if (Environment.GetEnvironmentVariable("SMS_BENCH_PASSAGES") is { Length: > 0 } extraFile)
{
    // simulation of a rebuild with extra passage lines ("lat,lng lat,lng …" per line, cost ×1, no depth): the active graph is loaded,
    // the lines are appended as passage edges (a vertex on an existing node joins it) and the router uses that graph. No links are
    // added (a rebuild links interior vertices every km), no channel band either - a conservative picture of the rebuilt graph
    using var scope = sp.CreateScope();
    var router = scope.ServiceProvider.GetRequiredService<SeaRouter>();
    var version = await router.ActiveVersionAsync(default);
    var g = await SeaRouter.LoadGraphAsync(new Npgsql.NpgsqlConnection(cs), version, sp.GetRequiredService<ILoggerFactory>().CreateLogger("bench"));
    T F<T>(string name) => (T)typeof(Sms.Domain.SeaGraph).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(g)!;
    var x = F<double[]>("_x").ToList(); var y = F<double[]>("_y").ToList();
    var src = F<int[]>("_src").ToList(); var tgt = F<int[]>("_tgt").ToList(); var cost = F<float[]>("_cost").ToList();
    var depth = F<float[]>("_depth").ToList(); var clear = F<float[]>("_clear").ToList(); var geomStart = F<int[]>("_geomStart").ToList(); var geom = F<double[]>("_geom");
    var first = F<int>("_firstPassage"); var isolated = F<bool[]?>("_isolated")?.ToList(); var assumed = F<bool[]?>("_assumed")?.ToList(); var adjacent = F<float[]?>("_adjacent")?.ToList(); var floor = F<float[]?>("_floor")?.ToList();
    var at = new Dictionary<(long, long), int>();
    for (var i = first; i < x.Count; i++) if (!double.IsNaN(x[i])) at[((long)Math.Round(x[i] * 1e6), (long)Math.Round(y[i] * 1e6))] = i;
    int Node(double lng, double lat)
    {
        if (at.TryGetValue(((long)Math.Round(lng * 1e6), (long)Math.Round(lat * 1e6)), out var id)) return id;
        x.Add(lng); y.Add(lat); isolated?.Add(false); return at[((long)Math.Round(lng * 1e6), (long)Math.Round(lat * 1e6))] = x.Count - 1;
    }
    var added = 0;
    foreach (var l in File.ReadAllLines(extraFile).Where(l => l.Trim().Length > 0))
    {
        var pts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray()).ToList();
        for (var k = 1; k < pts.Count; k++)
        {
            int a = Node(pts[k - 1][1], pts[k - 1][0]), c = Node(pts[k][1], pts[k][0]);
            if (a == c) continue;
            src.Add(a); tgt.Add(c); cost.Add((float)Sms.Domain.SeaGraph.Haversine(y[a], x[a], y[c], x[c])); depth.Add(float.NaN); clear.Add(float.NaN);
            geomStart.Add(geomStart[^1]); assumed?.Add(false); adjacent?.Add(float.NaN); floor?.Add(float.NaN); added++;
        }
    }
    var sim = new Sms.Domain.SeaGraph(x.ToArray(), y.ToArray(), src.ToArray(), tgt.ToArray(), cost.ToArray(), depth.ToArray(), clear.ToArray(), geomStart.ToArray(), geom, first, isolated?.ToArray(), assumed: assumed?.ToArray(), adjacent: adjacent?.ToArray(), floor: floor?.ToArray());
    var graphs = (System.Collections.IDictionary)typeof(SeaRouter).GetField("_graphs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    graphs[version] = new Lazy<Task<Sms.Domain.SeaGraph>>(() => Task.FromResult(sim));
    Console.WriteLine($"simulation: {added} passage edges added from {extraFile}");
}

if (Environment.GetEnvironmentVariable("SMS_BENCH_CARDINALS") is { Length: > 0 } cardArg)
{
    // CardinalMarks.Apply in memory: grid edges (not passage edges) through a cardinal's danger wedge cost x factor by the share inside ("hard": closed)
    using var scope = sp.CreateScope();
    var router = scope.ServiceProvider.GetRequiredService<SeaRouter>();
    var version = await router.ActiveVersionAsync(default);
    var g = await SeaRouter.LoadGraphAsync(new Npgsql.NpgsqlConnection(cs), version, sp.GetRequiredService<ILoggerFactory>().CreateLogger("bench"));
    T F<T>(string name) => (T)typeof(Sms.Domain.SeaGraph).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(g)!;
    var marks = await CardinalMarks.LoadAsync(scope.ServiceProvider.GetRequiredService<SmsDbContext>());
    var hard = cardArg == "hard"; var factor = hard ? 1e6 : double.Parse(cardArg, System.Globalization.CultureInfo.InvariantCulture);
    var index = new NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Prepared.IPreparedGeometry>();
    foreach (var (p, side) in marks) { var w = CardinalMarks.Wedge(p, side); index.Insert(w.EnvelopeInternal, NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(w)); }
    index.Build();
    var cost = F<float[]>("_cost"); var touched = 0;
    Parallel.For(0, g.EdgeCount, e =>
    {
        if (g.IsPassage(e)) return;
        var line = Geo.Factory.CreateLineString(g.Coords(e));
        var hits = index.Query(line.EnvelopeInternal);
        if (hits.Count == 0) return;
        var share = ChannelPreference.ShareInside(line, hits, 25);
        if (share <= 0) return;
        cost[e] = (float)(cost[e] * (1 + share * (factor - 1))); Interlocked.Increment(ref touched);
    });
    var sim = new Sms.Domain.SeaGraph(F<double[]>("_x"), F<double[]>("_y"), F<int[]>("_src"), F<int[]>("_tgt"), cost, F<float[]>("_depth"), F<float[]>("_clear"), F<int[]>("_geomStart"), F<double[]>("_geom"), F<int>("_firstPassage"), F<bool[]?>("_isolated"),
        assumed: F<bool[]?>("_assumed"), adjacent: F<float[]?>("_adjacent"), floor: F<float[]?>("_floor"), unknownBridge: F<bool[]?>("_unknownBridge"), ferry: F<bool[]?>("_ferry"));
    var graphs = (System.Collections.IDictionary)typeof(SeaRouter).GetField("_graphs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    graphs[version] = new Lazy<Task<Sms.Domain.SeaGraph>>(() => Task.FromResult(sim));
    Console.WriteLine($"simulation: {marks.Count} cardinals, {touched} grid edges through a danger wedge x{factor}");
}

if (Environment.GetEnvironmentVariable("SMS_BENCH_FERRY") is { Length: > 0 } ferryFile)
{
    // backfill-ferry in memory: the active graph with the edges along the ferry lines in the file flagged (adjacent band and floor dropped)
    using var scope = sp.CreateScope();
    var router = scope.ServiceProvider.GetRequiredService<SeaRouter>();
    var version = await router.ActiveVersionAsync(default);
    var g = await SeaRouter.LoadGraphAsync(new Npgsql.NpgsqlConnection(cs), version, sp.GetRequiredService<ILoggerFactory>().CreateLogger("bench"));
    T F<T>(string name) => (T)typeof(Sms.Domain.SeaGraph).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(g)!;
    var lines = File.ReadAllLines(ferryFile).Where(l => l.Trim().Length > 0).Select(l => Geo.Factory.CreateLineString(l.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray()).Select(v => new NetTopologySuite.Geometries.Coordinate(v[1], v[0])).ToArray())).ToList();
    var ferries = new FerryCorridors(lines, sp.GetRequiredService<RoutingGraphBuilder>().Options.FerryCorridorM);
    var adjacent = F<float[]>("_adjacent"); var floor = F<float[]>("_floor"); var ferry = new bool[g.EdgeCount];
    Parallel.For(0, g.EdgeCount, e =>
    {
        if (!ferries.Along(Geo.Factory.CreateLineString(g.Coords(e)))) return;
        ferry[e] = true; adjacent[e] = float.NaN; floor[e] = float.NaN;
    });
    var sim = new Sms.Domain.SeaGraph(F<double[]>("_x"), F<double[]>("_y"), F<int[]>("_src"), F<int[]>("_tgt"), F<float[]>("_cost"), F<float[]>("_depth"), F<float[]>("_clear"), F<int[]>("_geomStart"), F<double[]>("_geom"), F<int>("_firstPassage"), F<bool[]?>("_isolated"),
        assumed: F<bool[]?>("_assumed"), adjacent: adjacent, floor: floor, unknownBridge: F<bool[]?>("_unknownBridge"), ferry: ferry);
    var graphs = (System.Collections.IDictionary)typeof(SeaRouter).GetField("_graphs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    graphs[version] = new Lazy<Task<Sms.Domain.SeaGraph>>(() => Task.FromResult(sim));
    Console.WriteLine($"simulation: {lines.Count} ferry lines, {ferry.Count(f => f)} edges flagged");
}

if (Environment.GetEnvironmentVariable("SMS_BENCH_KNOWN") is { Length: > 0 } knownArg)
{
    // the TrafficAndDtm build in memory on the active graph (spec-emodnet-density), from local files instead of routing_traffic/routing_dtm:
    // "dtm=<dir of dtm_<lat>_<lng>.tif, 2° WCS tiffs>;traffic=<vesseldensity_04_<year>.tif>,…;sailing=10;isolated=1;nopull=1"
    //  - traffic: the import's cells (TrafficImport.Cells), sailing ≥ the threshold flag edges with a floor that run along them (TrafficAreas.Along)
    //  - dtm: the import's chunks (DtmImport.Chunk; noflag=1: interpolated cells count too), DtmMinM for the other edges with a floor below DtmGrid.MaxFloorM (DtmGrid.MinDepth)
    //  - isolated: edges through an isolated-danger disc cost × CardinalPenaltyFactor by the share inside (CardinalMarks.Apply)
    //  - nohazard: known water through cardinal danger wedges and isolated-danger discs too (DepthLimits' hazards off)
    //  - nopull: the smoothing does not know the traffic cells and the DTM (the pull/slide exemption off)
    var opt = knownArg.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "1");
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    using var scope = sp.CreateScope();
    var router = scope.ServiceProvider.GetRequiredService<SeaRouter>();
    var version = await router.ActiveVersionAsync(default);
    var g = await SeaRouter.LoadGraphAsync(new Npgsql.NpgsqlConnection(cs), version, sp.GetRequiredService<ILoggerFactory>().CreateLogger("bench"));
    T F<T>(string name) => (T)typeof(Sms.Domain.SeaGraph).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(g)!;
    var o = sp.GetRequiredService<RoutingGraphBuilder>().Options;
    var clock = Stopwatch.StartNew();
    var traffic = TrafficAreas.Empty;
    if (opt.TryGetValue("traffic", out var trafficFiles))
    {
        var sailing = opt.TryGetValue("sailing", out var sv) ? double.Parse(sv, inv) : o.TrafficKnownSailing;
        var files = trafficFiles.Split(',').Select(f => (TrafficImport.ParseName(f)!.Value.kind, new GeoTiff(File.ReadAllBytes(f)))).ToList();
        var cells = TrafficImport.Cells(files, new NetTopologySuite.Geometries.Envelope(o.Region.MinLng, o.Region.MaxLng, o.Region.MinLat, o.Region.MaxLat));
        traffic = new TrafficAreas(cells.Where(c => c.Sailing >= sailing).Select(c => c.Cell).ToList());
        Console.WriteLine($"traffic: {files.Count} files, {cells.Count} cells, {traffic.Count} with sailing ≥ {sailing} ({clock.Elapsed.TotalSeconds:0} s)");
    }
    var dtm = DtmGrid.Empty;
    if (opt.TryGetValue("dtm", out var dtmDir))
    {
        // a 2° tiff gives its four 1° chunks at once
        // the import's files, one ERDDAP griddap subset per 1° chunk (dtm_<lat>_<lng>.nc: elevation + interpolation_flag); noflag=1 ignores the flag
        var noFlag = opt.ContainsKey("noflag");
        dtm = new DtmGrid(k =>
        {
            var path = Path.Combine(dtmDir, $"dtm_{k.lat}_{k.lng}.nc");
            if (!File.Exists(path)) return null;
            var r = DtmRaster.FromNetCdf(File.ReadAllBytes(path));
            return DtmImport.Chunk(r, k.lng, k.lat).Decimetres(measuredOnly: !noFlag);
        });
    }
    var cost = (float[])F<float[]>("_cost").Clone();
    var trafficFlag = new bool[g.EdgeCount]; var dtmMin = new float[g.EdgeCount]; Array.Fill(dtmMin, float.NaN);
    NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Prepared.IPreparedGeometry>? discs = null;
    // DepthLimits' hazards: no traffic flag and no DTM depth through a cardinal's danger wedge or an isolated-danger disc (nohazard=1: off)
    var hazards = new NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Prepared.IPreparedGeometry>();
    if (!opt.ContainsKey("nohazard"))
        foreach (var h in CardinalMarks.Hazards(await CardinalMarks.LoadAsync(scope.ServiceProvider.GetRequiredService<SmsDbContext>()), await CardinalMarks.LoadIsolatedAsync(scope.ServiceProvider.GetRequiredService<SmsDbContext>())))
            hazards.Insert(h.EnvelopeInternal, NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(h));
    hazards.Build();
    if (opt.ContainsKey("isolated") && o.CardinalPenaltyFactor > 1)
    {
        discs = new();
        foreach (var p in await CardinalMarks.LoadIsolatedAsync(scope.ServiceProvider.GetRequiredService<SmsDbContext>())) { var d = CardinalMarks.Disc(p); discs.Insert(d.EnvelopeInternal, NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(d)); }
        discs.Build();
    }
    int flagged = 0, sampled = 0, deep = 0, priced = 0;
    Parallel.For(0, g.EdgeCount, e =>
    {
        var line = Geo.Factory.CreateLineString(g.Coords(e));
        if (discs is not null && discs.Query(line.EnvelopeInternal) is { Count: > 0 } hits && ChannelPreference.ShareInside(line, hits, 25) is var share && share > 0)
        {
            cost[e] = (float)(cost[e] * (1 + share * (o.CardinalPenaltyFactor - 1))); Interlocked.Increment(ref priced);
        }
        if (g.Ferry(e) || float.IsNaN(g.Floor(e))) return;
        if (hazards.Count > 0 && hazards.Query(line.EnvelopeInternal).Any(h => h.Intersects(line))) return;
        if (traffic.Along(line)) { trafficFlag[e] = true; Interlocked.Increment(ref flagged); return; }
        if (g.Floor(e) >= DtmGrid.MaxFloorM) return;
        Interlocked.Increment(ref sampled);
        if (dtm.MinDepth(line) is { } dd) { dtmMin[e] = (float)dd; if (dd >= 2.3) Interlocked.Increment(ref deep); }
    });
    var sim = new Sms.Domain.SeaGraph(F<double[]>("_x"), F<double[]>("_y"), F<int[]>("_src"), F<int[]>("_tgt"), cost, F<float[]>("_depth"), F<float[]>("_clear"), F<int[]>("_geomStart"), F<double[]>("_geom"), F<int>("_firstPassage"), F<bool[]?>("_isolated"),
        assumed: F<bool[]?>("_assumed"), adjacent: F<float[]?>("_adjacent"), floor: F<float[]?>("_floor"), unknownBridge: F<bool[]?>("_unknownBridge"), ferry: F<bool[]?>("_ferry"), traffic: trafficFlag, dtmMin: dtmMin);
    var graphs = (System.Collections.IDictionary)typeof(SeaRouter).GetField("_graphs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    graphs[version] = new Lazy<Task<Sms.Domain.SeaGraph>>(() => Task.FromResult(sim));
    var known = opt.ContainsKey("nopull") ? new KnownWater(TrafficAreas.Empty, DtmGrid.Empty) : new KnownWater(traffic, dtm);
    var knownCache = (System.Collections.IDictionary)typeof(SeaRouter).GetField("_known", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    knownCache[version] = new Lazy<Task<KnownWater>>(() => Task.FromResult(known));
    Console.WriteLine($"known-water simulation ({knownArg}): {flagged} edges flagged traffic, {sampled} sampled in the DTM ({dtm.Chunks} chunks), {deep} DTM-deep ≥ 2.3 m, {priced} edges through isolated-danger discs ({clock.Elapsed.TotalSeconds:0} s)");
}

if (Environment.GetEnvironmentVariable("SMS_BENCH_ZONE") is { Length: > 0 } zoneArg)
{
    // buoyed zones (spec buoyed zones) on the active graph in memory, before derive-fairways has written the "Untiefen" notes and before a
    // rebuild: "shoal=<file of line ids>" - the stored single-mark lines through charted shoal water (Fairways.ShoalShare >= ZoneShoalShare;
    // computed and written to the file when it does not exist). They join the channel zone (RoutingGraphBuilder.BenchShoalIds, read by the
    // builder's and the smoother's FairwayLinesAsync): grid edges priced by their share inside the wider zone, and with
    // Routing:ZoneBlocksKnownWater the edges whose band water reaches into the buoyed zone (RoutingGraphBuilder.BlockZone) lose their traffic
    // flag and DTM depth (DepthLimits.InZone)
    var zopt = zoneArg.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "1");
    using var scope = sp.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
    var router = scope.ServiceProvider.GetRequiredService<SeaRouter>();
    var o = sp.GetRequiredService<RoutingGraphBuilder>().Options;
    var clock = Stopwatch.StartNew();
    var stored = await db.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == Sms.Domain.CorrectionKind.Passage && c.Source == Sms.Domain.CorrectionSource.Derived && c.Note != null && c.Note.StartsWith("Fahrwasser")).Select(c => new { c.Id, c.Geometry, c.Note }).ToListAsync();
    var shoalFile = zopt.GetValueOrDefault("shoal");
    if (shoalFile is not null && !File.Exists(shoalFile))
    {
        // the derive endpoint's rule on the stored lines, per 2° chunk of their centroids (the endpoint's margin)
        var singles = stored.Where(c => c.Geometry is NetTopologySuite.Geometries.LineString && !c.Note!.Contains(Fairways.PairedNote) && !c.Note.Contains(Fairways.WideNote)).ToList();
        var ids = new List<string>();
        foreach (var chunk in singles.GroupBy(c => (lat: Math.Floor(c.Geometry.Centroid.Y / 2) * 2, lng: Math.Floor(c.Geometry.Centroid.X / 2) * 2)))
        {
            var mLat = 0.15; var mLng = mLat / Math.Cos((chunk.Key.lat + 1) * Math.PI / 180);
            await db.Database.OpenConnectionAsync();
            var (_, water) = await Fairways.InputsAsync((Npgsql.NpgsqlConnection)db.Database.GetDbConnection(), new NetTopologySuite.Geometries.Envelope(chunk.Key.lng - mLng, chunk.Key.lng + 2 + mLng, chunk.Key.lat - mLat, chunk.Key.lat + 2 + mLat), default);
            foreach (var c in chunk)
            {
                var share = Fairways.ShoalShare((NetTopologySuite.Geometries.LineString)c.Geometry, water, o.ChannelPreferenceM, o.ZoneShoalFloorM);
                Console.WriteLine(FormattableString.Invariant($"shoal share {share:0.000} {c.Id} {c.Note} {c.Geometry.Centroid.Y:F5},{c.Geometry.Centroid.X:F5}"));
                if (share >= o.ZoneShoalShare) ids.Add(c.Id.ToString());
            }
            Console.WriteLine($"shoal shares: chunk {chunk.Key.lat},{chunk.Key.lng}: {chunk.Count()} single-mark lines ({clock.Elapsed.TotalSeconds:0} s)");
        }
        File.WriteAllLines(shoalFile, ids);
    }
    if (shoalFile is not null) RoutingGraphBuilder.BenchShoalIds = File.ReadAllLines(shoalFile).Where(l => l.Trim().Length > 0).Select(Guid.Parse).ToHashSet();
    var version = await router.ActiveVersionAsync(default);
    var g = await SeaRouter.LoadGraphAsync(new Npgsql.NpgsqlConnection(cs), version, sp.GetRequiredService<ILoggerFactory>().CreateLogger("bench"));
    T F<T>(string name) => (T)typeof(Sms.Domain.SeaGraph).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(g)!;
    var (allLines, zoneLines) = await RoutingGraphBuilder.FairwayLinesAsync(db);
    var oldLines = stored.Where(c => c.Geometry is NetTopologySuite.Geometries.LineString && c.Note!.Contains(Fairways.PairedNote) && !c.Note.Contains(Fairways.WideNote)).Select(c => (NetTopologySuite.Geometries.LineString)c.Geometry).ToList(); // the graph's zone lines
    var areas = (await db.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == Sms.Domain.CorrectionKind.Fairway && !(c.Note != null && c.Note.Contains(Fairways.WideNote))).Select(c => c.Geometry).ToListAsync())
        .SelectMany(x => x is NetTopologySuite.Geometries.Polygon pp ? [pp] : x is NetTopologySuite.Geometries.MultiPolygon mp ? mp.Geometries.OfType<NetTopologySuite.Geometries.Polygon>() : []).ToList();
    NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Prepared.IPreparedGeometry> Tree(IEnumerable<NetTopologySuite.Geometries.Geometry> gs)
    {
        var t = new NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Prepared.IPreparedGeometry>();
        foreach (var z in gs) t.Insert(z.EnvelopeInternal, NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(z));
        t.Build(); return t;
    }
    // the x3 zones: the graph's (paired lines), the builder's now (zone lines, the gaps' corridors cut out)
    var oldZone = Tree(ChannelPreference.Zone(oldLines, areas, o.ChannelPreferenceM));
    var newZone = Tree(ChannelPreference.Zone(zoneLines, areas, o.ChannelPreferenceM, Fairways.GapChords(allLines), o.ZoneBlockCorridorM));
    var block = o.ZoneBlocksKnownWater ? Tree(RoutingGraphBuilder.BlockZone(allLines, zoneLines, areas, o.ChannelPreferenceM, o.ZoneBlockCorridorM)) : null;
    if (zopt.TryGetValue("dump", out var dumpBox))
    {
        // "dump=s,w,n,e": the fairway gaps' chords and the buoyed-zone polygons in the box as GeoJSON lines (rendering)
        var bb = dumpBox.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var box = Geo.Factory.ToGeometry(new NetTopologySuite.Geometries.Envelope(bb[1], bb[3], bb[0], bb[2]));
        string Line(NetTopologySuite.Geometries.Coordinate[] c, string col, int dash) => $"{{\"type\":\"Feature\",\"properties\":{{\"col\":\"{col}\",\"id\":\"\",\"dash\":{dash}}},\"geometry\":{{\"type\":\"LineString\",\"coordinates\":[{string.Join(",", c.Select(x => FormattableString.Invariant($"[{x.X:F6},{x.Y:F6}]")))}]}}}}";
        var feats = Fairways.GapChords(allLines).Where(c => c.Intersects(box)).Select(c => Line(c.Coordinates, "#7b3294", 0)).ToList();
        if (block is not null) foreach (var z in block.Query(box.EnvelopeInternal)) for (var k = 0; k < z.Geometry.NumGeometries; k++) if (z.Geometry.GetGeometryN(k) is NetTopologySuite.Geometries.Polygon pz) foreach (var ring in new[] { pz.ExteriorRing }.Concat(pz.InteriorRings)) feats.Add(Line(ring.Coordinates, "#c51b7d", 1));
        File.WriteAllText(Path.Combine(outDir ?? ".", "zone-dump.geojson"), $"{{\"type\":\"FeatureCollection\",\"features\":[{string.Join(",", feats)}]}}");
    }
    // the band water DepthLimits.InZone samples: depth areas with a floor below DtmGrid.MaxFloorM
    var depths = new NetTopologySuite.Index.Strtree.STRtree<(NetTopologySuite.Geometries.Prepared.IPreparedGeometry prepared, double depth)>();
    if (block is not null)
    {
        await db.Database.OpenConnectionAsync(); var cn = (Npgsql.NpgsqlConnection)db.Database.GetDbConnection();
        await using var rd = await new Npgsql.NpgsqlCommand($"SELECT geom, min_depth_m FROM routing_depth WHERE min_depth_m < {DtmGrid.MaxFloorM.ToString(System.Globalization.CultureInfo.InvariantCulture)}", cn) { CommandTimeout = 3600 }.ExecuteReaderAsync();
        while (await rd.ReadAsync()) { var dg = rd.GetFieldValue<NetTopologySuite.Geometries.Geometry>(0); depths.Insert(dg.EnvelopeInternal, (NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(dg), rd.GetDouble(1))); }
    }
    depths.Build();
    var chartedDepth = F<float[]>("_depth");
    var cost = (float[])F<float[]>("_cost").Clone(); var trafficFlag = (bool[])(F<bool[]?>("_traffic") ?? new bool[g.EdgeCount]).Clone(); var dtmMin = (float[])(F<float[]?>("_dtmMin") ?? Enumerable.Repeat(float.NaN, g.EdgeCount).ToArray()).Clone();
    int blocked = 0, priced = 0;
    Parallel.For(0, g.EdgeCount, e =>
    {
        var cs0 = g.Coords(e); if (cs0.Length < 2 || cs0.Any(c => double.IsNaN(c.X) || double.IsNaN(c.Y))) return;
        var line = Geo.Factory.CreateLineString(cs0);
        if (!g.IsPassageNode(g.Source(e)) && !g.IsPassageNode(g.Target(e)) && newZone.Query(line.EnvelopeInternal) is var nh && oldZone.Query(line.EnvelopeInternal) is var oh && nh.Count + oh.Count > 0)
        {
            // ChannelPreference.Apply with the wider line set and the gaps cut out: the graph's cost carries the old share's factor
            var s1 = nh.Count > 0 ? ChannelPreference.ShareInside(line, nh) : 0; var s0 = oh.Count > 0 ? ChannelPreference.ShareInside(line, oh) : 0;
            if (s1 != s0) { cost[e] = (float)(cost[e] * (1 + s1 * (o.ChannelPenaltyFactor - 1)) / (1 + s0 * (o.ChannelPenaltyFactor - 1))); Interlocked.Increment(ref priced); }
        }
        if (block is null || (!trafficFlag[e] && float.IsNaN(dtmMin[e]))) return;
        if (block.Query(line.EnvelopeInternal) is not { Count: > 0 } bh || !DepthLimits.InZone(line, bh, depths, charted: !float.IsNaN(chartedDepth[e]))) return;
        trafficFlag[e] = false; dtmMin[e] = float.NaN; Interlocked.Increment(ref blocked);
    });
    var sim = new Sms.Domain.SeaGraph(F<double[]>("_x"), F<double[]>("_y"), F<int[]>("_src"), F<int[]>("_tgt"), cost, F<float[]>("_depth"), F<float[]>("_clear"), F<int[]>("_geomStart"), F<double[]>("_geom"), F<int>("_firstPassage"), F<bool[]?>("_isolated"),
        assumed: F<bool[]?>("_assumed"), adjacent: F<float[]?>("_adjacent"), floor: F<float[]?>("_floor"), unknownBridge: F<bool[]?>("_unknownBridge"), ferry: F<bool[]?>("_ferry"), traffic: trafficFlag, dtmMin: dtmMin);
    var graphs = (System.Collections.IDictionary)typeof(SeaRouter).GetField("_graphs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    graphs[version] = new Lazy<Task<Sms.Domain.SeaGraph>>(() => Task.FromResult(sim));
    Console.WriteLine($"zone simulation ({zoneArg}): {allLines.Count} derived lines, {zoneLines.Count} zone lines ({oldLines.Count} in the graph's zone, {RoutingGraphBuilder.BenchShoalIds.Count} shoal ids), {areas.Count} areas; {blocked} edges lost their known-water evidence, {priced} grid edges repriced ({clock.Elapsed.TotalSeconds:0} s)");
}

(string name, double lat1, double lng1, double lat2, double lng2, double draft, double? minDepth, double? warnDepth, double? airDraft, bool avoidUnknownBridges)[] routes =
[
    ("1 Grömitz → IJsselmeer", 54.13532689555557, 10.947591658888884, 52.981277113025385, 5.246755642840981, 1.5, null, null, null, false),
    ("2 Grömitz → Jade (Block area)", 54.13532689555557, 10.947591658888884, 53.604536351699096, 8.212714462108408, 2, null, null, null, false),
    ("3 Grömitz → Kiel Canal", 54.13532689555557, 10.947591658888884, 54.370083589266216, 10.084013717414763, 1.5, null, null, null, false),
];
// further arguments replace the list: "lat1,lng1,lat2,lng2,draft[,minDepth[,warnDepth[,airDraft]]]" each (one run, geometry printed)
var custom = args.Skip(2).Select(a => a.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray()).ToList();
if (custom.Count > 0) routes = custom.Select((v, i) => ($"{i + 1} custom", v[0], v[1], v[2], v[3], v[4], v.Length > 5 && v[5] > 0 ? v[5] : (double?)null, v.Length > 6 && v[6] > 0 ? v[6] : (double?)null, v.Length > 7 && v[7] > 0 ? v[7] : (double?)null, v.Length > 8 && v[8] > 0)).ToArray();
var runs = custom.Count > 0 ? 1 : 3;
var before = GC.GetTotalMemory(true);
for (var run = 0; run < runs; run++)
    foreach (var r in routes)
    {
        using var scope = sp.CreateScope();
        var router = scope.ServiceProvider.GetRequiredService<SeaRouter>();
        var sw = Stopwatch.StartNew();
        var res = await router.RouteAsync(Geo.Point(r.lat1, r.lng1), Geo.Point(r.lat2, r.lng2), default, r.airDraft, router.MinDepth(r.draft > 0 ? r.draft : null, r.minDepth), r.warnDepth, r.avoidUnknownBridges);
        var t = sw.ElapsedMilliseconds;
        Console.WriteLine($"run {run} {r.name}: {t} ms, {(res is null ? "no route (straight line)" : $"{res.DistanceNm} nm, {res.Geometry.NumPoints} pts, {string.Join(' ', res.Timings!.Select(kv => $"{kv.Key}={kv.Value}"))}")}");
        if (custom.Count > 0 && res is not null) Console.WriteLine("  " + string.Join(" ", res.Geometry.Coordinates.Select(c => FormattableString.Invariant($"{c.Y:F6},{c.X:F6}"))) + string.Concat((res.Warnings ?? []).Select(w => FormattableString.Invariant($"  [{w.Kind}: {w.Note} @{w.Lat},{w.Lng}{(w.Kind is "checkDepth" or "uncertainDepth" ? $" {w.DistanceM} m" : "")}]"))));
        if (run == 1 && res is not null && outDir is not null)
            await File.WriteAllTextAsync(Path.Combine(outDir, $"route{r.name[0]}.json"), "[" + string.Join(",", res.Geometry.Coordinates.Select(c => FormattableString.Invariant($"[{c.X:F6},{c.Y:F6}]"))) + "]");
    }
GC.Collect();
Console.WriteLine($"managed heap after load: {(GC.GetTotalMemory(true) - before) >> 20} MB above start, process working set {Process.GetCurrentProcess().WorkingSet64 >> 20} MB");

