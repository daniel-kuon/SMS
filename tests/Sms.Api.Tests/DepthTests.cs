using Sms.Domain;
using NetTopologySuite.Geometries;
using Sms.Api;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class DepthTests
{
    [Fact]
    public void Densify_adds_bend_candidates_along_long_edges_only()
    {
        var d = StringPull.Densify([new(10.0, 54.0), new(10.0, 54.09), new(10.001, 54.09)], [false, false], 1000).points; // 10 km edge, then 65 m
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

    [Fact]
    public void Uncharted_coastal_edges_get_the_floor_of_the_band_next_to_them()
    {
        // land north of 54.5205 (the strip 54.5195-54.5205 has no data), the 1.83 m band south of 54.5195, the 0 m band to the east
        var band = F.CreatePolygon([new(10.00, 54.50), new(10.10, 54.50), new(10.10, 54.5195), new(10.00, 54.5195), new(10.00, 54.50)]);
        var flat = F.CreatePolygon([new(10.10, 54.50), new(10.20, 54.50), new(10.20, 54.5195), new(10.10, 54.5195), new(10.10, 54.50)]);
        var land = F.CreatePolygon([new(9.90, 54.5205), new(10.30, 54.5205), new(10.30, 54.60), new(9.90, 54.60), new(9.90, 54.5205)]);
        bool NearLand(Coordinate c, double m) => land.IsWithinDistance(F.CreatePoint(c), m / 111_320.0);
        var strip = Edge(1, 2, 10.02, 54.52, 10.06, 54.52);        // next to the 1.83 m band only
        var stripEast = Edge(3, 4, 10.08, 54.52, 10.12, 54.52);    // reaches the 0 m band too: the shallowest counts
        var offshore = Edge(5, 6, 10.02, 54.40, 10.06, 54.40);     // no data far from land: stays unlimited
        var wide = Edge(7, 8, 10.02, 54.52, 10.02, 54.47);         // charted on the way: its own depth
        var farStrip = Edge(9, 10, 10.25, 54.52, 10.28, 54.52);    // along the coast, no band in reach: unknown, not definitely deep
        var result = DepthLimits.Apply([strip, stripEast, offshore, wide, farStrip], [(band, 1.8288), (flat, 0)], NearLand);
        Assert.Null(result[0].MinDepthM); Assert.Equal(1.8288, result[0].AdjacentDepthM);
        Assert.Equal(0, result[1].AdjacentDepthM);
        Assert.Null(result[2].AdjacentDepthM);
        Assert.Equal(1.8288, result[3].MinDepthM); Assert.Null(result[3].AdjacentDepthM);
        Assert.Null(result[4].MinDepthM); Assert.Null(result[4].AdjacentDepthM); Assert.Equal(DepthLimits.UnknownCoastalFloorM, result[4].FloorM);
        Assert.All(result.Take(4), e => Assert.Null(e.FloorM));
        Assert.Null(DepthLimits.Apply([strip], [(band, 1.8288)])[0].AdjacentDepthM); // without land (passages): no coastal rule
    }

    [Fact]
    public void Ferry_corridor_edges_keep_the_charted_band_but_get_no_floor()
    {
        // the fixtures of Uncharted_coastal_edges_get_the_floor_of_the_band_next_to_them, a ferry track along the coastal strip
        var band = F.CreatePolygon([new(10.00, 54.50), new(10.10, 54.50), new(10.10, 54.5195), new(10.00, 54.5195), new(10.00, 54.50)]);
        var flat = F.CreatePolygon([new(10.10, 54.50), new(10.20, 54.50), new(10.20, 54.5195), new(10.10, 54.5195), new(10.10, 54.50)]);
        var land = F.CreatePolygon([new(9.90, 54.5205), new(10.30, 54.5205), new(10.30, 54.60), new(9.90, 54.60), new(9.90, 54.5205)]);
        bool NearLand(Coordinate c, double m) => land.IsWithinDistance(F.CreatePoint(c), m / 111_320.0);
        (Geometry, double)[] areas = [(band, 1.8288), (flat, 0)];
        var ferries = new FerryCorridors([F.CreateLineString([new(10.00, 54.52), new(10.30, 54.52)])], 50);
        var strip = Edge(1, 2, 10.02, 54.52, 10.06, 54.52);        // on the track
        var farStrip = Edge(9, 10, 10.25, 54.52, 10.28, 54.52);    // on the track, no band in reach
        var wide = Edge(7, 8, 10.02, 54.52, 10.02, 54.47);         // leaves the track across the band: ~50 m of 5.5 km within it
        var result = DepthLimits.Apply([strip, farStrip, wide], areas, NearLand, ferry: ferries);
        Assert.True(result[0].Ferry); Assert.Null(result[0].AdjacentDepthM); Assert.Null(result[0].FloorM); Assert.Null(result[0].MinDepthM);
        Assert.True(result[1].Ferry); Assert.Null(result[1].FloorM); // was UnknownCoastalFloorM
        Assert.False(result[2].Ferry); Assert.Equal(1.8288, result[2].MinDepthM);
        // a track through the charted band: flagged, the band stays (the band block for charted shallows)
        var inBand = Edge(11, 12, 10.02, 54.51, 10.06, 54.51);
        var through = DepthLimits.Apply([inBand], areas, NearLand, ferry: new FerryCorridors([F.CreateLineString([new(10.00, 54.51), new(10.30, 54.51)])], 50))[0];
        Assert.True(through.Ferry); Assert.Equal(1.8288, through.MinDepthM);
        // no tracks: as before
        List<GraphEdge> all = [strip, farStrip, wide, inBand];
        Assert.Equal(DepthLimits.Apply(all, areas, NearLand), DepthLimits.Apply(all, areas, NearLand, ferry: FerryCorridors.Empty));
    }
}
