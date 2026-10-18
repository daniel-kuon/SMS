using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace Sms.Infrastructure.Routing;

/// <summary>In-memory nearest-node lookup for the build (one SQL probe per link made the passage phase take minutes).</summary>
public sealed class NodeIndex
{
    private readonly STRtree<GraphNode> _tree = new();
    public static NodeIndex Build(IReadOnlyList<GraphNode> nodes)
    {
        var i = new NodeIndex();
        foreach (var n in nodes) i._tree.Insert(new Envelope(n.X, n.X, n.Y, n.Y), n);
        i._tree.Build();
        return i;
    }

    /// <summary>The nearest nodes within maxM (ids up to maxId), closest first.</summary>
    public List<GraphNode> Nearest(Coordinate p, double maxM, int limit, long maxId = long.MaxValue)
    {
        var dLat = maxM / WaterGraphTiler.MPerDegLat; var dLng = dLat / Math.Max(0.1, Math.Cos(p.Y * Math.PI / 180));
        return _tree.Query(new Envelope(p.X - dLng, p.X + dLng, p.Y - dLat, p.Y + dLat))
            .Where(n => n.Id <= maxId)
            .Select(n => (n, d: RoutingGraphBuilder.Haversine(p.Y, p.X, n.Y, n.X)))
            .Where(x => x.d <= maxM).OrderBy(x => x.d).Take(limit).Select(x => x.n).ToList();
    }
}
