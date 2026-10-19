// Times the area graph of an existing graph version (obstacle polygons → nodes → visibility edges → land check) without writing a
// graph: RoutingGraphBuilder.AreaGraphAsync only reads routing_land / routing_depth / corrections (the obstacle cache file under
// Routing:CachePath is the one thing it may write). Run twice to see a cache hit.
// usage (from the repository root): dotnet run --project tools/BuildBench -c Release -- "Host=localhost;Port=…;Database=sms;Username=postgres;Password=…" <graph_version>
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Npgsql;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;

var cs = args[0]; var version = int.Parse(args[1]);
var root = Directory.GetCurrentDirectory(); // run from the repository (the build output may live elsewhere)
while (!File.Exists(Path.Combine(root, "Sms.slnx"))) root = Path.GetDirectoryName(root)!;
var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(root, "src/Sms.Api/appsettings.json")).AddEnvironmentVariables("SMS_").Build();
config["Routing:CachePath"] = Path.Combine(root, "media/cache"); // appsettings' path is relative to src/Sms.Api
var services = new ServiceCollection()
    .AddLogging(l => l.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }).SetMinimumLevel(LogLevel.Information).AddFilter("Microsoft", LogLevel.Warning))
    .AddSingleton<IConfiguration>(config)
    .AddDbContext<SmsDbContext>(o => o.UseNpgsql(cs, n => n.UseNetTopologySuite()))
    .AddSingleton<RoutingGraphBuilder>();
services.AddHttpClient();
await using var sp = services.BuildServiceProvider();
await using var scope = sp.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
var builder = sp.GetRequiredService<RoutingGraphBuilder>();
var o = builder.Options;
var conn = (NpgsqlConnection)db.Database.GetDbConnection();
await conn.OpenAsync();
var landKey = await new NpgsqlCommand("SELECT value FROM routing_meta WHERE key = 'land_key'", conn).ExecuteScalarAsync() as string ?? "";
var region = new Envelope(o.Region.MinLng, o.Region.MaxLng, o.Region.MinLat, o.Region.MaxLat);
var harbours = await db.Harbours.Where(h => h.IsActive && h.MergedIntoId == null && h.Location.Intersects(Geo.Factory.ToGeometry(region))).Select(h => h.Location).ToListAsync();
var bridges = (await db.RoutingCorrections.Where(c => c.Active && (c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.Lock || c.Kind == CorrectionKind.OpeningBridge)).Select(c => new { c.Kind, c.Geometry, c.ClearanceM }).ToListAsync())
    .Select(c => (c.Geometry is LineString ? c.Geometry : c.Geometry.Centroid, c.Kind, c.ClearanceM)).ToList();
var sw = Stopwatch.StartNew();
var (nodes, edges) = await builder.AreaGraphAsync(conn, db, version, region, o, harbours.Select(h => h.Coordinate).ToList(), bridges, landKey, default);
Console.WriteLine($"area graph of version {version}: {nodes.Count} nodes, {edges.Count} edges in {sw.Elapsed.TotalSeconds:0} s");
// a fingerprint to compare runs (before/after an optimisation must give the same graph)
Console.WriteLine($"fingerprint nodes {nodes.Sum(n => Math.Round(n.X * 1e5) + Math.Round(n.Y * 1e5) * 3):0} edges {edges.Sum(e => Math.Round(e.Cost)):0}");
// compared with the graph the API wrote for this version, by coordinates (ids shift when one corner more or less comes first): area
// nodes are ids 1…N there, the visibility edges the ones between two of them
static (long, long) K(double x, double y) => ((long)Math.Round(x * 1e7), (long)Math.Round(y * 1e7));
var storedNodes = new Dictionary<long, (long, long)>();
await using (var cmd = new NpgsqlCommand("SELECT id, ST_X(geom), ST_Y(geom) FROM routing_node WHERE graph_version = @v", conn) { CommandTimeout = 600, Parameters = { new("v", version) } })
await using (var r = await cmd.ExecuteReaderAsync())
    while (await r.ReadAsync()) storedNodes[r.GetInt64(0)] = K(r.GetDouble(1), r.GetDouble(2));
if (storedNodes.Count == 0) { Console.WriteLine("no stored graph to compare with"); return; }
var area = (await new NpgsqlCommand("SELECT value FROM routing_meta WHERE key = 'grid_nodes'", conn).ExecuteScalarAsync() as string) is { } gn && version == int.Parse((string)(await new NpgsqlCommand("SELECT value FROM routing_meta WHERE key = 'active_version'", conn).ExecuteScalarAsync())!) ? long.Parse(gn) : long.Parse(Environment.GetEnvironmentVariable("BENCH_AREA_NODES") ?? "0");
var storedSet = storedNodes.Where(kv => kv.Key <= area).Select(kv => kv.Value).ToHashSet();
var mineSet = nodes.Select(n => K(n.X, n.Y)).ToHashSet();
Console.WriteLine($"stored area nodes {storedSet.Count} (ids ≤ {area}), mine {mineSet.Count}: {mineSet.Intersect(storedSet).Count()} same coordinates, {mineSet.Except(storedSet).Count()} only here, {storedSet.Except(mineSet).Count()} only stored");
var storedEdges = new HashSet<((long, long), (long, long))>();
await using (var cmd = new NpgsqlCommand("SELECT source, target FROM routing_edge WHERE graph_version = @v AND source <= @n AND target <= @n", conn) { CommandTimeout = 600, Parameters = { new("v", version), new("n", area) } })
await using (var r = await cmd.ExecuteReaderAsync())
    while (await r.ReadAsync()) { var a = storedNodes[r.GetInt64(0)]; var b = storedNodes[r.GetInt64(1)]; storedEdges.Add(a.CompareTo(b) < 0 ? (a, b) : (b, a)); }
var byId = nodes.ToDictionary(n => n.Id, n => K(n.X, n.Y));
var mineEdges = edges.Select(e => { var a = byId[e.Source]; var b = byId[e.Target]; return a.CompareTo(b) < 0 ? (a, b) : (b, a); }).ToHashSet();
Console.WriteLine($"stored visibility edges {storedEdges.Count}, mine {mineEdges.Count}: {mineEdges.Intersect(storedEdges).Count()} same, {mineEdges.Except(storedEdges).Count()} only here, {storedEdges.Except(mineEdges).Count()} only stored");
