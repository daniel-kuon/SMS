using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class CardinalMarksTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static Point P(double x, double y) => F.CreatePoint(new Coordinate(x, y));

    [Fact]
    public void Wedge_lies_on_the_danger_side()
    {
        var north = CardinalMarks.Wedge(new Coordinate(10, 54), MarkSide.North);
        Assert.True(north.Contains(P(10, 53.998)));  // 200 m south
        Assert.False(north.Contains(P(10, 54.002))); // 200 m north
        Assert.InRange(north.EnvelopeInternal.Height, 0.0026, 0.0028);
        var east = CardinalMarks.Wedge(new Coordinate(10, 54), MarkSide.East);
        Assert.True(east.Contains(P(10 - 200 / (WaterGraphTiler.MPerDegLat * Math.Cos(54 * Math.PI / 180)), 54))); // 200 m west
        Assert.False(east.Contains(P(10 + 200 / (WaterGraphTiler.MPerDegLat * Math.Cos(54 * Math.PI / 180)), 54)));
    }

    [Fact]
    public void Hazard_core_is_the_danger_side_within_60_m_and_the_safe_side_needs_no_clearance()
    {
        double N(double m) => m / WaterGraphTiler.MPerDegLat; double E(double m) => N(m) / Math.Cos(54 * Math.PI / 180);
        LineString EastWest(double northM) => F.CreateLineString([new Coordinate(10 - E(100), 54 + N(northM)), new Coordinate(10 + E(100), 54 + N(northM))]);
        var south = new Coordinate(10, 54); // a south cardinal: the danger lies north of it
        var core = CardinalMarks.Index([(south, MarkSide.South)], CardinalMarks.HazardCoreM);
        bool Hits(STRtree<Polygon> t, LineString l) => t.Query(l.EnvelopeInternal).Any(w => w.Intersects(l));
        Assert.True(Hits(core, EastWest(20)));    // 20 m north: the danger side
        Assert.False(Hits(core, EastWest(-20)));  // 20 m south: the safe side
        Assert.False(Hits(core, EastWest(100)));  // beyond the core
        var wedge = CardinalMarks.Index([(south, MarkSide.South)]);
        Assert.True(Hits(wedge, EastWest(100)));  // the full 300 m wedge still prices and blocks the pull there
        Assert.False(Hits(wedge, EastWest(-1)));  // 1 m off on the named side: no buffer round the mark
    }

    [Fact]
    public void Disc_keeps_60_m_round_an_isolated_danger_mark()
    {
        var d = CardinalMarks.Disc(new Coordinate(10, 54));
        double N(double m) => m / WaterGraphTiler.MPerDegLat; double E(double m) => N(m) / Math.Cos(54 * Math.PI / 180);
        Assert.True(d.Contains(P(10, 54 + N(59))));            // 59 m north
        Assert.True(d.Contains(P(10 + E(59), 54)));            // 59 m east
        Assert.True(d.Contains(P(10 - E(42), 54 - N(42))));    // 59.4 m south-west, between two vertices
        Assert.False(d.Contains(P(10, 54 - N(70))));           // 70 m south
        Assert.False(d.Contains(P(10 + E(70), 54)));           // 70 m east
    }

    [Fact]
    public void Apply_prices_the_share_inside()
    {
        (Coordinate, MarkSide)[] marks = [(new Coordinate(10, 54), MarkSide.North)];
        GraphEdge Edge(double y1, double y2) => new(1, 2, RoutingGraphBuilder.Haversine(y1, 10, y2, 10), F.CreateLineString([new Coordinate(10, y1), new Coordinate(10, y2)]));
        var through = Edge(53.996, 54.000); // 445 m north to the mark, the last 300 m inside the wedge
        var north = Edge(54.001, 54.005);
        var priced = CardinalMarks.Apply([through, north], marks, 3);
        Assert.InRange(priced[0].Cost / through.Cost, 1.5, 3);
        Assert.Equal(north.Cost, priced[1].Cost);
        Assert.Equal(through.Cost, CardinalMarks.Apply([through], marks, 1)[0].Cost);
        // an isolated-danger mark's 60 m disc counts like a wedge: 120 m of the 445 m edge inside
        var danger = CardinalMarks.Apply([through, north], [], 3, [new Coordinate(10, 53.998)]);
        Assert.InRange(danger[0].Cost / through.Cost, 1.4, 1.7);
        Assert.Equal(north.Cost, danger[1].Cost);
    }

    [Fact]
    public void PassesCardinal_matches_Wedge()
    {
        var rnd = new Random(7);
        var cos = Math.Cos(54 * Math.PI / 180);
        Coordinate Rand() => new(10 + (rnd.NextDouble() - 0.5) * 0.02 / cos, 54 + (rnd.NextDouble() - 0.5) * 0.02); // ±1.1 km round the mark
        foreach (var side in new[] { MarkSide.North, MarkSide.East, MarkSide.South, MarkSide.West })
        {
            var wedge = CardinalMarks.Wedge(new Coordinate(10, 54), side);
            for (var k = 0; k < 50; k++)
            {
                Coordinate a = Rand(), b = Rand();
                var line = F.CreateLineString([a, b]);
                var closeM = line.Distance(P(10, 54)) * WaterGraphTiler.MPerDegLat; // rough: degrees of latitude
                var passes = Fairways.PassesCardinal(a, b, new FairwayMark(54, 10, side));
                if (passes != wedge.Intersects(line)) continue;
                // the polygon's arc is a chord inside the circle: only lines that touch the 300 m rim may disagree
                var rimM = Enumerable.Range(0, 101).Select(i => new Coordinate(a.X + (b.X - a.X) * i / 100.0, a.Y + (b.Y - a.Y) * i / 100.0))
                    .Min(c => Math.Abs(RoutingGraphBuilder.Haversine(54, 10, c.Y, c.X) - Fairways.CardinalDangerM));
                Assert.True(rimM < 5, $"{side} {a} {b}: PassesCardinal {passes}, closest {closeM:0} m");
            }
        }
    }
}
