namespace Sms.Domain.Tests;

public class WalkNetworkTests
{
    //  1 --e10 100-- 2 --e11 100-- 3
    //                |             |
    //             e12 50        e14 500
    //                |             |
    //                4 ---e13 300-- 5
    private static readonly WalkNetwork Net = new([new(10, 1, 2, 100), new(11, 2, 3, 100), new(12, 2, 4, 50), new(13, 4, 5, 300), new(14, 3, 5, 500), new(20, 8, 9, 10)]);
    private static readonly WalkSnap Harbour = new(10, 0.5, 20);   // 20 m off the middle of e10

    [Fact]
    public void Distance_includes_both_snap_offsets_and_takes_the_shorter_end()
    {
        var s = Net.Search([Harbour], 5000);
        Assert.Equal(20 + 50 + 50 + 150 + 10, s.DistanceTo([new(13, 0.5, 10)]));   // via 2 and 4, not via 5
        Assert.Equal(20 + 50 + 50 + 300 + 50 + 7, s.DistanceTo([new(14, 0.9, 7)]));  // e14 from its far end 5
    }

    [Fact]
    public void Path_lists_the_edge_pieces_from_start_to_destination_in_walking_direction()
    {
        var p = Net.Search([Harbour], 5000).PathTo([new(14, 0.9, 7)])!;
        Assert.Equal([new WalkLeg(10, 0.5, 1), new(12, 0, 1), new(13, 0, 1), new(14, 1, 0.9)], p.Legs);
        Assert.Equal(477, p.DistanceM, 6);
    }

    [Fact]
    public void Same_edge_goes_straight_along_it()
    {
        var p = Net.Search([Harbour], 5000).PathTo([new(10, 0.9, 5)])!;
        Assert.Equal(20 + 40 + 5, p.DistanceM, 6);
        Assert.Equal([new WalkLeg(10, 0.5, 0.9)], p.Legs);
    }

    [Fact]
    public void Limit_and_unknown_or_unconnected_edges_give_no_distance()
    {
        var s = Net.Search([Harbour], 200);
        Assert.Null(s.DistanceTo([new(13, 0.5, 10)]));   // 280 m > 200 m
        Assert.Null(s.DistanceTo([new(999, 0.5, 0)]));   // edge not in the loaded area
        Assert.Null(s.DistanceTo([new(20, 0.5, 0)]));    // isolated fragment
        Assert.Equal(20 + 50 + 25 + 1, s.DistanceTo([new(20, 0.5, 0), new(12, 0.5, 1)]));   // the reachable candidate wins
        Assert.False(Net.Search([new(999, 0, 0)], 5000).HasStart);
    }

    [Fact]
    public void Nearest_start_candidate_on_an_isolated_fragment_does_not_cut_the_start_off()
    {
        var s = Net.Search([new(20, 0.5, 5), new(11, 1, 40)], 5000);   // a pier path 5 m away (not connected), the road 40 m away at node 3
        Assert.Equal(40 + 100 + 50 + 300 + 3, s.DistanceTo([new(13, 1, 3)]));   // 3 → 2 → 4 → 5 (450) beats e14 (500)
    }
}
