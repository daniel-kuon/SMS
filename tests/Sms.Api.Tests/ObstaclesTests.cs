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
        // an opening bridge costs waiting time but limits nothing unless its open clearance is known
        var opening = Obstacles.Apply([canal[0]], [(bridge, Sms.Domain.CorrectionKind.OpeningBridge, (double?)null)], 1852, 926);
        Assert.Null(opening[0].ClearanceM); Assert.Equal(canal[0].Cost + 926, opening[0].Cost, 6);
        var openingLow = Obstacles.Apply([canal[0]], [(bridge, Sms.Domain.CorrectionKind.OpeningBridge, 15.0)], 1852, 926);
        Assert.Equal(15, openingLow[0].ClearanceM);
        // a bridge as a line: every leg that meets the line gets the clearance, however far from the bridge's centre
        var longBridge = F.CreateLineString([new Coordinate(9.70, 54.295), new Coordinate(9.76, 54.305)]); // crosses the canal at x ≈ 9.73: the middle edge only
        var lined = Obstacles.Apply(canal, [((NetTopologySuite.Geometries.Geometry)longBridge, Sms.Domain.CorrectionKind.Bridge, 18.0)], 1852, 926, 60);
        Assert.Null(lined[0].ClearanceM); Assert.Equal(18, lined[1].ClearanceM); Assert.Null(lined[2].ClearanceM);
        var wide = F.CreateLineString([new(9.705, 54.29), new(9.715, 54.31), new(9.725, 54.29), new(9.735, 54.31), new(9.745, 54.29), new(9.755, 54.31)]); // zigzag: meets all three
        Assert.All(Obstacles.Apply(canal, [((NetTopologySuite.Geometries.Geometry)wide, Sms.Domain.CorrectionKind.Bridge, 18.0)], 1852, 926, 60), e => Assert.Equal(18, e.ClearanceM));
        var beside = Edge(9, 10, 9.70, 54.32, 9.76, 54.32); // parallel, 1.7 km north: untouched
        Assert.Null(Obstacles.Apply([beside], [((NetTopologySuite.Geometries.Geometry)longBridge, Sms.Domain.CorrectionKind.Bridge, 18.0)], 1852, 926, 60)[0].ClearanceM);
    }

    [Fact]
    public void A_bridge_with_charted_spans_is_passable_at_the_spans_only()
    {
        // a 6 km bridge line (east-west) with one charted span node 100 m south of its middle, 65 m; the line itself says 20 m
        var line = F.CreateLineString([new Coordinate(11.00, 55.34), new Coordinate(11.09, 55.34)]);
        var span = F.CreatePoint(new Coordinate(11.045, 55.339));
        var raw = new List<(NetTopologySuite.Geometries.Geometry geom, Sms.Domain.CorrectionKind kind, double? clearanceM)> { (line, Sms.Domain.CorrectionKind.Bridge, 20), (span, Sms.Domain.CorrectionKind.Bridge, 65) };
        var pieces = Obstacles.WithSpans(raw);
        Assert.Equal(3, pieces.Count); // span point, window, remainder
        Assert.Contains(pieces, p => p.geom is not Point && p.clearanceM == 65);
        Assert.Contains(pieces, p => p.geom is not Point && p.clearanceM == 0);
        Assert.Single(Obstacles.SpanPoints(raw));
        var atSpan = Edge(1, 2, 11.045, 55.33, 11.045, 55.35);   // crosses inside the window
        var outside = Edge(3, 4, 11.01, 55.33, 11.01, 55.35);    // crosses 2.2 km west of the span
        var away = Edge(5, 6, 11.045, 55.36, 11.045, 55.38);    // north of the bridge
        var result = Obstacles.Apply(new[] { atSpan, outside, away }, pieces, 1852, 926, 60);
        Assert.Equal(2, result.Count);
        Assert.Equal(65, result.Single(e => e.Source == 1).ClearanceM);
        Assert.Null(result.Single(e => e.Source == 5).ClearanceM);
        // without a charted span the whole line keeps its own clearance
        var plain = Obstacles.Apply(new[] { outside }, Obstacles.WithSpans([(line, Sms.Domain.CorrectionKind.Bridge, 20)]), 1852, 926, 60);
        Assert.Equal(20, plain[0].ClearanceM);
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
              { "type": "way", "id": 6, "tags": { "waterway": "canal", "CEMT": "Vb" }, "geometry": [ { "lat": 54.0, "lon": 9.0 }, { "lat": 54.1, "lon": 9.1 } ] },
              { "type": "way", "id": 7, "center": { "lat": 54.33, "lon": 10.13 }, "tags": { "bridge": "yes", "bridge:movable": "bascule", "seamark:bridge:clearance_height_closed": "5.2", "name": "Hörnbrücke" } },
              { "type": "node", "id": 8, "lat": 54.47, "lon": 9.86, "tags": { "seamark:type": "bridge", "seamark:bridge:category": "swing", "seamark:bridge:clearance_height": "4", "seamark:bridge:clearance_height_open": "12.5", "seamark:name": "Lindaunisbrücke" } },
              { "type": "way", "id": 9, "center": { "lat": 54.5, "lon": 9.5 }, "tags": { "bridge": "yes", "bridge:movable": "no" } },
              { "type": "way", "id": 10, "center": { "lat": 55.34, "lon": 11.03 }, "geometry": [ { "lat": 55.33, "lon": 11.0 }, { "lat": 55.35, "lon": 11.06 } ], "tags": { "bridge": "yes", "seamark:bridge:clearance_height": "65", "name": "Østbroen" } },
              { "type": "way", "id": 11, "geometry": [ { "lat": 55.60, "lon": 12.70 }, { "lat": 55.58, "lon": 12.80 } ], "tags": { "bridge": "yes", "highway": "motorway", "name": "Øresundsbron" } },
              { "type": "way", "id": 12, "geometry": [ { "lat": 54.00, "lon": 9.00 }, { "lat": 54.001, "lon": 9.001 } ], "tags": { "bridge": "yes", "highway": "residential" } }
            ] }
            """);
        var obstacles = Waterways.ParseObstacles(doc);
        Assert.Equal(8, obstacles.Count); // the 100 m residential bridge without clearance is not an obstacle, the 7 km Øresund bridge is
        var oeresund = obstacles.Single(o => o.Name == "Øresundsbron"); Assert.NotNull(oeresund.Line); Assert.Null(oeresund.ClearanceM); Assert.InRange(oeresund.Lat, 55.58, 55.60);
        Assert.Null(obstacles.Single(o => o.Name == "Rendsburger Hochbrücke").Line); // a node
        var east = obstacles.Single(o => o.Name == "Østbroen");
        Assert.NotNull(east.Line); Assert.Equal(2, east.Line!.NumPoints); Assert.Equal(65, east.ClearanceM);
        var bridge = obstacles.Single(o => o.Name == "Rendsburger Hochbrücke");
        Assert.False(bridge.IsLock); Assert.Equal(42, bridge.ClearanceM);
        Assert.Equal(5.5, obstacles.Single(o => o.Lng == 10.14).ClearanceM);
        Assert.Equal(2, obstacles.Count(o => o.IsLock));
        var hoern = obstacles.Single(o => o.Name == "Hörnbrücke");
        Assert.True(hoern.Opening); Assert.Null(hoern.ClearanceM); // closed height does not limit a bridge that opens
        var lindaunis = obstacles.Single(o => o.Name == "Lindaunisbrücke");
        Assert.True(lindaunis.Opening); Assert.Equal(12.5, lindaunis.ClearanceM);
        Assert.False(obstacles.Single(o => o.Name == "Rendsburger Hochbrücke").Opening);
        Assert.Null(Waterways.Clearance(new Dictionary<string, string> { ["maxheight"] = "default" }));
        Assert.Equal(3.8, Waterways.Clearance(new Dictionary<string, string> { ["maxheight"] = "3.8" }));
    }
}
