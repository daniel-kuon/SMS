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

app.MapGet("/", () => Results.Ok(new { service = "sms-tiles", layers = new[] { "seamark", "poi", "depth", "traffic", "dtm" }, url = "/tiles/seamark/{z}/{x}/{y}.mvt", poiUrl = "/tiles/poi/{z}/{x}/{y}.mvt", depthUrl = "/tiles/depth/{z}/{x}/{y}.mvt", trafficUrl = "/tiles/traffic/{z}/{x}/{y}.mvt", dtmUrl = "/tiles/dtm/{z}/{x}/{y}.png", minZoom = 8, maxZoom = 18 }));

// EMODnet vessel density cells (routing_traffic, filled by the admin traffic import) with sailing / pleasure craft hours per km² per month;
// known = sailing traffic the routing trusts as known-navigable water (Routing:TrafficKnownSailing, 0 = off). Below z9 only cells with ≥ 1 h.
var knownSailing = builder.Configuration.GetValue("Routing:TrafficKnownSailing", 10.0);
app.MapGet("/tiles/traffic/{z:int}/{x:int}/{y:int}.mvt", async (int z, int x, int y, NpgsqlDataSource db, IMemoryCache cache, HttpContext ctx, CancellationToken ct) =>
{
    if (z < 0 || z > 22 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z) return Results.NotFound();
    if (z < 6) return Results.Bytes([], "application/vnd.mapbox-vector-tile");
    var tile = await cache.GetOrCreateAsync($"traffic/{z}/{x}/{y}", async entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(30);
        await using var cmd = db.CreateCommand("""
            WITH b AS (SELECT ST_TileEnvelope(@z, @x, @y) AS geom, ST_Transform(ST_TileEnvelope(@z, @x, @y, margin => 1.0 / 64), 4326)::box2d AS m),
            q AS (
                SELECT round(t.sailing::numeric, 1)::float8 AS sailing, round(t.pleasure::numeric, 1)::float8 AS pleasure, @known > 0 AND t.sailing >= @known AS known,
                       ST_AsMVTGeom(ST_Transform(t.geom, 3857), b.geom, 4096, 64, true) AS geom
                FROM routing_traffic t, b WHERE t.geom && b.m AND (@z >= 9 OR t.sailing + t.pleasure >= 1)
            )
            SELECT COALESCE(ST_AsMVT(q, 'traffic', 4096, 'geom'), ''::bytea) FROM q WHERE geom IS NOT NULL
            """);
        cmd.Parameters.AddWithValue("z", z); cmd.Parameters.AddWithValue("x", x); cmd.Parameters.AddWithValue("y", y); cmd.Parameters.AddWithValue("known", (float)knownSailing);
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

// EMODnet Bathymetry DTM 2024 (routing_dtm, 1/960° cells) as a 256 px PNG from z9: depth classes in a palette, interpolated cells
// (no soundings) fainter, land and no data transparent. Chunks are decoded to one class byte per cell and kept in a cache of their own.
var dtmChunks = new MemoryCache(new MemoryCacheOptions { SizeLimit = 48 }); // ponytail: at most 48 decoded chunks (~44 MB); a z9 tile touches up to 4
var emptyPng = DtmTiles.Png(new byte[256 * 256]);
app.MapGet("/tiles/dtm/{z:int}/{x:int}/{y:int}.png", async (int z, int x, int y, NpgsqlDataSource db, IMemoryCache cache, HttpContext ctx, CancellationToken ct) =>
{
    if (z < 0 || z > 22 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z) return Results.NotFound();
    if (z < 9) return Results.Bytes(emptyPng, "image/png");
    var tile = await cache.GetOrCreateAsync($"dtm/{z}/{x}/{y}", async entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(30);
        var chunks = new Dictionary<(int, int), byte[]>();
        foreach (var key in DtmTiles.ChunksOf(z, x, y))
            chunks[key] = (await dtmChunks.GetOrCreateAsync(key, async e =>
            {
                var c = await DtmTiles.LoadAsync(db, key.lng, key.lat, ct);
                e.Size = c.Length == 0 ? 0 : 1; e.SlidingExpiration = TimeSpan.FromMinutes(30);
                return c;
            }))!;
        var bytes = chunks.Values.Any(c => c.Length > 0) ? DtmTiles.Png(DtmTiles.Render(z, x, y, chunks)) : emptyPng;
        entry.Size = bytes.Length + 64;
        return bytes;
    }) ?? emptyPng;
    var etag = $"\"{Convert.ToHexStringLower(System.IO.Hashing.XxHash64.Hash(tile))}\"";
    if (ctx.Request.Headers.IfNoneMatch.Contains(etag)) return Results.StatusCode(304);
    ctx.Response.Headers.ETag = etag;
    ctx.Response.Headers.CacheControl = "public,max-age=3600";
    return Results.Bytes(tile, "image/png");
});

// charted depth areas (routing_depth, filled by the admin depth import: Open Waters Seascape, fathom steps) as polygons with min/max depth
app.MapGet("/tiles/depth/{z:int}/{x:int}/{y:int}.mvt", async (int z, int x, int y, NpgsqlDataSource db, IMemoryCache cache, HttpContext ctx, CancellationToken ct) =>
{
    if (z < 0 || z > 22 || x < 0 || y < 0 || x >= 1 << z || y >= 1 << z) return Results.NotFound();
    if (z < 7) return Results.Bytes([], "application/vnd.mapbox-vector-tile");
    var tile = await cache.GetOrCreateAsync($"depth/{z}/{x}/{y}", async entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(30);
        await using var cmd = db.CreateCommand("""
            WITH b AS (SELECT ST_TileEnvelope(@z, @x, @y) AS geom, ST_Transform(ST_TileEnvelope(@z, @x, @y, margin => 1.0 / 64), 4326)::box2d AS m),
            -- all geometry work happens on the tile's pixel grid: clipped to the tile (+ margin) first, then simplified and turned into
            -- tile coordinates by ST_AsMVTGeom (which also makes them valid). The depth areas come per z12 source tile with ~2 % overlapping
            -- margins: each is cut back to its own z12 cell (the one with its centre), so they no longer overlap and need no ST_Union per
            -- step (that union and the 300 m buffer of whole coast polygons cost 100-700 ms per tile and kept Postgres busy on every pan)
            -- ponytail: still computed per request (cold z10 tiles 20-60 ms); a per-zoom table filled after the depth import / graph build if that matters
            src AS MATERIALIZED (
                SELECT d.min_depth_m AS min_depth, d.max_depth_m AS max_depth,
                       ST_AsMVTGeom(ST_Simplify(ST_ClipByBox2D(ST_Transform(ST_ClipByBox2D(g, b.m), 3857),
                                    ST_TileEnvelope(12, floor((ST_X(c) + 20037508.342789244) / 9783.939620502539)::int, floor((20037508.342789244 - ST_Y(c)) / 9783.939620502539)::int)),
                                    (ST_XMax(b.geom) - ST_XMin(b.geom)) / 2048), b.geom, 4096, 64, true) AS geom
                FROM routing_depth d, b, LATERAL (SELECT ST_Transform(ST_Centroid(ST_Envelope(d.geom)), 3857) AS c,
                                                         CASE WHEN ST_IsValid(d.geom) THEN d.geom ELSE ST_MakeValid(d.geom) END AS g) cc
                WHERE d.geom && b.m AND (@z >= 10 OR d.max_depth_m <= 10)
            ),
            d AS MATERIALIZED (SELECT min_depth, max_depth, geom FROM src WHERE min_depth < 18), -- deeper steps are drawn transparent
            -- water with no charted depth: the raster stops short of the coast and has no step above 6 ft, so whatever is neither land nor a
            -- depth area is shallower than 1.83 m (the Wadden flats, the strip along the shore) and comes as the shallowest step, unknown = 1.
            -- Not land here: the no-go Block corrections the graph build writes into routing_land (same bounding box as a Block correction),
            -- so no band rings the protected areas; land are the inland waters (Water corrections, no depth data) and everything outside the
            -- graph region. Only from z10, where the deeper steps are part of the tile.
            dry AS (
                SELECT ST_AsMVTGeom(ST_Transform(ST_Simplify(ST_ClipByBox2D(s.g, b.m), @tol), 3857), b.geom, 4096, 64, true) AS geom
                FROM b, LATERAL (
                    SELECT r.geom AS g FROM routing_land r
                    WHERE r.graph_version = (SELECT value::int FROM routing_meta WHERE key = 'active_version') AND r.geom && b.m
                      AND NOT EXISTS (SELECT 1 FROM "RoutingCorrections" c WHERE c."Kind" = 'Block' AND c."Geometry" ~= r.geom)
                    UNION ALL
                    SELECT c."Geometry" FROM "RoutingCorrections" c WHERE c."Kind" = 'Water' AND c."Active" AND c."Geometry" && b.m
                    UNION ALL
                    SELECT ST_Difference(ST_MakeEnvelope(-180, -85, 180, 85, 4326), ST_MakeEnvelope(a[1], a[2], a[3], a[4], 4326))
                    FROM (SELECT string_to_array(value, ',')::float8[] AS a FROM routing_meta WHERE key = 'region') r
                ) s WHERE @z >= 10
                UNION ALL SELECT geom FROM src WHERE @z >= 10
            ),
            coast AS MATERIALIZED (
                SELECT 0.0::float8 AS min_depth, 1.8288::float8 AS max_depth, 1 AS unknown,
                       ST_Difference(ST_MakeEnvelope(-64, -64, 4160, 4160, 3857), ST_UnaryUnion(ST_Collect(geom), 1)) AS geom
                FROM dry HAVING @z >= 10
            ),
            q AS (SELECT min_depth, max_depth, 0 AS unknown, geom FROM d UNION ALL SELECT * FROM coast)
            SELECT COALESCE(ST_AsMVT(q, 'depth', 4096, 'geom'), ''::bytea) FROM q WHERE geom IS NOT NULL AND NOT ST_IsEmpty(geom)
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
                WHERE (s."Line" IS NULL OR ST_IsClosed(s."Line")) AND s."Location" && ST_Expand(b.geom, (ST_XMax(b.geom) - ST_XMin(b.geom)) / 64)
                  AND (@z >= 12 OR s."Type" IN ('light_major', 'light_minor', 'harbour', 'buoy_cardinal', 'buoy_safe_water', 'buoy_isolated_danger', 'beacon_cardinal', 'landmark'))
                UNION ALL
                SELECT s."OsmId", s."OsmType", s."Type", s."Category", s."Name", s."Colour", s."Shape", s."Light", s."Sectors", NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL::float8, NULL::boolean, NULL,
                       ST_AsMVTGeom(s."Line", b.geom, 4096, 64, true) AS geom
                FROM "Seamarks" s, b
                WHERE s."Line" IS NOT NULL AND s."Line" && b.geom AND @z >= 10 AND (NOT ST_IsClosed(s."Line") OR s."Type" = ANY(@areas))
            )
            SELECT COALESCE(ST_AsMVT(q, 'seamark', 4096, 'geom'), ''::bytea) FROM q
            """);
        cmd.Parameters.AddWithValue("z", z); cmd.Parameters.AddWithValue("x", x); cmd.Parameters.AddWithValue("y", y); cmd.Parameters.AddWithValue("areas", AreaTypes);
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

public partial class Program
{
    /// <summary>Charted areas whose border is drawn (closed rings of these types also go into the line part of the seamark tile;
    /// every closed ring keeps its centre point for the symbol and name). Same list as SEAMARK_AREA_TYPES in sms-web core/map.ts.</summary>
    static readonly string[] AreaTypes = ["restricted_area", "military_area", "anchorage", "dumping_ground", "marine_farm", "cable_area", "pipeline_area",
        "fairway", "dredged_area", "separation_zone", "precautionary_area", "inshore_traffic_zone"];
}

/// <summary>The DTM raster tiles: routing_dtm chunks (row 0 = north, water cells only behind a bit mask, little-endian float32 elevation,
/// interpolation_flag bytes) decoded to a class byte per cell, sampled at each pixel centre and written as a palette PNG.</summary>
static class DtmTiles
{
    const int Cells = 960;
    /// <summary>Depth class bounds (m): below 1.8, 1.8-2.3, 2.3-3.7, 3.7-5.5, 5.5-10, deeper.</summary>
    static readonly double[] Bounds = [1.8, 2.3, 3.7, 5.5, 10];
    /// <summary>Shallow to deep; named in DTM_TITLE in sms-web core/map.ts.</summary>
    static readonly uint[] Colours = [0xc62828, 0xef6c00, 0xfbc02d, 0x7cb342, 0x29b6f6, 0x1565c0];

    /// <summary>Palette index: 0 none, 1-6 measured classes shallow to deep, 7-12 the same classes interpolated (fainter).</summary>
    public static byte Class(double depth, bool interpolated)
    {
        if (!(depth > 0)) return 0;
        var c = 1; while (c <= Bounds.Length && depth >= Bounds[c - 1]) c++;
        return (byte)(interpolated ? c + Bounds.Length + 1 : c);
    }

    /// <summary>One palette index per cell; empty when the chunk is not imported.</summary>
    public static async Task<byte[]> LoadAsync(NpgsqlDataSource db, int lng, int lat, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("SELECT water, elevation, interpolated FROM routing_dtm WHERE min_lng = @x AND min_lat = @y AND cells = @n");
        cmd.Parameters.AddWithValue("x", (double)lng); cmd.Parameters.AddWithValue("y", (double)lat); cmd.Parameters.AddWithValue("n", Cells);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return [];
        byte[] water = r.GetFieldValue<byte[]>(0), elev = r.GetFieldValue<byte[]>(1), interp = r.GetFieldValue<byte[]>(2);
        var cls = new byte[Cells * Cells];
        for (int i = 0, k = 0; i < cls.Length; i++)
        {
            if ((water[i >> 3] & (1 << (i & 7))) == 0) continue;
            cls[i] = Class(-System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(elev.AsSpan(k * 4)), interp[k] != 0); k++;
        }
        return cls;
    }

    static double Lat(int z, double py) => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * py / (256.0 * (1 << z))))) * 180 / Math.PI;
    static double Lng(int z, double px) => px / (256.0 * (1 << z)) * 360 - 180;

    /// <summary>The 1 degree chunks (south-west corners) a tile touches.</summary>
    public static IEnumerable<(int lng, int lat)> ChunksOf(int z, int x, int y)
    {
        int w = (int)Math.Floor(Lng(z, x * 256 + 0.5)), e = (int)Math.Floor(Lng(z, x * 256 + 255.5)), n = (int)Math.Floor(Lat(z, y * 256 + 0.5)), s = (int)Math.Floor(Lat(z, y * 256 + 255.5));
        for (var lat = s; lat <= n; lat++) for (var lng = w; lng <= e; lng++) yield return (lng, lat);
    }

    /// <summary>256 x 256 palette indices, each pixel the cell under its centre.</summary>
    public static byte[] Render(int z, int x, int y, Dictionary<(int, int), byte[]> chunks)
    {
        var px = new byte[256 * 256];
        var cols = new long[256];
        for (var c = 0; c < 256; c++) cols[c] = (long)Math.Floor(Lng(z, x * 256 + c + 0.5) * Cells);
        for (var r = 0; r < 256; r++)
        {
            var gy = (long)Math.Floor(Lat(z, y * 256 + r + 0.5) * Cells);
            var ky = (int)Math.Floor((double)gy / Cells); var row = (Cells - 1 - (gy - (long)ky * Cells)) * Cells;
            for (var c = 0; c < 256; c++)
            {
                var kx = (int)Math.Floor((double)cols[c] / Cells);
                if (chunks.TryGetValue((kx, ky), out var chunk) && chunk.Length > 0) px[r * 256 + c] = chunk[row + cols[c] - (long)kx * Cells];
            }
        }
        return px;
    }

    /// <summary>An 8-bit palette PNG (256 x 256) with per-entry alpha (tRNS): measured 200, interpolated 90, index 0 transparent.</summary>
    public static byte[] Png(byte[] px)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]);
        void Chunk(string type, byte[] data)
        {
            var head = new byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(head, data.Length); System.Text.Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
            ms.Write(head); ms.Write(data);
            var crc = new System.IO.Hashing.Crc32(); crc.Append(head.AsSpan(4)); crc.Append(data);
            var sum = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(sum, crc.GetCurrentHashAsUInt32()); ms.Write(sum);
        }
        Chunk("IHDR", [0, 0, 1, 0, 0, 0, 1, 0, 8, 3, 0, 0, 0]);
        var palette = new List<byte> { 0, 0, 0 }; var alpha = new List<byte> { 0 };
        foreach (var a in new byte[] { 200, 90 }) foreach (var c in Colours) { palette.AddRange([(byte)(c >> 16), (byte)(c >> 8), (byte)c]); alpha.Add(a); }
        Chunk("PLTE", [.. palette]); Chunk("tRNS", [.. alpha]);
        using (var raw = new MemoryStream())
        {
            using (var zs = new System.IO.Compression.ZLibStream(raw, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                for (var r = 0; r < 256; r++) { zs.WriteByte(0); zs.Write(px, r * 256, 256); }
            Chunk("IDAT", raw.ToArray());
        }
        Chunk("IEND", []);
        return ms.ToArray();
    }
}
