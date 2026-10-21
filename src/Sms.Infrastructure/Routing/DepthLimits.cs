using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Gives every grid edge the shallowest depth area it crosses; routes for a ship then skip edges below its band floor (DepthBands).
/// An edge that crosses no depth area but runs all along within <see cref="CoastalM"/> of land - the coastal strip the raster leaves
/// out because it is shallower than its first band - gets the floor of the shallowest depth area within <see cref="AdjacentM"/> as
/// AdjacentDepthM, and the router treats it as that band (DepthBands.Effective). Such an edge without a band in reach gets
/// FloorM = <see cref="UnknownCoastalFloorM"/>: no limit, but not definitely deep enough either (SeaGraph.Floor). Open water without data stays unlimited.
/// An edge along a ferry track (FerryCorridors) is flagged Ferry: it keeps the charted band it crosses (the band block), but is never
/// "not definitely deep enough" - no AdjacentDepthM, no FloorM.
/// </summary>
public static class DepthLimits
{
    public const double AdjacentM = 200, CoastalM = 500;
    /// <summary>Floor of coastal water without data and without a band in reach: depth unknown, at most the raster's first band - below every minimum depth.</summary>
    public const double UnknownCoastalFloorM = 0;

    /// <param name="nearLand">(point, metres) → land lies within that distance; null = no coastal rule (passage edges).</param>
    /// <param name="buoyed">Fairway areas (derived from the buoys, or charted): an edge at least half inside one is buoyed water and gets no
    /// depth of its own, like a passage edge – the strict depth rule leaves it open.</param>
    /// <param name="ferry">Ferry tracks: an edge along one gets its charted band and the Ferry flag, no coastal floor.</param>
    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> edges, IReadOnlyList<(Geometry area, double minDepthM)> areas, Func<Coordinate, double, bool>? nearLand = null, IReadOnlyList<Polygon>? buoyed = null, FerryCorridors? ferry = null)
    {
        if (areas.Count == 0 && ferry is not { Count: > 0 }) return edges.ToList();
        var index = new STRtree<(IPreparedGeometry prepared, double depth)>();
        foreach (var (area, depth) in areas) index.Insert(area.EnvelopeInternal, (PreparedGeometryFactory.Prepare(area), depth));
        index.Build();
        var fairways = new STRtree<IPreparedGeometry>();
        foreach (var b in buoyed ?? []) fairways.Insert(b.EnvelopeInternal, PreparedGeometryFactory.Prepare(b));
        fairways.Build();
        var result = new List<GraphEdge>(edges.Count);
        foreach (var e in edges)
        {
            if (buoyed is { Count: > 0 } && fairways.Query(e.Geom.EnvelopeInternal) is { Count: > 0 } inside && ChannelPreference.ShareInside(e.Geom, inside) >= 0.5) { result.Add(e); continue; }
            double? min = null;
            foreach (var (prepared, depth) in index.Query(e.Geom.EnvelopeInternal))
                if (prepared.Intersects(e.Geom) && (min is null || depth < min)) min = depth;
            if (ferry is { Count: > 0 } && ferry.Along(e.Geom)) { result.Add(e with { MinDepthM = min ?? e.MinDepthM, Ferry = true }); continue; }
            result.Add(min is not null ? e with { MinDepthM = min } : nearLand is null || !Coastal(e.Geom, nearLand) ? e
                : Adjacent(e.Geom, index) is { } adj ? e with { AdjacentDepthM = adj } : e with { FloorM = UnknownCoastalFloorM });
        }
        return result;
    }

    /// <summary>The edge runs all along within CoastalM of land.</summary>
    private static bool Coastal(LineString edge, Func<Coordinate, double, bool> nearLand)
    {
        var cs = edge.Coordinates;
        if (!nearLand(cs[0], CoastalM) || !nearLand(cs[^1], CoastalM)) return false;
        for (var k = 1; k < cs.Length; k++)
        {
            var n = (int)Math.Ceiling(RoutingGraphBuilder.Haversine(cs[k - 1].Y, cs[k - 1].X, cs[k].Y, cs[k].X) / 250);
            for (var i = 1; i < n; i++)
                if (!nearLand(new Coordinate(cs[k - 1].X + (cs[k].X - cs[k - 1].X) * i / n, cs[k - 1].Y + (cs[k].Y - cs[k - 1].Y) * i / n), CoastalM)) return false;
        }
        return true;
    }

    // ponytail: distances in planar degrees of latitude, so east-west reach is cos φ shorter (500 m → ~290 m at 55° N); a metric frame if that matters
    private static double? Adjacent(LineString edge, STRtree<(IPreparedGeometry prepared, double depth)> index)
    {
        var d = AdjacentM / WaterGraphTiler.MPerDegLat;
        var env = edge.EnvelopeInternal.Copy(); env.ExpandBy(d);
        double? min = null;
        foreach (var (prepared, depth) in index.Query(env))
            if ((min is null || depth < min) && prepared.Geometry.IsWithinDistance(edge, d)) min = depth;
        return min;
    }
}
