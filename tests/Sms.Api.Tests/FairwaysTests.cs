using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class FairwaysTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    /// <summary>Water where no hop is charted deep: no wide hops, no gap bridging, no straightened corners.</summary>
    private static readonly FairwayWater Shallow = new((_, _) => true, (_, _) => false);
    // a channel running north: port marks 150 m west, starboard marks 150 m east of the centreline, every ~500 m
    private static List<FairwayMark> Channel(int pairs, double lng0 = 10.15, double lat0 = 54.40) =>
        Enumerable.Range(0, pairs).SelectMany(i => new[]
        {
            new FairwayMark(lat0 + i * 0.0045, lng0 - 0.0023, MarkSide.Port),
            new FairwayMark(lat0 + i * 0.0045, lng0 + 0.0023, MarkSide.Starboard),
        }).ToList();
    // the same channel buoyed southwards (port marks east of the centreline), running south from lat0
    private static List<FairwayMark> ChannelSouth(int pairs, double lng0 = 10.15, double lat0 = 54.40) =>
        Channel(pairs, lng0, lat0).Select(m => m with { Lat = 2 * lat0 - m.Lat, Side = m.Side == MarkSide.Port ? MarkSide.Starboard : MarkSide.Port }).ToList();

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
        marks.Add(new FairwayMark(54.45, 10.30, MarkSide.Port)); // lonely mark, no partner within 600 m
        marks.Add(new FairwayMark(54.30, 10.00, MarkSide.Port));
        marks.Add(new FairwayMark(54.30, 10.0015, MarkSide.Starboard)); // a single pair → one point → no line
        var fairways = Fairways.Derive(marks, F);
        Assert.Equal(2, fairways.Count);
        Assert.All(fairways, fw => Assert.Equal(3, fw.Pairs));
        Assert.Empty(Fairways.Derive([], F));
    }

    [Fact]
    public void A_stretch_marked_on_one_side_only_joins_the_chains_on_either_side()
    {
        // 3 pairs, 3 km with starboard marks only (every ~750 m), 3 pairs: one fairway through the single marks, half a pair width
        // off them towards the channel (on the centreline)
        var marks = Channel(3);
        marks.AddRange(Channel(3, lat0: 54.40 + 0.009 + 0.027));
        for (var k = 1; k <= 3; k++) marks.Add(new FairwayMark(54.409 + k * 0.00675, 10.15 + 0.0023, MarkSide.Starboard));
        // three lines sharing their ends: 3 pairs, the stretch of 3 single marks (no pairs: no channel zone in the graph build), 3 pairs
        var pieces = Fairways.Derive(marks, F, Shallow);
        Assert.Equal(3, pieces.Count);
        Assert.Equal(6, pieces.Sum(p => p.Pairs)); Assert.Equal(3, pieces.Sum(p => p.Singles));
        var stretch = Assert.Single(pieces, p => p.Pairs == 0);
        Assert.Equal(5, stretch.Line.NumPoints); // pair, 3 single marks, pair
        Assert.All(pieces.SelectMany(p => p.Line.Coordinates), c => Assert.Equal(10.15, c.X, 6));
        Assert.Equal("Fahrwasser (3 Einzeltonnen)", Fairways.Note(stretch));
        Assert.Equal("Fahrwasser (3 Tonnenpaare)", Fairways.Note(pieces.First(p => p.Pairs > 0)));
        Assert.Equal("Fahrwasser (2 Tonnenpaare, 1 Einzeltonnen)", Fairways.Note(new Fairway(stretch.Line, 2, 1)));
        // without the single marks the 3 km gap splits the chains - unless the gap on that straight course is charted deep
        Assert.Equal(2, Fairways.Derive(marks.Take(12).ToList(), F, Shallow).Count);
        Assert.Single(Fairways.Derive(marks.Take(12).ToList(), F));
        // single marks off to the side (behind the chain's end, not ahead of it) join nothing
        var aside = Channel(3).Concat(Channel(3, lat0: 54.436)).Append(new FairwayMark(54.405, 10.20, MarkSide.Starboard)).ToList();
        Assert.Equal(2, Fairways.Derive(aside, F, Shallow).Count);
    }

    [Fact]
    public void Widely_spaced_single_marks_and_cardinals_continue_a_fairway_along_its_heading()
    {
        // 3 pairs northwards, then an east cardinal 1.2 km on, 100 m west of the course (pass east of it), then lateral marks
        // 3.5 km apart on the same heading, alternately on either side (Storstrøm east of Masnedø), ending at a safe-water mark
        var marks = Channel(3);
        var off = 100 / WaterGraphTiler.MPerDegLat / Math.Cos(54.42 * Math.PI / 180);
        marks.Add(new FairwayMark(54.420, 10.15 - off, MarkSide.East));
        marks.Add(new FairwayMark(54.4515, 10.1505, MarkSide.Port));
        marks.Add(new FairwayMark(54.483, 10.1495, MarkSide.Starboard));
        marks.Add(new FairwayMark(54.5145, 10.1505, MarkSide.Port));
        marks.Add(new FairwayMark(54.522, 10.15, MarkSide.SafeWater));
        var lines = Fairways.Derive(marks, F);
        Assert.Equal(3, lines.Sum(p => p.Pairs)); Assert.Equal(5, lines.Sum(p => p.Singles)); // the pairs, then the single-mark stretch
        var fairway = Assert.Single(lines, p => p.Pairs == 0);
        Assert.Contains(fairway.Line.Coordinates, c => Math.Abs(c.Y - 54.420) < 1e-9 && Math.Abs(c.X - 10.15) < 1e-9); // 100 m east of the cardinal
        Assert.True(fairway.Line.Coordinates[^1].Equals2D(new Coordinate(10.15, 54.522)) || fairway.Line.Coordinates[0].Equals2D(new Coordinate(10.15, 54.522)));
        // a wide hop only leads on to a mark that goes on somewhere: without the safe-water mark the last port mark is left out
        Assert.Equal(3, Fairways.Derive(marks.SkipLast(1).ToList(), F).Sum(p => p.Singles));
        // the same marks where the 3.5 km hops are not charted deep: the fairway ends at the cardinal (one single, trimmed)
        var shallow = Assert.Single(Fairways.Derive(marks, F, Shallow));
        Assert.Equal(0, shallow.Singles);
        // single marks 3.5 km apart at a 50° angle to the course are not chained
        var bent = Channel(3).Append(new FairwayMark(54.43, 10.19, MarkSide.Port)).Append(new FairwayMark(54.45, 10.23, MarkSide.Port)).ToList();
        Assert.Equal(0, Assert.Single(Fairways.Derive(bent, F)).Singles);
    }

    [Fact]
    public void Turns_are_penalised_and_out_and_back_corners_cut()
    {
        // a channel running east with a 1.8 km gap; the pair of another fairway 1.3 km north-east of its end is nearer than the
        // continuation, but the chain keeps its heading
        static List<FairwayMark> East(double lat, double lng0, int n) => Enumerable.Range(0, n).SelectMany(i => new[]
        {
            new FairwayMark(lat + 0.0012, lng0 + i * 0.0077, MarkSide.Port), new FairwayMark(lat - 0.0012, lng0 + i * 0.0077, MarkSide.Starboard),
        }).ToList();
        var marks = East(54.40, 10.00, 3).Concat(East(54.40, 10.0154 + 0.028, 3)).ToList();
        marks.Add(new FairwayMark(54.4115, 10.0244, MarkSide.Port)); marks.Add(new FairwayMark(54.4085, 10.0244, MarkSide.Starboard)); // the decoy
        var fairway = Fairways.Derive(marks, F, Shallow).OrderByDescending(x => x.Pairs).First();
        Assert.Equal(6, fairway.Pairs);
        Assert.All(fairway.Line.Coordinates, c => Assert.Equal(54.40, c.Y, 6));
        // an out-and-back spike is cut where the straight hop is fine, kept where it is not
        var spike = new[] { new Coordinate(10.0, 54.40), new Coordinate(10.0077, 54.409), new Coordinate(10.0154, 54.40), new Coordinate(10.03, 54.40) };
        Assert.Equal(new[] { 0, 2, 3 }, Fairways.Straighten(spike, (_, _) => true));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Fairways.Straighten(spike, (_, _) => false));
    }

    [Fact]
    public void Approaches_buoyed_away_from_a_common_water_are_not_joined()
    {
        // Bogø/Stubbekøbing: two approaches 1.6 km apart on one line, each buoyed from the strait between them outwards
        var marks = Channel(3, lat0: 54.415).Concat(ChannelSouth(3, lat0: 54.40)).ToList();
        var fairways = Fairways.Derive(marks, F);
        Assert.Equal(2, fairways.Count);
        Assert.All(fairways, fw => Assert.Equal(3, fw.Pairs));
        // the same two pieces buoyed one way (one fairway through) are joined
        Assert.Single(Fairways.Derive(Channel(3, lat0: 54.415).Concat(Channel(3, lat0: 54.391)).ToList(), F));
    }

    [Fact]
    public void A_safe_water_mark_ends_an_approach_and_is_passed_by_a_through_fairway()
    {
        // an approach buoyed northwards to a harbour, its safe-water mark 300 m south of the outer pair; a pair chain 1.5 km
        // further south-east is not reached across it
        var approach = Channel(3);
        var safe = new FairwayMark(54.40 - 300 / WaterGraphTiler.MPerDegLat, 10.15, MarkSide.SafeWater);
        var beyond = Channel(3, lng0: 10.165, lat0: 54.385 - 0.009).ToList();
        var fairways = Fairways.Derive([.. approach, safe, .. beyond], F);
        Assert.Equal(2, fairways.Count(fw => fw.Pairs > 0));
        var app = fairways.Where(fw => fw.Line.Coordinates.Any(c => Math.Abs(c.X - 10.15) < 1e-6 && c.Y >= 54.40 - 1e-9)).ToList();
        Assert.Equal(3, app.Sum(fw => fw.Pairs));
        // the approach ends at the mark: its last hop is a line of its own (no pairs, so no channel zone round the end hop)
        var end = Assert.Single(app, fw => fw.Line.Coordinates.Any(c => c.Equals2D(new Coordinate(safe.Lng, safe.Lat))));
        Assert.Equal(0, end.Pairs);
        Assert.DoesNotContain(app.SelectMany(fw => fw.Line.Coordinates), c => c.X > 10.16);
        // junction: a through fairway running east passes a safe-water mark between its pairs; an approach from the north ends there
        static List<FairwayMark> East(double lng0, int n) => Enumerable.Range(0, n).SelectMany(i => new[]
        {
            new FairwayMark(54.4012, lng0 + i * 0.0077, MarkSide.Port), new FairwayMark(54.3988, lng0 + i * 0.0077, MarkSide.Starboard),
        }).ToList();
        var junction = new FairwayMark(54.40, 10.0376, MarkSide.SafeWater);
        var branch = Channel(3, lng0: 10.0376, lat0: 54.405); // buoyed northwards to a harbour, its outer pair 550 m north of the mark
        var all = Fairways.Derive([.. East(10.0, 4), .. East(10.052, 4), junction, .. branch], F, Shallow);
        var through = all.Single(fw => fw.Pairs == 8);
        Assert.True(through.Line.Coordinates.Min(c => c.X) < 10.01 && through.Line.Coordinates.Max(c => c.X) > 10.07, "one line past the mark");
        Assert.Single(all, fw => fw.Pairs == 3);
        var spurEnd = Assert.Single(all, fw => fw.Pairs == 0);
        Assert.True(spurEnd.Line.Coordinates[0].Equals2D(new Coordinate(junction.Lng, junction.Lat)) || spurEnd.Line.Coordinates[^1].Equals2D(new Coordinate(junction.Lng, junction.Lat)));
    }

    [Fact]
    public void Hops_keep_out_of_a_cardinal_marks_danger_quadrant()
    {
        var north = new FairwayMark(54.40, 10.15, MarkSide.North);
        Coordinate At(double northM, double eastM) => new(10.15 + eastM / WaterGraphTiler.MPerDegLat / Math.Cos(54.40 * Math.PI / 180), 54.40 + northM / WaterGraphTiler.MPerDegLat);
        Assert.True(Fairways.PassesCardinal(At(150, -1000), At(150, 1000), north)); // west → east 150 m north of it
        Assert.False(Fairways.PassesCardinal(At(-150, -1000), At(-150, 1000), north)); // 150 m south: through the danger side
        Assert.True(Fairways.PassesCardinal(At(-400, -1000), At(-400, 1000), north)); // 400 m south: out of reach
        Assert.True(Fairways.PassesCardinal(At(-1000, 250), At(1000, 250), north)); // south → north 250 m east: beside it, not south of it
        Assert.False(Fairways.PassesCardinal(At(-1000, 0), At(1000, 0), north)); // straight over the mark from the south
        // a chain does not run south of a north cardinal next to its course: the hop is refused, the line ends before it
        var marks = Channel(3).Append(new FairwayMark(54.416, 10.1503, MarkSide.North)).Concat(Channel(3, lat0: 54.4225)).ToList();
        Assert.Equal(2, Fairways.Derive(marks, F, Shallow).Count);
    }

    [Fact]
    public void A_wide_gap_with_land_on_the_straight_hop_bends_round_it_through_deep_water()
    {
        // Grønsund: 3 pairs, then lateral single marks 3.5 km apart; between the 2nd and 3rd single mark a headland lies on the
        // straight line (lat 54.47 … 54.48 east of 10.149), the water path bends 500 m west round it
        var marks = Channel(3);
        marks.Add(new FairwayMark(54.4405, 10.1505, MarkSide.Port));
        marks.Add(new FairwayMark(54.4720, 10.1495, MarkSide.Starboard));
        marks.Add(new FairwayMark(54.5035, 10.1505, MarkSide.Port));
        marks.Add(new FairwayMark(54.511, 10.15, MarkSide.SafeWater));
        static bool Headland(Coordinate c) => c.Y > 54.480 && c.Y < 54.495 && c.X > 10.1490;
        var bend = new Coordinate(10.140, 54.4875);
        var water = new FairwayWater(
            (a, b) => !Enumerable.Range(1, 99).Any(k => Headland(new Coordinate(a.X + (b.X - a.X) * k / 100, a.Y + (b.Y - a.Y) * k / 100))),
            (_, _) => true,
            (a, b) => [bend]);
        var lines = Fairways.Derive(marks, F, water);
        Assert.Contains(lines, l => l.Line.Coordinates.Any(c => c.Equals2D(bend)));
        Assert.True(lines.SelectMany(l => l.Line.Coordinates).Max(c => c.Y) > 54.50);
        Assert.Equal(4, lines.Sum(l => l.Singles)); // the bend is no mark
        // without a water path the fairway ends before the headland
        Assert.True(Fairways.Derive(marks, F, water with { Path = null }).SelectMany(l => l.Line.Coordinates).Max(c => c.Y) < 54.48);
    }

    [Fact]
    public void Harbour_entrance_lights_pair_up_and_join_the_fairway_beside_them()
    {
        // Kalvehave: F.R and Fl.G on the pier heads 110 m apart, the entrance facing a channel running north 580 m west of it;
        // the lit pair (no buoys) joins the channel; a lone red light elsewhere is no mark
        var marks = Channel(5);
        marks.Add(new FairwayMark(54.4095, 10.1590, MarkSide.Port, Light: true));
        marks.Add(new FairwayMark(54.4085, 10.1590, MarkSide.Starboard, Light: true));
        marks.Add(new FairwayMark(54.3900, 10.1700, MarkSide.Port, Light: true));
        var lines = Fairways.Derive(marks, F);
        var entrance = new Coordinate(10.1590, (54.4095 + 54.4085) / 2);
        Assert.Contains(lines, l => l.Line.Coordinates.Any(c => c.Equals2D(entrance)));
        // with water behind the entrance the line leads on 150 m into the harbour (its end inside, not in the mouth: Glückstadt)
        var harbour = new FairwayWater((_, _) => true, (_, _) => true, null, c => true);
        Assert.Contains(Fairways.Derive(marks, F, harbour).SelectMany(l => l.Line.Coordinates), c => c.X > 10.160);
        // two buoys 1.4 km off the channel stay a lone pair, no spur to them (Marstrand: a 1.8 km spur to a lone pair); as lights
        // they are a harbour entrance and get one
        var far = Channel(5).Concat(marks.Skip(10).Take(2).Select(m => m with { Lng = m.Lng + 0.012, Light = false })).ToList();
        Assert.DoesNotContain(Fairways.Derive(far, F).SelectMany(l => l.Line.Coordinates), c => c.X > 10.16);
        Assert.Contains(Fairways.Derive(far.Select(m => m.Lng > 10.16 ? m with { Light = true } : m).ToList(), F).SelectMany(l => l.Line.Coordinates), c => c.X > 10.16);
        Assert.DoesNotContain(lines.SelectMany(l => l.Line.Coordinates), c => Math.Abs(c.X - 10.17) < 1e-3);
        // a light and a buoy never pair
        Assert.Null(Fairways.SideOf("light_minor", null, "white"));
        Assert.Equal(MarkSide.Port, Fairways.SideOf("light_minor", null, "red"));
    }

    [Fact]
    public void A_line_of_mostly_cardinals_is_no_fairway()
    {
        // cardinal marks round a harbour (Helgoland Südhafen) with two port marks among them: no line
        var marks = new List<FairwayMark>
        {
            new(54.400, 10.150, MarkSide.Port), new(54.404, 10.150, MarkSide.West), new(54.408, 10.150, MarkSide.West),
            new(54.412, 10.150, MarkSide.East), new(54.416, 10.150, MarkSide.West), new(54.420, 10.150, MarkSide.Port),
        };
        Assert.Empty(Fairways.Derive(marks, F));
        // the same with lateral marks for most of them is a fairway
        Assert.NotEmpty(Fairways.Derive(marks.Select((m, k) => k is 2 or 4 ? m with { Side = MarkSide.Port } : m).ToList(), F));
    }

    [Fact]
    public void Hops_across_land_are_refused()
    {
        // land across the channel between the 3rd and 4th pair (a causeway): two lines, not one across it
        var land = new FairwayWater((a, b) => (a.Y - 54.411) * (b.Y - 54.411) > 0, (_, _) => false);
        var fairways = Fairways.Derive(Channel(5), F, land);
        Assert.Equal(2, fairways.Count);
        Assert.Equal(new[] { 2, 3 }, fairways.Select(fw => fw.Pairs).Order());
    }

    [Fact]
    public void Side_mapping_follows_iala_a_categories()
    {
        Assert.Equal(MarkSide.Port, Fairways.SideOf("buoy_lateral", "port"));
        Assert.Equal(MarkSide.Starboard, Fairways.SideOf("beacon_lateral", "starboard"));
        Assert.Equal(MarkSide.Port, Fairways.SideOf("buoy_lateral", "preferred_channel_starboard"));
        Assert.Equal(MarkSide.North, Fairways.SideOf("buoy_cardinal", "north"));
        Assert.Equal(MarkSide.West, Fairways.SideOf("beacon_cardinal", "west"));
        Assert.Equal(MarkSide.SafeWater, Fairways.SideOf("buoy_safe_water", null));
        Assert.Null(Fairways.SideOf("buoy_lateral", null));
        Assert.Equal(MarkSide.Starboard, Fairways.SideOf("buoy_lateral", null, "green")); // no category: the colour (IALA A)
        Assert.Null(Fairways.SideOf("buoy_lateral", "waterway_left", "red"));
        Assert.Null(Fairways.SideOf("buoy_special_purpose", null, "yellow"));
    }

    [Fact]
    public void Mercator_round_trip()
    {
        var p = Sms.Api.Endpoints.SeamarkEndpoints.ToMercator(54.3721, 10.1234);
        var (lat, lng) = Fairways.FromMercator(p.X, p.Y);
        Assert.Equal(54.3721, lat, 6);
        Assert.Equal(10.1234, lng, 6);
    }

    [Fact]
    public void A_wide_river_with_staggered_rows_becomes_one_corridor_with_an_area()
    {
        // red marks 600 m west and green marks 600 m east of the river's centre, every km, staggered by half a km: too far apart to
        // pair, chained as two one-sided rows - which are the borders of one corridor: pairs with the foot on the other row, the area
        // between the rows, the line on the medial line
        const double lng0 = 10.15, lat0 = 54.40, half = 0.0092;
        var marks = new List<FairwayMark>();
        for (var i = 0; i < 6; i++)
        {
            marks.Add(new FairwayMark(lat0 + i * 0.009, lng0 - half, MarkSide.Port));
            marks.Add(new FairwayMark(lat0 + 0.0045 + i * 0.009, lng0 + half, MarkSide.Starboard));
        }
        var corridor = Assert.Single(Fairways.Derive(marks, F));
        Assert.True(corridor.Pairs >= 8, $"pairs {corridor.Pairs}");
        Assert.All(corridor.Line.Coordinates, c => Assert.InRange(c.X, lng0 - 0.002, lng0 + 0.002));
        Assert.NotNull(corridor.Area);
        Assert.True(corridor.Area!.Contains(F.CreatePoint(new Coordinate(lng0, lat0 + 0.025))));
        Assert.Contains(Fairways.PairedNote, Fairways.Note(corridor));
        // without the merge (widePairMaxM 0) the rows stay two single-mark lines
        Assert.All(Fairways.Derive(marks, F, widePairMaxM: 0), f => Assert.Equal(0, f.Pairs));
    }

    [Fact]
    public void A_row_without_pairs_moves_towards_the_deeper_charted_band_beside_it()
    {
        // five red marks in a row running north, no green marks: a 5 m band east of the row, 2 m west of it - the line runs
        // 150 m east of the marks (half the band's run of 300 m probed); without band data it stays on the marks
        const double lng0 = 10.15, lat0 = 54.40;
        var marks = Enumerable.Range(0, 5).Select(i => new FairwayMark(lat0 + i * 0.0045, lng0, MarkSide.Port)).ToList();
        var water = new FairwayWater((_, _) => true, (_, _) => true, Floor: c => c.X > lng0 ? 5.0 : 2.0);
        var line = Assert.Single(Fairways.Derive(marks, F, water));
        var eastM = line.Line.Coordinates.Select(c => (c.X - lng0) * WaterGraphTiler.MPerDegLat * Math.Cos(lat0 * Math.PI / 180)).ToList();
        Assert.All(eastM, m => Assert.InRange(m, Fairways.RowOffsetM - 20, Fairways.RowOffsetM + 20));
        Assert.All(Fairways.Derive(marks, F).Single().Line.Coordinates, c => Assert.Equal(lng0, c.X, 9));
    }

    [Fact]
    public void Cardinals_of_one_kind_in_a_row_form_a_fairway_but_a_ring_round_a_danger_does_not()
    {
        // four west cardinals along a shoal's west edge, 600 m apart: the fairway runs 100 m west of them
        const double lng0 = 10.15, lat0 = 54.40;
        var shoal = Enumerable.Range(0, 4).Select(i => new FairwayMark(lat0 + i * 0.0054, lng0, MarkSide.West)).ToList();
        var line = Assert.Single(Fairways.Derive(shoal, F));
        Assert.Equal(4, line.Singles);
        Assert.All(line.Line.Coordinates, c => Assert.InRange((lng0 - c.X) * WaterGraphTiler.MPerDegLat * Math.Cos(lat0 * Math.PI / 180), 80, 120));
        // a ring of four cardinals round a wreck (one of each kind) is no fairway
        var ring = new List<FairwayMark>
        {
            new(lat0 + 0.002, lng0, MarkSide.North), new(lat0, lng0 + 0.0035, MarkSide.East), new(lat0 - 0.002, lng0, MarkSide.South), new(lat0, lng0 - 0.0035, MarkSide.West),
        };
        Assert.Empty(Fairways.Derive(ring, F));
    }

    [Fact]
    public void Narrow_channels_get_no_area()
    {
        // pairs 460 m wide carry an area; the same channel 65 m wide (a harbour entrance: Ærøskøbing) is its line only
        const double lng0 = 10.15, lat0 = 54.40;
        var wide = Assert.Single(Fairways.Derive(Channel(5), F));
        Assert.Equal(5, wide.Pairs);
        Assert.NotNull(wide.Area);
        Assert.True(wide.Area!.Contains(F.CreatePoint(new Coordinate(lng0, lat0 + 0.009))));
        var narrowMarks = Channel(5).Select(m => m with { Lng = m.Side == MarkSide.Port ? lng0 - 0.0005 : lng0 + 0.0005 }).ToList();
        var narrow = Assert.Single(Fairways.Derive(narrowMarks, F));
        Assert.Equal(5, narrow.Pairs);
        Assert.Null(narrow.Area);
        Assert.Equal(Fairways.Note(wide), Fairways.Note(narrow));
    }

    [Fact]
    public void A_short_approach_of_three_single_marks_is_a_fairway()
    {
        // three port marks 500 m apart running north (a harbour approach), nothing else
        const double lng0 = 10.15, lat0 = 54.40;
        var marks = Enumerable.Range(0, 3).Select(k => new FairwayMark(lat0 + k * 0.0045, lng0, MarkSide.Port)).ToList();
        Assert.Equal(3, Assert.Single(Fairways.Derive(marks, F)).Singles);
        // two marks alone are no fairway
        Assert.Empty(Fairways.Derive(marks.Take(2).ToList(), F));
        // two marks ending at a safe-water mark 800 m beyond the last are
        var safe = new Coordinate(lng0, lat0 + 0.0045 + 800 / WaterGraphTiler.MPerDegLat);
        var line = Assert.Single(Fairways.Derive([.. marks.Take(2), new FairwayMark(safe.Y, safe.X, MarkSide.SafeWater)], F));
        Assert.True(line.Line.Coordinates[0].Equals2D(safe) || line.Line.Coordinates[^1].Equals2D(safe));
    }

    [Fact]
    public void A_line_ends_at_a_safe_water_mark_up_to_3_km_ahead()
    {
        // the safe-water mark 2.8 km beyond the last pair ends the line; 3.9 km beyond it does not
        const double lng0 = 10.15, lat0 = 54.40;
        bool EndsAt(double lat) => Fairways.Derive([.. Channel(3), new FairwayMark(lat, lng0, MarkSide.SafeWater)], F)
            .Any(l => l.Line.Coordinates[^1].Equals2D(new Coordinate(lng0, lat)) || l.Line.Coordinates[0].Equals2D(new Coordinate(lng0, lat)));
        Assert.True(EndsAt(lat0 + 0.009 + 0.025));
        Assert.False(EndsAt(lat0 + 0.009 + 0.035));
    }

    [Fact]
    public void Opposite_cardinals_on_opposite_sides_form_a_channel()
    {
        // an east-west channel 290 m wide between two shoals: north cardinals on its south side, south cardinals on its north side,
        // staggered 195 m (every 390 m on either side); the line zigzags between their safe-side points, 45 m either side of the middle
        const double lng0 = 10.15, lat0 = 54.40, width = 0.0026;
        var marks = Enumerable.Range(0, 3).SelectMany(k => new[]
        {
            new FairwayMark(lat0, lng0 + k * 0.006, MarkSide.North), new FairwayMark(lat0 + width, lng0 + 0.003 + k * 0.006, MarkSide.South),
        }).ToList();
        var line = Assert.Single(Fairways.Derive(marks, F));
        Assert.Equal(6, line.Singles);
        Assert.All(line.Line.Coordinates, c => Assert.InRange(Math.Abs(c.Y - (lat0 + width / 2)) * WaterGraphTiler.MPerDegLat, 0, 50));
        // all north cardinals on one line (no south cardinals across the channel) are a shoal's edge as before; mixed with an east
        // cardinal (two kinds that are not opposite) they are no fairway
        Assert.Single(Fairways.Derive(marks.Where(m => m.Side == MarkSide.North).ToList(), F));
        Assert.Empty(Fairways.Derive(marks.Select((m, k) => k == 2 ? m with { Side = MarkSide.East } : m).Where(m => m.Side != MarkSide.South).ToList(), F));
    }
}
