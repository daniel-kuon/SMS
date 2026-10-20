// Re-checks the harbour access links of a graph version in memory, read-only: for every harbour of the region without an access row
// it tries the 40 nearest nodes within Routing:HarbourAccessM as NearestWaterLinkAsync does, with the old rule (links under 300 m were
// never land-tested, before 2eaf76f) and the current one (SeaGraph.LinkLandOk), and prints the harbours only the old rule reached with
// the land pattern of their link (run lengths of L(and)/W(ater) samples every ~25 m from the harbour).
// usage (from the repository root): dotnet run --project tools/HarbourAccessBench -- "Host=localhost;Port=…;Database=sms;Username=postgres;Password=…" [graph_version]
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;
using Npgsql;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;

var cs = args[0];
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "Sms.slnx"))) root = Path.GetDirectoryName(root)!;
var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(root, "src/Sms.Api/appsettings.json")).Build();
var accessM = config.GetValue("Routing:HarbourAccessM", 3000.0);
await using var db = new SmsDbContext(new DbContextOptionsBuilder<SmsDbContext>().UseNpgsql(cs, n => n.UseNetTopologySuite()).Options);
var conn = (NpgsqlConnection)db.Database.GetDbConnection();
await conn.OpenAsync();
var version = args.Length > 1 ? int.Parse(args[1]) : int.Parse((string)(await new NpgsqlCommand("SELECT value FROM routing_meta WHERE key = 'active_version'", conn).ExecuteScalarAsync())!);
var r4 = ((string)(await new NpgsqlCommand("SELECT value FROM routing_meta WHERE key = 'region'", conn).ExecuteScalarAsync())!).Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
var region = Geo.Factory.ToGeometry(new Envelope(r4[0], r4[2], r4[1], r4[3]));

var blocks = await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Block).Select(c => c.Geometry).ToListAsync();
var waters = await db.RoutingCorrections.Where(c => c.Active && c.Kind == CorrectionKind.Water).Select(c => c.Geometry).ToListAsync();
var land = await RoutingGraphBuilder.LandIndexAsync(conn, version, blocks, default);
var nodes = new List<GraphNode>();
await using (var cmd = new NpgsqlCommand("SELECT id, ST_X(geom), ST_Y(geom) FROM routing_node WHERE graph_version = @v", conn) { CommandTimeout = 600, Parameters = { new("v", version) } })
await using (var r = await cmd.ExecuteReaderAsync())
    while (await r.ReadAsync()) nodes.Add(new GraphNode(r.GetInt64(0), r.GetDouble(1), r.GetDouble(2), false));
var index = NodeIndex.Build(nodes);
var reached = new HashSet<Guid>();
await using (var cmd = new NpgsqlCommand("SELECT harbour_id FROM routing_harbour_access WHERE graph_version = @v", conn) { Parameters = { new("v", version) } })
await using (var r = await cmd.ExecuteReaderAsync())
    while (await r.ReadAsync()) reached.Add(r.GetGuid(0));
var harbours = await db.Harbours.Where(h => h.IsActive && h.MergedIntoId == null && h.Location.Intersects(region)).Select(h => new { h.Id, h.Name, h.Location }).ToListAsync();
Console.WriteLine($"graph {version}: {nodes.Count} nodes, {harbours.Count} harbours, {reached.Count} with access, {harbours.Count - reached.Count} without");

static string Runs(bool[]? land)
{
    if (land is null) return "-";
    var s = new List<string>();
    for (var k = 0; k < land.Length;) { var j = k; while (j < land.Length && land[j] == land[k]) j++; s.Add($"{(land[k] ? 'L' : 'W')}{j - k}"); k = j; }
    return string.Join(' ', s);
}
bool OnLand(Coordinate c) { var pt = Geo.Factory.CreatePoint(c); return land.Query(new Envelope(c)).Any(l => l.prepared.Contains(pt)); }
bool InWater(Coordinate c) { var pt = Geo.Factory.CreatePoint(c); return waters.Any(w => w.EnvelopeInternal.Contains(c) && w.Contains(pt)); }

var isolated = new HashSet<long>();
await using (var cmd = new NpgsqlCommand("SELECT id FROM routing_isolated WHERE graph_version = @v", conn) { Parameters = { new("v", version) } })
await using (var r = await cmd.ExecuteReaderAsync())
    while (await r.ReadAsync()) isolated.Add(r.GetInt64(0));
var gridNodes = long.Parse((string)(await new NpgsqlCommand("SELECT value FROM routing_meta WHERE key = 'grid_nodes'", conn).ExecuteScalarAsync())!);
var lostOld = 0; var restored = 0; var extra = 0;
foreach (var h in harbours.Where(h => !reached.Contains(h.Id)))
{
    var p = h.Location.Coordinate;
    var cands = index.Nearest(p, accessM, 40).Select(n => new Cand(n, RoutingGraphBuilder.Haversine(p.Y, p.X, n.Y, n.X), RoutingGraphBuilder.LinkLandSamples(land, p, new Coordinate(n.X, n.Y)))).ToList();
    // as the builder: strict links first (main component, then any node), the harbour-basin rule only when none passes
    Cand? Pick(bool harbour) => cands.FirstOrDefault(c => !isolated.Contains(c.N.Id) && c.Ok(harbour)) ?? cands.FirstOrDefault(c => c.Ok(harbour));
    var old = cands.FirstOrDefault(c => c.M < 300 || c.Ok(false));
    var now = Pick(false) ?? Pick(true);
    string Desc(Cand c) => $"{c.M:0} m to {(c.N.Id > gridNodes ? "passage" : "area")} node{(isolated.Contains(c.N.Id) ? " (isolated)" : "")} at {c.N.Y:0.00000} N {c.N.X:0.00000} E | {Runs(c.Land)}";
    var where = $"{h.Name} | {p.Y:0.00000} N {p.X:0.00000} E | start {(OnLand(p) ? "land" : "water")}{(InWater(p) ? " (Water correction)" : "")}";
    if (old is not null) { lostOld++; if (now is not null) restored++; Console.WriteLine($"{(now is null ? "STILL LOST" : "RESTORED")} {where} | old: {Desc(old)}{(now is null ? "" : $" | now: {Desc(now)}")}"); }
    else if (now is not null) { extra++; Console.WriteLine($"NEW {where} | now: {Desc(now)}"); }
}
Console.WriteLine($"{lostOld} harbours reached by the old rule only: {restored} restored; {extra} reached that the old rule did not reach; expected reachable {reached.Count + restored + extra}/{harbours.Count}");

record Cand(GraphNode N, double M, bool[]? Land)
{
    public bool Ok(bool harbour) => Land is null || SeaGraph.LinkLandOk(Land, M / (Land.Length + 1), harbour);
}
