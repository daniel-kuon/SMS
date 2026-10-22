using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class ChannelPreferenceTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static GraphEdge Edge(long a, long b, double x1, double y1, double x2, double y2) =>
        new(a, b, RoutingGraphBuilder.Haversine(y1, x1, y2, x2), F.CreateLineString([new Coordinate(x1, y1), new Coordinate(x2, y2)]));

    [Fact]
    public void Edges_near_a_fairway_get_the_penalty_others_do_not()
    {
        var fairway = F.CreateLineString([new Coordinate(10.15, 54.40), new Coordinate(10.15, 54.45)]); // channel running north
        var beside = Edge(1, 2, 10.155, 54.42, 10.155, 54.43);   // ≈ 330 m east of it
        var far = Edge(3, 4, 10.25, 54.42, 10.25, 54.43);        // ≈ 6.5 km east
        var beyond = Edge(5, 6, 10.15, 54.47, 10.15, 54.48);     // 2 km north of the channel's end
        var result = ChannelPreference.Apply([beside, far, beyond], [fairway], 1500, 1.5);
        Assert.Equal(beside.Cost * 1.5, result[0].Cost, 3); // fully inside the band
        Assert.Equal(far.Cost, result[1].Cost);
        Assert.Equal(beyond.Cost, result[2].Cost);
        // disabled configurations leave everything untouched
        Assert.Equal(beside.Cost, ChannelPreference.Apply([beside], [fairway], 0, 1.5)[0].Cost);
        Assert.Equal(beside.Cost, ChannelPreference.Apply([beside], [fairway], 1500, 1)[0].Cost);
        Assert.Equal(beside.Cost, ChannelPreference.Apply([beside], [], 1500, 1.5)[0].Cost);
        // a long leg that only cuts through the band pays for the part inside it: 1.5 km band, 6.6 km leg → roughly a quarter inside
        var cutting = Edge(7, 8, 10.10, 54.425, 10.20, 54.425);
        var priced = ChannelPreference.Apply([cutting], [fairway], 1500, 1.5)[0].Cost / cutting.Cost;
        Assert.InRange(priced, 1.15, 1.30);
        // inside a charted fairway area the band does not apply
        var area = F.CreatePolygon([new(10.14, 54.41), new(10.16, 54.41), new(10.16, 54.44), new(10.14, 54.44), new(10.14, 54.41)]);
        Assert.Equal(beside.Cost, ChannelPreference.Apply([beside], [fairway], [area], 1500, 1.5)[0].Cost, 3);
    }

    [Fact]
    public void Block_zone_keeps_the_wide_corridor_free()
    {
        var line = F.CreateLineString([new Coordinate(10.15, 54.40), new Coordinate(10.15, 54.45)]); // a zone line running north
        var mPerDegLng = WaterGraphTiler.MPerDegLat * Math.Cos(54.425 * Math.PI / 180);
        Point East(double m) => F.CreatePoint(new Coordinate(10.15 + m / mPerDegLng, 54.425));
        bool Inside(List<Polygon> zone, double m) => zone.Any(z => z.Contains(East(m)));
        var zone = RoutingGraphBuilder.BlockZone([line], [line], [], 1500, 500);
        Assert.False(Inside(zone, 300)); Assert.True(Inside(zone, 700)); Assert.False(Inside(zone, 1700));
        Assert.True(Inside(RoutingGraphBuilder.BlockZone([line], [line], [], 1500, 150), 300));
        // a line that is no zone line (in allLines only) still cuts its corridor out of the first line's zone
        var other = F.CreateLineString([new Coordinate(10.15 + 1000 / mPerDegLng, 54.41), new Coordinate(10.15 + 1000 / mPerDegLng, 54.44)]);
        var both = RoutingGraphBuilder.BlockZone([line, other], [line], [], 1500, 500);
        Assert.False(Inside(both, 700)); Assert.False(Inside(both, 1200)); Assert.True(Inside(zone, 1200));
        // a fairway with a 3 km gap (two lines that continue each other): the water along the gap is fairway, neither blocked nor x3
        var south = F.CreateLineString([new Coordinate(10.15, 54.40), new Coordinate(10.15, 54.42)]);
        var north = F.CreateLineString([new Coordinate(10.15, 54.447), new Coordinate(10.15, 54.47)]);
        Point Gap(double m) => F.CreatePoint(new Coordinate(10.15 + m / mPerDegLng, 54.425)); // 550 m past the southern line's end
        var gapZone = RoutingGraphBuilder.BlockZone([south, north], [south, north], [], 1500, 300);
        Assert.DoesNotContain(gapZone, z => z.Contains(Gap(200))); Assert.Contains(gapZone, z => z.Contains(Gap(700)));
        var priced = ChannelPreference.Zone([south, north], [], 1500, Fairways.GapChords([south, north]), 300);
        Assert.DoesNotContain(priced, z => z.Contains(Gap(200))); Assert.Contains(priced, z => z.Contains(Gap(700)));
        Assert.Contains(ChannelPreference.Zone([south, north], [], 1500), z => z.Contains(Gap(200))); // without the gaps: x3 there
    }

    [Fact]
    public void Uncharted_zone_metres_count_the_samples_inside()
    {
        static NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Prepared.IPreparedGeometry> Tree(params Geometry[] gs)
        {
            var t = new NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Prepared.IPreparedGeometry>();
            foreach (var g in gs) t.Insert(g.EnvelopeInternal, NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(g));
            t.Build();
            return t;
        }
        Polygon Box(double x1, double y1, double x2, double y2) => F.CreatePolygon([new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2), new(x1, y1)]);
        var leg = F.CreateLineString([new Coordinate(10, 54), new Coordinate(10, 54 + 2000 / WaterGraphTiler.MPerDegLat)]); // 2 km north
        var zone = Tree(Box(9.99, 53.99, 10.01, 54.0088)); // the southern half
        Assert.InRange(SeaRouter.UnchartedZoneM(leg, zone, Tree()), 950, 1050);
        Assert.Equal(0, SeaRouter.UnchartedZoneM(leg, zone, Tree(Box(9.99, 53.99, 10.01, 54.03)))); // charted deep enough all along
        Assert.Equal(0, SeaRouter.UnchartedZoneM(leg, Tree(), Tree()));
    }
}
