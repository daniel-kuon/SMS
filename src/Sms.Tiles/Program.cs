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

app.MapGet("/", () => Results.Ok(new { service = "sms-tiles", layers = new[] { "seamark", "poi", "depth" }, url = "/tiles/seamark/{z}/{x}/{y}.mvt", poiUrl = "/tiles/poi/{z}/{x}/{y}.mvt", depthUrl = "/tiles/depth/{z}/{x}/{y}.mvt", minZoom = 8, maxZoom = 18 }));

// charted depth areas (routing_depth, filled by the admin depth import: Open Waters Seascape, fathom steps) as polygons with min/max depth
app.MapGet("/tiles/depth/{z:int}/{x:int}/{y:int}.mvt", async (int z, int x, int y, NpgsqlDataSource db, IMemoryCache cache, HttpContext ctx, CancellationToken ct) =>
{
    if (z < 0 || z > 22 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z) return Results.NotFound();
    if (z < 7) return Results.Bytes([], "application/vnd.mapbox-vector-tile");
    var tile = await cache.GetOrCreateAsync($"depth/{z}/{x}/{y}", async entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(30);
        await using var cmd = db.CreateCommand("""
            WITH b AS (SELECT ST_TileEnvelope(@z, @x, @y) AS geom, ST_Transform(ST_TileEnvelope(@z, @x, @y), 4326) AS geom4326),
            -- the depth areas come per z12 source tile with overlapping margins: unioned per depth step so no seams or double fills show;
            -- made valid and unioned on a grid (simplified rings self-intersected: GEOS TopologyException, tile 500)
            d AS (
                SELECT d.min_depth_m AS min_depth, d.max_depth_m AS max_depth,
                       ST_Union(ST_MakeValid(ST_SimplifyPreserveTopology(d.geom, @tol)), @tol / 8) AS geom
                FROM routing_depth d, b WHERE d.geom && ST_Expand(b.geom4326, (ST_XMax(b.geom4326) - ST_XMin(b.geom4326)) / 32)
                  AND (@z >= 10 OR d.max_depth_m <= 10)
                GROUP BY d.min_depth_m, d.max_depth_m
            ),
            -- the depth raster stops short of the coast (and has no band above 6 ft): the water within ~300 m of the active land that no depth area
            -- covers is added as the shallowest step (0 - 1.83 m, unknown = 1), so the shallow colour reaches the land; only from z10 (sub-pixel below)
            land AS (
                SELECT ST_Union(ST_MakeValid(ST_SimplifyPreserveTopology(r.geom, @tol))) AS geom
                FROM routing_land r, b
                WHERE @z >= 10 AND r.graph_version = (SELECT value::int FROM routing_meta WHERE key = 'active_version')
                  AND r.geom && ST_Expand(b.geom4326, GREATEST((ST_XMax(b.geom4326) - ST_XMin(b.geom4326)) / 32, 0.006))
            ),
            coast AS (
                SELECT 0.0::float8 AS min_depth, 1.8288::float8 AS max_depth, 1 AS unknown,
                       ST_Difference(ST_Buffer(ST_Transform(l.geom, 3857), 300 / cos(radians(ST_Y(ST_Centroid(b.geom4326))))),
                                     ST_Union(ST_Transform(l.geom, 3857), COALESCE((SELECT ST_Transform(ST_Union(d.geom), 3857) FROM d), 'GEOMETRYCOLLECTION EMPTY'::geometry))) AS geom
                FROM land l, b WHERE l.geom IS NOT NULL
            ),
            q AS (
                SELECT min_depth, max_depth, 0 AS unknown, ST_AsMVTGeom(ST_Transform(d.geom, 3857), b.geom, 4096, 32, true) AS geom FROM d, b
                UNION ALL
                SELECT min_depth, max_depth, unknown, ST_AsMVTGeom(coast.geom, b.geom, 4096, 32, true) FROM coast, b
            )
            SELECT COALESCE(ST_AsMVT(q, 'depth', 4096, 'geom'), ''::bytea) FROM q WHERE geom IS NOT NULL
            """);
        cmd.Parameters.AddWithValue("z", z); cmd.Parameters.AddWithValue("x", x); cmd.Parameters.AddWithValue("y", y);
        cmd.Parameters.AddWithValue("tol", 360.0 / (1 << z) / 2048); // about two tile pixels in degrees
        var bytes = await cmd.ExecuteScalarAsync(ct) as byte[] ?? [];
        entry.Size = bytes.Length + 64;
        return bytes;
    }) ?? [];
    var etag = $"\"{Convert.ToHexStringLower(System.IO.Hashing.XxHash64.Hash(tile))}\"";
    if (ctx.Request.Headers.IfNoneMatch.Contains(etag)) return Results.StatusCode(304);
    ctx.Response.Headers.ETag = etag;
    ctx.Response.Headers.CacheControl = "public,max-age=3600";
    return Results.Bytes(tile, "application/vnd.mapbox-vector-tile");
});

// POIs near harbours (shops, restaurants, toilets …) from z13; the click goes to the API for opening hours and links
app.MapGet("/tiles/poi/{z:int}/{x:int}/{y:int}.mvt", async (int z, int x, int y, NpgsqlDataSource db, IMemoryCache cache, HttpContext ctx, CancellationToken ct) =>
{
    if (z < 0 || z > 22 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z) return Results.NotFound();
    if (z < 13) return Results.Bytes([], "application/vnd.mapbox-vector-tile");
    var tile = await cache.GetOrCreateAsync($"poi/{z}/{x}/{y}", async entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(30);
        await using var cmd = db.CreateCommand("""
            WITH b AS (SELECT ST_TileEnvelope(@z, @x, @y) AS geom),
            q AS (
                SELECT p."OsmId" AS osm_id, p."OsmType" AS osm_type, p."Class" AS class, p."Kind" AS kind, p."Name" AS name,
                       ST_AsMVTGeom(p."Location", b.geom, 4096, 64, true) AS geom
                FROM "Pois" p, b WHERE p."Location" && ST_Expand(b.geom, (ST_XMax(b.geom) - ST_XMin(b.geom)) / 64)
                  AND (@z >= 15 OR p."Kind" IN ('toilets', 'fuel', 'supermarket', 'restaurant', 'hotel', 'marina', 'chandler', 'boat_repair', 'drinking_water', 'shower', 'pharmacy', 'hospital', 'atm', 'bank', 'police', 'station', 'ferry_terminal'))
            )
            SELECT COALESCE(ST_AsMVT(q, 'poi', 4096, 'geom'), ''::bytea) FROM q
            """);
        cmd.Parameters.AddWithValue("z", z); cmd.Parameters.AddWithValue("x", x); cmd.Parameters.AddWithValue("y", y);
        var bytes = await cmd.ExecuteScalarAsync(ct) as byte[] ?? [];
        entry.Size = bytes.Length + 64;
        return bytes;
    }) ?? [];
    var etag = $"\"{Convert.ToHexStringLower(System.IO.Hashing.XxHash64.Hash(tile))}\"";
    if (ctx.Request.Headers.IfNoneMatch.Contains(etag)) return Results.StatusCode(304);
    ctx.Response.Headers.ETag = etag;
    ctx.Response.Headers.CacheControl = "public,max-age=3600";
    return Results.Bytes(tile, "application/vnd.mapbox-vector-tile");
});

app.MapGet("/tiles/seamark.json", (HttpRequest req) => Results.Ok(new
{
    tilejson = "3.0.0", name = "seamark", attribution = "© OpenStreetMap-Mitwirkende, OpenSeaMap",
    tiles = new[] { $"{req.Scheme}://{req.Host}/tiles/seamark/{{z}}/{{x}}/{{y}}.mvt" }, minzoom = 8, maxzoom = 18,
    vector_layers = new[] { new { id = "seamark", fields = new { type = "String", category = "String", name = "String", colour = "String", shape = "String", light = "String", sectors = "String", osm_id = "Number", osm_type = "String", colour_pattern = "String", colour_key = "String", topmark_colour_key = "String", topmark_shape = "String", topmark_colour = "String", light_colour = "String", water_level = "String", depth = "Number", conspicuous = "Boolean", function = "String" } } },
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
                SELECT s."OsmId" AS osm_id, s."OsmType" AS osm_type, s."Type" AS type, s."Category" AS category, s."Name" AS name, s."Colour" AS colour, s."Shape" AS shape, s."Light" AS light, s."Sectors" AS sectors,
                       s."TagsJson"->>('seamark:' || s."Type" || ':colour_pattern') AS colour_pattern,
                       replace(replace(COALESCE(s."Colour", 'generic'), ';', '_'), ',', '_') AS colour_key,
                       replace(COALESCE(s."TagsJson"->>'seamark:topmark:colour', 'generic'), ';', '_') AS topmark_colour_key,
                       replace(replace(replace(replace(s."TagsJson"->>'seamark:topmark:shape', ', ', '_'), ' ', '_'), 'x-shape', 'cross'), '-', '_') AS topmark_shape,
                       s."TagsJson"->>'seamark:topmark:colour' AS topmark_colour,
                       split_part(COALESCE(s."TagsJson"->>'seamark:light:colour', s."TagsJson"->>'seamark:light:1:colour'), ';', 1) AS light_colour,
                       COALESCE(s."TagsJson"->>('seamark:' || s."Type" || ':water_level'), s."TagsJson"->>'seamark:water_level') AS water_level,
                       NULLIF(regexp_replace(COALESCE(s."TagsJson"->>'depth', s."TagsJson"->>('seamark:' || s."Type" || ':depth')), '[^0-9.\-]', '', 'g'), '')::float8 AS depth,
                       (s."TagsJson"->>'seamark:landmark:conspicuity') = 'conspicuous' AS conspicuous,
                       split_part(COALESCE(s."TagsJson"->>('seamark:' || s."Type" || ':function'), ''), ';', 1) AS function,
                       ST_AsMVTGeom(s."Location", b.geom, 4096, 64, true) AS geom
                FROM "Seamarks" s, b
                WHERE s."Line" IS NULL AND s."Location" && ST_Expand(b.geom, (ST_XMax(b.geom) - ST_XMin(b.geom)) / 64)
                  AND (@z >= 12 OR s."Type" IN ('light_major', 'light_minor', 'harbour', 'buoy_cardinal', 'buoy_safe_water', 'buoy_isolated_danger', 'beacon_cardinal', 'landmark'))
                UNION ALL
                SELECT s."OsmId", s."OsmType", s."Type", s."Category", s."Name", s."Colour", s."Shape", s."Light", s."Sectors", NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL::float8, NULL::boolean, NULL,
                       ST_AsMVTGeom(s."Line", b.geom, 4096, 64, true) AS geom
                FROM "Seamarks" s, b
                WHERE s."Line" IS NOT NULL AND s."Line" && b.geom AND @z >= 10
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
