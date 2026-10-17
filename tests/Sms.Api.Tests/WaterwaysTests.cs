using System.Text.Json;
using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class WaterwaysTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static Dictionary<string, string> Tags(params (string k, string v)[] t) => t.ToDictionary(x => x.k, x => x.v);

    [Fact]
    public void Navigability_follows_the_osm_tags()
    {
        Assert.True(Waterways.IsNavigable(Tags(("waterway", "canal"), ("CEMT", "Vb"))));
        Assert.True(Waterways.IsNavigable(Tags(("waterway", "river"), ("boat", "yes"))));
        Assert.True(Waterways.IsNavigable(Tags(("waterway", "fairway"))));
        Assert.False(Waterways.IsNavigable(Tags(("waterway", "river"))));            // no permission known
        Assert.False(Waterways.IsNavigable(Tags(("waterway", "canal"), ("CEMT", "I"), ("motorboat", "no"))));
        Assert.False(Waterways.IsNavigable(Tags(("waterway", "stream"), ("boat", "yes"))));
    }

    [Fact]
    public void Parses_overpass_json_and_merges_connected_ways()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "waterways.json")));
        var ways = Waterways.Parse(doc, F);
        // canal (2 ways) + Eider (joined at the shared end point) merge into one chain; the others are dropped
        var chain = Assert.Single(ways);
        Assert.Equal(3, chain.Ways);
        Assert.Equal("Nord-Ostsee-Kanal", chain.Name);
        Assert.Equal(6, chain.Line.NumPoints);
        Assert.Equal(4326, chain.Line.SRID);
    }

    [Fact]
    public void Junctions_stay_separate_lines_sharing_a_coordinate()
    {
        var a = F.CreateLineString([new Coordinate(10, 54), new Coordinate(10.1, 54)]);
        var b = F.CreateLineString([new Coordinate(10.1, 54), new Coordinate(10.2, 54)]);
        var c = F.CreateLineString([new Coordinate(10.1, 54), new Coordinate(10.1, 54.1)]); // branch at the junction
        var merged = Waterways.Merge([(a, "Main"), (b, "Main"), (c, "Branch")], F);
        Assert.Equal(3, merged.Count); // LineMerger does not merge through a degree-3 node
        Assert.Contains(merged, m => m.Name == "Branch");
        Assert.All(merged, m => Assert.Contains(m.Line.Coordinates, co => co.Equals2D(new Coordinate(10.1, 54))));
    }
}
