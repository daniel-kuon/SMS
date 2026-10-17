using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OsmSharp;
using OsmSharp.Streams;
using Sms.Api.Endpoints;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;

namespace Sms.Api;

public sealed record PbfImportRequest(string Url, bool Seamarks = true, bool Harbours = true, bool Waterways = true, string? Bbox = null);
public sealed record PbfProgress(string Phase, string Message, DateTimeOffset At);

/// <summary>
/// Spec 10 §10.1 / §10.3: Europe-wide imports from a Geofabrik PBF extract instead of Overpass. Streams the file
/// twice with OsmSharp (pass 1: matching nodes and ways, pass 2: coordinates of the ways' nodes), converts the
/// matches into Overpass-style JSON batches and feeds the existing importers (sea marks, harbours, waterways,
/// bridges/locks). Memory stays bounded by the matches, not by the extract (europe-latest ≈ 28 GB, hours).
/// </summary>
public sealed class PbfImportJob(IServiceScopeFactory scopes, IHttpClientFactory http, IConfiguration cfg, ILogger<PbfImportJob> logger)
{
    private readonly List<PbfProgress> _progress = [];
    private int _running;
    public IReadOnlyList<PbfProgress> Progress => _progress;
    public bool IsRunning => _running == 1;

    public bool TryStart(PbfImportRequest req, Guid? userId)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(req, userId, CancellationToken.None); }
            catch (Exception ex) { Report("failed", ex.Message); logger.LogError(ex, "PBF import failed"); }
            finally { Interlocked.Exchange(ref _running, 0); }
        });
        return true;
    }

    private void Report(string phase, string message)
    {
        lock (_progress) { if (_progress.Count > 200) _progress.RemoveAt(0); _progress.Add(new PbfProgress(phase, message, DateTimeOffset.UtcNow)); }
        logger.LogInformation("PBF import [{Phase}] {Message}", phase, message);
    }

    public async Task RunAsync(PbfImportRequest req, Guid? userId, CancellationToken ct)
    {
        lock (_progress) _progress.Clear();
        var path = await EnsureFileAsync(req.Url, ct);
        await using var stream = File.OpenRead(path);
        await ImportStreamAsync(new PBFOsmStreamSource(stream), req, userId, ct);
    }

    /// <summary>Core of the job on an already opened OSM stream (tests feed a small PBF).</summary>
    public async Task<Dictionary<string, int>> ImportStreamAsync(OsmStreamSource source, PbfImportRequest req, Guid? userId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var bbox = ParseBbox(req.Bbox);
        var nodes = new List<Node>(); var ways = new List<Way>();
        Report("scan", "Pass 1: matching nodes and ways…");
        long seen = 0;
        foreach (var geo in source)
        {
            ct.ThrowIfCancellationRequested();
            if (++seen % 5_000_000 == 0) Report("scan", $"{seen / 1_000_000} M objects read, {nodes.Count} nodes / {ways.Count} ways matched ({sw.Elapsed:hh\\:mm\\:ss})");
            if (geo.Tags is null || geo.Tags.Count == 0) continue;
            var kind = Classify(geo.Tags, req);
            if (kind is null) continue;
            if (geo is Node n && n.Latitude is { } la && n.Longitude is { } lo) { if (bbox is null || bbox.Value.Contains(la, lo)) nodes.Add(n); }
            else if (geo is Way w && w.Nodes is { Length: > 1 }) ways.Add(w);
        }
        Report("scan", $"Pass 1 done: {nodes.Count} nodes, {ways.Count} ways ({sw.Elapsed:hh\\:mm\\:ss})");

        var needed = new HashSet<long>(ways.SelectMany(w => w.Nodes));
        var coords = new Dictionary<long, (double lat, double lon)>(needed.Count);
        if (needed.Count > 0)
        {
            Report("scan", $"Pass 2: coordinates of {needed.Count} way nodes…");
            source.Reset();
            foreach (var geo in source)
            {
                ct.ThrowIfCancellationRequested();
                if (geo is Node n && n.Id is { } id && needed.Contains(id) && n.Latitude is { } la && n.Longitude is { } lo) { coords[id] = (la, lo); if (coords.Count == needed.Count) break; }
            }
        }

        // Overpass-style elements: nodes with lat/lon, ways with center + geometry
        var elements = new List<object>(nodes.Count + ways.Count);
        foreach (var n in nodes) elements.Add(new { type = "node", id = n.Id ?? 0, lat = n.Latitude, lon = n.Longitude, tags = ToDict(n.Tags) });
        foreach (var w in ways)
        {
            var geom = w.Nodes.Where(coords.ContainsKey).Select(id => new { lat = coords[id].lat, lon = coords[id].lon }).ToList();
            if (geom.Count < 2) continue;
            var center = new { lat = geom.Average(g => g.lat), lon = geom.Average(g => g.lon) };
            if (bbox is { } b && !b.Contains(center.lat, center.lon)) continue;
            elements.Add(new { type = "way", id = w.Id ?? 0, center, geometry = geom, tags = ToDict(w.Tags) });
        }
        nodes.Clear(); ways.Clear(); coords.Clear();

        var totals = new Dictionary<string, int>();
        void Add(string key, int v) => totals[key] = totals.GetValueOrDefault(key) + v;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        const int batch = 4000;
        for (var i = 0; i < elements.Count; i += batch)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new { elements = elements.Skip(i).Take(batch) }));
            if (req.Seamarks) { var r = await SeamarkEndpoints.ImportAsync(Filter(doc, e => e.ContainsKey("seamark:type")), db, ct); Add("seamarksCreated", r.created); Add("seamarksUpdated", r.updated); }
            if (req.Harbours) { var r = await HarbourEndpoints.ImportAsync(Filter(doc, IsHarbour), db, userId, ct); Add("harboursCreated", r.created); Add("harboursUpdated", r.updated); }
            if (req.Waterways)
            {
                var r = await RoutingEndpoints.ImportWaterwaysAsync(Filter(doc, t => IsWaterway(t) || IsObstacle(t)), db, userId, ct);
                Add("waterways", r.Created); Add("bridges", r.Bridges); Add("locks", r.Locks);
            }
            db.ChangeTracker.Clear();
            Report("import", $"{Math.Min(i + batch, elements.Count)}/{elements.Count} elements imported ({sw.Elapsed:hh\\:mm\\:ss})");
        }
        Report("done", $"Done: {string.Join(", ", totals.Select(t => $"{t.Key} {t.Value}"))} ({sw.Elapsed:hh\\:mm\\:ss})");
        return totals;
    }

    private static string? Classify(OsmSharp.Tags.TagsCollectionBase tags, PbfImportRequest req)
    {
        var d = ToDict(tags);
        if (req.Seamarks && d.ContainsKey("seamark:type")) return "seamark";
        if (req.Harbours && IsHarbour(d)) return "harbour";
        if (req.Waterways && (IsWaterway(d) || IsObstacle(d))) return "waterway";
        return null;
    }

    public static bool IsHarbour(IReadOnlyDictionary<string, string> t) => t.GetValueOrDefault("leisure") == "marina" || t.GetValueOrDefault("seamark:type") == "harbour" || t.GetValueOrDefault("harbour") == "yes";
    public static bool IsWaterway(IReadOnlyDictionary<string, string> t) => Waterways.IsNavigable(t);
    public static bool IsObstacle(IReadOnlyDictionary<string, string> t) =>
        t.GetValueOrDefault("seamark:type") is "bridge" or "gate" || (t.ContainsKey("bridge") && t.ContainsKey("seamark:bridge:clearance_height")) || t.GetValueOrDefault("waterway") == "lock_gate" || t.GetValueOrDefault("lock") == "yes";

    private static Dictionary<string, string> ToDict(OsmSharp.Tags.TagsCollectionBase? tags) => tags is null ? [] : tags.ToDictionary(t => t.Key, t => t.Value);

    /// <summary>Sub-document with the elements whose tags match (the importers each expect only their kind).</summary>
    private static JsonDocument Filter(JsonDocument doc, Func<Dictionary<string, string>, bool> match)
    {
        var kept = new List<JsonElement>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
            if (el.TryGetProperty("tags", out var t) && match(t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? ""))) kept.Add(el);
        return JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new { elements = kept }));
    }

    private readonly record struct Box(double S, double W, double N, double E) { public bool Contains(double lat, double lon) => lat >= S && lat <= N && lon >= W && lon <= E; }
    private static Box? ParseBbox(string? bbox)
    {
        if (string.IsNullOrWhiteSpace(bbox)) return null;
        var p = bbox.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return p.Length == 4 ? new Box(p[0], p[1], p[2], p[3]) : null;
    }

    private async Task<string> EnsureFileAsync(string urlOrPath, CancellationToken ct)
    {
        if (File.Exists(urlOrPath)) { Report("file", $"Using {urlOrPath}"); return urlOrPath; }
        if (!Uri.TryCreate(urlOrPath, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new InvalidOperationException("URL oder Pfad einer .osm.pbf-Datei angeben.");
        var dir = Path.GetFullPath(Path.Combine(cfg["Routing:CachePath"] ?? "media/cache", "osm"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, Path.GetFileName(uri.LocalPath));
        if (File.Exists(path) && new FileInfo(path).Length > 0) { Report("file", $"Using cached {path}"); return path; }
        Report("file", $"Downloading {uri}…");
        var client = http.CreateClient("routing");
        client.Timeout = TimeSpan.FromHours(6);
        await using (var src = await client.GetStreamAsync(uri, ct))
        await using (var dst = File.Create(path + ".part"))
            await src.CopyToAsync(dst, ct);
        File.Move(path + ".part", path, overwrite: true);
        Report("file", $"Downloaded {new FileInfo(path).Length / 1_048_576} MB");
        return path;
    }
}

