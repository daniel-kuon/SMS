// Times SeaRouter point → point routes against the dev database without the API: graph load (first route), then warm runs.
// usage: dotnet run --project tools/RouteBench -c Release -- "Host=localhost;Port=…;Database=sms;Username=postgres;Password=…" [geojson-out-dir]
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

(string name, double lat1, double lng1, double lat2, double lng2, double draft)[] routes =
[
    ("1 Grömitz → IJsselmeer", 54.13532689555557, 10.947591658888884, 52.981277113025385, 5.246755642840981, 1.5),
    ("2 Grömitz → Jade (Block area)", 54.13532689555557, 10.947591658888884, 53.604536351699096, 8.212714462108408, 2),
    ("3 Grömitz → Kiel Canal", 54.13532689555557, 10.947591658888884, 54.370083589266216, 10.084013717414763, 1.5),
];
var before = GC.GetTotalMemory(true);
for (var run = 0; run < 3; run++)
    foreach (var r in routes)
    {
        using var scope = sp.CreateScope();
        var router = scope.ServiceProvider.GetRequiredService<SeaRouter>();
        var sw = Stopwatch.StartNew();
        var res = await router.RouteAsync(Geo.Point(r.lat1, r.lng1), Geo.Point(r.lat2, r.lng2), default, null, r.draft);
        var t = sw.ElapsedMilliseconds;
        Console.WriteLine($"run {run} {r.name}: {t} ms, {(res is null ? "no route (straight line)" : $"{res.DistanceNm} nm, {res.Geometry.NumPoints} pts, {string.Join(' ', res.Timings!.Select(kv => $"{kv.Key}={kv.Value}"))}")}");
        if (run == 1 && res is not null && outDir is not null)
            await File.WriteAllTextAsync(Path.Combine(outDir, $"route{r.name[0]}.json"), "[" + string.Join(",", res.Geometry.Coordinates.Select(c => FormattableString.Invariant($"[{c.X:F6},{c.Y:F6}]"))) + "]");
    }
GC.Collect();
Console.WriteLine($"managed heap after load: {(GC.GetTotalMemory(true) - before) >> 20} MB above start, process working set {Process.GetCurrentProcess().WorkingSet64 >> 20} MB");
