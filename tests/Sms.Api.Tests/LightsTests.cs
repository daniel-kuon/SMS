using System.Text.Json;
using Sms.Api;
using Sms.Api.Endpoints;

namespace Sms.Api.Tests;

public class LightsTests
{
    [Fact]
    public void Charts_a_sectored_light_the_way_opensea_map_does()
    {
        var tags = new Dictionary<string, string>
        {
            ["seamark:type"] = "light_major", ["seamark:name"] = "Leuchtturm Heiligenhafen",
            ["seamark:light:1:character"] = "Oc", ["seamark:light:1:group"] = "2", ["seamark:light:1:colour"] = "white", ["seamark:light:1:period"] = "9", ["seamark:light:1:height"] = "16", ["seamark:light:1:range"] = "13", ["seamark:light:1:sector_start"] = "220", ["seamark:light:1:sector_end"] = "240",
            ["seamark:light:2:character"] = "Oc", ["seamark:light:2:group"] = "2", ["seamark:light:2:colour"] = "red", ["seamark:light:2:period"] = "9", ["seamark:light:2:height"] = "16", ["seamark:light:2:range"] = "9", ["seamark:light:2:sector_start"] = "240", ["seamark:light:2:sector_end"] = "260",
            ["seamark:light:3:character"] = "Oc", ["seamark:light:3:group"] = "2", ["seamark:light:3:colour"] = "green", ["seamark:light:3:period"] = "9", ["seamark:light:3:height"] = "16", ["seamark:light:3:range"] = "9", ["seamark:light:3:sector_start"] = "200", ["seamark:light:3:sector_end"] = "220",
        };
        var (label, sectors) = Lights.Describe(tags);
        Assert.Equal("Oc(2)WRG.9s16m13-9M", label); // ranges 13 and 9
        Assert.Equal("W:220:240:13;R:240:260:9;G:200:220:9", sectors);
    }

    [Fact]
    public void Charts_simple_lights_and_ignores_marks_without_one()
    {
        Assert.Equal("Iso.3s18m17M", Lights.Describe(new Dictionary<string, string> { ["seamark:light:character"] = "Iso", ["seamark:light:colour"] = "white", ["seamark:light:period"] = "3", ["seamark:light:height"] = "18", ["seamark:light:range"] = "17" }).label);
        Assert.Equal("Fl.G.4s", Lights.Describe(new Dictionary<string, string> { ["seamark:light:character"] = "Fl", ["seamark:light:colour"] = "green", ["seamark:light:period"] = "4" }).label);
        Assert.Equal("Q(3).10s", Lights.Describe(new Dictionary<string, string> { ["seamark:light:character"] = "Q", ["seamark:light:group"] = "3", ["seamark:light:colour"] = "white", ["seamark:light:period"] = "10 s" }).label);
        var (none, noSectors) = Lights.Describe(new Dictionary<string, string> { ["seamark:type"] = "buoy_lateral" });
        Assert.Null(none); Assert.Null(noSectors);
        Assert.Null(Lights.Describe(new Dictionary<string, string> { ["seamark:light:character"] = "Fl", ["seamark:light:colour"] = "red" }).sectors); // no angles, no range: nothing to draw
    }

    [Fact]
    public void Way_geometry_becomes_a_line_and_closed_rings_and_relations_their_border()
    {
        using var open = JsonDocument.Parse("""[{"lat":54.1,"lon":10.1},{"lat":54.2,"lon":10.2}]""");
        var line = SeamarkEndpoints.ToMercatorLine(open.RootElement);
        Assert.NotNull(line); Assert.Equal(3857, line.SRID); Assert.Equal(2, line.NumPoints);
        using var ring = JsonDocument.Parse("""[{"lat":54.1,"lon":10.1},{"lat":54.2,"lon":10.2},{"lat":54.1,"lon":10.3},{"lat":54.1,"lon":10.1}]""");
        Assert.True(SeamarkEndpoints.ToMercatorLine(ring.RootElement)!.IsClosed);
        Assert.Null(SeamarkEndpoints.ToMercatorLine(JsonDocument.Parse("""[{"lat":54.1,"lon":10.1}]""").RootElement));
        // a multipolygon whose outer ring is split over two ways: joined into one closed border, the inner way ignored
        using var members = JsonDocument.Parse("""
            [{"type":"way","role":"outer","geometry":[{"lat":54.1,"lon":10.1},{"lat":54.2,"lon":10.2},{"lat":54.1,"lon":10.3}]},
             {"type":"way","role":"outer","geometry":[{"lat":54.1,"lon":10.3},{"lat":54.0,"lon":10.2},{"lat":54.1,"lon":10.1}]},
             {"type":"way","role":"inner","geometry":[{"lat":54.1,"lon":10.15},{"lat":54.12,"lon":10.2},{"lat":54.1,"lon":10.15}]}]
            """);
        var border = SeamarkEndpoints.OuterLine(members.RootElement);
        Assert.NotNull(border); Assert.True(border.IsClosed); Assert.Equal(5, border.NumPoints); Assert.Equal(3857, border.SRID);
    }
}
