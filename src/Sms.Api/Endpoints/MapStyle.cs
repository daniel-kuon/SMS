using System.Text.Json;
using System.Text.Json.Nodes;
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

    public static bool BasemapAvailable(IConfiguration cfg, IWebHostEnvironment env) =>
        cfg["Map:BaseTilesUrl"] is { Length: > 0 } && cfg["Map:BasemapFile"] is { Length: > 0 } f && File.Exists(Path.GetFullPath(Path.Combine(env.ContentRootPath, f)));

    public static void MapMapStyle(this IEndpointRouteBuilder api)
    {
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
            var style = Rewrite(await File.ReadAllTextAsync(cached, ct), cfg["Map:BaseTilesUrl"]!, cfg.GetValue("Map:KeepRemoteRaster", true), AssetsBase(httpRequest, cfg) + "/api/map");
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
        req.Headers.Origin.FirstOrDefault() ?? cfg["Web:BaseUrl"]?.TrimEnd('/') ?? cfg.GetSection("Web:AllowedOrigins").Get<string[]>()?.FirstOrDefault()?.TrimEnd('/') ?? $"{req.Scheme}://{req.Host}";

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

    /// <summary>Points the OpenMapTiles vector source at our tile server; optionally drops remote raster sources (low-zoom shading) and their layers.</summary>
    public static string Rewrite(string styleJson, string tilesUrl, bool keepRemoteRaster = true, string? assetsBaseUrl = null)
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
        style["name"] = (style["name"]?.GetValue<string>() ?? "Liberty") + " (self-hosted)";
        return style.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
