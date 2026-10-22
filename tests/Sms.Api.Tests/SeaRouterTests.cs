using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class SeaRouterTests
{
    [Fact]
    public void Bridge_hits_merge_per_crossing()
    {
        // the old Storstrøm bridge's 25.2 m span and, 1.3 km along the route, the new bridge's unknown northern span: two crossings;
        // an unknown line 220 m from the known span is the same crossing drawn twice
        List<(double at, Coordinate where, double? clearance)> hits = [(0.000, new Coordinate(11.886, 54.967), 25.2), (0.012, new Coordinate(11.867, 54.974), null), (0.002, new Coordinate(11.884, 54.968), null)];
        var merged = SeaRouter.MergeBridgeHits(hits, 500);
        Assert.Equal([0.000, 0.012], merged.Select(h => h.at).Order());
    }
}
