using NetTopologySuite.Geometries;
using Sms.Domain;

namespace Sms.Infrastructure.Routing;

/// <summary>Bridges and locks on passages: a bridge limits the clearance of the edges under it, a lock adds a time-equivalent cost, an opening bridge adds a (smaller) waiting cost and limits nothing unless an open clearance is known.</summary>
public static class Obstacles
{
    public const double MatchM = 60;

    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> edges, IReadOnlyList<(Point point, bool isLock, double? clearanceM)> obstacles, double lockPenaltyM) =>
        Apply(edges, obstacles.Select(o => (o.point, o.isLock ? CorrectionKind.Lock : CorrectionKind.Bridge, o.clearanceM)).ToList(), lockPenaltyM, 0);

    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> edges, IReadOnlyList<(Point point, CorrectionKind kind, double? clearanceM)> obstacles, double lockPenaltyM, double openingPenaltyM)
    {
        if (obstacles.Count == 0) return edges.ToList();
        var result = new List<GraphEdge>(edges.Count);
        foreach (var e in edges)
        {
            var cost = e.Cost; double? clearance = e.ClearanceM;
            var lat = e.Geom.Centroid.Y;
            var tolDeg = MatchM / WaterGraphTiler.MPerDegLat; // rough: the same tolerance in degrees for both axes (longitude is wider in metres)
            foreach (var (point, kind, clearanceM) in obstacles)
            {
                if (e.Geom.EnvelopeInternal.Distance(point.EnvelopeInternal) > tolDeg) continue;
                if (e.Geom.Distance(point) * WaterGraphTiler.MPerDegLat * (Math.Abs(point.X - e.Geom.Centroid.X) > Math.Abs(point.Y - lat) ? Math.Cos(lat * Math.PI / 180) : 1) > MatchM) continue;
                if (kind == CorrectionKind.Lock) cost += lockPenaltyM;
                else if (kind == CorrectionKind.OpeningBridge) cost += openingPenaltyM;
                if (kind != CorrectionKind.Lock && clearanceM is { } cm) clearance = clearance is { } cur ? Math.Min(cur, cm) : cm;
            }
            result.Add(e with { Cost = cost, ClearanceM = clearance });
        }
        return result;
    }
}
