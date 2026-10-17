using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Spec 10 §10.5 step 3: where a marked channel exists, open water next to it is usually shallow or restricted
/// (Wadden Sea, Schlei, estuaries). Grid edges within <c>distanceM</c> of a derived fairway get their cost
/// multiplied, so routes leave the fairway only when the detour is worth it. Passage edges themselves are untouched.
/// </summary>
public static class ChannelPreference
{
    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> gridEdges, IReadOnlyList<LineString> fairways, double distanceM, double factor)
    {
        if (fairways.Count == 0 || factor <= 1 || distanceM <= 0) return gridEdges.ToList();
        var index = new STRtree<(IPreparedGeometry prepared, LineString line)>();
        foreach (var f in fairways) index.Insert(f.EnvelopeInternal, (PreparedGeometryFactory.Prepare(f), f));
        index.Build();
        var degLat = distanceM / WaterGraphTiler.MPerDegLat;
        var result = new List<GraphEdge>(gridEdges.Count);
        foreach (var e in gridEdges)
        {
            var mid = e.Geom.Centroid;
            var cosLat = Math.Cos(mid.Y * Math.PI / 180);
            var query = new Envelope(mid.X - degLat / cosLat, mid.X + degLat / cosLat, mid.Y - degLat, mid.Y + degLat);
            var near = false;
            foreach (var (_, line) in index.Query(query))
            {
                // distance in degrees → metres with the local longitude scale (good enough for a 1–2 km band)
                var d = NetTopologySuite.Operation.Distance.DistanceOp.Distance(line, mid);
                var nearest = NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(line, mid);
                var dx = (nearest[0].X - nearest[1].X) * cosLat; var dy = nearest[0].Y - nearest[1].Y;
                if (Math.Sqrt(dx * dx + dy * dy) * WaterGraphTiler.MPerDegLat <= distanceM) { near = true; break; }
                _ = d;
            }
            result.Add(near ? e with { Cost = e.Cost * factor } : e);
        }
        return result;
    }
}
