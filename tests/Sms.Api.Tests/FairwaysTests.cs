using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class FairwaysTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    // a channel running north: port marks 150 m west, starboard marks 150 m east of the centreline, every ~500 m
    private static List<LateralMark> Channel(int pairs, double lng0 = 10.15, double lat0 = 54.40) =>
        Enumerable.Range(0, pairs).SelectMany(i => new[]
        {
            new LateralMark(lat0 + i * 0.0045, lng0 - 0.0023, LateralSide.Port),
            new LateralMark(lat0 + i * 0.0045, lng0 + 0.0023, LateralSide.Starboard),
        }).ToList();

    [Fact]
    public void Pairs_marks_across_the_channel_and_chains_the_midpoints()
    {
        var fairway = Assert.Single(Fairways.Derive(Channel(5), F));
        Assert.Equal(5, fairway.Pairs);
        Assert.Equal(5, fairway.Line.NumPoints);
        Assert.All(fairway.Line.Coordinates, c => Assert.Equal(10.15, c.X, 6)); // midpoints lie on the centreline
        var lats = fairway.Line.Coordinates.Select(c => c.Y).ToList();
        Assert.True(lats.SequenceEqual(lats.OrderBy(x => x)) || lats.SequenceEqual(lats.OrderByDescending(x => x)), "chain is ordered along the channel");
        Assert.Equal(4326, fairway.Line.SRID);
    }

    [Fact]
    public void Gaps_split_chains_and_unpaired_marks_are_ignored()
    {
        var marks = Channel(3);
        marks.AddRange(Channel(3, lat0: 54.50)); // 11 km further north: a second channel
        marks.Add(new LateralMark(54.45, 10.30, LateralSide.Port)); // lonely mark, no partner within 600 m
        marks.Add(new LateralMark(54.30, 10.00, LateralSide.Port));
        marks.Add(new LateralMark(54.30, 10.0015, LateralSide.Starboard)); // a single pair → one point → no line
        var fairways = Fairways.Derive(marks, F);
        Assert.Equal(2, fairways.Count);
        Assert.All(fairways, fw => Assert.Equal(3, fw.Pairs));
        Assert.Empty(Fairways.Derive([], F));
    }

    [Fact]
    public void Side_mapping_follows_iala_a_categories()
    {
        Assert.Equal(LateralSide.Port, Fairways.SideOf("buoy_lateral", "port"));
        Assert.Equal(LateralSide.Starboard, Fairways.SideOf("beacon_lateral", "starboard"));
        Assert.Equal(LateralSide.Port, Fairways.SideOf("buoy_lateral", "preferred_channel_starboard"));
        Assert.Null(Fairways.SideOf("buoy_cardinal", "north"));
        Assert.Null(Fairways.SideOf("buoy_lateral", null));
    }

    [Fact]
    public void Mercator_round_trip()
    {
        var p = Sms.Api.Endpoints.SeamarkEndpoints.ToMercator(54.3721, 10.1234);
        var (lat, lng) = Fairways.FromMercator(p.X, p.Y);
        Assert.Equal(54.3721, lat, 6);
        Assert.Equal(10.1234, lng, 6);
    }
}
