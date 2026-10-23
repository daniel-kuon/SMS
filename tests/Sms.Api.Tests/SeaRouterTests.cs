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

    [Fact]
    public void Known_bridges_warn_below_25_m_without_an_air_draft_and_within_1_m_of_one()
    {
        Assert.Null(SeaRouter.KnownBridgeNote(42, null, 15));        // the NOK bridges
        Assert.Null(SeaRouter.KnownBridgeNote(25, null, 15));
        Assert.Contains("24,0 m", SeaRouter.KnownBridgeNote(24, null, 15));
        Assert.Contains("angenommene Masthöhe", SeaRouter.KnownBridgeNote(12, null, 15));
        Assert.Null(SeaRouter.KnownBridgeNote(20, null, 0));         // DefaultAirDraftM off
        Assert.Null(SeaRouter.KnownBridgeNote(20, 18, 15));          // 2 m above the mast
        Assert.Null(SeaRouter.KnownBridgeNote(19, 18, 15));
        Assert.Contains("weniger als 1 m über", SeaRouter.KnownBridgeNote(18.5, 18, 15));
        Assert.Null(SeaRouter.KnownBridgeNote(24, 12, 15));          // the mast is known: no question below 25 m either
    }
}
