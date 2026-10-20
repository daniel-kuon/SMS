using NetTopologySuite.Geometries;

namespace Sms.Domain.Tests;

public class SeaGraphTests
{
    private const double D = 0.01; // grid spacing in degrees (~650 m east-west, ~1.1 km north-south at 54° N)

    // nodes 0..n-1 at (10 + x·D, 54 + y·D); edges cost = haversine length × factor
    private sealed class G
    {
        public readonly List<double> X = [], Y = [];
        public readonly List<int> S = [], T = [];
        public readonly List<float> Cost = [], Depth = [], Clear = [];
        public readonly List<int> GeomStart = []; public readonly List<double> Geom = [];
        public int Node(double x, double y) { X.Add(10 + x * D); Y.Add(54 + y * D); return X.Count - 1; }
        public int Edge(int a, int b, double factor = 1, float depth = float.NaN, float clear = float.NaN, params (double x, double y)[] interior)
        {
            var pts = new List<(double, double)> { (X[a], Y[a]) };
            pts.AddRange(interior.Select(p => (10 + p.x * D, 54 + p.y * D)));
            pts.Add((X[b], Y[b]));
            var len = 0.0; for (var k = 1; k < pts.Count; k++) len += SeaGraph.Haversine(pts[k - 1].Item2, pts[k - 1].Item1, pts[k].Item2, pts[k].Item1);
            S.Add(a); T.Add(b); Cost.Add((float)(len * factor)); Depth.Add(depth); Clear.Add(clear);
            GeomStart.Add(Geom.Count / 2);
            foreach (var p in interior) { Geom.Add(10 + p.x * D); Geom.Add(54 + p.y * D); }
            return S.Count - 1;
        }
        public SeaGraph Build(int firstPassage = int.MaxValue, double snapMinM = 1000, float[]? adjacent = null) =>
            new([.. X], [.. Y], [.. S], [.. T], [.. Cost], [.. Depth], [.. Clear], [.. GeomStart, Geom.Count / 2], [.. Geom], firstPassage, null, snapMinM, adjacent: adjacent);
        public Coordinate C(int n) => new(X[n], Y[n]);
    }

    [Fact]
    public void Route_takes_the_cheaper_of_two_ways_and_draws_it()
    {
        var g = new G();
        int a = g.Node(0, 0), b = g.Node(4, 0), up = g.Node(2, 1), down = g.Node(2, -3);
        g.Edge(a, up); g.Edge(up, b); g.Edge(a, down); g.Edge(down, b);
        var graph = g.Build();
        var path = graph.Route(new SeaGraph.End(g.C(a), a), new SeaGraph.End(g.C(b), b), new())!;
        Assert.Equal([g.C(a), g.C(up), g.C(b)], path.Points);
        Assert.All(path.Fixed, Assert.False);
    }

    [Fact]
    public void Low_bridge_closes_its_edge_for_a_high_mast()
    {
        var g = new G();
        int a = g.Node(0, 0), b = g.Node(4, 0), up = g.Node(2, 1), down = g.Node(2, -3);
        g.Edge(a, up, clear: 12); g.Edge(up, b); g.Edge(a, down); g.Edge(down, b);
        var graph = g.Build();
        Assert.Contains(g.C(up), graph.Route(new(g.C(a), a), new(g.C(b), b), new(AirDraftM: 10))!.Points);
        Assert.Contains(g.C(down), graph.Route(new(g.C(a), a), new(g.C(b), b), new(AirDraftM: 18))!.Points);
    }

    [Fact]
    public void Shallow_edge_is_closed_away_from_the_ends_and_costs_three_times_near_them()
    {
        var g = new G();
        int a = g.Node(0, 0), m = g.Node(4, 0), b = g.Node(8, 0), c = g.Node(9, 0), detour = g.Node(6, -6);
        g.Edge(m, b, depth: 1.8f); // 2.6 km, charted 1.8 m
        g.Edge(a, m); g.Edge(m, detour); g.Edge(detour, b); g.Edge(b, c); // the detour: 2 × 6.8 km
        var graph = g.Build();
        var limits = new SeaGraph.Limits(DepthLimitM: 2.0);
        // the shallow edge touches the end node: allowed at ×3 (7.8 km), still cheaper than the detour
        var toB = graph.Route(new(g.C(a), a), new(g.C(b), b), limits)!;
        Assert.DoesNotContain(g.C(detour), toB.Points);
        Assert.True(toB.Cost > 3 * 2500);
        // ending one node further on, it is closed
        Assert.Contains(g.C(detour), graph.Route(new(g.C(a), a), new(g.C(c), c), limits)!.Points);
        // … unless it lies within the harbour approach of an end point
        Assert.DoesNotContain(g.C(detour), graph.Route(new(g.C(a), a), new(g.C(c), c), limits with { ExemptDeg = 0.02 })!.Points);
        // without a draft it is a plain edge
        Assert.True(graph.Route(new(g.C(a), a), new(g.C(b), b), new())!.Cost < 6000);
    }

    [Fact]
    public void Uncharted_coastal_edge_counts_as_the_band_next_to_it()
    {
        var g = new G();
        int a = g.Node(0, 0), m = g.Node(4, 0), b = g.Node(8, 0), c = g.Node(9, 0), detour = g.Node(6, -6);
        var strip = g.Edge(m, b); // no charted depth: the strip next to the coast
        g.Edge(a, m); g.Edge(m, detour); g.Edge(detour, b); g.Edge(b, c);
        var adjacent = Enumerable.Repeat(float.NaN, g.S.Count).ToArray();
        var limits = new SeaGraph.Limits(DepthLimitM: 1.8288); // floor of the first band passing for the minimum depth
        // next to the 1.83 m band: passes like that band
        adjacent[strip] = 1.8288f;
        var graph = g.Build(adjacent: adjacent);
        Assert.DoesNotContain(g.C(detour), graph.Route(new(g.C(a), a), new(g.C(c), c), limits)!.Points);
        Assert.True(graph.DepthInferred(strip)); Assert.Equal(1.8288f, graph.Depth(strip));
        // next to drying ground: closed like it
        adjacent[strip] = -16f;
        Assert.Contains(g.C(detour), g.Build(adjacent: adjacent).Route(new(g.C(a), a), new(g.C(c), c), limits)!.Points);
        // no band next to it (open water without data): unlimited
        adjacent[strip] = float.NaN;
        var open = g.Build(adjacent: adjacent);
        Assert.DoesNotContain(g.C(detour), open.Route(new(g.C(a), a), new(g.C(c), c), limits)!.Points);
        Assert.False(open.DepthInferred(strip));
    }

    [Fact]
    public void Charted_depth_wins_over_the_adjacent_band()
    {
        var g = new G();
        int a = g.Node(0, 0), b = g.Node(4, 0);
        var e = g.Edge(a, b, depth: 5.4864f);
        var graph = g.Build(adjacent: [0f]);
        Assert.Equal(5.4864f, graph.Depth(e)); Assert.False(graph.DepthInferred(e));
        Assert.NotNull(graph.Route(new(g.C(a), a), new(g.C(b), b), new(DepthLimitM: 3.6576)));
    }

    [Fact]
    public void Heuristic_is_admissible_and_astar_matches_dijkstra()
    {
        var rnd = new Random(7);
        var g = new G();
        for (var i = 0; i < 300; i++) g.Node(rnd.NextDouble() * 40, rnd.NextDouble() * 20);
        for (var i = 0; i < 300; i++)
            foreach (var j in Enumerable.Range(0, 300).OrderBy(j => Math.Pow(g.X[i] - g.X[j], 2) + Math.Pow(g.Y[i] - g.Y[j], 2)).Skip(1).Take(4))
                g.Edge(i, j, factor: 0.8 + rnd.NextDouble()); // passage-like edges below 1 per metre
        var graph = g.Build();
        for (var e = 0; e < g.S.Count; e++)
            Assert.True(graph.HeuristicFactor * SeaGraph.Haversine(g.Y[g.S[e]], g.X[g.S[e]], g.Y[g.T[e]], g.X[g.T[e]]) <= g.Cost[e] + 1e-6);
        for (var trial = 0; trial < 20; trial++)
        {
            int s = rnd.Next(300), t = rnd.Next(300);
            var expected = Dijkstra(g, s, t);
            var path = graph.Route(new(g.C(s), s), new(g.C(t), t), new());
            if (double.IsPositiveInfinity(expected)) { Assert.Null(path); continue; }
            Assert.Equal(expected, path!.Cost, 3);
        }
    }

    private static double Dijkstra(G g, int s, int t)
    {
        var dist = Enumerable.Repeat(double.PositiveInfinity, g.X.Count).ToArray(); dist[s] = 0;
        var pq = new PriorityQueue<int, double>(); pq.Enqueue(s, 0);
        while (pq.TryDequeue(out var u, out var d))
        {
            if (d > dist[u]) continue;
            for (var e = 0; e < g.S.Count; e++)
            {
                var v = g.S[e] == u ? g.T[e] : g.T[e] == u ? g.S[e] : -1;
                if (v >= 0 && d + g.Cost[e] < dist[v]) { dist[v] = d + g.Cost[e]; pq.Enqueue(v, dist[v]); }
            }
        }
        return dist[t];
    }

    // a canal: one contracted passage edge with a bend, between two passage vertices (ids ≥ firstPassage), linked to the sea at both ends
    private static (G g, SeaGraph graph, int seaW, int seaE, int canal) Canal()
    {
        var g = new G();
        int seaW = g.Node(-2, 0), seaE = g.Node(12, 4);
        int w = g.Node(0, 0), e = g.Node(10, 4);
        g.Edge(seaW, w); g.Edge(e, seaE);
        var canal = g.Edge(w, e, interior: [(5, 0), (5, 4)]); // west 5, north 4, east 5
        return (g, g.Build(firstPassage: 2), seaW, seaE, canal);
    }

    [Fact]
    public void End_on_a_contracted_canal_is_split_onto_the_canal()
    {
        var (g, graph, seaW, _, canal) = Canal();
        var p = new Coordinate(10 + 5.0002 * D, 54 + 2 * D); // on the north-running middle stretch
        var snap = graph.SnapToPassage(p, 500)!;
        Assert.Equal(canal, snap.Edge);
        Assert.Equal(1, snap.Segment);
        Assert.InRange(snap.Fraction, 0.4, 0.6);
        Assert.True(snap.DistanceM < 5);
        var path = graph.Route(new(g.C(seaW), seaW), new(p, OnEdge: snap), new())!;
        // sea → canal entrance → along the canal to the bend → up to the projected point → the clicked point
        Assert.Equal(g.C(seaW), path.Points[0]);
        Assert.Contains(new Coordinate(10 + 5 * D, 54), path.Points);
        Assert.Equal(p, path.Points[^1]);
        Assert.Equal(snap.Point, path.Points[^2]);
        Assert.True(path.Fixed[^2]); // canal stretch to the projection is passage geometry
        Assert.False(path.Fixed[^1]); // the leg off the canal is not
        Assert.False(path.Fixed[0]);
    }

    [Fact]
    public void Both_ends_on_the_same_canal_edge_follow_the_stretch_between()
    {
        var (_, graph, _, _, _) = Canal();
        var p = new Coordinate(10 + 2 * D, 54); var q = new Coordinate(10 + 5 * D, 54 + 3 * D);
        var path = graph.Route(new(p, OnEdge: graph.SnapToPassage(p, 500)), new(q, OnEdge: graph.SnapToPassage(q, 500)), new())!;
        Assert.Equal(p, path.Points[0]); Assert.Equal(q, path.Points[^1]);
        Assert.Contains(new Coordinate(10 + 5 * D, 54), path.Points); // round the bend, not across it
        Assert.InRange(StringPull.Length(path.Points), 5200, 5500);
        Assert.Contains(true, path.Fixed);
    }

    [Fact]
    public void Short_passages_are_not_snapped_onto()
    {
        var (_, graph, _, _, _) = Canal();
        Assert.Null(graph.SnapToPassage(new Coordinate(10 - 1 * D, 54), 300)); // next to the sea link only
    }

    [Fact]
    public void End_on_a_passage_edge_also_takes_the_node_in_sight_when_that_is_cheaper()
    {
        // the canal's ends are far round the bend; a water node next to the click, reached straight from the west sea, is the shorter way in
        var g = new G();
        int seaW = g.Node(-2, 0), seaE = g.Node(12, 4), w = g.Node(0, 0), e = g.Node(10, 4), near = g.Node(6, 2);
        g.Edge(seaW, w); g.Edge(e, seaE); g.Edge(seaW, near);
        g.Edge(w, e, interior: [(5, 0), (5, 4)]);
        var graph = g.Build(firstPassage: 2);
        var p = new Coordinate(10 + 5.0002 * D, 54 + 2 * D);
        var snap = graph.SnapToPassage(p, 500)!;
        var viaEdge = graph.Route(new(g.C(seaW), seaW), new(p, OnEdge: snap), new())!;
        Assert.Contains(new Coordinate(10 + 5 * D, 54), viaEdge.Points); // round the canal's bend
        var both = graph.Route(new(g.C(seaW), seaW), new(p, near, snap), new())!;
        Assert.Equal([g.C(seaW), g.C(near), p], both.Points);
        Assert.True(both.Cost < viaEdge.Cost);
    }

    [Fact]
    public void Penalized_link_is_avoided_but_still_used_when_nothing_else_leads_on()
    {
        // a short link straight across a mole (a → b) and the long way round its head (a → head → b)
        var g = new G();
        int a = g.Node(0, 0), b = g.Node(1, 0), head = g.Node(0.5, -3);
        var link = g.Edge(a, b); g.Edge(a, head); g.Edge(head, b);
        var graph = g.Build();
        Assert.DoesNotContain(g.C(head), graph.Route(new(g.C(a), a), new(g.C(b), b), new())!.Points);
        var limits = new SeaGraph.Limits(Penalized: new HashSet<int> { link });
        var round = graph.Route(new(g.C(a), a), new(g.C(b), b), limits)!;
        Assert.Contains(g.C(head), round.Points);
        Assert.DoesNotContain(link, round.Edges!);
        // without the way round the link stays usable (a harbour reached only that way keeps its route)
        var g2 = new G();
        int a2 = g2.Node(0, 0), b2 = g2.Node(1, 0);
        var only = g2.Edge(a2, b2);
        Assert.NotNull(g2.Build().Route(new(g2.C(a2), a2), new(g2.C(b2), b2), new SeaGraph.Limits(Penalized: new HashSet<int> { only })));
    }

    [Fact]
    public void Interior_land_is_a_mole_leading_or_trailing_land_is_a_shore()
    {
        // land as x-ranges along a leg from x = 0 to x = 1 (≈ 650 m at 54° N)
        Func<Coordinate, bool> Land(params (double from, double to)[] r) => c => r.Any(x => (c.X - 10) / D >= x.from && (c.X - 10) / D <= x.to);
        Coordinate a = new(10, 54), b = new(10 + D, 54);
        Assert.True(SeaGraph.CrossesInteriorLand(a, b, Land((0.5, 0.54)))); // a 25 m mole in the middle
        Assert.False(SeaGraph.CrossesInteriorLand(a, b, Land((0, 0.3)))); // a passage end on the shore
        Assert.False(SeaGraph.CrossesInteriorLand(a, b, Land((0.8, 1)))); // the far end in a basin charted as land
        Assert.False(SeaGraph.CrossesInteriorLand(a, b, Land()));
        Assert.True(SeaGraph.CrossesInteriorLand(a, b, Land((0, 0.2), (0.6, 0.62)))); // shore, water, then a mole
    }

    [Fact]
    public void Component_is_bounded()
    {
        var g = new G();
        int a = g.Node(0, 0), b = g.Node(1, 0), c = g.Node(2, 0), lone = g.Node(5, 5);
        g.Edge(a, b); g.Edge(b, c);
        var graph = g.Build();
        Assert.Equal([a, b, c], graph.Component(a, 10)!.Order());
        Assert.Null(graph.Component(a, 2));
        Assert.Equal([lone], graph.Component(lone, 10));
    }
}

public class StringPullTests
{
    private static List<Coordinate> Line(params (double x, double y)[] c) => c.Select(p => new Coordinate(10 + p.x * 0.01, 54 + p.y * 0.01)).ToList();
    private static readonly Func<Coordinate, Coordinate, bool> Open = (_, _) => false;

    [Fact]
    public void Free_dogleg_is_pulled_straight()
    {
        var p = Line((0, 0), (1, 1), (2, 0), (3, 1), (4, 0));
        Assert.Equal([p[0], p[^1]], StringPull.Pull(p, [false, false, false, false], Open));
    }

    [Fact]
    public void Obstacle_keeps_the_bend()
    {
        var p = Line((0, 0), (2, 2), (4, 0));
        // an island at (2, 0.5): any leg below y = 1 at x = 2 is blocked
        bool Island(Coordinate a, Coordinate b)
        {
            if ((a.X - 10.02) * (b.X - 10.02) > 0) return false;
            var t = (10.02 - a.X) / (b.X - a.X); var y = a.Y + (b.Y - a.Y) * t;
            return y < 54.01;
        }
        Assert.Equal(p, StringPull.Pull(p, [false, false], Island));
    }

    [Fact]
    public void Removing_points_never_lengthens_the_route()
    {
        var rnd = new Random(3);
        var p = Enumerable.Range(0, 60).Select(i => new Coordinate(10 + i * 0.01, 54 + rnd.NextDouble() * 0.02)).ToList();
        var fixedSeg = Enumerable.Range(0, 59).Select(i => i is >= 20 and < 30).ToList();
        Func<Coordinate, Coordinate, bool> blocked = (a, b) => Math.Abs(a.X - b.X) > 0.05 && rnd.Next(2) == 0;
        var pulled = StringPull.Pull(p, fixedSeg, blocked);
        Assert.True(StringPull.Length(pulled) <= StringPull.Length(p) + 1e-6);
        Assert.Equal(p[0], pulled[0]); Assert.Equal(p[^1], pulled[^1]);
    }

    [Fact]
    public void Passage_stretch_keeps_its_shape_but_may_be_joined_late_and_left_early()
    {
        // free approach (0..2), a long zig-zag passage (2..12, ~0.6 km steps), free leave (12..14)
        var pts = new List<(double, double)> { (0, 3), (1, 3) };
        for (var i = 2; i <= 12; i++) pts.Add((i, i % 2 == 0 ? 0 : 0.5));
        pts.Add((13, 3)); pts.Add((14, 3));
        var p = Line([.. pts]);
        var fixedSeg = Enumerable.Range(0, p.Count - 1).Select(k => k >= 2 && k < 12).ToList();
        var pulled = StringPull.Pull(p, fixedSeg, Open, trimM: 1500);
        // the inside of the passage (4..10) is untouched, its ends are cut by at most 1.5 km
        for (var i = 5; i <= 9; i++) Assert.Contains(p[i], pulled);
        Assert.DoesNotContain(p[2], pulled);
        Assert.DoesNotContain(p[12], pulled);
        // without trimming the whole passage is ridden from end to end
        var strict = StringPull.Pull(p, fixedSeg, Open, trimM: 0);
        for (var i = 2; i <= 12; i++) Assert.Contains(p[i], strict);
    }

    [Fact]
    public void Short_passage_is_never_skipped_whole()
    {
        var p = Line((0, 0), (1, 1), (2, 1), (3, 0));
        var pulled = StringPull.Pull(p, [false, true, false], Open, trimM: 50_000);
        Assert.Equal(p, pulled);
    }

    [Fact]
    public void PullFlags_marks_kept_passage_segments_only()
    {
        var p = Line((0, 0), (1, 1), (2, 1), (3, 1), (4, 0));
        var (pts, fx) = StringPull.PullFlags(p, [false, true, true, false], Open, trimM: 0);
        Assert.Equal(p, pts); // a short run is never skipped
        Assert.Equal([false, true, true, false], fx);
        var (free, ffx) = StringPull.PullFlags(p, [false, false, false, false], Open);
        Assert.Equal([p[0], p[^1]], free); Assert.Equal([false], ffx);
    }

    // a square island round (5, 0), 2 × 2 grid units
    private static readonly Polygon Island = new GeometryFactory().CreatePolygon(Line((4, -1), (6, -1), (6, 1), (4, 1), (4, -1)).ToArray());
    private static bool HitsIsland(Coordinate a, Coordinate b) => new GeometryFactory().CreateLineString([a, b]).Intersects(Island);

    [Fact]
    public void Tighten_pulls_a_wide_V_in_round_the_obstacle()
    {
        var p = Line((0, 0), (5, -6), (10, 0)); // the graph path swung far south of the island
        var (t, fx) = StringPull.Tighten(p, [false, false], HitsIsland);
        Assert.Equal(p[0], t[0]); Assert.Equal(p[^1], t[^1]);
        for (var i = 1; i < t.Count; i++) Assert.False(HitsIsland(t[i - 1], t[i]));
        Assert.True(StringPull.Length(t) < StringPull.Length(p) - 1000);
        Assert.True(t.Min(c => c.Y) > 54 - 0.03); // close to the island's south side (y = -1), not out at -6
        Assert.Equal(t.Count - 1, fx.Count);
        // a corner the way is free across is dropped
        Assert.Equal([p[0], p[^1]], StringPull.Tighten(p, [false, false], Open).points);
    }

    [Fact]
    public void Tighten_leaves_corners_at_passage_geometry()
    {
        var p = Line((0, 0), (5, -6), (10, 0));
        Assert.Equal(p, StringPull.Tighten(p, [true, false], HitsIsland).points);
    }

    [Fact]
    public void Densify_leaves_passage_segments_alone()
    {
        var p = Line((0, 0), (5, 0), (10, 0));
        var (pts, fx) = StringPull.Densify(p, [false, true], 1000);
        Assert.Equal(p[1], pts[^2]); Assert.True(fx[^1]);
        Assert.True(pts.Count > 4);
        Assert.All(fx.Take(fx.Count - 1), Assert.False);
    }

    [Fact]
    public void InteriorLandM_measures_land_between_water_only()
    {
        Func<Coordinate, bool> island = c => c.X >= 10.01 && c.X <= 10.02; // ~650 m wide at 54 N
        Assert.InRange(SeaGraph.InteriorLandM(new(10.0, 54), new(10.03, 54), island), 600, 700);
        Assert.Equal(0, SeaGraph.InteriorLandM(new(10.015, 54), new(10.03, 54), island)); // starts on land: a quay, not an island
        Assert.Equal(0, SeaGraph.InteriorLandM(new(10.0, 54), new(10.009, 54), island));
    }

    // land samples every 25 m from the link's start, as runs: "W2 L3" = 50 m water, then 75 m land
    private static bool[] Runs(string runs) => runs.Split(' ').SelectMany(r => Enumerable.Repeat(r[0] == 'L', int.Parse(r[1..]))).ToArray();

    [Theory]
    [InlineData("W10", true, true)]
    [InlineData("L20 W10", true, true)]        // harbour on its pier / Siel behind its dike: leading land up to 1.5 km
    [InlineData("L70 W10", false, false)]      // a field
    [InlineData("W10 L2", true, true)]         // node on the shore
    [InlineData("W10 L3", false, false)]
    [InlineData("W4 L1 W4", false, true)]      // Großenbrode: a passage end in the marina is never linked across the mole; a harbour is
    [InlineData("W2 L3 W2", false, true)]      // marina basin, quay, water outside (Fiskehamn, Skagen Sejlklub)
    [InlineData("W1 L5 W1", false, true)]      // inland basin cut out as Water, bank, river passage (Lüneburg, Wittingen)
    [InlineData("W2 L3", false, true)]         // basin, then the river bank up to a passage node on the coastline land (Meppen)
    [InlineData("W2 L8 W2", false, true)]      // 200 m quay or mole
    [InlineData("W2 L9 W2", false, false)]     // 225 m: an island or a field, not the harbour's quay
    [InlineData("W7 L2 W2", false, false)]     // 175 m of water first: open water, the land is an islet
    [InlineData("W2 L2 W3 L2 W2", false, false)] // out of the basin, then across an islet
    [InlineData("W2 L2 W3 L3", false, false)]  // ... or a node 75 m inland
    public void Link_land_crossing_is_classified(string runs, bool strict, bool harbour)
    {
        Assert.Equal(strict, SeaGraph.LinkLandOk(Runs(runs)));
        Assert.Equal(harbour, SeaGraph.LinkLandOk(Runs(runs), harbour: true));
    }
}
