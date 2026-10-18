using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Warning areas (firing ranges, restricted-entry zones): passable outside their active hours, so not land – but a route should
/// only cross one when the way around is long. The part of an edge inside such an area costs ×factor.
/// </summary>
public static class Avoid
{
    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> edges, IReadOnlyList<Geometry> areas, double factor)
    {
        if (areas.Count == 0 || factor <= 1) return edges.ToList();
        var index = new STRtree<IPreparedGeometry>();
        foreach (var a in areas) if (!a.IsEmpty) index.Insert(a.EnvelopeInternal, PreparedGeometryFactory.Prepare(a));
        index.Build();
        var result = new List<GraphEdge>(edges.Count);
        foreach (var e in edges)
        {
            var hits = index.Query(e.Geom.EnvelopeInternal);
            var share = hits.Count == 0 ? 0 : ChannelPreference.ShareInside(e.Geom, hits);
            result.Add(share <= 0 ? e : e with { Cost = e.Cost * (1 + share * (factor - 1)) });
        }
        return result;
    }
}
