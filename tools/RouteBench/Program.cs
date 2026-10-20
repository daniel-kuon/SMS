// Times SeaRouter point → point routes against the dev database without the API: graph load (first route), then warm runs.
// usage: dotnet run --project tools/RouteBench -c Release -- "Host=localhost;Port=…;Database=sms;Username=postgres;Password=…" [geojson-out-dir]
//        [routes "lat1,lng1,lat2,lng2,draft[,minDepth[,warnDepth]]" …]   (draft 0 = none; minDepth replaces draft + margin)
//        or as third argument "backfill-adjacent": DepthLimits' coastal no-data rule (adjacent_depth_m) for the active graph version, without a rebuild
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

(string name, double lat1, double lng1, double lat2, double lng2, double draft, double? minDepth, double? warnDepth)[] routes =
[
    ("1 Grömitz → IJsselmeer", 54.13532689555557, 10.947591658888884, 52.981277113025385, 5.246755642840981, 1.5, null, null),
    ("2 Grömitz → Jade (Block area)", 54.13532689555557, 10.947591658888884, 53.604536351699096, 8.212714462108408, 2, null, null),
    ("3 Grömitz → Kiel Canal", 54.13532689555557, 10.947591658888884, 54.370083589266216, 10.084013717414763, 1.5, null, null),
];
// further arguments replace the list: "lat1,lng1,lat2,lng2,draft[,minDepth[,warnDepth]]" each (one run, geometry printed)
var custom = args.Skip(2).Select(a => a.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray()).ToList();
if (custom.Count > 0) routes = custom.Select((v, i) => ($"{i + 1} custom", v[0], v[1], v[2], v[3], v[4], v.Length > 5 && v[5] > 0 ? v[5] : (double?)null, v.Length > 6 && v[6] > 0 ? v[6] : (double?)null)).ToArray();
var runs = custom.Count > 0 ? 1 : 3;
var before = GC.GetTotalMemory(true);
for (var run = 0; run < runs; run++)
    foreach (var r in routes)
    {
        using var scope = sp.CreateScope();
        var router = scope.ServiceProvider.GetRequiredService<SeaRouter>();
        var sw = Stopwatch.StartNew();
        var res = await router.RouteAsync(Geo.Point(r.lat1, r.lng1), Geo.Point(r.lat2, r.lng2), default, null, router.MinDepth(r.draft > 0 ? r.draft : null, r.minDepth), r.warnDepth);
        var t = sw.ElapsedMilliseconds;
        Console.WriteLine($"run {run} {r.name}: {t} ms, {(res is null ? "no route (straight line)" : $"{res.DistanceNm} nm, {res.Geometry.NumPoints} pts, {string.Join(' ', res.Timings!.Select(kv => $"{kv.Key}={kv.Value}"))}")}");
        if (custom.Count > 0 && res is not null) Console.WriteLine("  " + string.Join(" ", res.Geometry.Coordinates.Select(c => FormattableString.Invariant($"{c.Y:F6},{c.X:F6}"))) + string.Concat((res.Warnings ?? []).Select(w => FormattableString.Invariant($"  [{w.Kind}: {w.Note} @{w.Lat},{w.Lng}{(w.Kind == "checkDepth" ? $" {w.DistanceM} m" : "")}]"))));
        if (run == 1 && res is not null && outDir is not null)
            await File.WriteAllTextAsync(Path.Combine(outDir, $"route{r.name[0]}.json"), "[" + string.Join(",", res.Geometry.Coordinates.Select(c => FormattableString.Invariant($"[{c.X:F6},{c.Y:F6}]"))) + "]");
    }
GC.Collect();
Console.WriteLine($"managed heap after load: {(GC.GetTotalMemory(true) - before) >> 20} MB above start, process working set {Process.GetCurrentProcess().WorkingSet64 >> 20} MB");
