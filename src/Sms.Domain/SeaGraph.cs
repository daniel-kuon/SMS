using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace Sms.Domain;

/// <summary>
/// The routing graph of one graph version held in memory (spec 10 §10.2): node coordinates indexed by node id, edges as flat
/// arrays with a CSR adjacency, only the interior points of multi-point edges (contracted canal stretches, fairway chains)
/// kept – a two-point edge is drawn between its nodes. A* with a haversine heuristic scaled by the cheapest cost per metre
/// of any edge (admissible and consistent), the same filters the SQL search had: bridges lower than the air draft are closed,
/// edges charted shallower than the depth limit are closed except at the route ends (anchor nodes, ExemptDeg around the end
/// points), where they cost ×ShallowFactor. Route ends snap either to a node or onto a passage edge, which is then split
/// virtually at the projected point (an end on a canal follows the canal instead of jumping to the nearest junction).
/// Edges between two passage vertices (ids ≥ firstPassageNode) are "fixed": their geometry is kept by the smoothing.
/// Assumed edges (AssumedPassages: a ferry line or buoy gap across the coastline land) are reported per path, or closed by the limits.
/// Edges under a fixed bridge without a charted clearance (UnknownBridge) are passed at Limits.UnknownBridgePenaltyM extra, or closed by Limits.AvoidUnknownBridges.
/// Edges along a scheduled ferry track (Ferry) or through dense sailing traffic (Traffic, EMODnet vessel density) are never uncertain,
/// nor are edges whose EMODnet DTM depth (DtmMin) reaches the minimum depth; the charted band block still applies to all of them.
/// </summary>
public sealed class SeaGraph
{
    public const double ShallowFactor = 3;
    public const double MPerDeg = 111_320.0;
    private readonly double[] _x, _y;
    private readonly int[] _src, _tgt;
    private readonly float[] _cost, _depth, _clear;
    private readonly int[] _geomStart; private readonly double[] _geom;
    private readonly int[] _adjStart, _adj;
    private readonly int _firstPassage;
    private readonly bool[]? _isolated;
    private readonly bool[]? _assumed;
    private readonly float[]? _adjacent;
    private readonly float[]? _floor;
    private readonly bool[]? _unknownBridge;
    private readonly bool[]? _ferry;
    private readonly bool[]? _traffic;
    private readonly float[]? _dtmMin;
    private readonly STRtree<int> _passages = new();

    /// <summary>Lower bound of cost per metre over all edges (the heuristic's scale).</summary>
    public double HeuristicFactor { get; }
    public int NodeSlots => _x.Length;
    public int EdgeCount => _src.Length;
    public int SnapEdgeCount { get; }

    /// <param name="x">Longitude per node id (NaN for unused ids).</param>
    /// <param name="depth">Shallowest charted depth per edge, NaN = none.</param>
    /// <param name="clearance">Bridge clearance per edge, NaN = none.</param>
    /// <param name="geomStart">EdgeCount + 1 offsets into geom (in points): the interior points of edge e are geomStart[e] .. geomStart[e+1]-1.</param>
    /// <param name="geom">Interior points as x,y pairs, oriented from source to target.</param>
    /// <param name="snapMinM">Passage edges at least this long are indexed for snapping route ends onto them.</param>
    /// <param name="assumed">Per edge: an assumed passage (null = none).</param>
    /// <param name="adjacent">Per edge without a charted depth: the floor of the shallowest band next to it in the coastal strip, NaN = none
    /// (<see cref="DepthBands.Effective"/>; null = no such data).</param>
    /// <param name="floor">Per edge of a derived channel ("Enge", no buoys) that the depth limit leaves open: the floor of the band it
    /// runs through (or, without data in the coastal strip, of the band next to it), NaN = none. Counts for <see cref="Floor"/> only.</param>
    /// <param name="unknownBridge">Per edge: passes under a fixed bridge without a charted clearance (null = none).</param>
    /// <param name="ferry">Per edge: runs along a scheduled ferry track, known-navigable water (null = none).</param>
    /// <param name="traffic">Per edge: runs through dense sailing traffic, known-navigable water (null = none).</param>
    /// <param name="dtmMin">Per edge with a band floor: the shallowest DTM depth along it (DtmGrid.MinDepth), NaN = none (null = no such data).</param>
    public SeaGraph(double[] x, double[] y, int[] src, int[] tgt, float[] cost, float[] depth, float[] clearance, int[] geomStart, double[] geom, int firstPassageNode, bool[]? isolated = null, double snapMinM = 1000, bool[]? assumed = null, float[]? adjacent = null, float[]? floor = null, bool[]? unknownBridge = null, bool[]? ferry = null, bool[]? traffic = null, float[]? dtmMin = null)
    {
        _x = x; _y = y; _src = src; _tgt = tgt; _cost = cost; _depth = depth; _clear = clearance; _geomStart = geomStart; _geom = geom; _firstPassage = firstPassageNode; _isolated = isolated; _assumed = assumed; _adjacent = adjacent; _floor = floor; _unknownBridge = unknownBridge; _ferry = ferry; _traffic = traffic; _dtmMin = dtmMin;
        int n = x.Length, m = src.Length;
        _adjStart = new int[n + 1];
        for (var e = 0; e < m; e++) { _adjStart[src[e] + 1]++; _adjStart[tgt[e] + 1]++; }
        for (var i = 0; i < n; i++) _adjStart[i + 1] += _adjStart[i];
        _adj = new int[2 * m];
        var fill = new int[n]; Array.Copy(_adjStart, fill, n);
        for (var e = 0; e < m; e++) { _adj[fill[src[e]]++] = e; _adj[fill[tgt[e]]++] = e; }
        var f = double.MaxValue;
        for (var e = 0; e < m; e++)
        {
            var d = Haversine(y[src[e]], x[src[e]], y[tgt[e]], x[tgt[e]]);
            // a zero-length link (cost 0) whose two nodes differ by float noise (1e-14 degrees) made the factor 0: A* ran as Dijkstra
            if (d > 1e-3) f = Math.Min(f, cost[e] / d);
        }
        HeuristicFactor = f == double.MaxValue ? 0 : Math.Max(0, f * (1 - 1e-6)); // float costs: a hair below the true minimum
        var count = 0;
        for (var e = 0; e < m; e++)
        {
            if (!IsPassage(e) || (isolated is not null && isolated[src[e]])) continue;
            var cs = Coords(e);
            var len = 0.0; for (var k = 1; k < cs.Length; k++) len += Haversine(cs[k - 1].Y, cs[k - 1].X, cs[k].Y, cs[k].X);
            if (len < snapMinM) continue;
            var env = new Envelope(); foreach (var c in cs) env.ExpandToInclude(c);
            _passages.Insert(env, e); count++;
        }
        _passages.Build();
        SnapEdgeCount = count;
    }

    /// <summary>Approximate bytes held (arrays and the snap index).</summary>
    public long ApproxBytes => (long)_x.Length * 16 + (_isolated?.Length ?? 0) + (long)_src.Length * (4 * 6) + (_adjacent?.Length ?? 0) * 4L + (_floor?.Length ?? 0) * 4L + (_unknownBridge?.Length ?? 0) + (_ferry?.Length ?? 0) + (_traffic?.Length ?? 0) + (_dtmMin?.Length ?? 0) * 4L + _geom.Length * 8L + _adjStart.Length * 4L + _adj.Length * 4L + SnapEdgeCount * 100L;

    public bool IsPassage(int e) => _src[e] >= _firstPassage && _tgt[e] >= _firstPassage;
    public bool IsAssumed(int e) => _assumed is not null && _assumed[e];
    public bool UnknownBridge(int e) => _unknownBridge is not null && _unknownBridge[e];
    /// <summary>The edge runs along a scheduled ferry track: known-navigable water, never <see cref="Uncertain"/>.</summary>
    public bool Ferry(int e) => _ferry is not null && _ferry[e];
    /// <summary>The edge runs through dense sailing traffic: known-navigable water, never <see cref="Uncertain"/>.</summary>
    public bool Traffic(int e) => _traffic is not null && _traffic[e];
    /// <summary>Known-navigable water (ferry track or dense sailing traffic).</summary>
    public bool Known(int e) => Ferry(e) || Traffic(e);
    /// <summary>The shallowest DTM depth along the edge, NaN = none.</summary>
    public float DtmMin(int e) => _dtmMin?[e] ?? float.NaN;
    /// <summary>A straight link between a passage vertex and a water node (the builder lets these cross up to 50 m of land, a mole or pier).</summary>
    public bool IsLink(int e) => (_src[e] >= _firstPassage) != (_tgt[e] >= _firstPassage) && _geomStart[e] == _geomStart[e + 1];
    /// <summary>A node id of a passage vertex (passage edges and links hook into them).</summary>
    public bool IsPassageNode(int id) => id >= _firstPassage;
    /// <summary>A straight edge between two passage vertices that is not an assumed passage: a gap link between two channel pieces
    /// (the builder links ends up to 200 m apart without a land test) or a two-point channel or fairway piece.</summary>
    public bool IsStraightPassage(int e) => IsPassage(e) && _geomStart[e] == _geomStart[e + 1] && !IsAssumed(e);
    /// <summary>The depth the edge counts with (charted, else the adjacent band's floor; NaN = unknown).</summary>
    public float Depth(int e) => DepthBands.Effective(_depth[e], _adjacent?[e] ?? float.NaN);
    /// <summary>The band floor the edge runs through, for the "definitely deep enough" test: its depth, or the floor of a derived
    /// channel the depth limit leaves open (the shallower of both); NaN = unknown (open water without data, buoyed fairways, canals).</summary>
    public float Floor(int e)
    {
        var d = Depth(e); var c = _floor?[e] ?? float.NaN;
        return float.IsNaN(d) ? c : float.IsNaN(c) ? d : Math.Min(d, c);
    }
    /// <summary>The edge's band floor lies below the minimum depth: not definitely deep enough (a buoyed channel, open water without data,
    /// known-navigable water never is, nor water the DTM shows deep enough all along).</summary>
    public bool Uncertain(int e, double minDepthM) => !Known(e) && Floor(e) is var f && !float.IsNaN(f) && f < (float)minDepthM && !(DtmMin(e) >= (float)minDepthM);
    /// <summary>Bridge clearance of the edge, NaN = none.</summary>
    public float Clearance(int e) => _clear[e];
    /// <summary>The edge has no charted depth of its own and counts with the band next to it.</summary>
    public bool DepthInferred(int e) => float.IsNaN(_depth[e]) && !float.IsNaN(Depth(e));
    public int Source(int e) => _src[e];
    public int Target(int e) => _tgt[e];
    /// <summary>The edges at a node.</summary>
    public IEnumerable<int> EdgesAt(int node) { for (var i = _adjStart[node]; i < _adjStart[node + 1]; i++) yield return _adj[i]; }
    public bool IsIsolated(int node) => _isolated is not null && node < _isolated.Length && _isolated[node];

    /// <summary>The nodes connected to <paramref name="start"/> (breadth first), null when there are more than <paramref name="max"/>.</summary>
    public List<int>? Component(int start, int max)
    {
        var seen = new HashSet<int> { start }; var queue = new Queue<int>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var u))
            for (var i = _adjStart[u]; i < _adjStart[u + 1]; i++)
            {
                var e = _adj[i]; var v = _src[e] == u ? _tgt[e] : _src[e];
                if (!seen.Add(v)) continue;
                if (seen.Count > max) return null;
                queue.Enqueue(v);
            }
        return seen.ToList();
    }

    /// <summary>
    /// True when the straight leg a→b crosses land with water on both sides of it (a mole, a spit), sampled every stepM: land at the
    /// start or end only (a passage end on the shore, a harbour basin the coastline counts as land) does not count.
    /// </summary>
    public static bool CrossesInteriorLand(Coordinate a, Coordinate b, Func<Coordinate, bool> onLand, double stepM = 5)
    {
        var n = Math.Max(2, (int)Math.Ceiling(Haversine(a.Y, a.X, b.Y, b.X) / stepM));
        var water = false; var landAfterWater = false;
        for (var k = 0; k <= n; k++)
        {
            var land = onLand(new Coordinate(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n));
            if (!land) { if (landAfterWater) return true; water = true; }
            else if (water) landAfterWater = true;
        }
        return false;
    }

    /// <summary>Metres of land at the start of the leg a → b before the first sample in water (0 when a lies in water, the leg's length when
    /// no sample is water): the shore a passage end stands on – or the isthmus a link jumps when the passage ends in inland water the
    /// coastline counts as land (the Baaber Bek chain in the Selliner See, linked 1.4 km across Sellin to the Baltic).</summary>
    public static double LeadingLandM(Coordinate a, Coordinate b, Func<Coordinate, bool> onLand, double stepM = 5)
    {
        var len = Haversine(a.Y, a.X, b.Y, b.X);
        var n = Math.Max(2, (int)Math.Ceiling(len / stepM));
        var k = 0;
        while (k <= n && onLand(new Coordinate(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n))) k++;
        return Math.Min(k, n) * len / n;
    }

    /// <summary>The longest stretch (metres, sampled every stepM) of the leg a→b that lies on land with water before and after it: an
    /// island the leg jumps over. Land at the start or end only counts nothing.</summary>
    public static double InteriorLandM(Coordinate a, Coordinate b, Func<Coordinate, bool> onLand, double stepM = 25)
    {
        var n = Math.Max(2, (int)Math.Ceiling(Haversine(a.Y, a.X, b.Y, b.X) / stepM));
        var step = Haversine(a.Y, a.X, b.Y, b.X) / n;
        var water = false; var run = 0; var longest = 0;
        for (var k = 0; k <= n; k++)
        {
            if (onLand(new Coordinate(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n))) { if (water) run++; }
            else { longest = Math.Max(longest, run); run = 0; water = true; }
        }
        return longest * step;
    }

    /// <summary>Whether a link into the water graph may cross the land of <paramref name="land"/> (flags of the points every
    /// <paramref name="stepM"/> from the link's start, ends excluded). Land at the start up to 1.5 km is fine (a harbour on its pier, a
    /// Siel behind its dike, the harbour's own basin or creek counted as land), so is up to 50 m at the far end (a node on the shore).
    /// Land between water is a mole, breakwater or spit: never crossed (Großenbrode: channel ends in the marina were linked across the
    /// mole to the water outside; 150 m let the Schleimünde links cross the Lotseninsel, so routes cut through the island).
    /// <paramref name="harbour"/>: a harbour's access link (never a transit edge), tried only when no link passes the strict test. The
    /// harbour point may lie in its own basin (a marina pool behind its quay or mole, an imported inner Water polygon): water up to
    /// <see cref="HarbourBasinM"/> and then up to <see cref="HarbourMoleM"/> of land (the quay, mole or river bank) count as the start;
    /// land after that is an island or a field as for any link (60 harbours, v64, lost their access when the 300 m shortcut went).</summary>
    public static bool LinkLandOk(IReadOnlyList<bool> land, double stepM = 25, bool harbour = false)
    {
        var leading = 0; while (leading < land.Count && land[leading]) leading++;
        var maxLeadingM = 1500.0;
        if (harbour && leading == 0)
        {
            var basin = 0; while (basin < land.Count && !land[basin]) basin++;
            if (basin < land.Count && basin * stepM <= HarbourBasinM)
            {
                leading = basin; while (leading < land.Count && land[leading]) leading++;
                maxLeadingM = basin * stepM + HarbourMoleM;
            }
        }
        var trailing = 0; while (trailing < land.Count - leading && land[land.Count - 1 - trailing]) trailing++;
        for (var k = leading; k < land.Count - trailing; k++) if (land[k]) return false;
        return trailing * stepM <= 50 && leading * stepM <= maxLeadingM;
    }
    public const double HarbourBasinM = 150, HarbourMoleM = 200;
    public Coordinate Node(int id) => new(_x[id], _y[id]);

    /// <summary>The edge's geometry from source to target.</summary>
    public Coordinate[] Coords(int e)
    {
        int a = _geomStart[e], b = _geomStart[e + 1];
        var cs = new Coordinate[b - a + 2];
        cs[0] = Node(_src[e]);
        for (var k = a; k < b; k++) cs[k - a + 1] = new Coordinate(_geom[2 * k], _geom[2 * k + 1]);
        cs[^1] = Node(_tgt[e]);
        return cs;
    }

    /// <param name="NoAssumed">Assumed passages closed (Routing:AssumedPassages off, or the derive endpoint testing what the graph connects without them).</param>
    /// <param name="Penalized">Edges that cost PenaltyM extra: links found crossing a mole, used only where nothing else leads on.</param>
    /// <param name="MinDepthM">The ship's minimum depth: an edge whose band floor (<see cref="Floor"/>) lies below it is not definitely deep
    /// enough – the band's middle may pass (DepthLimitM), its floor does not – and costs ×UncertainFactor, so a buoyed channel (no floor)
    /// wins wherever one leads; water whose floor reaches it, open water without data and the channel itself cost as before.</param>
    /// <param name="StrictDepth">Such uncertain edges are closed outside the harbour approach (the router's first search; it falls back
    /// to the ×UncertainFactor rule only when nothing else leads there).</param>
    /// <param name="AvoidUnknownBridges">Edges under a fixed bridge without a charted clearance are closed.</param>
    /// <param name="UnknownBridgePenaltyM">Extra cost of an edge under a fixed bridge without a charted clearance (when passed).</param>
    public sealed record Limits(double? AirDraftM = null, double? DepthLimitM = null, double ExemptDeg = 0, bool NoAssumed = false, IReadOnlySet<int>? Penalized = null, double PenaltyM = 20_000, double? MinDepthM = null, double UncertainFactor = 1, bool StrictDepth = false, bool AvoidUnknownBridges = false, double UnknownBridgePenaltyM = 0);
    /// <summary>A route end projected onto a passage edge: on segment Segment of its geometry, at Fraction of its length.</summary>
    public sealed record Snap(int Edge, int Segment, double Fraction, Coordinate Point, double DistanceM);
    /// <summary>A route end: the clicked point and either a node or a snap onto an edge.</summary>
    /// <param name="OnLand">The clicked point when it lay on land and the end moved to the nearest navigable water (Point).</param>
    public sealed record End(Coordinate Point, int Node = -1, Snap? OnEdge = null, Coordinate? OnLand = null);
    /// <summary>The route: points from the start point to the end point; Fixed[i] says whether the segment Points[i] → Points[i+1] is passage geometry.
    /// Assumed: the midpoint of every assumed passage edge the route uses (where to warn).</summary>
    public sealed record Path(List<Coordinate> Points, List<bool> Fixed, double Cost, int Settled, List<Coordinate>? Assumed = null, List<int>? Edges = null);

    /// <summary>Nearest point on an indexed passage edge within maxM (local metric frame), or null.</summary>
    public Snap? SnapToPassage(Coordinate p, double maxM)
    {
        var cos = Math.Max(0.2, Math.Cos(p.Y * Math.PI / 180));
        var dLat = maxM / MPerDeg; var dLng = dLat / cos;
        Snap? best = null;
        foreach (var e in _passages.Query(new Envelope(p.X - dLng, p.X + dLng, p.Y - dLat, p.Y + dLat)))
        {
            var cs = Coords(e);
            for (var s = 0; s < cs.Length - 1; s++)
            {
                var (q, _, d) = Project(p, cs[s], cs[s + 1], cos);
                if (d <= maxM && (best is null || d < best.DistanceM)) best = new Snap(e, s, 0, q, d);
            }
        }
        if (best is null) return null;
        // fraction of the edge's length up to the projected point (local metric, like the cost)
        var c2 = Coords(best.Edge); double total = 0, upTo = 0;
        for (var s = 0; s < c2.Length - 1; s++)
        {
            var len = LocalM(c2[s], c2[s + 1], cos);
            if (s < best.Segment) upTo += len; else if (s == best.Segment) upTo += LocalM(c2[s], best.Point, cos);
            total += len;
        }
        return best with { Fraction = total > 0 ? upTo / total : 0 };
    }

    private static (Coordinate q, double t, double m) Project(Coordinate p, Coordinate a, Coordinate b, double cos)
    {
        double ax = a.X * cos, ay = a.Y, bx = b.X * cos, by = b.Y, px = p.X * cos, py = p.Y;
        double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
        var t = l2 == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / l2, 0, 1);
        var q = new Coordinate(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        return (q, t, LocalM(p, q, cos));
    }

    private static double LocalM(Coordinate a, Coordinate b, double cos)
    {
        var dx = (b.X - a.X) * cos; var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy) * MPerDeg;
    }

    /// <summary>Cost of an edge under the limits, +∞ when closed.</summary>
    private double EdgeCost(int e, Limits l, End from, End to)
    {
        if (l.NoAssumed && IsAssumed(e)) return double.PositiveInfinity;
        var cl = _clear[e];
        if (l.AirDraftM is { } ad && !float.IsNaN(cl) && cl < ad) return double.PositiveInfinity;
        if (l.AvoidUnknownBridges && UnknownBridge(e)) return double.PositiveInfinity;
        double c = _cost[e]; var d = Depth(e);
        if (l.Penalized is { } pen && pen.Contains(e)) c += l.PenaltyM;
        // compared as floats: the depths are stored as floats, and (float)1.8288 < 1.8288 closed the band floor itself (the whole
        // 1.83-3.66 m band was shut in A* for every draft, only the pull and the harbour approach used it)
        if (l.DepthLimitM is { } lim && !float.IsNaN(d) && d < (float)lim)
        {
            if (!Exempt(e, l, from, to)) return double.PositiveInfinity;
            c *= ShallowFactor;
        }
        // "Tonnen first": water whose band floor lies below the minimum depth is uncertain (×UncertainFactor, on top of ShallowFactor in the
        // harbour approach), so the buoyed channel is left only where the water is definitely deep enough; strict: closed outside the approach
        if (l.MinDepthM is { } md && l.UncertainFactor != 1 && Uncertain(e, md))
        {
            if (l.StrictDepth && !Exempt(e, l, from, to)) return double.PositiveInfinity;
            c *= l.UncertainFactor;
        }
        return UnknownBridge(e) ? c + l.UnknownBridgePenaltyM : c;
    }

    /// <summary>The edges of a path the strict search closes (uncertain water outside the harbour approach): where a fallback route warns.</summary>
    public List<int> UncertainEdges(IEnumerable<int> edges, Limits l, End from, End to) =>
        l.MinDepthM is { } md ? edges.Distinct().Where(e => Uncertain(e, md) && !Exempt(e, l, from, to)).ToList() : [];

    // the harbour approach: edges at the end nodes or within ExemptDeg (planar degrees, like ST_DWithin on 4326) of an end point
    private bool Exempt(int e, Limits l, End from, End to)
    {
        foreach (var end in (ReadOnlySpan<End>)[from, to])
        {
            if (end.OnEdge?.Edge == e) return true;
            var s = end.OnEdge is { } sn ? _src[sn.Edge] : end.Node; var t = end.OnEdge is { } tn ? _tgt[tn.Edge] : end.Node;
            if (_src[e] == s || _src[e] == t || _tgt[e] == s || _tgt[e] == t) return true;
            if (end.Node >= 0 && (_src[e] == end.Node || _tgt[e] == end.Node)) return true;
        }
        if (l.ExemptDeg <= 0) return false;
        var cs = Coords(e);
        for (var k = 0; k < cs.Length - 1; k++)
            if (PlanarDist(from.Point, cs[k], cs[k + 1]) <= l.ExemptDeg || PlanarDist(to.Point, cs[k], cs[k + 1]) <= l.ExemptDeg) return true;
        return false;
    }

    private static double PlanarDist(Coordinate p, Coordinate a, Coordinate b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
        var t = l2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2, 0, 1);
        double qx = a.X + dx * t - p.X, qy = a.Y + dy * t - p.Y;
        return Math.Sqrt(qx * qx + qy * qy);
    }

    private readonly record struct Anchor(int Node, double Cost, Coordinate[] Leg, bool OnEdge = false); // Leg: from the end's projected point to the node

    private Anchor[] Anchors(End end, Limits l, End from, End to)
    {
        if (end.OnEdge is not { } s) return end.Node >= 0 ? [new Anchor(end.Node, 0, [Node(end.Node)])] : [];
        // an end on a passage edge with a node in sight as well (End.Node): both, the node at the cost of its straight leg – a click on
        // a fairway is reached across open water when the edge's own ends lie far off (Neustadt: the edge's south end 2.6 km away gave a V)
        var node = end.Node >= 0 ? [new Anchor(end.Node, Haversine(end.Point.Y, end.Point.X, _y[end.Node], _x[end.Node]), [Node(end.Node)])] : Array.Empty<Anchor>();
        var c = EdgeCost(s.Edge, l, from, to);
        if (double.IsPositiveInfinity(c)) return node;
        var cs = Coords(s.Edge);
        var back = new List<Coordinate> { s.Point }; for (var k = s.Segment; k >= 0; k--) back.Add(cs[k]);
        var fwd = new List<Coordinate> { s.Point }; for (var k = s.Segment + 1; k < cs.Length; k++) fwd.Add(cs[k]);
        return [new Anchor(_src[s.Edge], s.Fraction * c, back.ToArray(), true), new Anchor(_tgt[s.Edge], (1 - s.Fraction) * c, fwd.ToArray(), true), .. node];
    }

    /// <summary>Cheapest route between two ends, or null when none exists under the limits.</summary>
    public Path? Route(End from, End to, Limits limits)
    {
        var starts = Anchors(from, limits, from, to); var goals = Anchors(to, limits, from, to);
        var n = _x.Length;
        var g = new double[n]; Array.Fill(g, double.PositiveInfinity);
        var via = new int[n]; var done = new bool[n];
        var pq = new PriorityQueue<int, double>();
        double H(int v)
        {
            var h = double.PositiveInfinity;
            foreach (var a in goals) h = Math.Min(h, Haversine(_y[v], _x[v], _y[a.Node], _x[a.Node]));
            return HeuristicFactor * h;
        }
        for (var k = 0; k < starts.Length; k++)
        {
            var a = starts[k];
            if (a.Cost < g[a.Node]) { g[a.Node] = a.Cost; via[a.Node] = -1 - k; pq.Enqueue(a.Node, a.Cost + H(a.Node)); }
        }
        double best = double.PositiveInfinity; int bestNode = -1, bestGoal = -1, settled = 0;
        while (goals.Length > 0 && pq.TryDequeue(out var u, out var f))
        {
            if (f >= best) break;
            if (done[u]) continue;
            done[u] = true; settled++;
            for (var k = 0; k < goals.Length; k++)
                if (goals[k].Node == u && g[u] + goals[k].Cost < best) { best = g[u] + goals[k].Cost; bestNode = u; bestGoal = k; }
            for (var i = _adjStart[u]; i < _adjStart[u + 1]; i++)
            {
                var e = _adj[i]; var v = _src[e] == u ? _tgt[e] : _src[e];
                if (done[v]) continue;
                var c = EdgeCost(e, limits, from, to);
                if (double.IsPositiveInfinity(c)) continue;
                var nd = g[u] + c;
                if (nd < g[v]) { g[v] = nd; via[v] = e; pq.Enqueue(v, nd + H(v)); }
            }
        }
        // both ends on the same edge: the stretch between the two projections may beat any route through the graph
        if (from.OnEdge is { } s1 && to.OnEdge is { } s2 && s1.Edge == s2.Edge)
        {
            var c = Math.Abs(s1.Fraction - s2.Fraction) * EdgeCost(s1.Edge, limits, from, to);
            if (c < best)
            {
                var cs = Coords(s1.Edge);
                var pts = new List<Coordinate> { from.Point }; var fx = new List<bool>();
                void Add(Coordinate p, bool fixd) { if (pts[^1].Equals2D(p)) return; pts.Add(p); fx.Add(fixd); }
                Add(s1.Point, false);
                if (s1.Fraction <= s2.Fraction) for (var k = s1.Segment + 1; k <= s2.Segment; k++) Add(cs[k], true);
                else for (var k = s1.Segment; k > s2.Segment; k--) Add(cs[k], true);
                Add(s2.Point, true); Add(to.Point, false);
                return new Path(pts, fx, c, settled, AssumedMidpoints([s1.Edge]), [s1.Edge]);
            }
        }
        if (bestNode < 0) return null;
        var edges = new List<int>();
        var w = bestNode;
        while (via[w] >= 0) { var e = via[w]; edges.Add(e); w = _src[e] == w ? _tgt[e] : _src[e]; }
        edges.Reverse();
        var start = starts[-1 - via[w]];
        var points = new List<Coordinate> { from.Point }; var fixedSeg = new List<bool>();
        void Push(Coordinate p, bool fixd) { if (points[^1].Equals2D(p)) return; points.Add(p); fixedSeg.Add(fixd); }
        for (var k = 0; k < start.Leg.Length; k++) Push(start.Leg[k], k > 0);
        var at = start.Node;
        foreach (var e in edges)
        {
            var cs = Coords(e); var fixd = IsPassage(e);
            if (_src[e] != at) Array.Reverse(cs);
            foreach (var c in cs) Push(c, fixd);
            at = _src[e] == at ? _tgt[e] : _src[e];
        }
        var goal = goals[bestGoal];
        for (var k = goal.Leg.Length - 1; k >= 0; k--) Push(goal.Leg[k], k < goal.Leg.Length - 1);
        Push(to.Point, false);
        var used = new List<int>(edges);
        if (from.OnEdge is { } fs && start.OnEdge) used.Add(fs.Edge);
        if (to.OnEdge is { } ts && goal.OnEdge) used.Add(ts.Edge);
        return new Path(points, fixedSeg, best, settled, AssumedMidpoints(used), used);
    }

    private List<Coordinate> AssumedMidpoints(IEnumerable<int> edges) => edges.Distinct().Where(IsAssumed).Select(e => AssumedPassages.Midpoint(Coords(e))).ToList();

    public static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6_371_008.8;
        var dLat = (lat2 - lat1) * Math.PI / 180; var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }
}

/// <summary>
/// String pulling over a route whose segments are free (grid legs, links) or fixed (passage geometry): the shortest chain of
/// the route's own points whose legs are not blocked ("removing a point can't make the route longer"). A dynamic programme
/// over the points (each point is reached from the earlier point that gives the shortest total, cheapest candidates tested
/// first) – a greedy farthest-visible pull ran into dead ends: from just before the Fehmarnsund bridge the farthest visible
/// point lay up at the Kiel lighthouse, while the leg from just after the bridge led straight to the Förde.
/// Fixed stretches: no leg cuts across the inside of a stretch (its shape is kept), and none skips a whole stretch; a stretch
/// may be joined late and left early by up to trimM along it (a corner cut into a buoyed channel), each within its own half
/// so at least one of its segments stays. Legs reach at most maxLegM along the route (bounds the work; a longer straight
/// run keeps a collinear vertex).
/// </summary>
public static class StringPull
{
    /// <summary>Intermediate points every ≤ stepM on the free segments (bend candidates for the pulling).</summary>
    public static (List<Coordinate> points, List<bool> fixedSeg) Densify(IReadOnlyList<Coordinate> path, IReadOnlyList<bool> fixedSeg, double stepM)
    {
        var pts = new List<Coordinate> { path[0] }; var fx = new List<bool>();
        for (var i = 1; i < path.Count; i++)
        {
            var a = path[i - 1]; var b = path[i];
            var n = fixedSeg[i - 1] ? 1 : Math.Max(1, (int)Math.Ceiling(SeaGraph.Haversine(a.Y, a.X, b.Y, b.X) / stepM));
            for (var k = 1; k < n; k++) { pts.Add(new Coordinate(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n)); fx.Add(false); }
            pts.Add(b); fx.Add(fixedSeg[i - 1]);
        }
        return (pts, fx);
    }

    /// <summary>
    /// "No worse than before" budgets for the pull: per measure (metres per leg of what the route must not get more of, with a slack of
    /// about one sample), a leg p → q is blocked when it measures more than the path's own legs between p and q. Keyed by the path's point
    /// objects (the pull hands them back); a leg whose end is not a path point (a chamfer's, a detour's new point) is not judged here.
    /// </summary>
    public static Func<Coordinate, Coordinate, bool> NoWorse(IReadOnlyList<Coordinate> pts, IReadOnlyList<(Func<Coordinate, Coordinate, double> m, double slack)> measures)
    {
        if (measures.Count == 0 || pts.Count == 0) return (_, _) => false;
        var ats = measures.Select(x =>
        {
            var at = new Dictionary<Coordinate, double>(ReferenceEqualityComparer.Instance); var sum = 0.0; at[pts[0]] = 0;
            for (var k = 1; k < pts.Count; k++) { sum += x.m(pts[k - 1], pts[k]); at[pts[k]] = sum; }
            return at;
        }).ToArray();
        return (p, q) =>
        {
            for (var i = 0; i < ats.Length; i++)
                if (ats[i].TryGetValue(p, out var a) && ats[i].TryGetValue(q, out var b) && measures[i].m(p, q) > Math.Abs(b - a) + measures[i].slack) return true;
            return false;
        };
    }

    public static double Length(IReadOnlyList<Coordinate> p)
    {
        var m = 0.0;
        for (var i = 1; i < p.Count; i++) m += SeaGraph.Haversine(p[i - 1].Y, p[i - 1].X, p[i].Y, p[i].X);
        return m;
    }

    /// <param name="maxTests">Blocked tests per point at most (the cheapest allowed candidates): bounds the work along a winding coast.</param>
    /// <param name="shapeM">A leg may skip passage points that all lie within this of its line (spur, back-step, overshoot); ≤ 0 = off.</param>
    /// <param name="followBlocked">Blocks such a shape-following skip in addition to <paramref name="blocked"/> (piers: the passage ran between them).</param>
    public static List<Coordinate> Pull(IReadOnlyList<Coordinate> p, IReadOnlyList<bool> fixedSeg, Func<Coordinate, Coordinate, bool> blocked, double trimM = 3000, double maxLegM = 30_000, int maxTests = 64, double shapeM = 60, Func<Coordinate, Coordinate, bool>? followBlocked = null) =>
        PullFlags(p, fixedSeg, blocked, trimM, maxLegM, maxTests, shapeM, followBlocked).points;

    /// <summary>
    /// Corner cutting after the pull: the pull can only bend at the path's own points, so where the graph path swung wide (a long
    /// visibility edge out to a channel's outer end, Neustadt) the pulled route keeps a V whose apex lies far off the obstacle it goes
    /// round. Each free corner u→v→w is replaced by the chamfer a→b (a on v→u, b on v→w at the same fraction, the largest fraction
    /// whose leg a→b is not blocked, binary search) – both remaining pieces are parts of the old legs, so the route stays valid and
    /// never gets longer. A leg u→w that is free drops v outright. Repeated passes tighten the chamfers round the obstacle. Corners at
    /// passage geometry (fixed segments) stay.
    /// </summary>
    /// <param name="minGainM">A chamfer that saves less than this is not made (the corner is tight enough).</param>
    /// <param name="clearM">A chamfer keeps this far off what blocks it: the same leg shifted by clearM away from v must be free too (a leg
    /// found by bisection would touch the coast).</param>
    /// <param name="worse">Optional, as in <see cref="Slide"/>: per leg, metres of what the route must not get more of; the chamfer (or the leg that drops v)
    /// may not exceed the corner's two old legs in any of them.</param>
    public static (List<Coordinate> points, List<bool> fixedSeg) Tighten(IReadOnlyList<Coordinate> points, IReadOnlyList<bool> fixedSeg, Func<Coordinate, Coordinate, bool> blocked, int passes = 4, double minGainM = 10, double clearM = 20, IReadOnlyList<Func<Coordinate, Coordinate, double>>? worse = null)
    {
        var p = points.ToList(); var fx = fixedSeg.ToList();
        static double D(Coordinate a, Coordinate b) => SeaGraph.Haversine(a.Y, a.X, b.Y, b.X);
        static Coordinate Lerp(Coordinate a, Coordinate b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        for (var pass = 0; pass < passes; pass++)
        {
            var changed = false;
            for (var i = 1; i < p.Count - 1; i++)
            {
                if (fx[i - 1] || fx[i]) continue;
                Coordinate u = p[i - 1], v = p[i], w = p[i + 1];
                if (D(u, v) + D(v, w) - D(u, w) < minGainM) continue;
                var budget = worse?.Select(m => m(u, v) + m(v, w) + 1).ToArray();
                bool Worse(Coordinate a, Coordinate b) => budget is not null && worse!.Where((m, k) => m(a, b) > budget[k]).Any();
                if (!blocked(u, w) && !Worse(u, w)) { p.RemoveAt(i); fx.RemoveAt(i); i--; changed = true; continue; }
                // the unit normal of u→w pointing away from v (local metric frame), as a shift in degrees
                var cos = Math.Cos(v.Y * Math.PI / 180);
                double nx = -(w.Y - u.Y), ny = (w.X - u.X) * cos; var nl = Math.Sqrt(nx * nx + ny * ny);
                if (nl == 0) continue;
                nx /= nl; ny /= nl;
                if (nx * (v.X - u.X) * cos + ny * (v.Y - u.Y) > 0) { nx = -nx; ny = -ny; }
                double sx = nx * clearM / (SeaGraph.MPerDeg * cos), sy = ny * clearM / SeaGraph.MPerDeg;
                bool Free(Coordinate a, Coordinate b) => !blocked(a, b) && !Worse(a, b) && !blocked(new Coordinate(a.X + sx, a.Y + sy), new Coordinate(b.X + sx, b.Y + sy));
                double lo = 0, hi = 1;
                for (var k = 0; k < 12; k++) { var mid = (lo + hi) / 2; if (Free(Lerp(v, u, mid), Lerp(v, w, mid))) lo = mid; else hi = mid; }
                var a = Lerp(v, u, lo); var b = Lerp(v, w, lo);
                if (D(a, v) + D(v, b) - D(a, b) < minGainM) continue;
                p[i] = a; p.Insert(i + 1, b); fx.Insert(i, false);
                i++; changed = true;
            }
            if (!changed) break;
        }
        return (p, fx);
    }

    /// <summary>
    /// Vertex sliding after the pull and the chamfers: a corner a→b→c whose two legs are free is moved towards the chord (b' on the
    /// line from b to its foot on a–c) as far as both new legs stay unblocked (binary search), or else back along one of its legs
    /// (b' on b→a or b→c: the shortened leg is a part of the old one, only the other is tested); a corner at the end of a passage
    /// run slides back along the run's last segment only (the run is left early, by at most trimM and within its own half), so the
    /// route stays on the passage line. The length is convex in b', so every move shortens the route; a vertex that reaches the
    /// chord is dropped (collinear). Passes repeat while a pass gains at least minGainM. Like Tighten's chamfer, a moved vertex
    /// keeps clearM short of what blocks it (the leg one step further must be free too).
    /// </summary>
    /// <param name="blocked">The leg test of the final route (land, depth, zone, cardinals, piers, coast clearance).</param>
    /// <param name="worse">Optional: per leg, metres of what the route must not get more of (uncharted water, an uncertain band): the two moved
    /// legs together may not exceed the corner's two old legs in any of them (no worse than before, where the pull's blockers allow it).</param>
    public static (List<Coordinate> points, List<bool> fixedSeg) Slide(IReadOnlyList<Coordinate> points, IReadOnlyList<bool> fixedSeg, Func<Coordinate, Coordinate, bool> blocked, IReadOnlyList<Func<Coordinate, Coordinate, double>>? worse = null, double trimM = 3000, int passes = 8, double minGainM = 1, double clearM = 20)
    {
        var p = points.ToList(); var fx = fixedSeg.ToList();
        static double D(Coordinate a, Coordinate b) => SeaGraph.Haversine(a.Y, a.X, b.Y, b.X);
        static Coordinate Lerp(Coordinate a, Coordinate b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        // per segment: the original length of the passage run it belongs to (0 = free) and what has been cut off the run at this
        // segment's outer end so far (the trim allowance is of the original run, not of what is left of it)
        var run0 = new List<double>(new double[fx.Count]); var cut = new List<double>(new double[fx.Count]);
        for (var k = 0; k < fx.Count; k++)
        {
            if (!fx[k] || (k > 0 && fx[k - 1])) continue;
            var e = k; var len = 0.0;
            while (e < fx.Count && fx[e]) { len += D(p[e], p[e + 1]); e++; }
            for (var q = k; q < e; q++) run0[q] = len;
        }
        // a vertex is retried in a later pass only when it or a neighbour moved (what held it still holds)
        var dirty = new List<bool>(Enumerable.Repeat(true, p.Count));
        for (var pass = 0; pass < passes; pass++)
        {
            var gained = 0.0;
            for (var i = 1; i < p.Count - 1; i++)
            {
                bool fa = fx[i - 1], fc = fx[i];
                if (fa && fc || !dirty[i]) continue;
                dirty[i] = false;
                Coordinate a = p[i - 1], b = p[i], c = p[i + 1];
                var before = D(a, b) + D(b, c);
                if (before - D(a, c) < minGainM) continue;
                // a move is tested leg by leg; the no-worse budgets are checked on the whole corner (both new legs) once a move is found
                var budget = worse?.Select(w => w(a, b) + w(b, c) + 1).ToArray();
                bool Worse(Coordinate q) => budget is not null && worse!.Where((w, k) => w(a, q) + w(q, c) > budget[k]).Any();
                // candidates: target, the farthest parameter allowed, which legs the move changes
                var cands = new List<(Coordinate target, double maxT, bool testA, bool testC)>();
                if (!fa && !fc)
                {
                    var cos = Math.Cos(b.Y * Math.PI / 180);
                    double dx = (c.X - a.X) * cos, dy = c.Y - a.Y, l2 = dx * dx + dy * dy;
                    var t = l2 == 0 ? 0 : Math.Clamp(((b.X - a.X) * cos * dx + (b.Y - a.Y) * dy) / l2, 0, 1);
                    cands.Add((Lerp(a, c, t), 1, true, true));
                    cands.Add((a, 1, false, true)); // tried only when the move towards the chord is blocked at once
                    cands.Add((c, 1, true, false));
                }
                else if (fa) cands.Add((a, Math.Min(1, (Math.Min(trimM, run0[i - 1] / 2) - cut[i - 1]) / D(a, b)), false, true));
                else cands.Add((c, Math.Min(1, (Math.Min(trimM, run0[i] / 2) - cut[i]) / D(b, c)), true, false));
                Coordinate? best = null; var bestGain = minGainM;
                foreach (var (target, maxT, testA, testC) in cands)
                {
                    if (best is not null) break;
                    var reach = D(b, target);
                    if (reach < 1 || maxT <= 0) continue;
                    bool Free(double t) { var q = Lerp(b, target, t); return (!testA || !blocked(a, q)) && (!testC || !blocked(q, c)) && !Worse(q); }
                    // bisection on the guarded position (one step further must be free), the result verified once
                    var step = Math.Min(maxT, clearM / reach);
                    double lo = 0, hi = maxT;
                    if (Free(maxT)) lo = maxT;
                    else
                    {
                        for (var k = 0; k < 8; k++) { var mid = (lo + hi) / 2; if (Free(Math.Min(maxT, mid + step))) lo = mid; else hi = mid; }
                        if (lo > 0 && !Free(lo)) lo = 0;
                    }
                    if (lo == 0) continue;
                    var q = Lerp(b, target, lo); var gain = before - D(a, q) - D(q, c);
                    if (gain > bestGain) { best = q; bestGain = gain; }
                }
                if (best is null) continue;
                gained += bestGain;
                dirty[i - 1] = dirty[i] = dirty[i + 1] = true;
                // on the chord (or merged into a neighbour): the vertex goes, the one leg left is free
                if (D(a, best) < 1 || D(best, c) < 1 || D(a, best) + D(best, c) - D(a, c) < 0.01)
                {
                    p.RemoveAt(i); fx.RemoveAt(i); run0.RemoveAt(i); cut.RemoveAt(i); dirty.RemoveAt(i);
                    fx[i - 1] = false; run0[i - 1] = 0; cut[i - 1] = 0; i--; continue;
                }
                if (fa) cut[i - 1] += D(b, best); else if (fc) cut[i] += D(b, best);
                p[i] = best;
            }
            if (gained < minGainM) break;
        }
        return (p, fx);
    }

    /// <summary>Pull, also returning per result segment whether it is passage geometry (a kept segment of a fixed run; a leg that skips points is free).</summary>
    public static (List<Coordinate> points, List<bool> fixedSeg) PullFlags(IReadOnlyList<Coordinate> p, IReadOnlyList<bool> fixedSeg, Func<Coordinate, Coordinate, bool> blocked, double trimM = 3000, double maxLegM = 30_000, int maxTests = 64, double shapeM = 60, Func<Coordinate, Coordinate, bool>? followBlocked = null)
    {
        var n = p.Count;
        if (n < 3) return (p.ToList(), fixedSeg.ToList());
        // runs of fixed segments: first and last point per run, run id per point (a run's end points belong to it)
        var run = new int[n]; var runS = new List<int>(); var runE = new List<int>();
        Array.Fill(run, -1);
        for (var k = 0; k < n - 1; k++)
        {
            if (!fixedSeg[k]) continue;
            if (k == 0 || !fixedSeg[k - 1]) { runS.Add(k); runE.Add(k + 1); } else runE[^1] = k + 1;
            run[k] = run[k + 1] = runS.Count - 1;
        }
        var along = new double[n];
        for (var k = 1; k < n; k++) along[k] = along[k - 1] + SeaGraph.Haversine(p[k - 1].Y, p[k - 1].X, p[k].Y, p[k].X);
        double Trim(int r) => Math.Min(trimM, (along[runE[r]] - along[runS[r]]) / 2);
        // a leg i → j (j > i + 1) keeps the shape of every run it touches - or follows it: every skipped point lies within shapeM of
        // the leg's line (a spur or back-step inside a fairway line, a hop that overshoots the harbour and comes back: the shape is kept
        // within shapeM, only the detour goes)
        bool Follows(int i, int j)
        {
            if (shapeM <= 0) return false;
            var cos = Math.Cos(p[i].Y * Math.PI / 180);
            double ax = p[i].X * cos, ay = p[i].Y, dx = p[j].X * cos - ax, dy = p[j].Y - ay, l2 = dx * dx + dy * dy;
            if (l2 == 0) return false;
            var len = Math.Sqrt(l2) * SeaGraph.MPerDeg;
            for (var k = i + 1; k < j; k++)
            {
                double px = p[k].X * cos - ax, py = p[k].Y - ay;
                var t = (px * dx + py * dy) / l2;
                if (t < -0.05 || t * len > len + trimM) return false; // behind the start, or beyond the end by more than the trim
                if (Math.Abs(px * dy - py * dx) / Math.Sqrt(l2) * SeaGraph.MPerDeg > shapeM) return false;
            }
            return true;
        }
        bool Keeps(int i, int j)
        {
            var ri = run[i]; var rj = run[j];
            if (ri >= 0 && ri == rj) return false; // across the inside of a run
            if (ri >= 0 && runE[ri] > i && along[runE[ri]] - along[i] >= Trim(ri)) return false; // leaves a run too early
            if (rj >= 0 && runS[rj] < j && along[j] - along[runS[rj]] >= Trim(rj)) return false; // joins a run too late
            for (var r = ri >= 0 ? ri + 1 : 0; r < runS.Count && runS[r] < j; r++)
                if (runS[r] > i && runE[r] < j) return false; // skips a whole run
            return true;
        }
        var best = new double[n]; var prev = new int[n];
        var cands = new List<(double cost, int i)>();
        for (var j = 1; j < n; j++)
        {
            best[j] = best[j - 1] + along[j] - along[j - 1]; prev[j] = j - 1;
            cands.Clear();
            for (var i = j - 2; i >= 0 && along[j] - along[i] <= maxLegM; i--)
            {
                var c = best[i] + SeaGraph.Haversine(p[i].Y, p[i].X, p[j].Y, p[j].X);
                if (c < best[j] - 1e-6) cands.Add((c, i));
            }
            cands.Sort((a, b) => a.cost.CompareTo(b.cost));
            var tests = 0;
            foreach (var (c, i) in cands)
            {
                var keeps = Keeps(i, j);
                if (!keeps && !Follows(i, j)) continue;
                if (!blocked(p[i], p[j]) && (keeps || followBlocked is null || !followBlocked(p[i], p[j]))) { best[j] = c; prev[j] = i; break; }
                if (++tests >= maxTests) break;
            }
        }
        var result = new List<Coordinate>(); var fx = new List<bool>();
        for (var k = n - 1; k > 0; k = prev[k]) { result.Add(p[k]); fx.Add(prev[k] == k - 1 && fixedSeg[k - 1]); }
        result.Add(p[0]);
        result.Reverse(); fx.Reverse();
        return (result, fx);
    }
}
