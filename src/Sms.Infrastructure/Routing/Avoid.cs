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
        var index = new STRtree<(IPreparedGeometry prepared, Geometry geom)>();
        foreach (var a in areas) if (!a.IsEmpty) index.Insert(a.EnvelopeInternal, (PreparedGeometryFactory.Prepare(a), a));
        index.Build();
        var result = new List<GraphEdge>(edges.Count);
        foreach (var e in edges)
        {
            var inside = 0.0;
            foreach (var (prepared, geom) in index.Query(e.Geom.EnvelopeInternal))
            {
                if (!prepared.Intersects(e.Geom)) continue;
                var part = geom.Intersection(e.Geom);
                if (!part.IsEmpty) inside += part.Length;
            }
            if (inside <= 0) { result.Add(e); continue; }
            var share = Math.Min(1, inside / Math.Max(e.Geom.Length, 1e-9));
            result.Add(e with { Cost = e.Cost * (1 + share * (factor - 1)) });
        }
        return result;
    }
}
