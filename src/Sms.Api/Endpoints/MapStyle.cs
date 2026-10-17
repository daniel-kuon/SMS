using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sms.Api.Endpoints;

/// <summary>
/// Self-hosted base map (spec 07 G / docs 13): when a Planetiler PMTiles file exists (Map:BasemapFile, built by
/// tools/build-basemap.sh) and Martin serves it (Map:BaseTilesUrl, wired by the AppHost), the OpenFreeMap "Liberty"
/// style is served from here with its vector source pointed at Martin. Glyphs and sprites stay on OpenFreeMap
/// (static files; mirror them if you need a fully offline setup).
/// </summary>
public static class MapStyleEndpoints
{
    public const string DefaultStyleUrl = "https://tiles.openfreemap.org/styles/liberty";

    public static bool BasemapAvailable(IConfiguration cfg, IWebHostEnvironment env) =>
        cfg["Map:BaseTilesUrl"] is { Length: > 0 } && cfg["Map:BasemapFile"] is { Length: > 0 } f && File.Exists(Path.GetFullPath(Path.Combine(env.ContentRootPath, f)));

    public static void MapMapStyle(this IEndpointRouteBuilder api)
    {
        api.MapGet("/map/style.json", async (IConfiguration cfg, IWebHostEnvironment env, IHttpClientFactory http, CancellationToken ct) =>
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
            var style = Rewrite(await File.ReadAllTextAsync(cached, ct), cfg["Map:BaseTilesUrl"]!, cfg.GetValue("Map:KeepRemoteRaster", true));
            return Results.Content(style, "application/json");
        }).WithTags("Map");
    }

    /// <summary>Points the OpenMapTiles vector source at our tile server; optionally drops remote raster sources (low-zoom shading) and their layers.</summary>
    public static string Rewrite(string styleJson, string tilesUrl, bool keepRemoteRaster = true)
    {
        var style = JsonNode.Parse(styleJson)!.AsObject();
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
