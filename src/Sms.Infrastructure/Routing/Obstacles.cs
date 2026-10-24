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
    /// Lines without charted spans are kept whole. A span point is consumed by its window: it stays as a disc of MatchM (an edge that
    /// passes the point keeps its clearance even where the line is drawn short or over the next arm), never as a point - in area mode a
    /// point matches edges within 1.5 km and carried its clearance to the bridge's other spans (the Storstrøm's unknown northern span got
    /// the main span's 26 m and lost its unknown flag). A point without a line of its kind keeps its match.
    /// A line without a clearance that continues a known one (<see cref="ClosedInChains"/>: an approach viaduct, the fixed spans next to a
    /// flap) is closed (clearance 0); a bridge unknown along its whole length stays passable with a warning.
    /// </summary>
    public static List<(Geometry geom, CorrectionKind kind, double? clearanceM)> WithSpans(IReadOnlyList<(Geometry geom, CorrectionKind kind, double? clearanceM)> obstacles, double spanMatchM = SpanMatchM, double windowM = SpanWindowM)
    {
        var points = obstacles.Where(o => o.geom is Point && o.kind != CorrectionKind.Lock).ToList();
        var pointIndex = PointIndex(points);
        var result = new List<(Geometry geom, CorrectionKind kind, double? clearanceM)>(obstacles.Count);
        var consumed = new HashSet<Geometry>(ReferenceEqualityComparer.Instance);
        var members = new List<(LineString line, bool known, int unknownAt)>();
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
                if (p.kind == o.kind) consumed.Add(p.geom); // an opening bridge's point keeps its waiting penalty
                // overlay results share Coordinate objects with their input: copy before scaling back, or the scaled source line
                // (and every other piece) is corrupted into a line spanning tens of degrees
                window = window.Copy(); window.Apply(new Scale(1 / cos));
                result.Add((window, o.kind, p.clearanceM ?? o.clearanceM));
            }
            if (windows.Count == 0)
            {
                var unknown = o.kind == CorrectionKind.Bridge && o.clearanceM is null;
                members.Add((line, !unknown, unknown ? result.Count : -1)); result.Add(o); continue;
            }
            // a line known only by its span points closes its own rest, not the ways it continues: Essingeleden's charted 17.5 m span
            // (Lilla Essingen side) would close its unknown southern ways over Mälaren's channel to Stockholm (Gröndal)
            members.Add((line, o.kind == CorrectionKind.OpeningBridge || o.clearanceM is not null, -1));
            var rest = scaled.Difference(new GeometryCollection(windows.ToArray()).Union()).Copy();
            if (!rest.IsEmpty) { rest.Apply(new Scale(1 / cos)); result.Add((rest, o.kind, 0)); }
        }
        for (var i = 0; i < result.Count; i++)
            if (result[i].geom is Point pt && consumed.Contains(pt))
            {
                var cos = Math.Cos(pt.Y * Math.PI / 180);
                var sp = (Point)pt.Copy(); sp.Apply(new Scale(cos));
                var disc = sp.Buffer(MatchM / WaterGraphTiler.MPerDegLat); disc.Apply(new Scale(1 / cos));
                result[i] = (disc, result[i].kind, result[i].clearanceM);
            }
        foreach (var (i, closed, kept) in ClosedInChains(members))
        {
            var kind = result[i].kind;
            result[i] = (closed, kind, 0);
            if (!kept.IsEmpty) result.Add((kept, kind, null));
        }
        return result;
    }

    /// <summary>Bridge ways in series join within this distance end to end; a known line lying along an unknown one (within it) is part of it.</summary>
    public const double ChainJoinM = 20;
    /// <summary>... and continue each other's direction within this angle (a road meeting the bridge at a junction is no part of it).</summary>
    public const double ChainAngleDeg = 30;

    /// <summary>
    /// OSM draws a long bridge as ways in series (main span, fixed spans, approach viaducts), and often only one of them has a clearance or
    /// an opening span (Øresundsbroen's 14 km of approach, Limfjordsbroen's fixed spans next to the flap, Storebælt Østbroen's approaches:
    /// a Havnelods Manual line, an OSM clearance tag). Lines joining end to end (<see cref="ChainJoinM"/>, in line within
    /// <see cref="ChainAngleDeg"/>), or a known line lying along an unknown one (a Manual main span on the OSM way that runs on over the
    /// approaches), form one bridge. The unknown lines (fixed, no clearance, no span window) of a bridge with a known line (a clearance of
    /// its own, or opening) are closed (clearance 0) except within ChainJoinM of its known lines (they stay unknown there: Apply lets the
    /// known clearance count): the bridge is passed at its known spans only. A line known only by charted span points is no such seed (it
    /// closes its own rest, as before): a span point marks one channel, not the bridge's others (Essingeleden).
    /// Trade-off: a navigable fixed span without data in series with a known line is closed too (a detour or no route, never a pass under
    /// an unknown viaduct); a bridge unknown along its whole length keeps the warning. ponytail: geometry only - names and OSM relations
    /// are not in the rows; join by name if a bridge drawn with gaps shows up.
    /// </summary>
    /// <returns>per closed member: its result index, the closed part and the part kept unknown (empty when none)</returns>
    public static IEnumerable<(int at, Geometry closed, Geometry kept)> ClosedInChains(IReadOnlyList<(LineString line, bool known, int unknownAt)> members)
    {
        var parent = Enumerable.Range(0, members.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        var joinDeg = ChainJoinM / WaterGraphTiler.MPerDegLat; var minDot = Math.Cos(ChainAngleDeg * Math.PI / 180);
        var ends = new STRtree<(int member, Coordinate at, double dx, double dy)>(); var lines = new STRtree<int>();
        var all = new List<(int member, Coordinate at, double dx, double dy)>();
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].line.NumPoints < 2) continue;
            foreach (var start in new[] { true, false }) { var e = End(i, members[i].line, start); all.Add(e); ends.Insert(new Envelope(e.at), e); }
            lines.Insert(members[i].line.EnvelopeInternal, i);
        }
        if (all.Count == 0) yield break;
        ends.Build(); lines.Build();
        foreach (var a in all)
        {
            var cos = Math.Cos(a.at.Y * Math.PI / 180);
            var env = new Envelope(a.at); env.ExpandBy(joinDeg / Math.Max(0.1, cos), joinDeg);
            foreach (var b in ends.Query(env))
                // in series: the two ways leave the joint in opposite directions (a way drawn twice leaves it in the same one)
                if (b.member != a.member && Math.Sqrt(Math.Pow((b.at.X - a.at.X) * cos, 2) + Math.Pow(b.at.Y - a.at.Y, 2)) <= joinDeg && a.dx * b.dx + a.dy * b.dy <= -minDot)
                    parent[Find(a.member)] = Find(b.member);
        }
        for (var k = 0; k < members.Count; k++)
            if (members[k].known)
                foreach (var u in Near(lines, members[k].line, ChainJoinM))
                    if (members[u].unknownAt >= 0 && Along(members[k].line, members[u].line, joinDeg)) parent[Find(k)] = Find(u);
        var known = Enumerable.Range(0, members.Count).Where(i => members[i].known).ToLookup(Find);
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i].unknownAt < 0 || !known.Contains(Find(i))) continue;
            var line = members[i].line; var cos = Math.Cos(line.Centroid.Y * Math.PI / 180);
            var scaled = (LineString)line.Copy(); scaled.Apply(new Scale(cos));
            var near = known[Find(i)].Where(k => members[k].line.EnvelopeInternal.Distance(line.EnvelopeInternal) <= 2 * joinDeg)
                .Select(k => { var s = members[k].line.Copy(); s.Apply(new Scale(cos)); return s.Buffer(joinDeg); }).ToArray();
            if (near.Length == 0) { yield return (members[i].unknownAt, line, Geo.Factory.CreateLineString()); continue; }
            var cover = new GeometryCollection(near).Union();
            var closed = scaled.Difference(cover).Copy(); // overlay output shares coordinates with its input: copy before scaling back
            if (closed.IsEmpty) continue; // the known line covers it (a deck beside it, the same way drawn twice)
            var kept = scaled.Intersection(cover).Copy();
            closed.Apply(new Scale(1 / cos)); kept.Apply(new Scale(1 / cos));
            yield return (members[i].unknownAt, closed, kept);
        }
    }

    // every vertex of the known line within d (degrees, scaled) of the unknown one
    private static bool Along(LineString known, LineString unknown, double d)
    {
        var cos = Math.Cos(known.Centroid.Y * Math.PI / 180);
        var su = unknown.Copy(); su.Apply(new Scale(cos));
        return known.Coordinates.All(c => su.IsWithinDistance(Geo.Factory.CreatePoint(new Coordinate(c.X * cos, c.Y)), d));
    }

    // an end of the line and the unit direction (metres) into it, taken ~50 m along: the last vertex step of a bent or split way is noise
    private static (int member, Coordinate at, double dx, double dy) End(int member, LineString line, bool start)
    {
        var cs = line.Coordinates; var n = cs.Length;
        var a = start ? cs[0] : cs[n - 1]; var cos = Math.Cos(a.Y * Math.PI / 180);
        double dx = 0, dy = 0;
        for (var k = 1; k < n; k++)
        {
            var c = start ? cs[k] : cs[n - 1 - k];
            dx = (c.X - a.X) * cos; dy = c.Y - a.Y;
            if (Math.Sqrt(dx * dx + dy * dy) * WaterGraphTiler.MPerDegLat >= 50) break;
        }
        var len = Math.Sqrt(dx * dx + dy * dy);
        return len > 0 ? (member, a, dx / len, dy / len) : (member, a, 0, 0);
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
