using NetTopologySuite.Geometries;

namespace Sms.Domain.Tests;

public class AssumedPassagesTests
{
    private static readonly GeometryFactory F = new();
    // an east-west guide line at 54° N: 0.1° ≈ 6.5 km
    private static readonly Coordinate[] Line = [new(10.0, 54), new(10.05, 54), new(10.1, 54)];
    private static double M(double dx) => SeaGraph.Haversine(54, 10, 54, 10 + dx); // metres along the line from its start
    private static Func<Coordinate, bool> Land(params (double from, double to)[] strips) => c => strips.Any(s => c.X >= s.from && c.X <= s.to);

    [Fact]
    public void A_land_crossing_becomes_a_stretch_widened_by_the_margin()
    {
        var s = Assert.Single(AssumedPassages.Stretches(Line, Land((10.04, 10.045))));
        Assert.True(s.Land);
        Assert.InRange(s.From, M(0.04) - 500 - 30, M(0.04) - 500 + 30);
        Assert.InRange(s.To, M(0.045) + 500 - 30, M(0.045) + 500 + 30);
    }

    [Fact]
    public void Land_at_the_ends_is_the_quay_and_ignored()
    {
        Assert.Empty(AssumedPassages.Stretches(Line, Land((9.9, 10.002), (10.098, 10.2))));
    }

    [Fact]
    public void A_long_coast_zone_run_from_the_line_end_is_kept()
    {
        // the line starts in a narrow sound: 1.6 km in the coast zone from the terminal on
        var s = Assert.Single(AssumedPassages.Stretches(Line, Land(), Land((9.9, 10.025))));
        Assert.False(s.Land);
        Assert.Equal(0, s.From);
    }

    [Fact]
    public void Close_crossings_are_joined_far_ones_kept_apart_long_ones_dropped()
    {
        // 10.045 → 10.06 ≈ 980 m apart: within 2 × margin + join
        Assert.Single(AssumedPassages.Stretches(Line, Land((10.04, 10.045), (10.06, 10.062))));
        Assert.Equal(2, AssumedPassages.Stretches(Line, Land((10.02, 10.025), (10.08, 10.082))).Count);
        // 2.6 km of land: a line drawn across an island
        Assert.Empty(AssumedPassages.Stretches(Line, Land((10.01, 10.05))));
    }

    [Fact]
    public void Coast_zone_water_gives_a_stretch_without_land()
    {
        var s = Assert.Single(AssumedPassages.Stretches(Line, _ => false, Land((10.05, 10.055))));
        Assert.False(s.Land);
    }

    [Fact]
    public void SubLine_and_Midpoint_follow_the_line()
    {
        var sub = AssumedPassages.SubLine(Line, M(0.02), M(0.07));
        Assert.Equal(3, sub.Length); // through the middle vertex
        Assert.Equal(10.02, sub[0].X, 4); Assert.Equal(10.05, sub[1].X, 6); Assert.Equal(10.07, sub[2].X, 4);
        Assert.Equal(10.05, AssumedPassages.Midpoint(Line).X, 4);
    }

    [Fact]
    public void Buoy_gaps_need_a_short_land_strip_and_are_clustered()
    {
        Coordinate[] marks =
        [
            new(10.0, 54), new(10.01, 54),          // 650 m apart, 130 m of land between: a gap
            new(9.9995, 54.0005), new(10.01, 54.0005), // the same strip again, a longer pair: clustered away
            new(10.1, 54), new(10.11, 54),           // 650 m apart, 400 m of land: too much
            new(10.2, 54), new(10.205, 54),          // open water between: no gap
            new(10.3, 54), new(10.31, 54),           // the first one on land
        ];
        var land = Land((10.004, 10.006), (10.103, 10.109), (10.295, 10.301));
        var gap = Assert.Single(AssumedPassages.BuoyGaps(marks, land));
        Assert.Equal((0, 1), (gap.A, gap.B));
        Assert.InRange(gap.LandM, 100, 160);
        Assert.Equal(10.005, gap.LandMid.X, 3);
    }

    [Fact]
    public void Buoy_pairs_farther_apart_than_the_limit_are_no_gap()
    {
        Coordinate[] marks = [new(10.0, 54), new(10.03, 54)]; // ~2 km
        Assert.Empty(AssumedPassages.BuoyGaps(marks, Land((10.014, 10.016))));
    }

    [Fact]
    public void A_route_follows_a_passage_it_runs_along_not_one_it_crosses()
    {
        Coordinate[] passage = [new(10.04, 54), new(10.05, 54)];
        var along = F.CreateLineString([new(10.0, 54.001), new(10.039, 54), new(10.05, 54), new(10.06, 54.002)]);
        var across = F.CreateLineString([new(10.045, 53.99), new(10.045, 54.01)]);
        Assert.True(AssumedPassages.Follows(along, passage));
        Assert.False(AssumedPassages.Follows(across, passage));
    }

    // a — m (water) — b (assumed), plus a long detour a — d — b
    private static (SeaGraph graph, Coordinate[] c) Graph()
    {
        Coordinate[] c = [new(10.0, 54), new(10.02, 54), new(10.04, 54), new(10.02, 53.9)];
        int[] src = [0, 1, 0, 3], tgt = [1, 2, 3, 2];
        bool[] assumed = [false, true, false, false];
        var cost = src.Select((s, e) => (float)SeaGraph.Haversine(c[s].Y, c[s].X, c[tgt[e]].Y, c[tgt[e]].X)).ToArray();
        var nan = Enumerable.Repeat(float.NaN, 4).ToArray();
        var graph = new SeaGraph(c.Select(p => p.X).ToArray(), c.Select(p => p.Y).ToArray(), src, tgt, cost, nan, nan, [0, 0, 0, 0, 0], [], int.MaxValue, assumed: assumed);
        return (graph, c);
    }

    [Fact]
    public void A_path_over_an_assumed_edge_reports_its_midpoint()
    {
        var (graph, c) = Graph();
        var path = graph.Route(new SeaGraph.End(c[0], 0), new SeaGraph.End(c[2], 2), new())!;
        Assert.Equal([c[0], c[1], c[2]], path.Points);
        var at = Assert.Single(path.Assumed!);
        Assert.Equal(10.03, at.X, 4); Assert.Equal(54, at.Y, 6);
    }

    [Fact]
    public void Closed_assumed_edges_force_the_detour_without_warning()
    {
        var (graph, c) = Graph();
        var path = graph.Route(new SeaGraph.End(c[0], 0), new SeaGraph.End(c[2], 2), new(NoAssumed: true))!;
        Assert.Contains(c[3], path.Points);
        Assert.Empty(path.Assumed!);
    }
}
