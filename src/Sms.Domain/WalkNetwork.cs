namespace Sms.Domain;

/// <summary>An undirected way segment of the walking network (walk_edge): OSM node ids at both ends, length in metres.</summary>
public readonly record struct WalkEdge(long Id, long Source, long Target, double LengthM);

/// <summary>A point attached to the network: on edge <c>Edge</c> at <c>Fraction</c> (0 = source end, 1 = target end), <c>OffsetM</c> straight metres away.</summary>
public readonly record struct WalkSnap(long Edge, double Fraction, double OffsetM);

/// <summary>A walked piece of an edge, from fraction <c>From</c> to <c>To</c> (To &lt; From = against the edge direction).</summary>
public readonly record struct WalkLeg(long Edge, double From, double To);

/// <summary>Shortest walk: total metres (snap offsets included) and the edge pieces from the start to the destination.</summary>
public sealed record WalkPath(double DistanceM, IReadOnlyList<WalkLeg> Legs);

/// <summary>The walking network of an area as an adjacency list; <see cref="Search"/> runs one Dijkstra from a start point.</summary>
public sealed class WalkNetwork
{
    internal readonly WalkEdge[] Edges;
    internal readonly Dictionary<long, int> EdgeIndex = [];
    internal readonly int[] Src, Tgt;          // node index of each edge's ends
    internal readonly int[] AdjStart, Adj;     // CSR: edges at node n are Adj[AdjStart[n]..AdjStart[n+1]]
    internal int NodeCount => AdjStart.Length - 1;

    public WalkNetwork(IEnumerable<WalkEdge> edges)
    {
        Edges = edges.ToArray();
        var nodes = new Dictionary<long, int>();
        int Node(long id) { if (!nodes.TryGetValue(id, out var n)) nodes[id] = n = nodes.Count; return n; }
        Src = new int[Edges.Length]; Tgt = new int[Edges.Length];
        for (var i = 0; i < Edges.Length; i++) { EdgeIndex[Edges[i].Id] = i; Src[i] = Node(Edges[i].Source); Tgt[i] = Node(Edges[i].Target); }
        AdjStart = new int[nodes.Count + 1];
        foreach (var n in Src.Concat(Tgt)) AdjStart[n + 1]++;
        for (var n = 0; n < nodes.Count; n++) AdjStart[n + 1] += AdjStart[n];
        Adj = new int[Edges.Length * 2];
        var fill = AdjStart[..^1];
        for (var i = 0; i < Edges.Length; i++) { Adj[fill[Src[i]]++] = i; Adj[fill[Tgt[i]]++] = i; }
    }

    public int EdgeCount => Edges.Length;

    /// <summary>Single-source Dijkstra from the start snaps (several candidate edges near the start; their offsets count), up to <paramref name="maxM"/>.</summary>
    public WalkSearch Search(IReadOnlyList<WalkSnap> from, double maxM)
    {
        var dist = new double[NodeCount];
        Array.Fill(dist, double.PositiveInfinity);
        var pred = new int[NodeCount];          // edge index walked into the node, or -1 - start snap index
        var queue = new PriorityQueue<int, double>();
        void Relax(int n, double d, int p) { if (d < dist[n] && d <= maxM) { dist[n] = d; pred[n] = p; queue.Enqueue(n, d); } }
        var starts = new List<(int Edge, WalkSnap Snap)>();
        foreach (var s in from)
        {
            if (!EdgeIndex.TryGetValue(s.Edge, out var e)) continue;
            starts.Add((e, s));
            Relax(Src[e], s.OffsetM + s.Fraction * Edges[e].LengthM, -starts.Count);
            Relax(Tgt[e], s.OffsetM + (1 - s.Fraction) * Edges[e].LengthM, -starts.Count);
        }
        while (queue.TryDequeue(out var n, out var d))
        {
            if (d > dist[n]) continue;
            for (var k = AdjStart[n]; k < AdjStart[n + 1]; k++)
            {
                var e = Adj[k];
                Relax(Src[e] == n ? Tgt[e] : Src[e], d + Edges[e].LengthM, e);
            }
        }
        return new WalkSearch(this, dist, pred, starts, maxM);
    }
}

/// <summary>Result of one Dijkstra: walking distances from the start to every node within the limit, and the paths to any destination snap.</summary>
public sealed class WalkSearch
{
    private readonly WalkNetwork _net;
    private readonly double[] _dist;
    private readonly int[] _pred;
    private readonly List<(int Edge, WalkSnap Snap)> _starts;
    private readonly double _maxM;

    internal WalkSearch(WalkNetwork net, double[] dist, int[] pred, List<(int, WalkSnap)> starts, double maxM) { _net = net; _dist = dist; _pred = pred; _starts = starts; _maxM = maxM; }

    /// <summary>The start had at least one snap on this network.</summary>
    public bool HasStart => _starts.Count > 0;

    /// <summary>Walking metres to the nearest of the destination snaps (offsets included), null when none is reached within the limit.</summary>
    public double? DistanceTo(IReadOnlyList<WalkSnap> to) => Best(to) is { } b ? b.D : null;

    /// <summary>The shortest walk to the destination snaps, null when none is reached within the limit.</summary>
    public WalkPath? PathTo(IReadOnlyList<WalkSnap> to)
    {
        if (Best(to) is not { } b) return null;
        var net = _net;
        var e = b.Edge;
        if (b.Start is { } s) return new WalkPath(b.D, [new WalkLeg(net.Edges[e].Id, _starts[s].Snap.Fraction, b.Snap.Fraction)]);
        var legs = new List<WalkLeg> { new(net.Edges[e].Id, b.ViaSource ? 0 : 1, b.Snap.Fraction) };
        var n = b.ViaSource ? net.Src[e] : net.Tgt[e];
        while (_pred[n] >= 0)
        {
            var p = _pred[n];
            var atSource = net.Src[p] == n;     // walked p into n: from its other end
            legs.Add(new WalkLeg(net.Edges[p].Id, atSource ? 1 : 0, atSource ? 0 : 1));
            n = atSource ? net.Tgt[p] : net.Src[p];
        }
        var (se, ss) = _starts[-_pred[n] - 1];
        legs.Add(new WalkLeg(net.Edges[se].Id, ss.Fraction, net.Src[se] == n ? 0 : 1));
        legs.Reverse();
        return new WalkPath(b.D, legs);
    }

    private (double D, int Edge, WalkSnap Snap, bool ViaSource, int? Start)? Best(IReadOnlyList<WalkSnap> to)
    {
        (double D, int Edge, WalkSnap Snap, bool ViaSource, int? Start)? best = null;
        void Offer(double d, int e, WalkSnap t, bool viaSource, int? start) { if (d <= _maxM && (best is null || d < best.Value.D)) best = (d, e, t, viaSource, start); }
        foreach (var t in to)
        {
            if (!_net.EdgeIndex.TryGetValue(t.Edge, out var e)) continue;
            var len = _net.Edges[e].LengthM;
            Offer(_dist[_net.Src[e]] + t.Fraction * len + t.OffsetM, e, t, true, null);
            Offer(_dist[_net.Tgt[e]] + (1 - t.Fraction) * len + t.OffsetM, e, t, false, null);
            for (var i = 0; i < _starts.Count; i++)   // start and destination on the same edge: straight along it
                if (_starts[i].Edge == e) Offer(_starts[i].Snap.OffsetM + Math.Abs(_starts[i].Snap.Fraction - t.Fraction) * len + t.OffsetM, e, t, true, i);
        }
        return best;
    }
}
