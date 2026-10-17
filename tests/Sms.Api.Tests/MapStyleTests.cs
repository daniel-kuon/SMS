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

    [Fact]
    public void Rewrites_the_vector_source_and_optionally_drops_remote_raster_layers()
    {
        var kept = JsonDocument.Parse(MapStyleEndpoints.Rewrite(Liberty, "http://localhost:5161/basemap")).RootElement;
        Assert.Equal("http://localhost:5161/basemap", kept.GetProperty("sources").GetProperty("openmaptiles").GetProperty("url").GetString());
        Assert.True(kept.GetProperty("sources").TryGetProperty("ne2_shaded", out _));
        Assert.Equal(2, kept.GetProperty("layers").GetArrayLength());
        Assert.Equal("OpenFreeMap Liberty (self-hosted)", kept.GetProperty("name").GetString());
        Assert.Contains("openfreemap.org/fonts", kept.GetProperty("glyphs").GetString()); // glyphs stay remote

        var offline = JsonDocument.Parse(MapStyleEndpoints.Rewrite(Liberty, "http://localhost:5161/basemap", keepRemoteRaster: false)).RootElement;
        Assert.False(offline.GetProperty("sources").TryGetProperty("ne2_shaded", out _));
        Assert.Single(offline.GetProperty("layers").EnumerateArray());
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
}
