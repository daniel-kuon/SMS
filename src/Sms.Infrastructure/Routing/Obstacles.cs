using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using Sms.Domain;

namespace Sms.Infrastructure.Routing;

/// <summary>Bridges and locks on passages: a bridge limits the clearance of the edges under it, a lock adds a time-equivalent cost, an opening bridge adds a (smaller) waiting cost and limits nothing unless an open clearance is known.
/// A fixed bridge without a charted clearance (a road bridge over a sound, Öknöström) marks its edges UnknownBridge.</summary>
public static class Obstacles
{
    public const double MatchM = 60;
    /// <summary>A charted bridge point (seamark:type=bridge node) within this distance of a bridge line marks a navigation span of that line.</summary>
    public const double SpanMatchM = 250;
    /// <summary>Half-width of the passable window around a charted span point; the rest of the line is impassable.</summary>
    public const double SpanWindowM = 500;

    /// <summary>
    /// Long bridges (Great Belt, Fehmarnsund, Øresund) may only be passed at their charted navigation spans: OSM has the bridge
    /// as a way with one clearance and the spans as seamark bridge nodes with their own clearance. A line with span points
    /// is replaced by the window pieces around the spans (the span's clearance) and the remainder (clearance 0, impassable).
    /// Lines without charted spans are kept whole.
    /// </summary>
    public static List<(Geometry geom, CorrectionKind kind, double? clearanceM)> WithSpans(IReadOnlyList<(Geometry geom, CorrectionKind kind, double? clearanceM)> obstacles, double spanMatchM = SpanMatchM, double windowM = SpanWindowM)
    {
        var points = obstacles.Where(o => o.geom is Point && o.kind != CorrectionKind.Lock).ToList();
        if (points.Count == 0) return obstacles.ToList();
        var pointIndex = PointIndex(points);
        var result = new List<(Geometry, CorrectionKind, double?)>(obstacles.Count);
        foreach (var o in Chains(obstacles))
        {
            if (o.geom is not LineString line || o.kind == CorrectionKind.Lock) { result.Add(o); continue; }
            var lat = line.Centroid.Y;
            var cos = Math.Cos(lat * Math.PI / 180);
            var matchDeg = spanMatchM / WaterGraphTiler.MPerDegLat; var windowDeg = windowM / WaterGraphTiler.MPerDegLat;
            var scaled = (LineString)line.Copy(); scaled.Apply(new Scale(cos));
            var windows = new List<Geometry>();
            foreach (var p in Near(pointIndex, line, spanMatchM))
            {
                var sp = (Point)p.geom.Copy(); sp.Apply(new Scale(cos));
                if (sp.Distance(scaled) > matchDeg) continue;
                var window = scaled.Intersection(sp.Buffer(windowDeg));
                if (window.IsEmpty) continue;
                windows.Add(sp.Buffer(windowDeg));
                // overlay results share Coordinate objects with their input: copy before scaling back, or the scaled source line
                // (and every other piece) is corrupted into a line spanning tens of degrees
                window = window.Copy(); window.Apply(new Scale(1 / cos));
                result.Add((window, o.kind, p.clearanceM ?? o.clearanceM));
            }
            if (windows.Count == 0) { result.Add(o); continue; }
            var rest = scaled.Difference(new GeometryCollection(windows.ToArray()).Union()).Copy();
            if (!rest.IsEmpty) { rest.Apply(new Scale(1 / cos)); result.Add((rest, o.kind, 0)); }
        }
        return result;
    }

    /// <summary>OSM maps a long bridge as several ways (main span, approach viaducts) sharing end nodes: lines of the same kind and
    /// clearance that meet end to end are merged into one chain, so the charted spans of the main span govern the approaches too.</summary>
    public static List<(Geometry geom, CorrectionKind kind, double? clearanceM)> Chains(IReadOnlyList<(Geometry geom, CorrectionKind kind, double? clearanceM)> obstacles)
    {
        var result = obstacles.Where(o => o.geom is not LineString || o.kind == CorrectionKind.Lock).ToList();
        foreach (var group in obstacles.Where(o => o.geom is LineString && o.kind != CorrectionKind.Lock).GroupBy(o => (o.kind, o.clearanceM)))
        {
            var merger = new NetTopologySuite.Operation.Linemerge.LineMerger();
            merger.Add(group.Select(o => o.geom).ToList());
            foreach (var line in merger.GetMergedLineStrings().OfType<LineString>()) result.Add((line, group.Key.kind, group.Key.clearanceM));
        }
        return result;
    }

    /// <summary>The charted span points of the bridge lines (visibility anchors: a leg through a span starts or ends there).</summary>
    public static List<Coordinate> SpanPoints(IReadOnlyList<(Geometry geom, CorrectionKind kind, double? clearanceM)> obstacles, double spanMatchM = SpanMatchM)
    {
        var lines = new STRtree<Geometry>();
        foreach (var o in Chains(obstacles).Where(o => o.geom is LineString)) lines.Insert(o.geom.EnvelopeInternal, o.geom);
        var result = new List<Coordinate>();
        foreach (var p in obstacles.Where(o => o.geom is Point && o.kind != CorrectionKind.Lock))
        {
            var cos = Math.Cos(p.geom.Coordinate.Y * Math.PI / 180);
            foreach (var l in Near(lines, p.geom, spanMatchM))
            {
                var sp = (Point)p.geom.Copy(); sp.Apply(new Scale(cos)); var sl = l.Copy(); sl.Apply(new Scale(cos));
                if (sp.Distance(sl) <= spanMatchM / WaterGraphTiler.MPerDegLat) { result.Add(p.geom.Coordinate); break; }
            }
        }
        return result;
    }

    // candidates within m of g by envelope (the exact test follows): every line against every point took 19 s for 6 000 points and 1 900
    // lines, and the import of road bridges without a clearance adds thousands of lines
    private static STRtree<(Geometry geom, CorrectionKind kind, double? clearanceM)> PointIndex(List<(Geometry geom, CorrectionKind kind, double? clearanceM)> points)
    {
        var index = new STRtree<(Geometry, CorrectionKind, double?)>();
        foreach (var p in points) index.Insert(p.geom.EnvelopeInternal, p);
        index.Build();
        return index;
    }
    private static IList<T> Near<T>(STRtree<T> index, Geometry g, double m)
    {
        var env = new Envelope(g.EnvelopeInternal); var d = m / WaterGraphTiler.MPerDegLat;
        env.ExpandBy(d / Math.Max(0.1, Math.Cos(Math.Max(Math.Abs(env.MinY), Math.Abs(env.MaxY)) * Math.PI / 180)), d);
        return index.Query(env);
    }

    private sealed class Scale(double sx) : ICoordinateSequenceFilter
    {
        public bool Done => false; public bool GeometryChanged => true;
        public void Filter(CoordinateSequence seq, int i) => seq.SetX(i, seq.GetX(i) * sx);
    }

    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> edges, IReadOnlyList<(Point point, bool isLock, double? clearanceM)> obstacles, double lockPenaltyM) =>
        Apply(edges, obstacles.Select(o => (o.point, o.isLock ? CorrectionKind.Lock : CorrectionKind.Bridge, o.clearanceM)).ToList(), lockPenaltyM, 0);

    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> edges, IReadOnlyList<(Point point, CorrectionKind kind, double? clearanceM)> obstacles, double lockPenaltyM, double openingPenaltyM, double matchM = MatchM) =>
        Apply(edges, obstacles.Select(o => ((Geometry)o.point, o.kind, o.clearanceM)).ToList(), lockPenaltyM, openingPenaltyM, matchM);

    /// <summary>Point obstacles match edges within matchM; line obstacles (bridge ways) match every edge that intersects the line.
    /// An edge whose clearance ends up at 0 (the impassable part of a bridge with charted spans) is dropped. A fixed bridge without a clearance
    /// marks the edges it crosses UnknownBridge; as a point only within MatchM (a span point, a charted bridge node), not within the area
    /// mode's wide matchM – a point without a clearance closes nothing, and a 1.5 km circle of "unknown" open water would; not where the edge has
    /// a known clearance.</summary>
    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> edges, IReadOnlyList<(Geometry geom, CorrectionKind kind, double? clearanceM)> obstacles, double lockPenaltyM, double openingPenaltyM, double matchM)
    {
        if (obstacles.Count == 0) return edges.ToList();
        var tolDeg = matchM / WaterGraphTiler.MPerDegLat; // rough: the same tolerance in degrees for both axes (longitude is wider in metres)
        var index = new STRtree<(Geometry geom, CorrectionKind kind, double? clearanceM)>();
        foreach (var o in obstacles) index.Insert(o.geom.EnvelopeInternal, o);
        index.Build();
        var result = new List<GraphEdge>(edges.Count);
        foreach (var e in edges)
        {
            var cost = e.Cost; double? clearance = e.ClearanceM; var unknown = e.UnknownBridge;
            var lat = e.Geom.Centroid.Y;
            var near = new Envelope(e.Geom.EnvelopeInternal); near.ExpandBy(tolDeg);
            foreach (var (geom, kind, clearanceM) in index.Query(near))
            {
                if (e.Geom.EnvelopeInternal.Distance(geom.EnvelopeInternal) > tolDeg) continue;
                var unknownHere = kind == CorrectionKind.Bridge && clearanceM is null;
                if (geom is Point point)
                {
                    var m = e.Geom.Distance(point) * WaterGraphTiler.MPerDegLat * (Math.Abs(point.X - e.Geom.Centroid.X) > Math.Abs(point.Y - lat) ? Math.Cos(lat * Math.PI / 180) : 1);
                    if (m > (unknownHere ? Math.Min(matchM, MatchM) : matchM)) continue;
                }
                else if (!geom.Intersects(e.Geom)) continue;
                unknown |= unknownHere;
                if (kind == CorrectionKind.Lock) cost += lockPenaltyM;
                else if (kind == CorrectionKind.OpeningBridge) cost += openingPenaltyM;
                if (kind != CorrectionKind.Lock && clearanceM is { } cm) clearance = clearance is { } cur ? Math.Min(cur, cm) : cm;
            }
            if (clearance is <= 0) continue;
            // a known clearance on the edge names the bridge: the unknown piece is the same crossing drawn twice (Rügendamm: a 40 m charted
            // point and the OSM road lines without one; the bridge warnings merge them the same way). ponytail: a visibility leg that passes a
            // charted bridge and another, unknown one loses the unknown; per-crossing matching if that shows up
            result.Add(e with { Cost = cost, ClearanceM = clearance, UnknownBridge = unknown && clearance is null });
        }
        return result;
    }
}
