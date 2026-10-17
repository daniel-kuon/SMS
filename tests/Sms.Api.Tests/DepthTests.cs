using NetTopologySuite.Geometries;
using Sms.Api;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class DepthTests
{
    [Fact]
    public void Densify_adds_bend_candidates_along_long_edges_only()
    {
        var d = SeaRouter.Densify([new(10.0, 54.0), new(10.0, 54.09), new(10.001, 54.09)], 1000); // 10 km edge, then 65 m
        Assert.Equal(13, d.Count); Assert.Equal(new Coordinate(10.0, 54.0), d[0]); Assert.Equal(new Coordinate(10.001, 54.09), d[^1]);
        Assert.Equal(54.0082, d[1].Y, 4); // 10.0 km → 11 steps of 0.00818°
    }

    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static GraphEdge Edge(long a, long b, double x1, double y1, double x2, double y2) =>
        new(a, b, RoutingGraphBuilder.Haversine(y1, x1, y2, x2), F.CreateLineString([new Coordinate(x1, y1), new Coordinate(x2, y2)]));

    [Fact]
    public void Decodes_depth_areas_from_a_seascape_tile()
    {
        using var pbf = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "seascape-10-540-330.pbf"));
        var areas = SeascapeDepthProvider.Decode(pbf, 540, 330, 10);
        Assert.NotEmpty(areas);
        Assert.All(areas, a => { Assert.True(a.MaxDepthM >= a.MinDepthM); Assert.True(a.Area.IsValid || a.Area.Area > 0); Assert.Equal(4326, a.Area.SRID); });
        // tile 540/330 at z10 spans ≈ 9.84–10.19° E, 53.54–53.85° N
        var env = new Envelope(); foreach (var a in areas) env.ExpandToInclude(a.Area.EnvelopeInternal);
        Assert.InRange(env.MinX, 9.5, 10.5); Assert.InRange(env.MinY, 53.3, 54.0);
        Assert.Contains(areas, a => a.MinDepthM < 5);
    }

    [Fact]
    public void Grid_edges_get_the_shallowest_area_they_cross_and_passages_stay_unlimited()
    {
        var shallow = F.CreatePolygon([new(10.00, 54.50), new(10.10, 54.50), new(10.10, 54.52), new(10.00, 54.52), new(10.00, 54.50)]);
        var deep = F.CreatePolygon([new(10.00, 54.52), new(10.10, 54.52), new(10.10, 54.55), new(10.00, 54.55), new(10.00, 54.52)]);
        var inShallow = Edge(1, 2, 10.02, 54.51, 10.05, 54.51);
        var crossing = Edge(3, 4, 10.05, 54.51, 10.05, 54.54);  // through both: the shallow one counts
        var outside = Edge(5, 6, 10.20, 54.60, 10.25, 54.60);
        var result = DepthLimits.Apply([inShallow, crossing, outside], [(shallow, 1.5), (deep, 10)]);
        Assert.Equal(1.5, result[0].MinDepthM); Assert.Equal(1.5, result[1].MinDepthM); Assert.Null(result[2].MinDepthM);
        Assert.Equal(inShallow.Cost, result[0].Cost); // depth never changes the cost, it only closes edges per draft at query time
        Assert.Null(DepthLimits.Apply([inShallow], [])[0].MinDepthM);
    }
}
