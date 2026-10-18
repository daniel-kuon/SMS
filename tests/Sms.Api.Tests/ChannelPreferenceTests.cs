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
}
