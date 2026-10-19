using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sms.Infrastructure;
using System.Text.RegularExpressions;

namespace Sms.Api.Endpoints;

/// <summary>
/// Self-hosted base map (spec 07 G / docs 13): when a Planetiler PMTiles file exists (Map:BasemapFile, built by
/// tools/build-basemap.sh) and Martin serves it (Map:BaseTilesUrl, wired by the AppHost), the OpenFreeMap "Liberty"
/// style is served from here with its vector source pointed at Martin and its glyphs/sprites pointed at a cache proxy
/// below, so after the first visit nothing is fetched from OpenFreeMap any more.
/// </summary>
public static class MapStyleEndpoints
{
    public const string DefaultStyleUrl = "https://tiles.openfreemap.org/styles/liberty";

    /// <summary>Map:RemoteBasemap=true uses OpenFreeMap's own tiles (full Europe, all detail) even when a local base map exists.</summary>
    public static bool BasemapAvailable(IConfiguration cfg, IWebHostEnvironment env) =>
        !cfg.GetValue("Map:RemoteBasemap", false) && cfg["Map:BaseTilesUrl"] is { Length: > 0 } && cfg["Map:BasemapFile"] is { Length: > 0 } f && File.Exists(Path.GetFullPath(Path.Combine(env.ContentRootPath, f)));

    /// <summary>A second tile set with full detail (z12–14) only around harbours: media/tiles/basemap-detail.pmtiles (tools/build-basemap.sh detail …).</summary>
    public static bool DetailAvailable(IConfiguration cfg, IWebHostEnvironment env) =>
        cfg["Map:BasemapFile"] is { Length: > 0 } f && File.Exists(Path.GetFullPath(Path.Combine(env.ContentRootPath, f.Replace("basemap.pmtiles", "basemap-detail.pmtiles"))));

    public static void MapMapStyle(this IEndpointRouteBuilder api)
    {
        // Planetiler polygon for the harbour-detail tile set: the union of 3 km (radiusKm) circles around every harbour, .poly format
        api.MapGet("/admin/map/harbour-areas.poly", async (double? radiusKm, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var r = Math.Clamp(radiusKm ?? 3, 0.5, 20) * 1000;
            var rows = await db.Database.SqlQueryRaw<string>("""
                SELECT ST_AsGeoJSON((ST_Dump(ST_Union(ST_Buffer("Location", {0}, 8)::geometry))).geom) AS "Value" FROM "Harbours"
                """, r).ToListAsync(ct);
            var sb = new System.Text.StringBuilder("harbours\n");
            var n = 0;
            foreach (var g in rows)
            {
                var coords = System.Text.Json.JsonDocument.Parse(g).RootElement.GetProperty("coordinates")[0]; // outer ring only
                sb.Append(++n).Append('\n');
                foreach (var c in coords.EnumerateArray()) sb.Append("   ").Append(c[0].GetDouble().ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(c[1].GetDouble().ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("END\n");
            }
            sb.Append("END\n");
            return Results.Text(sb.ToString(), "text/plain", System.Text.Encoding.UTF8);
        }).WithTags("Map");

        api.MapGet("/map/style.json", async (HttpRequest httpRequest, IConfiguration cfg, IWebHostEnvironment env, IHttpClientFactory http, CancellationToken ct) =>
        {
            if (!BasemapAvailable(cfg, env)) return Results.NotFound(new { message = "Keine selbst gehostete Basiskarte (tools/build-basemap.sh)." });
            var cacheDir = Path.GetFullPath(Path.Combine(env.ContentRootPath, cfg["Routing:CachePath"] ?? "media/cache"));
            Directory.CreateDirectory(cacheDir);
            var cached = Path.Combine(cacheDir, "style-liberty.json");
            if (!File.Exists(cached))
            {
                var upstream = await http.CreateClient("routing").GetStringAsync(cfg["Map:StyleUrl"] ?? DefaultStyleUrl, ct);
                await File.WriteAllTextAsync(cached, upstream, ct);
            }
            // glyphs and sprites go through our cache proxy, so after a first visit the map works without OpenFreeMap
            // a relative base-tiles URL ("/basemap", proxied by the dev server to Martin) is made absolute with the page origin, so the
            // map also works when the dev server is opened from a phone on the LAN
            var baseTiles = cfg["Map:BaseTilesUrl"]!.StartsWith('/') ? AssetsBase(httpRequest, cfg) + cfg["Map:BaseTilesUrl"] : cfg["Map:BaseTilesUrl"]!;
            var style = Rewrite(await File.ReadAllTextAsync(cached, ct), baseTiles, cfg.GetValue("Map:KeepRemoteRaster", true), AssetsBase(httpRequest, cfg) + "/api/map", DetailAvailable(cfg, env) ? baseTiles + "-detail" : null);
            return Results.Content(style, "application/json");
        }).WithTags("Map");

        // Cache proxy for the style's static assets (fonts: {fontstack}/{range}.pbf, sprites: ofm.json / ofm.png / ofm@2x.*).
        api.MapGet("/map/fonts/{fontstack}/{range}.pbf", (string fontstack, string range, IConfiguration cfg, IWebHostEnvironment env, IHttpClientFactory http, CancellationToken ct) =>
            Regex.IsMatch(fontstack, @"^[A-Za-z0-9 ,\-]{1,80}$") && Regex.IsMatch(range, @"^\d{1,5}-\d{1,5}$")
                ? ProxyAsync($"fonts/{Uri.EscapeDataString(fontstack)}/{range}.pbf", $"fonts/{fontstack.Replace(' ', '_')}/{range}.pbf", "application/x-protobuf", cfg, env, http, ct)
                : Task.FromResult(Results.NotFound())).WithTags("Map");
        api.MapGet("/map/sprites/{file}", (string file, IConfiguration cfg, IWebHostEnvironment env, IHttpClientFactory http, CancellationToken ct) =>
            Regex.IsMatch(file, @"^ofm(@2x)?\.(json|png)$")
                ? ProxyAsync($"sprites/ofm_f384/{file}", $"sprites/{file}", file.EndsWith(".json") ? "application/json" : "image/png", cfg, env, http, ct)
                : Task.FromResult(Results.NotFound())).WithTags("Map");
    }

    /// <summary>Public origin the browser reaches us on: Origin header, Web:BaseUrl, the first allowed origin (dev: the Vite server), else the request host.</summary>
    private static string AssetsBase(HttpRequest req, IConfiguration cfg) =>
        req.Headers.Origin.FirstOrDefault()
        ?? (req.Headers["X-Forwarded-Host"].FirstOrDefault() is { Length: > 0 } fh ? $"{req.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? "http"}://{fh}" : null) // dev proxy (xfwd)
        ?? cfg["Web:BaseUrl"]?.TrimEnd('/') ?? cfg.GetSection("Web:AllowedOrigins").Get<string[]>()?.FirstOrDefault()?.TrimEnd('/') ?? $"{req.Scheme}://{req.Host}";

    /// <summary>Serves an upstream asset from media/cache/map, fetching it once; a failed fetch is not cached.</summary>
    private static async Task<IResult> ProxyAsync(string upstreamPath, string cachePath, string contentType, IConfiguration cfg, IWebHostEnvironment env, IHttpClientFactory http, CancellationToken ct)
    {
        var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, cfg["Routing:CachePath"] ?? "media/cache", "map"));
        var local = Path.GetFullPath(Path.Combine(root, cachePath));
        if (!local.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
        if (!File.Exists(local))
        {
            var upstream = (cfg["Map:AssetsUpstream"] ?? "https://tiles.openfreemap.org").TrimEnd('/') + "/" + upstreamPath;
            using var resp = await http.CreateClient("routing").GetAsync(upstream, ct);
            if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            await File.WriteAllBytesAsync(local, await resp.Content.ReadAsByteArrayAsync(ct), ct);
        }
        return Results.File(local, contentType, enableRangeProcessing: false);
    }

    /// <summary>What the lean tiles carry at z13+ (overzoomed z12, tools/build-basemap.sh LEAN_FILTER – keep in sync): source-layer → classes
    /// without ramps (null = every feature). Detail twins draw only the rest.</summary>
    private static readonly Dictionary<string, string[]?> LeanClasses = new()
    {
        ["water"] = null, ["waterway"] = null,
        ["transportation"] = ["motorway", "trunk", "primary", "secondary", "ferry", "rail"],
        ["transportation_name"] = ["motorway", "trunk", "primary", "secondary"],
        ["place"] = ["continent", "country", "state", "province", "city", "town", "island", "village", "suburb"],
    };

    /// <summary>Source-layers the detail build leaves out (tools/build-basemap.sh --exclude_layers): MapLibre logs an error for every style layer
    /// that names a source-layer missing from the source's TileJSON, so their layers are dropped.</summary>
    private static readonly HashSet<string> DetailExcluded = ["landcover", "housenumber", "mountain_peak"];

    /// <summary>Points the OpenMapTiles vector source at our tile server; optionally drops remote raster sources (low-zoom shading) and their layers.</summary>
    public static string Rewrite(string styleJson, string tilesUrl, bool keepRemoteRaster = true, string? assetsBaseUrl = null, string? detailTilesUrl = null)
    {
        var style = JsonNode.Parse(styleJson)!.AsObject();
        if (assetsBaseUrl is not null) { style["glyphs"] = assetsBaseUrl + "/fonts/{fontstack}/{range}.pbf"; style["sprite"] = assetsBaseUrl + "/sprites/ofm"; }
        var sources = style["sources"]!.AsObject();
        var removed = new List<string>();
        foreach (var (name, src) in sources.ToList())
        {
            var o = src!.AsObject();
            if (o["type"]?.GetValue<string>() == "vector") { o.Remove("tiles"); o["url"] = tilesUrl; }
            else if (!keepRemoteRaster) { sources.Remove(name); removed.Add(name); }
        }
        if (removed.Count > 0 && style["layers"] is JsonArray layers)
            foreach (var layer in layers.Where(l => removed.Contains(l?["source"]?.GetValue<string>() ?? "")).ToList()) layers.Remove(layer);
        if (detailTilesUrl is not null && style["layers"] is JsonArray all)
        {
            // Lean tiles (z ≤ 12, overzoomed, class-filtered by tools/build-basemap.sh) everywhere; the detail set (z13+, harbour areas only)
            // supplies what lean lacks. Layers on source-layers lean does not carry (landuse, landcover, park, poi, building, aeroway,
            // housenumber, …) move to the detail source from z13; transportation/transportation_name/place stay on lean at every zoom and
            // get a detail twin that draws only the classes lean filtered out, so main roads and town labels are never drawn twice
            // and stay visible at z13+ outside the harbour areas. water/waterway get a plain twin (same colour, the finer z14 coast
            // covers the overzoomed z12 one); boundary/water_name get none (lean has them complete).
            sources["openmaptiles_detail"] = new JsonObject { ["type"] = "vector", ["url"] = detailTilesUrl };
            var vectorSource = sources.First(s => s.Value!["type"]?.GetValue<string>() == "vector" && s.Key != "openmaptiles_detail").Key;
            for (var i = 0; i < all.Count; i++)
            {
                var l = all[i]!.AsObject();
                if (l["source"]?.GetValue<string>() != vectorSource) continue;
                var sl = l["source-layer"]?.GetValue<string>() ?? "";
                if (sl is "boundary" or "water_name") continue;
                var lean = LeanClasses.TryGetValue(sl, out var inLean);
                var target = lean ? JsonNode.Parse(l.ToJsonString())!.AsObject() : l;
                if (target["maxzoom"] is JsonValue mz && mz.GetValue<double>() <= 13 || !lean && DetailExcluded.Contains(sl)) { if (!lean) all.RemoveAt(i--); continue; }
                target["source"] = "openmaptiles_detail";
                target["minzoom"] = Math.Max(l["minzoom"]?.GetValue<double>() ?? 0, 13);
                if (!lean) continue; // moved, not cloned
                target["id"] = l["id"]!.GetValue<string>() + "-detail";
                if (inLean is not null)
                {
                    var notLean = new JsonArray("!", new JsonArray("all", new JsonArray("match", new JsonArray("get", "class"), new JsonArray(inLean.Select(c => (JsonNode?)c).ToArray()), true, false), new JsonArray("!=", new JsonArray("get", "ramp"), 1)));
                    target["filter"] = l["filter"] is { } f ? new JsonArray("all", f.DeepClone(), notLean) : notLean;
                }
                all.Insert(++i, target);
            }
        }
        // Liberty has no rule for piers; harbour piers ("Stege") matter here – on the main source whenever it carries z13+ (full builds)
        if (style["layers"] is JsonArray ls && ls.All(l => l?["id"]?.GetValue<string>() != "pier"))
        {
            var src = sources.First(s => s.Value!["type"]?.GetValue<string>() == "vector" && s.Key != "openmaptiles_detail").Key;
            var hit = ls.Select((l, i) => (l, i)).LastOrDefault(x => x.l?["source-layer"]?.GetValue<string>() == "transportation" && x.l?["type"]?.GetValue<string>() == "line");
            var anchor = hit.l is null ? ls.Count - 1 : hit.i; // after the roads, or at the end when the style has none
            var pierArea = JsonNode.Parse("""{"id":"pier-area","type":"fill","source-layer":"landuse","minzoom":13,"filter":["==",["get","class"],"pier"],"paint":{"fill-color":"#e0e0e0"}}""")!.AsObject();
            var pier = JsonNode.Parse("""{"id":"pier","type":"line","source-layer":"transportation","minzoom":13,"filter":["==",["get","class"],"pier"],"paint":{"line-color":"#9e9e9e","line-width":["interpolate",["linear"],["zoom"],13,1,17,5]}}""")!.AsObject();
            if (detailTilesUrl is not null) src = "openmaptiles_detail"; // lean tiles have no piers
            pierArea["source"] = src; pier["source"] = src;
            ls.Insert(anchor + 1, pierArea); ls.Insert(anchor + 2, pier);
        }
        style["name"] = (style["name"]?.GetValue<string>() ?? "Liberty") + " (self-hosted)";
        return style.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
