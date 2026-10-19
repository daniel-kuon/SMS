using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace Sms.Domain.Tests;

public class PierAvoidanceTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static LineString Line(params (double x, double y)[] c) => F.CreateLineString(c.Select(p => new Coordinate(p.x, p.y)).ToArray());
    private static STRtree<Geometry> Index(params Geometry[] gs) { var t = new STRtree<Geometry>(); foreach (var g in gs) t.Insert(g.EnvelopeInternal, g); t.Build(); return t; }
    private static bool Crosses(List<Coordinate> route, Geometry pier, int skipLast = 0)
    {
        for (var i = 1; i < route.Count - skipLast; i++) if (Line((route[i - 1].X, route[i - 1].Y), (route[i].X, route[i].Y)).Intersects(pier)) return true;
        return false;
    }

    // a jetty sticking north from the shore to 54.0005, crossed by an east-west leg at 54.0
    private static readonly LineString Jetty = Line((10.005, 53.999), (10.005, 54.0005));

    [Fact]
    public void Leg_across_a_jetty_goes_round_its_head()
    {
        var route = PierAvoidance.Apply([new(10.0, 54.0), new(10.01, 54.0)], Index(Jetty), (_, _) => false);
        Assert.False(Crosses(route, Jetty));
        Assert.Equal(new Coordinate(10.0, 54.0), route[0]);
        Assert.Equal(new Coordinate(10.01, 54.0), route[^1]);
        Assert.True(route.Max(c => c.Y) > 54.0005); // round the head, not the root
        Assert.True(route.Max(c => c.Y) < 54.0006); // and close to it (5 m clearance)
    }

    [Fact]
    public void Land_still_blocks_the_detour()
    {
        Func<Coordinate, Coordinate, bool> land = (a, b) => Math.Max(a.Y, b.Y) > 54.0004 || Math.Min(a.Y, b.Y) < 53.9992; // both ends of the jetty run into land: no way round, the leg is kept as it was (never no route)
        var route = PierAvoidance.Apply([new(10.0, 54.0), new(10.01, 54.0)], Index(Jetty), land);
        Assert.Equal([new Coordinate(10.0, 54.0), new Coordinate(10.01, 54.0)], route);
    }

    [Fact]
    public void Harbour_point_on_a_jetty_is_reached_from_the_water_next_to_it()
    {
        var harbour = new Coordinate(10.005, 54.0002);
        var route = PierAvoidance.Apply([new(10.0, 54.0), harbour], Index(Jetty), (_, _) => false);
        Assert.Equal(harbour, route[^1]);
        Assert.True(PierAvoidance.Dist(route[^2], harbour) < 6);
        Assert.True(route[^2].X < 10.005); // the side the boat comes from
        Assert.False(Crosses(route, Jetty, skipLast: 1));
    }

    [Fact]
    public void Harbour_between_two_jetties_is_entered_through_the_gap()
    {
        var east = Line((10.0056, 53.999), (10.0056, 54.0005)); // ~40 m east of Jetty
        var harbour = new Coordinate(10.0053, 53.9995);
        var route = PierAvoidance.Apply([new(10.0, 54.0), harbour], Index(Jetty, east), (_, _) => false);
        Assert.Equal(harbour, route[^1]);
        Assert.False(Crosses(route, Jetty));
        Assert.False(Crosses(route, east));
    }
}
