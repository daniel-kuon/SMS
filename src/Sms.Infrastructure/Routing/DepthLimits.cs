using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;

namespace Sms.Infrastructure.Routing;

/// <summary>Gives every grid edge the shallowest depth area it crosses; routes for a ship with draft d then skip edges with MinDepthM &lt; d + margin.</summary>
public static class DepthLimits
{
    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> edges, IReadOnlyList<(Geometry area, double minDepthM)> areas)
    {
        if (areas.Count == 0) return edges.ToList();
        var index = new STRtree<(IPreparedGeometry prepared, double depth)>();
        foreach (var (area, depth) in areas) index.Insert(area.EnvelopeInternal, (PreparedGeometryFactory.Prepare(area), depth));
        index.Build();
        var result = new List<GraphEdge>(edges.Count);
        foreach (var e in edges)
        {
            double? min = null;
            foreach (var (prepared, depth) in index.Query(e.Geom.EnvelopeInternal))
                if (prepared.Intersects(e.Geom) && (min is null || depth < min)) min = depth;
            result.Add(min is null ? e : e with { MinDepthM = min });
        }
        return result;
    }
}
