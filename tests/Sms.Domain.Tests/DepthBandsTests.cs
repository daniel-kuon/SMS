using NetTopologySuite.Geometries;
using Sms.Domain;
using Xunit;

namespace Sms.Domain.Tests;

public class DepthBandsTests
{
    private static readonly (double, double)[] Emodnet = [(0, 1.8288), (1.8288, 3.6576), (3.6576, 5.4864), (5.4864, 9.144), (9.144, 18.288)];

    [Fact]
    public void A_1_8_m_boat_passes_the_1_83_to_3_66_band()
        => Assert.Equal(1.8288, DepthBands.EffectiveFloor(1.8 + 0.5, Emodnet), 4);

    [Fact]
    public void A_deeper_boat_needs_the_next_band()
        => Assert.Equal(3.0, DepthBands.EffectiveFloor(2.5 + 0.5, Emodnet), 4); // the 1.83 band (middle 2.74) stays closed

    [Fact]
    public void Never_looser_than_the_limit_when_no_band_passes()
        => Assert.Equal(30, DepthBands.EffectiveFloor(30, Emodnet));

    [Fact]
    public void Mixed_schemes_open_a_floor_only_when_every_band_from_there_on_passes()
    {
        // Kartverket's metric areas next to the fathom bands: 0.5-10 (middle 5.25) passes for 2.8 m, but 0.5-2 shares its floor
        (double, double)[] mixed = [.. Emodnet, (0.5, 2), (0.5, 10), (2, 5), (5, 10), (10, 15), (10, 20)];
        Assert.Equal(2, DepthBands.EffectiveFloor(2.8, mixed), 4); // 2-5 passes, the 1.83 band (2.74) and the 0.5 floor stay closed
        Assert.Equal(9.144, DepthBands.EffectiveFloor(12.5, mixed), 4); // 10-15 (12.5) passes, 5.49-9.14 does not: 5-10 stays closed
        Assert.Equal(5, DepthBands.EffectiveFloor(7, [.. Emodnet, (5, 10)]), 4); // 5-10 (7.5) and every band above pass
        Assert.Equal(7.4, DepthBands.EffectiveFloor(7.4, [.. Emodnet, (5, 10)]), 4); // 5-10 passes (7.5), but the 5.49 fathom band above it (7.32) does not
        Assert.Equal(DepthBands.EffectiveFloor(2.3, Emodnet), DepthBands.EffectiveFloor(2.3, [.. Emodnet, (0, 0), (-16, -16), (4.5, 4.5)])); // flat rows: no band
    }

    [Fact]
    public void Minimum_depth_is_taken_as_given_and_a_bare_draft_gets_the_margin()
    {
        Assert.Equal(2.0, DepthBands.MinDepth(1.8, 2.0)); // the owner's own value, no margin on top
        Assert.Equal(2.3, DepthBands.MinDepth(1.8, null)!.Value, 6); // draft only (old clients, ships without the setting)
        Assert.Equal(2.0, DepthBands.MinDepth(null, 2.0));
        Assert.Null(DepthBands.MinDepth(null, null));
        Assert.Equal(2.3, DepthBands.MinDepth(1.8, 0)!.Value, 6); // 0 = not set
    }

    [Fact]
    public void Band_blocks_by_its_middle_and_warns_by_its_floor()
    {
        // the user's 1.8 m boat with minimum depth 2.0 and warning depth 2.5: sails the 1.83-3.66 m band (middle 2.74) with a warning
        var floor = DepthBands.EffectiveFloor(2.0, Emodnet);
        Assert.Equal(1.8288, floor, 4);
        Assert.True(DepthBands.Warns(1.8288, 2.5));
        Assert.False(DepthBands.Warns(3.6576, 2.5));
        Assert.False(DepthBands.Warns(0, null)); // no warning depth: no warnings
        Assert.True(1.8288 >= floor && 0 < floor); // the 0-1.83 band (middle 0.91) stays closed
    }

    [Fact]
    public void Uncharted_water_counts_as_the_adjacent_band_only_without_a_charted_depth()
    {
        Assert.Equal(1.8288f, DepthBands.Effective(float.NaN, 1.8288f));
        Assert.Equal(5.4864f, DepthBands.Effective(5.4864f, 0f));
        Assert.True(float.IsNaN(DepthBands.Effective(float.NaN, float.NaN)));
    }

    private static readonly GeometryFactory F = new();
    private static DepthBands.Piece P(double km, double floor, double lengthM) => new(new Coordinate(10 + km / 65.5, 54), floor, floor + 1.83, lengthM);

    [Fact]
    public void Warnings_merge_nearby_pieces_and_keep_the_shallowest_few_in_route_order()
    {
        var route = F.CreateLineString([new Coordinate(10, 54), new Coordinate(10 + 30 / 65.5, 54)]); // 30 km east (65.5 km per degree at 54 N)
        var pieces = new[]
        {
            P(2, 3.6576, 200), P(2.6, 1.8288, 200), // 400 m apart: one stretch, located at the shallower piece
            P(10, 1.8288, 100), P(14, 1.8288, 900), P(18, 3.6576, 100), P(22, 1.8288, 50), P(26, 0, 30),
        };
        var w = DepthBands.Stretches(route, pieces, max: 4);
        Assert.Equal(4, w.Count);
        Assert.Equal(2.6, (w[0].At.X - 10) * 65.5, 3); Assert.Equal(400, w[0].LengthM);
        Assert.Equal([0.0, 1.8288, 1.8288, 1.8288], w.Select(x => x.FloorM).Order());
        Assert.DoesNotContain(w, x => x.FloorM == 3.6576 && x.LengthM == 100); // the deeper lone piece is dropped first
        Assert.True(w.Zip(w.Skip(1)).All(p => p.First.At.X < p.Second.At.X)); // route order
    }

    [Fact]
    public void Uncapped_stretches_list_every_one_and_a_tie_sits_at_the_longest_piece()
    {
        // "uncertainDepth": every piece has the same floor (the one band between the limit and the minimum depth), max = int.MaxValue lists all
        var route = F.CreateLineString([new Coordinate(10, 54), new Coordinate(10 + 30 / 65.5, 54)]);
        var pieces = new[] { P(1, 1.8288, 50), P(1.4, 1.8288, 600), P(1.9, 1.8288, 100), P(5, 1.8288, 75), P(9, 1.8288, 75), P(13, 1.8288, 75), P(17, 1.8288, 75), P(21, 1.8288, 75), P(25, 1.8288, 75) };
        var w = DepthBands.Stretches(route, pieces, max: int.MaxValue);
        Assert.Equal(7, w.Count); // the first three merge (within 1 km of each other), six lone pieces 4 km apart stay
        Assert.Equal(750, w[0].LengthM); Assert.Equal(1.4, (w[0].At.X - 10) * 65.5, 3); // located at the longest piece, not the first
        Assert.Equal(5, DepthBands.Stretches(route, pieces).Count); // the default cap still holds for "checkDepth"
    }

    [Fact]
    public void Strict_route_is_kept_only_within_the_detour_bound()
    {
        // Femø → Rødvig: 124.3 km round Møn against 89.4 km through 4.3 km of uncertain water – a detour of 8 × the uncertain stretch
        Assert.False(DepthBands.KeepsStrict(124305, 89379, 4343, 5));
        // Nyborg → Svendborg: 4.6 km round 1.2 km of band (3.8 ×) – kept
        Assert.True(DepthBands.KeepsStrict(49000, 44400, 1200, 5));
        // the strict route shorter in metres (it only cost more, Vordingborg → Femø) – kept
        Assert.True(DepthBands.KeepsStrict(37974, 38210, 1055, 5));
        // exactly at the bound – kept
        Assert.True(DepthBands.KeepsStrict(110_000, 100_000, 2000, 5));
        // 0 = the rule before: the strict route whenever one exists
        Assert.True(DepthBands.KeepsStrict(124305, 89379, 4343, 0));
    }
}
