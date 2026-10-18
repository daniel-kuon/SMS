using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class PassageContractionTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static GraphEdge E(long a, long b, double x1, double y1, double x2, double y2, double? depth = null) =>
        new(a, b, RoutingGraphBuilder.Haversine(y1, x1, y2, x2), F.CreateLineString([new Coordinate(x1, y1), new Coordinate(x2, y2)]), null, depth);

    [Fact]
    public void A_canal_becomes_one_edge_per_junction_to_junction_stretch()
    {
        // canal 1-2-3-4-5 with a side arm 3-6; node 1 and 5 are ends, 3 is a junction (three edges); 4 carries a link to water node 90
        var edges = new List<GraphEdge>
        {
            E(1, 2, 9.0, 54.0, 9.01, 54.0), E(2, 3, 9.01, 54.0, 9.02, 54.0, 3.0), E(3, 4, 9.02, 54.0, 9.03, 54.0), E(4, 5, 9.03, 54.0, 9.04, 54.0, 2.0),
            E(3, 6, 9.02, 54.0, 9.02, 54.01),
            E(4, 90, 9.03, 54.0, 9.03, 53.99),
        };
        var result = PassageContraction.Contract(edges, new HashSet<long> { 1, 5, 4 }, id => id >= 90);
        // 1-3 merged (through 2), 3-4, 4-5, 3-6, 4-90 stay: five edges
        Assert.Equal(5, result.Count);
        var merged = result.Single(e => (e.Source == 1 && e.Target == 3) || (e.Source == 3 && e.Target == 1));
        Assert.Equal(3, merged.Geom.NumPoints);
        Assert.Equal(3.0, merged.MinDepthM); // the strictest depth of the parts
        Assert.Equal(edges[0].Cost + edges[1].Cost, merged.Cost, 6);
        Assert.DoesNotContain(result, e => e.Source == 2 || e.Target == 2); // vertex 2 is gone
        // without the link at 4 the whole 3-4-5 stretch would be one edge
        var looser = PassageContraction.Contract(edges.Take(5).ToList(), new HashSet<long> { 1, 5 }, id => id >= 90);
        Assert.Equal(3, looser.Count);
        Assert.Contains(looser, e => e.Geom.NumPoints == 3 && e.MinDepthM == 2.0);
    }
}
