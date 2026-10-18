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
using NetTopologySuite.Geometries;

namespace Sms.Api;

public sealed record PbfImportRequest(string Url, bool Seamarks = true, bool Harbours = true, bool Waterways = true, string? Bbox = null, bool Pois = true, double PoiRadiusKm = 3);
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
            catch (Exception ex) { Report("failed", ex is InvalidOperationException ? ex.Message : $"Import fehlgeschlagen: {ex.GetType().Name}: {ex.Message[..Math.Min(ex.Message.Length, 300)]}{(ex.InnerException is { } ie ? $" ← {ie.GetType().Name}: {ie.Message[..Math.Min(ie.Message.Length, 200)]}" : "")}"); logger.LogError(ex, "PBF import failed"); }
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
        // POIs are kept only near harbours: the harbours already in the database plus harbour nodes met in this pass (marina areas
        // only get their centre in pass 2, so a new region needs a second run for the POIs around them)
        var poiNear = req.Pois ? await HarbourIndexAsync(req.PoiRadiusKm * 1000, ct) : null;
        Report("scan", "Pass 1: matching nodes and ways…");
        long seen = 0;
        foreach (var geo in source)
        {
            ct.ThrowIfCancellationRequested();
            if (++seen % 5_000_000 == 0) Report("scan", $"{seen / 1_000_000} M objects read, {nodes.Count} nodes / {ways.Count} ways matched ({sw.Elapsed:hh\\:mm\\:ss})");
            if (geo.Tags is null || geo.Tags.Count == 0) continue;
            var kind = Classify(geo.Tags, req);
            if (kind is null) continue;
            if (geo is Node n && n.Latitude is { } la && n.Longitude is { } lo)
            {
                if (bbox is not null && !bbox.Value.Contains(la, lo)) continue;
                if (kind == "poi" && !poiNear!.Near(la, lo)) continue;
                if (kind == "harbour") poiNear?.Add(la, lo);
                nodes.Add(n);
            }
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
        // derived waterways/bridges/locks inside the extract's extent are replaced once, before the batches (a per-batch
        // replacement would delete what earlier batches just added)
        if (req.Waterways)
        {
            var env = new Envelope();
            foreach (var el in elements)
            {
                var json = JsonSerializer.SerializeToElement(el);
                if (json.TryGetProperty("geometry", out var g)) foreach (var c in g.EnumerateArray()) env.ExpandToInclude(c.GetProperty("lon").GetDouble(), c.GetProperty("lat").GetDouble());
                else if (json.TryGetProperty("lat", out var la)) env.ExpandToInclude(json.GetProperty("lon").GetDouble(), la.GetDouble());
            }
            if (!env.IsNull)
            {
                var extent = Geo.Factory.ToGeometry(env); extent.SRID = 4326;
                var removed = await db.RoutingCorrections.Where(c => (c.Kind == CorrectionKind.Passage || c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.Lock || c.Kind == CorrectionKind.OpeningBridge || c.Kind == CorrectionKind.Fairway) && c.Source == CorrectionSource.Derived && c.Geometry.Within(extent)).ExecuteDeleteAsync(ct);
                Add("derivedReplaced", removed);
            }
        }
        const int batch = 4000;
        for (var i = 0; i < elements.Count; i += batch)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new { elements = elements.Skip(i).Take(batch) }));
            // one bad element must not sink the whole Europe import: a failed batch is logged, counted and skipped
            try
            {
                if (req.Seamarks) { var r = await SeamarkEndpoints.ImportAsync(Filter(doc, e => e.ContainsKey("seamark:type")), db, ct); Add("seamarksCreated", r.created); Add("seamarksUpdated", r.updated); }
                if (req.Harbours) { var r = await HarbourEndpoints.ImportAsync(Filter(doc, IsHarbour), db, userId, ct); Add("harboursCreated", r.created); Add("harboursUpdated", r.updated); }
                if (req.Pois) { var r = await PoiEndpoints.ImportAsync(Filter(doc, IsPoi), poiNear!, db, ct); Add("pois", r); }
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pg)
            {
                Add("failedBatches", 1); db.ChangeTracker.Clear();
                Report("import", $"Batch {i / batch + 1} skipped: {pg.SqlState} {pg.MessageText[..Math.Min(pg.MessageText.Length, 120)]}");
                logger.LogWarning(ex, "PBF import batch {Batch} skipped", i / batch + 1);
            }
            if (req.Waterways)
            {
                var r = await RoutingEndpoints.ImportWaterwaysAsync(Filter(doc, t => IsWaterway(t) || IsObstacle(t) || IsFairwayArea(t)), db, userId, ct, replaceExisting: false);
                Add("waterways", r.Created); Add("bridges", r.Bridges); Add("locks", r.Locks); Add("fairwayAreas", r.FairwayAreas);
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
        if (req.Waterways && IsFairwayArea(d)) return "waterway"; // an area, not a point mark
        if (req.Seamarks && d.ContainsKey("seamark:type")) return "seamark";
        if (req.Harbours && IsHarbour(d)) return "harbour";
        if (req.Waterways && (IsWaterway(d) || IsObstacle(d))) return "waterway";
        if (req.Pois && IsPoi(d)) return "poi";
        return null;
    }

    private static readonly HashSet<string> PoiKeys = ["amenity", "shop", "tourism", "leisure", "craft", "office", "healthcare"];
    private static readonly HashSet<string> PoiSkip = ["parking", "bench", "waste_basket", "bicycle_parking", "parking_space", "parking_entrance", "grave_yard", "vending_machine", "recycling", "shelter", "hunting_stand", "picnic_table", "yes", "pitch", "garden", "playground", "swimming_pool", "track", "park", "nature_reserve", "artwork", "information", "viewpoint"];
    /// <summary>Named shops, restaurants, services; toilets and fuel also without a name (as OpenSeaMap shows them).</summary>
    public static bool IsPoi(IReadOnlyDictionary<string, string> t)
    {
        if (IsHarbour(t) || t.ContainsKey("seamark:type")) return false;
        foreach (var k in PoiKeys)
            if (t.TryGetValue(k, out var v) && !PoiSkip.Contains(v))
                return t.ContainsKey("name") || v is "toilets" or "fuel" or "drinking_water" or "atm" or "shower";
        return false;
    }

    /// <summary>Harbour positions as a coarse grid for a cheap "within r metres of a harbour" test during the scan.</summary>
    public sealed class NearIndex(double radiusM)
    {
        private readonly double _cell = radiusM / 111_320.0;
        private readonly HashSet<(int, int)> _cells = [];
        public int Count { get; private set; }
        public void Add(double lat, double lng)
        {
            Count++;
            var cx = (int)Math.Floor(lng / _cell * Math.Cos(lat * Math.PI / 180)); var cy = (int)Math.Floor(lat / _cell);
            for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++) _cells.Add((cx + dx, cy + dy));
        }
        public bool Near(double lat, double lng) => _cells.Contains(((int)Math.Floor(lng / _cell * Math.Cos(lat * Math.PI / 180)), (int)Math.Floor(lat / _cell)));
    }

    private async Task<NearIndex> HarbourIndexAsync(double radiusM, CancellationToken ct)
    {
        var index = new NearIndex(radiusM);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        foreach (var h in await db.Harbours.AsNoTracking().Select(h => h.Location).ToListAsync(ct)) index.Add(h.Y, h.X);
        return index;
    }

    public static bool IsHarbour(IReadOnlyDictionary<string, string> t) => t.GetValueOrDefault("leisure") == "marina" || t.GetValueOrDefault("seamark:type") == "harbour" || t.GetValueOrDefault("harbour") == "yes";
    public static bool IsWaterway(IReadOnlyDictionary<string, string> t) => Waterways.IsNavigable(t);
    public static bool IsFairwayArea(IReadOnlyDictionary<string, string> t) => t.GetValueOrDefault("seamark:type") == "fairway" && !t.ContainsKey("waterway");
    public static bool IsObstacle(IReadOnlyDictionary<string, string> t) =>
        t.GetValueOrDefault("seamark:type") is "bridge" or "gate" || (t.ContainsKey("bridge") && (t.ContainsKey("seamark:bridge:clearance_height") || t.ContainsKey("bridge:movable"))) || t.GetValueOrDefault("waterway") == "lock_gate" || t.GetValueOrDefault("lock") == "yes";

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

    /// <summary>Import directory for cached extracts (the only place local file names are resolved against).</summary>
    public string ImportDir => Path.GetFullPath(Path.Combine(cfg["Routing:CachePath"] ?? "media/cache", "osm"));

    /// <summary>
    /// The source must be an https URL on an allow-listed host (Import:AllowedHosts, default download.geofabrik.de)
    /// or the bare file name of an extract already in the import directory – an admin form must not become an
    /// SSRF or local-file-read primitive. Returns the error message or null.
    /// </summary>
    public string? Validate(string urlOrPath)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath)) return "URL oder Dateiname einer .osm.pbf-Datei angeben.";
        if (Uri.TryCreate(urlOrPath, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            var allowed = cfg.GetSection("Import:AllowedHosts").Get<string[]>() is { Length: > 0 } a ? a : ["download.geofabrik.de"];
            if (uri.Scheme != "https") return "Nur https-Downloads.";
            if (!allowed.Contains(uri.Host.TrimEnd('.'), StringComparer.OrdinalIgnoreCase)) return $"Host nicht erlaubt (Import:AllowedHosts): {string.Join(", ", allowed)}.";
            if (!uri.AbsolutePath.EndsWith(".osm.pbf", StringComparison.OrdinalIgnoreCase)) return "Erwartet eine .osm.pbf-Datei.";
            return null;
        }
        if (urlOrPath != Path.GetFileName(urlOrPath) || !urlOrPath.EndsWith(".osm.pbf", StringComparison.OrdinalIgnoreCase)) return "Lokale Dateien: nur der Dateiname einer Datei im Importverzeichnis.";
        return File.Exists(Path.Combine(ImportDir, urlOrPath)) ? null : "Datei nicht im Importverzeichnis.";
    }

    private async Task<string> EnsureFileAsync(string urlOrPath, CancellationToken ct)
    {
        if (Validate(urlOrPath) is { } error) throw new InvalidOperationException(error);
        var dir = ImportDir;
        Directory.CreateDirectory(dir);
        if (!Uri.TryCreate(urlOrPath, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            var local = Path.GetFullPath(Path.Combine(dir, urlOrPath));
            if (!local.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Ungültiger Dateiname.");
            Report("file", $"Using {Path.GetFileName(local)}"); return local;
        }
        var path = Path.Combine(dir, Path.GetFileName(uri.LocalPath));
        if (File.Exists(path) && new FileInfo(path).Length > 0) { Report("file", $"Using cached {Path.GetFileName(path)}"); return path; }
        Report("file", $"Downloading {uri}…");
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromHours(6) }; // no redirects: the allow-listed host must serve the file itself
        using var resp = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Download fehlgeschlagen ({(int)resp.StatusCode}).");
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(path + ".part"))
            await src.CopyToAsync(dst, ct);
        File.Move(path + ".part", path, overwrite: true);
        Report("file", $"Downloaded {new FileInfo(path).Length / 1_048_576} MB");
        return path;
    }
}

