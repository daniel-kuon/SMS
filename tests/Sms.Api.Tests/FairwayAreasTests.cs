using System.Text.Json;
using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class FairwayAreasTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static GraphEdge Edge(long a, long b, double x1, double y1, double x2, double y2) =>
        new(a, b, RoutingGraphBuilder.Haversine(y1, x1, y2, x2), F.CreateLineString([new Coordinate(x1, y1), new Coordinate(x2, y2)]));

    [Fact]
    public void Water_inside_a_fairway_area_is_free_and_water_beside_it_is_penalised()
    {
        var area = F.CreatePolygon([new(10.00, 54.50), new(10.10, 54.50), new(10.10, 54.52), new(10.00, 54.52), new(10.00, 54.50)]);
        var inside = Edge(1, 2, 10.04, 54.51, 10.06, 54.51);
        var beside = Edge(3, 4, 10.04, 54.525, 10.06, 54.525); // ≈550 m north of the area
        var far = Edge(5, 6, 10.04, 54.60, 10.06, 54.60);      // 9 km away
        var result = ChannelPreference.Apply([inside, beside, far], [], [area], 1500, 1.5);
        Assert.Equal(inside.Cost, result[0].Cost);
        Assert.Equal(beside.Cost * 1.5, result[1].Cost, 6);
        Assert.Equal(far.Cost, result[2].Cost);
        // a derived buoy line through the area does not penalise the area itself
        var line = F.CreateLineString([new Coordinate(10.0, 54.51), new Coordinate(10.1, 54.51)]);
        Assert.Equal(inside.Cost, ChannelPreference.Apply([inside], [line], [area], 1500, 1.5)[0].Cost);
    }

    [Fact]
    public void Military_and_no_entry_restricted_areas_are_no_go_areas_other_restrictions_are_not()
    {
        using var doc = JsonDocument.Parse("""
            { "elements": [
              { "type": "way", "id": 1, "tags": { "seamark:type": "restricted_area", "seamark:restricted_area:restriction": "entry_prohibited", "seamark:name": "Todendorf" }, "geometry": [ { "lat": 54.4, "lon": 10.6 }, { "lat": 54.4, "lon": 10.7 }, { "lat": 54.5, "lon": 10.7 }, { "lat": 54.4, "lon": 10.6 } ] },
              { "type": "way", "id": 2, "tags": { "seamark:type": "restricted_area", "seamark:restricted_area:restriction": "no_anchoring" }, "geometry": [ { "lat": 54.0, "lon": 10.0 }, { "lat": 54.0, "lon": 10.1 }, { "lat": 54.1, "lon": 10.1 }, { "lat": 54.0, "lon": 10.0 } ] },
              { "type": "way", "id": 3, "tags": { "seamark:type": "military_area", "seamark:military_area:category": "firing_danger_area" }, "geometry": [ { "lat": 54.2, "lon": 10.0 }, { "lat": 54.2, "lon": 10.1 }, { "lat": 54.3, "lon": 10.1 }, { "lat": 54.2, "lon": 10.0 } ] }
            ] }
            """);
        var noGo = Waterways.ParseNoGoAreas(doc, F);
        Assert.Equal(2, noGo.Count);
        Assert.Equal("Sperrgebiet Todendorf (entry_prohibited)", noGo[0].label);
        Assert.Equal("Sperrgebiet (military_area)", noGo[1].label);
        Assert.All(noGo, n => Assert.True(n.hard));
        // a warning area (firing range, restricted entry at times) is avoided, not blocked
        Assert.True(Waterways.IsNoGoArea(new Dictionary<string, string> { ["seamark:type"] = "restricted_area", ["seamark:restricted_area:restriction"] = "restricted_entry" }));
        Assert.False(Waterways.IsHardNoGo(new Dictionary<string, string> { ["seamark:type"] = "restricted_area", ["seamark:restricted_area:restriction"] = "restricted_entry" }));
        Assert.False(Waterways.IsNoGoArea(new Dictionary<string, string> { ["seamark:type"] = "restricted_area", ["seamark:restricted_area:restriction"] = "no_anchoring;no_fishing" }));
        Assert.True(Waterways.IsNoGoArea(new Dictionary<string, string> { ["seamark:type"] = "restricted_area", ["seamark:restricted_area:restriction"] = "no_anchoring;no_entry" }));
    }

    [Fact]
    public void Parses_closed_ways_and_multipolygon_outer_rings_as_fairway_areas()
    {
        using var doc = JsonDocument.Parse("""
            { "elements": [
              { "type": "way", "id": 1, "tags": { "seamark:type": "fairway", "seamark:name": "Kieler Förde" }, "geometry": [ { "lat": 54.5, "lon": 10.0 }, { "lat": 54.5, "lon": 10.1 }, { "lat": 54.6, "lon": 10.1 }, { "lat": 54.5, "lon": 10.0 } ] },
              { "type": "way", "id": 2, "tags": { "seamark:type": "fairway", "waterway": "fairway" }, "geometry": [ { "lat": 54.0, "lon": 9.0 }, { "lat": 54.1, "lon": 9.1 } ] },
              { "type": "relation", "id": 3, "tags": { "type": "multipolygon", "seamark:type": "fairway", "name": "Schlei" }, "members": [
                  { "type": "way", "ref": 10, "role": "outer", "geometry": [ { "lat": 54.6, "lon": 9.8 }, { "lat": 54.6, "lon": 9.9 }, { "lat": 54.7, "lon": 9.9 }, { "lat": 54.6, "lon": 9.8 } ] },
                  { "type": "way", "ref": 11, "role": "inner", "geometry": [ { "lat": 54.62, "lon": 9.85 }, { "lat": 54.62, "lon": 9.86 }, { "lat": 54.63, "lon": 9.86 }, { "lat": 54.62, "lon": 9.85 } ] } ] },
              { "type": "node", "id": 4, "lat": 54.0, "lon": 9.0, "tags": { "seamark:type": "buoy_lateral" } }
            ] }
            """);
        var areas = Waterways.ParseFairwayAreas(doc, F);
        Assert.Equal(2, areas.Count);
        Assert.Equal("Kieler Förde", areas[0].name); Assert.True(areas[0].area.IsValid); Assert.Equal(4, areas[0].area.ExteriorRing.NumPoints);
        Assert.Equal("Schlei", areas[1].name);
        Assert.Single(Waterways.Parse(doc, F)); // the open fairway line is still a passage, the areas are not
    }
}
