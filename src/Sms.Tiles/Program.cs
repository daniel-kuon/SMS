using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

// Vector tile server (Mapbox Vector Tiles) for the sea-mark overlay. Reads the "Seamarks" table that the API
// imports from OpenStreetMap/OpenSeaMap and renders tiles with PostGIS ST_AsMVT. Separate service so tile
// traffic does not compete with the API; tiles are cached in memory and carry ETags.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("sms");
builder.Services.AddMemoryCache(o => o.SizeLimit = 50_000_000); // ~50 MB of tiles
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().WithMethods("GET")));

var app = builder.Build();
app.UseCors();
var source = new ActivitySource("Sms");

app.MapGet("/", () => Results.Ok(new { service = "sms-tiles", layers = new[] { "seamark" }, url = "/tiles/seamark/{z}/{x}/{y}.mvt", minZoom = 8, maxZoom = 18 }));

app.MapGet("/tiles/seamark.json", (HttpRequest req) => Results.Ok(new
{
    tilejson = "3.0.0", name = "seamark", attribution = "© OpenStreetMap-Mitwirkende, OpenSeaMap",
    tiles = new[] { $"{req.Scheme}://{req.Host}/tiles/seamark/{{z}}/{{x}}/{{y}}.mvt" }, minzoom = 8, maxzoom = 18,
    vector_layers = new[] { new { id = "seamark", fields = new { type = "String", category = "String", name = "String", colour = "String", shape = "String", light = "String" } } },
}));

app.MapGet("/tiles/seamark/{z:int}/{x:int}/{y:int}.mvt", async (int z, int x, int y, NpgsqlDataSource db, IMemoryCache cache, HttpContext ctx, CancellationToken ct) =>
{
    if (z < 0 || z > 22 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z) return Results.NotFound();
    if (z < 8) return Results.Bytes([], "application/vnd.mapbox-vector-tile"); // sea marks are noise below ~1:500k
    using var activity = source.StartActivity("tiles.seamark");
    activity?.SetTag("tile.z", z); activity?.SetTag("tile.x", x); activity?.SetTag("tile.y", y);
    var key = $"seamark/{z}/{x}/{y}";
    var tile = await cache.GetOrCreateAsync(key, async entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(30);
        await using var cmd = db.CreateCommand("""
            WITH b AS (SELECT ST_TileEnvelope(@z, @x, @y) AS geom),
            q AS (
                SELECT s."OsmId" AS osm_id, s."OsmType" AS osm_type, s."Type" AS type, s."Category" AS category, s."Name" AS name, s."Colour" AS colour, s."Shape" AS shape, s."Light" AS light,
                       ST_AsMVTGeom(s."Location", b.geom, 4096, 64, true) AS geom
                FROM "Seamarks" s, b
                WHERE s."Location" && ST_Expand(b.geom, (ST_XMax(b.geom) - ST_XMin(b.geom)) / 64)
                  AND (@z >= 12 OR s."Type" IN ('light_major', 'light_minor', 'harbour', 'buoy_cardinal', 'buoy_safe_water', 'buoy_isolated_danger', 'beacon_cardinal', 'landmark'))
            )
            SELECT COALESCE(ST_AsMVT(q, 'seamark', 4096, 'geom'), ''::bytea) FROM q
            """);
        cmd.Parameters.AddWithValue("z", z); cmd.Parameters.AddWithValue("x", x); cmd.Parameters.AddWithValue("y", y);
        var result = await cmd.ExecuteScalarAsync(ct);
        var bytes = result as byte[] ?? [];
        entry.Size = bytes.Length + 64;
        return bytes;
    }) ?? [];
    activity?.SetTag("tile.bytes", tile.Length);
    var etag = $"\"{Convert.ToHexStringLower(System.IO.Hashing.XxHash64.Hash(tile))}\"";
    if (ctx.Request.Headers.IfNoneMatch.Contains(etag)) return Results.StatusCode(304);
    ctx.Response.Headers.ETag = etag;
    ctx.Response.Headers.CacheControl = "public,max-age=3600";
    return Results.Bytes(tile, "application/vnd.mapbox-vector-tile");
});

app.MapDefaultEndpoints();
app.Run();

public partial class Program;
