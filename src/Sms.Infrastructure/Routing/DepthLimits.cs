using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Gives every grid edge the shallowest depth area it crosses; routes for a ship then skip edges below its band floor (DepthBands).
/// An edge that crosses no depth area but runs all along within <see cref="CoastalM"/> of land - the coastal strip the raster leaves
/// out because it is shallower than its first band - gets the floor of the shallowest depth area within <see cref="AdjacentM"/> as
/// AdjacentDepthM, and the router treats it as that band (DepthBands.Effective). Open water without data stays unlimited.
/// </summary>
public static class DepthLimits
{
    public const double AdjacentM = 200, CoastalM = 500;

    /// <param name="nearLand">(point, metres) → land lies within that distance; null = no coastal rule (passage edges).</param>
    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> edges, IReadOnlyList<(Geometry area, double minDepthM)> areas, Func<Coordinate, double, bool>? nearLand = null)
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
            result.Add(min is not null ? e with { MinDepthM = min } : nearLand is not null && Adjacent(e.Geom, index, nearLand) is { } adj ? e with { AdjacentDepthM = adj } : e);
        }
        return result;
    }

    // ponytail: distances in planar degrees of latitude, so east-west reach is cos φ shorter (500 m → ~290 m at 55° N); a metric frame if that matters
    private static double? Adjacent(LineString edge, STRtree<(IPreparedGeometry prepared, double depth)> index, Func<Coordinate, double, bool> nearLand)
    {
        var cs = edge.Coordinates;
        if (!nearLand(cs[0], CoastalM) || !nearLand(cs[^1], CoastalM)) return null;
        for (var k = 1; k < cs.Length; k++)
        {
            var n = (int)Math.Ceiling(RoutingGraphBuilder.Haversine(cs[k - 1].Y, cs[k - 1].X, cs[k].Y, cs[k].X) / 250);
            for (var i = 1; i < n; i++)
                if (!nearLand(new Coordinate(cs[k - 1].X + (cs[k].X - cs[k - 1].X) * i / n, cs[k - 1].Y + (cs[k].Y - cs[k - 1].Y) * i / n), CoastalM)) return null;
        }
        var d = AdjacentM / WaterGraphTiler.MPerDegLat;
        var env = edge.EnvelopeInternal.Copy(); env.ExpandBy(d);
        double? min = null;
        foreach (var (prepared, depth) in index.Query(env))
            if ((min is null || depth < min) && prepared.Geometry.IsWithinDistance(edge, d)) min = depth;
        return min;
    }
}
