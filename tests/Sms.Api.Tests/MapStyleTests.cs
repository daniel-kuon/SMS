using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sms.Api.Endpoints;

namespace Sms.Api.Tests;

public class MapStyleTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private const string Liberty = """
        { "version": 8, "name": "OpenFreeMap Liberty", "glyphs": "https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf",
          "sources": { "ne2_shaded": { "type": "raster", "tiles": ["https://tiles.openfreemap.org/natural_earth/{z}/{x}/{y}.png"] }, "openmaptiles": { "type": "vector", "url": "https://tiles.openfreemap.org/planet" } },
          "layers": [ { "id": "shade", "type": "raster", "source": "ne2_shaded" }, { "id": "water", "type": "fill", "source": "openmaptiles", "source-layer": "water" } ] }
        """;

    private const string LeanAndDetail = """
        { "version": 8, "sources": { "ne2_shaded": { "type": "raster", "tiles": ["x"] }, "openmaptiles": { "type": "vector", "url": "x" } },
          "layers": [ { "id": "shade", "type": "raster", "source": "ne2_shaded" }, { "id": "water", "type": "fill", "source": "openmaptiles", "source-layer": "water" },
            { "id": "landuse", "type": "fill", "source": "openmaptiles", "source-layer": "landuse" },
            { "id": "landcover_wood", "type": "fill", "source": "openmaptiles", "source-layer": "landcover" },
            { "id": "building", "type": "fill", "source": "openmaptiles", "source-layer": "building", "minzoom": 12, "maxzoom": 13 },
            { "id": "road", "type": "line", "source": "openmaptiles", "source-layer": "transportation", "filter": ["==", ["get", "brunnel"], "bridge"] },
            { "id": "boundary", "type": "line", "source": "openmaptiles", "source-layer": "boundary" },
            { "id": "label_city", "type": "symbol", "source": "openmaptiles", "source-layer": "place", "minzoom": 14 },
            { "id": "label_state", "type": "symbol", "source": "openmaptiles", "source-layer": "place", "maxzoom": 8 } ] }
        """;

    [Fact]
    public void Rewrites_the_vector_source_and_optionally_drops_remote_raster_layers()
    {
        var kept = JsonDocument.Parse(MapStyleEndpoints.Rewrite(Liberty, "http://localhost:5161/basemap")).RootElement;
        Assert.Equal("http://localhost:5161/basemap", kept.GetProperty("sources").GetProperty("openmaptiles").GetProperty("url").GetString());
        Assert.True(kept.GetProperty("sources").TryGetProperty("ne2_shaded", out _));
        Assert.Equal(4, kept.GetProperty("layers").GetArrayLength()); // + pier-area, pier
        Assert.Equal("OpenFreeMap Liberty (self-hosted)", kept.GetProperty("name").GetString());
        Assert.Contains("openfreemap.org/fonts", kept.GetProperty("glyphs").GetString()); // glyphs stay remote without an assets base
        var proxied = JsonDocument.Parse(MapStyleEndpoints.Rewrite(Liberty, "http://localhost:5161/basemap", true, "http://localhost:5173/api/map")).RootElement;
        Assert.Equal("http://localhost:5173/api/map/fonts/{fontstack}/{range}.pbf", proxied.GetProperty("glyphs").GetString());
        Assert.Equal("http://localhost:5173/api/map/sprites/ofm", proxied.GetProperty("sprite").GetString());

        // a harbour-detail tile set: lean layers keep their source and get a z13+ detail twin (roads/places only for the classes lean lacks),
        // layers lean does not carry move to the detail source, boundaries are not doubled, the pier rules read the detail source
        var detail = JsonDocument.Parse(MapStyleEndpoints.Rewrite(LeanAndDetail, "http://localhost:5161/basemap", true, null, "http://localhost:5161/basemap-detail")).RootElement;
        Assert.Equal("http://localhost:5161/basemap-detail", detail.GetProperty("sources").GetProperty("openmaptiles_detail").GetProperty("url").GetString());
        var layers = detail.GetProperty("layers").EnumerateArray().ToDictionary(l => l.GetProperty("id").GetString()!);
        Assert.Equal(["shade", "water", "water-detail", "landuse", "road", "road-detail", "pier-area", "pier", "boundary", "label_city", "label_city-detail", "label_state"], layers.Keys); // building (maxzoom 13) and landcover (not in the detail build) are dropped
        Assert.Equal("openmaptiles", layers["road"].GetProperty("source").GetString()); Assert.False(layers["road"].TryGetProperty("minzoom", out _));
        Assert.Equal("openmaptiles_detail", layers["road-detail"].GetProperty("source").GetString()); Assert.Equal(13, layers["road-detail"].GetProperty("minzoom").GetDouble());
        var roadFilter = layers["road-detail"].GetProperty("filter").GetRawText();
        Assert.StartsWith("[\"all\",[\"==\",[\"get\",\"brunnel\"],\"bridge\"],[\"!\"", roadFilter); Assert.Contains("motorway", roadFilter);
        Assert.Equal("openmaptiles_detail", layers["landuse"].GetProperty("source").GetString()); Assert.Equal(13, layers["landuse"].GetProperty("minzoom").GetDouble());
        Assert.Equal(14, layers["label_city-detail"].GetProperty("minzoom").GetDouble());
        Assert.False(layers["water-detail"].TryGetProperty("filter", out _));
        Assert.Equal("openmaptiles_detail", layers["pier"].GetProperty("source").GetString());

        var offline = JsonDocument.Parse(MapStyleEndpoints.Rewrite(Liberty, "http://localhost:5161/basemap", keepRemoteRaster: false)).RootElement;
        Assert.False(offline.GetProperty("sources").TryGetProperty("ne2_shaded", out _));
        Assert.Equal(3, offline.GetProperty("layers").GetArrayLength());
        Assert.Equal("water", offline.GetProperty("layers")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Without_a_basemap_file_the_config_points_at_openfreemap_and_the_style_is_404()
    {
        var cfg = await f.Anonymous().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.Equal(MapStyleEndpoints.DefaultStyleUrl, cfg.GetProperty("mapStyleUrl").GetString());
        Assert.False(cfg.GetProperty("selfHostedBasemap").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await f.Anonymous().GetAsync("/api/map/style.json")).StatusCode);
    }

    [Fact]
    public async Task Asset_proxy_caches_fonts_and_sprites_and_rejects_odd_names()
    {
        var anon = f.Anonymous();
        var glyph = await anon.GetAsync("/api/map/fonts/Noto%20Sans%20Regular/0-255.pbf");
        glyph.EnsureSuccessStatusCode();
        Assert.Equal("application/x-protobuf", glyph.Content.Headers.ContentType?.MediaType);
        Assert.True((await glyph.Content.ReadAsByteArrayAsync()).Length > 1000);
        var again = await anon.GetAsync("/api/map/fonts/Noto%20Sans%20Regular/0-255.pbf"); // served from the cache now
        again.EnsureSuccessStatusCode();
        var sprite = await anon.GetAsync("/api/map/sprites/ofm.json");
        sprite.EnsureSuccessStatusCode();
        Assert.Equal("application/json", sprite.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/map/fonts/..%2F..%2Fx/0-255.pbf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/map/sprites/..%2Fstyle.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/map/sprites/evil.png")).StatusCode);
    }
}
