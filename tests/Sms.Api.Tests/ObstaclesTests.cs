using System.Text.Json;
using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class ObstaclesTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static GraphEdge Edge(long a, long b, double x1, double y1, double x2, double y2) =>
        new(a, b, RoutingGraphBuilder.Haversine(y1, x1, y2, x2), F.CreateLineString([new Coordinate(x1, y1), new Coordinate(x2, y2)]));

    [Fact]
    public void Bridges_limit_clearance_and_locks_add_cost_on_the_edges_they_touch()
    {
        var canal = new[] { Edge(1, 2, 9.70, 54.30, 9.72, 54.30), Edge(2, 3, 9.72, 54.30, 9.74, 54.30), Edge(3, 4, 9.74, 54.30, 9.76, 54.30) };
        var bridge = F.CreatePoint(new Coordinate(9.71, 54.3002)); // 20 m north of the first edge
        var lock1 = F.CreatePoint(new Coordinate(9.75, 54.30));
        var far = F.CreatePoint(new Coordinate(9.73, 54.31));      // 1 km away: ignored
        var result = Obstacles.Apply(canal, [(bridge, false, 12.5), (lock1, true, null), (far, false, 3.0)], 1852);
        Assert.Equal(12.5, result[0].ClearanceM);
        Assert.Null(result[1].ClearanceM);
        Assert.Null(result[2].ClearanceM);
        Assert.Equal(canal[0].Cost, result[0].Cost);
        Assert.Equal(canal[2].Cost + 1852, result[2].Cost, 6);
        // two bridges over one edge: the lower one counts
        var twice = Obstacles.Apply([canal[0]], [(bridge, false, 12.5), (F.CreatePoint(new Coordinate(9.715, 54.30)), false, 8)], 1852);
        Assert.Equal(8, twice[0].ClearanceM);
    }

    [Fact]
    public void Parses_bridge_clearance_and_locks_from_overpass_json()
    {
        using var doc = JsonDocument.Parse("""
            { "elements": [
              { "type": "node", "id": 1, "lat": 54.31, "lon": 9.63, "tags": { "seamark:type": "bridge", "seamark:bridge:clearance_height": "42" , "name": "Rendsburger Hochbrücke" } },
              { "type": "way", "id": 2, "center": { "lat": 54.37, "lon": 10.14 }, "tags": { "bridge": "yes", "seamark:bridge:clearance_height": "5,5 m" } },
              { "type": "node", "id": 3, "lat": 53.90, "lon": 9.14, "tags": { "waterway": "lock_gate", "name": "Schleuse Brunsbüttel" } },
              { "type": "way", "id": 4, "center": { "lat": 54.0, "lon": 9.0 }, "tags": { "lock": "yes" } },
              { "type": "node", "id": 5, "lat": 54.0, "lon": 9.0, "tags": { "seamark:type": "buoy_lateral" } },
              { "type": "way", "id": 6, "tags": { "waterway": "canal", "CEMT": "Vb" }, "geometry": [ { "lat": 54.0, "lon": 9.0 }, { "lat": 54.1, "lon": 9.1 } ] }
            ] }
            """);
        var obstacles = Waterways.ParseObstacles(doc);
        Assert.Equal(4, obstacles.Count);
        var bridge = obstacles.Single(o => o.Name == "Rendsburger Hochbrücke");
        Assert.False(bridge.IsLock); Assert.Equal(42, bridge.ClearanceM);
        Assert.Equal(5.5, obstacles.Single(o => o.Lng == 10.14).ClearanceM);
        Assert.Equal(2, obstacles.Count(o => o.IsLock));
        Assert.Null(Waterways.Clearance(new Dictionary<string, string> { ["maxheight"] = "default" }));
        Assert.Equal(3.8, Waterways.Clearance(new Dictionary<string, string> { ["maxheight"] = "3.8" }));
    }
}
