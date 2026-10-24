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
        Assert.Equal(3, pieces.Count); // window, remainder, the span point as a 60 m disc (consumed by its window)
        Assert.Contains(pieces, p => p.geom is not Point && p.clearanceM == 65);
        Assert.Contains(pieces, p => p.geom is not Point && p.clearanceM == 0);
        Assert.Single(Obstacles.SpanPoints(raw));
        // every piece stays inside the bridge's own extent (overlay results share coordinates with the scaled input: a scale-back
        // on a shared Coordinate once stretched pieces across ten degrees of longitude)
        var two = Obstacles.WithSpans([(line, Sms.Domain.CorrectionKind.Bridge, 20), (span, Sms.Domain.CorrectionKind.Bridge, 65), (F.CreatePoint(new Coordinate(11.07, 55.3405)), Sms.Domain.CorrectionKind.Bridge, 30)]);
        Assert.All(two.Where(p => p.geom is ILineal), p => Assert.True(line.EnvelopeInternal.Contains(p.geom.EnvelopeInternal), p.geom.EnvelopeInternal.ToString()));
        var atSpan = Edge(1, 2, 11.045, 55.33, 11.045, 55.35);   // crosses inside the window
        var outside = Edge(3, 4, 11.01, 55.33, 11.01, 55.35);    // crosses 2.2 km west of the span
        var away = Edge(5, 6, 11.045, 55.36, 11.045, 55.38);    // north of the bridge
        var result = Obstacles.Apply(new[] { atSpan, outside, away }, pieces, 1852, 926, 60);
        Assert.Equal(2, result.Count);
        Assert.Equal(65, result.Single(e => e.Source == 1).ClearanceM);
        Assert.Null(result.Single(e => e.Source == 5).ClearanceM);
        // an approach way sharing an end node with the main span belongs to the same chain: the span governs it too
        var approach = F.CreateLineString([new Coordinate(11.09, 55.34), new Coordinate(11.13, 55.345)]);
        var chained = Obstacles.WithSpans([(line, Sms.Domain.CorrectionKind.Bridge, (double?)null), (approach, Sms.Domain.CorrectionKind.Bridge, null), (span, Sms.Domain.CorrectionKind.Bridge, 65)]);
        var onApproach = Edge(7, 8, 11.11, 55.33, 11.11, 55.36);
        Assert.Empty(Obstacles.Apply(new[] { onApproach }, chained, 1852, 926, 60));
        // without a charted span the whole line keeps its own clearance
        var plain = Obstacles.Apply(new[] { outside }, Obstacles.WithSpans([(line, Sms.Domain.CorrectionKind.Bridge, 20)]), 1852, 926, 60);
        Assert.Equal(20, plain[0].ClearanceM);
        // a span point without a clearance on a line without one (new Storstrøm bridge, northern channel): that window is passed as unknown, not closed
        var unknownSpan = Obstacles.WithSpans([(line, Sms.Domain.CorrectionKind.Bridge, (double?)null), (span, Sms.Domain.CorrectionKind.Bridge, 65), (F.CreatePoint(new Coordinate(11.01, 55.3405)), Sms.Domain.CorrectionKind.Bridge, (double?)null)]);
        var passed = Assert.Single(Obstacles.Apply(new[] { outside }, unknownSpan, 1852, 926, 60));
        Assert.Null(passed.ClearanceM); Assert.True(passed.UnknownBridge);
    }

    [Fact]
    public void Span_points_are_consumed_by_their_window()
    {
        // the 6 km line without a clearance, a 65 m span at 11.045 and an unknown span 1.3 km west of it (the Storstrøm's northern span):
        // in area mode (1.5 km point match) the 65 m point reached the unknown window and named it 65 m, unflagged
        var line = F.CreateLineString([new Coordinate(11.00, 55.34), new Coordinate(11.09, 55.34)]);
        var span = F.CreatePoint(new Coordinate(11.045, 55.339));
        var lone = F.CreatePoint(new Coordinate(11.20, 55.40)); // a bridge drawn as a point only: keeps its area match
        var pieces = Obstacles.WithSpans([(line, Sms.Domain.CorrectionKind.Bridge, (double?)null), (span, Sms.Domain.CorrectionKind.Bridge, 65), (F.CreatePoint(new Coordinate(11.025, 55.3405)), Sms.Domain.CorrectionKind.Bridge, (double?)null), (lone, Sms.Domain.CorrectionKind.Bridge, 30)]);
        Assert.Single(pieces, p => p.geom is Point); // span points are 60 m discs now, the lone point stays a point
        Assert.Contains(pieces, p => ReferenceEquals(p.geom, lone));
        var between = Edge(9, 10, 11.025, 55.33, 11.025, 55.35); // through the unknown span, 1.27 km from the 65 m point
        var passed = Assert.Single(Obstacles.Apply([between], pieces, 1852, 926, 1500));
        Assert.Null(passed.ClearanceM); Assert.True(passed.UnknownBridge);
        Assert.Equal(65, Assert.Single(Obstacles.Apply([Edge(1, 2, 11.045, 55.33, 11.045, 55.35)], pieces, 1852, 926, 1500)).ClearanceM);
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
        Assert.Equal(7, obstacles.Count); // road bridges without a clearance, short or long (Øresund), are the unknown-bridge import's
        Assert.DoesNotContain(obstacles, o => o.Name == "Øresundsbron");
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

    [Fact]
    public void A_fixed_bridge_without_a_clearance_marks_its_edges_unknown()
    {
        var canal = new[] { Edge(1, 2, 9.70, 54.30, 9.72, 54.30), Edge(2, 3, 9.72, 54.30, 9.74, 54.30) };
        var road = F.CreateLineString([new Coordinate(9.73, 54.29), new Coordinate(9.73, 54.31)]); // crosses the second edge
        var lined = Obstacles.Apply(canal, [((Geometry)road, Sms.Domain.CorrectionKind.Bridge, (double?)null)], 1852, 926, 60);
        Assert.False(lined[0].UnknownBridge); Assert.True(lined[1].UnknownBridge); Assert.Null(lined[1].ClearanceM);
        // an opening bridge without a clearance is unlimited when open, a known clearance is no unknown
        Assert.False(Obstacles.Apply([canal[1]], [((Geometry)road, Sms.Domain.CorrectionKind.OpeningBridge, (double?)null)], 1852, 926, 60)[0].UnknownBridge);
        Assert.False(Obstacles.Apply([canal[1]], [((Geometry)road, Sms.Domain.CorrectionKind.Bridge, 12.0)], 1852, 926, 60)[0].UnknownBridge);
        // a point without a clearance: within MatchM only, also with the area mode's 1.5 km match (which a known clearance keeps)
        var point = F.CreatePoint(new Coordinate(9.71, 54.305)); // ~550 m north of the first edge
        var area = Obstacles.Apply([canal[0]], [((Geometry)point, Sms.Domain.CorrectionKind.Bridge, (double?)null)], 1852, 926, 1500);
        Assert.False(area[0].UnknownBridge);
        Assert.Equal(10, Obstacles.Apply([canal[0]], [((Geometry)point, Sms.Domain.CorrectionKind.Bridge, 10.0)], 1852, 926, 1500)[0].ClearanceM);
        Assert.True(Obstacles.Apply([canal[0]], [((Geometry)F.CreatePoint(new Coordinate(9.71, 54.3003)), Sms.Domain.CorrectionKind.Bridge, (double?)null)], 1852, 926, 1500)[0].UnknownBridge);
        // the same crossing with a known clearance as well (a charted point next to the OSM road line): the known one counts
        var both = Obstacles.Apply([canal[1]], [((Geometry)road, Sms.Domain.CorrectionKind.Bridge, (double?)null), ((Geometry)F.CreatePoint(new Coordinate(9.7302, 54.30)), Sms.Domain.CorrectionKind.Bridge, 40.0)], 1852, 926, 60);
        Assert.False(both[0].UnknownBridge); Assert.Equal(40, both[0].ClearanceM);
        // the flag survives the contraction of a canal into one edge
        Assert.True(PassageContraction.Contract(lined, new HashSet<long> { 1, 3 }, _ => false).Single().UnknownBridge);
    }

    [Fact]
    public void Bridge_ways_in_series_are_passable_at_the_known_span_only()
    {
        // three ways end to end (west approach, main span, east approach): only the main span is known
        Geometry W(double x1, double x2) => F.CreateLineString([new Coordinate(x1, 55.34), new Coordinate(x2, 55.34)]);
        var west = W(11.00, 11.03); var main = W(11.03, 11.06); var east = W(11.06, 11.09);
        var crossings = new[] { Edge(1, 2, 11.015, 55.33, 11.015, 55.35), Edge(3, 4, 11.045, 55.33, 11.045, 55.35), Edge(5, 6, 11.075, 55.33, 11.075, 55.35) };
        const Sms.Domain.CorrectionKind B = Sms.Domain.CorrectionKind.Bridge;
        // a charted span point in the middle: its window only
        var spanned = Obstacles.Apply(crossings, Obstacles.WithSpans([(west, B, null), (main, B, 20), (east, B, null), (F.CreatePoint(new Coordinate(11.045, 55.3401)), B, 65)]), 1852, 926, 60);
        Assert.Equal(65, Assert.Single(spanned).ClearanceM);
        // a span point on a main span without a clearance of its own marks one channel, not the bridge: the approaches (6 m off, a shared
        // node merges same-clearance ways, Chains) stay unknown
        var pointOnly = Obstacles.Apply(crossings, Obstacles.WithSpans([(W(11.00, 11.0299), B, null), (main, B, null), (W(11.0601, 11.09), B, null), (F.CreatePoint(new Coordinate(11.045, 55.3401)), B, 65)]), 1852, 926, 60);
        Assert.Equal(3, pointOnly.Count); Assert.Equal(65, pointOnly.Single(e => e.Source == 3).ClearanceM); Assert.True(pointOnly.Single(e => e.Source == 1).UnknownBridge);
        // the main span's clearance (a Manual row) without a point: the main span only, the approaches are closed
        var known = Obstacles.Apply(crossings, Obstacles.WithSpans([(west, B, null), (main, B, 55), (east, B, null)]), 1852, 926, 60);
        Assert.Equal(55, Assert.Single(known).ClearanceM);
        // an opening span (a flap between fixed spans): the flap only
        var flap = Obstacles.Apply(crossings, Obstacles.WithSpans([(west, B, null), (main, Sms.Domain.CorrectionKind.OpeningBridge, null), (east, B, null)]), 1852, 926, 60);
        Assert.Equal(3, Assert.Single(flap).Source);
        // the OSM way of the main span drawn under the Manual row (reversed) is covered by it: not closed, the known clearance counts
        var dup = Obstacles.Apply(crossings, Obstacles.WithSpans([(west, B, null), (main, B, 55), (main.Reverse(), B, null), (east, B, null)]), 1852, 926, 60);
        Assert.Equal(55, Assert.Single(dup).ClearanceM);
        // unknown along its whole length: passed with a warning everywhere
        var unknown = Obstacles.Apply(crossings, Obstacles.WithSpans([(west, B, null), (main, B, null), (east, B, null)]), 1852, 926, 60);
        Assert.Equal(3, unknown.Count); Assert.All(unknown, e => Assert.True(e.UnknownBridge));
        // a road meeting the bridge's end at a right angle (another crossing; 6 m off: a shared node merges same-clearance ways, Chains)
        // or a way 100 m off its end is no part of it
        var road = F.CreateLineString([new Coordinate(11.0901, 55.34), new Coordinate(11.0901, 55.36)]);
        var gap = W(11.0916, 11.12);
        var others = Obstacles.Apply([Edge(7, 8, 11.08, 55.35, 11.10, 55.35), Edge(9, 10, 11.105, 55.33, 11.105, 55.35)], Obstacles.WithSpans([(main, B, 55), (east, B, null), (road, B, null), (gap, B, null)]), 1852, 926, 60);
        Assert.Equal(2, others.Count); Assert.All(others, e => Assert.True(e.UnknownBridge));
    }

    [Fact]
    public void Span_points_are_matched_by_index_as_before()
    {
        // a long bridge line with a charted span point next to it, and one far away: one window with the span's clearance, the rest impassable
        var line = F.CreateLineString([new Coordinate(11.00, 55.30), new Coordinate(11.10, 55.30)]);
        var span = F.CreatePoint(new Coordinate(11.05, 55.3005)); // 55 m off the line
        var far = F.CreatePoint(new Coordinate(11.05, 55.40));
        var pieces = Obstacles.WithSpans([((Geometry)line, Sms.Domain.CorrectionKind.Bridge, (double?)null), ((Geometry)span, Sms.Domain.CorrectionKind.Bridge, 65.0), ((Geometry)far, Sms.Domain.CorrectionKind.Bridge, 3.0)]);
        Assert.Single(pieces, p => p.geom is ILineal && p.clearanceM == 65); // the window (the span point itself is a 60 m disc)
        Assert.Single(pieces, p => p.geom is ILineal && p.clearanceM == 0);
        Assert.Equal([span.Coordinate], Obstacles.SpanPoints([((Geometry)line, Sms.Domain.CorrectionKind.Bridge, (double?)null), ((Geometry)span, Sms.Domain.CorrectionKind.Bridge, 65.0), ((Geometry)far, Sms.Domain.CorrectionKind.Bridge, 3.0)]));
    }
}
