using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Distance;

namespace Sms.Domain;

/// <summary>
/// Assumed passages ("Annahme"): the coastline polygons close some straits boats pass (Baggensstäket), so where a ferry line or a
/// recommended track crosses the routing land, or lateral marks lie on both sides of a short land strip, a passage is assumed
/// along the line or between the marks. Routes that use one carry a warning. The geometry is here; the admin endpoint
/// (derive-assumed-passages) supplies land, marks and a router test.
/// </summary>
public static class AssumedPassages
{
    /// <summary>Note prefix of the derived corrections ("Annahme: Fährlinie …", "Annahme: Tonnenlücke").</summary>
    public const string NotePrefix = "Annahme";

    /// <summary>A stretch of a guide line by distance along it (metres); Land: it crosses land, not only the coast zone.</summary>
    public readonly record struct Stretch(double From, double To, bool Land);

    /// <summary>
    /// Stretches of a guide line where it is blocked (samples every stepM on land, or in the coast zone when nearLand is given):
    /// only runs with open water before and after them (a run touching an end is the ferry's own quay), runs with more than
    /// maxLandM of land dropped (a line drawn across an island), widened by marginM each way and merged when they come within joinM.
    /// </summary>
    public static List<Stretch> Stretches(IReadOnlyList<Coordinate> line, Func<Coordinate, bool> onLand, Func<Coordinate, bool>? nearLand = null,
        double stepM = 25, double marginM = 500, double joinM = 500, double maxLandM = 2000, double quayM = 400)
    {
        var (pts, along) = Samples(line, stepM);
        var state = pts.Select(p => onLand(p) ? 2 : nearLand?.Invoke(p) == true ? 1 : 0).ToArray();
        var total = along[^1];
        var runs = new List<Stretch>();
        for (var i = 0; i < state.Length; i++)
        {
            if (state[i] == 0) continue;
            var j = i; var landM = 0.0;
            while (j + 1 < state.Length && state[j + 1] != 0) j++;
            for (var k = i; k <= j; k++) if (state[k] == 2) landM += k + 1 < along.Length ? along[k + 1] - along[k] : 0;
            // a run at a line end is the ferry's quay – unless it is long: in a narrow sound the line lies in the coast zone from the
            // terminal on (Skurusundet–Lännerstasundet–Baggensstäket, 59.30 N 18.22–18.29 E, merged ferry ways ending in the sound)
            var atEnd = i == 0 || j == state.Length - 1;
            if ((!atEnd || along[j] - along[i] > quayM) && landM <= maxLandM)
                runs.Add(new Stretch(Math.Max(0, along[i] - marginM), Math.Min(total, along[j] + marginM), landM > 0));
            i = j;
        }
        var merged = new List<Stretch>();
        foreach (var r in runs)
            if (merged.Count > 0 && r.From - merged[^1].To <= joinM) merged[^1] = new Stretch(merged[^1].From, Math.Max(merged[^1].To, r.To), merged[^1].Land || r.Land);
            else merged.Add(r);
        return merged;
    }

    /// <summary>The part of the line between two distances along it (metres).</summary>
    public static Coordinate[] SubLine(IReadOnlyList<Coordinate> line, double from, double to)
    {
        var result = new List<Coordinate>();
        var at = 0.0;
        for (var k = 1; k < line.Count; k++)
        {
            var a = line[k - 1]; var b = line[k];
            var len = SeaGraph.Haversine(a.Y, a.X, b.Y, b.X);
            if (at + len >= from && at <= to)
            {
                if (result.Count == 0) result.Add(Lerp(a, b, len == 0 ? 0 : (from - at) / len));
                if (at + len <= to) result.Add(b);
                else { result.Add(Lerp(a, b, (to - at) / len)); break; }
            }
            at += len;
        }
        return result.Where((c, i) => i == 0 || !c.Equals2D(result[i - 1])).ToArray();
    }

    /// <summary>The point halfway along the line.</summary>
    public static Coordinate Midpoint(IReadOnlyList<Coordinate> line)
    {
        var len = 0.0;
        for (var k = 1; k < line.Count; k++) len += SeaGraph.Haversine(line[k - 1].Y, line[k - 1].X, line[k].Y, line[k].X);
        var sub = SubLine(line, 0, len / 2);
        return sub.Length > 0 ? sub[^1] : line[0];
    }

    /// <summary>A pair of lateral marks with a short land strip between them (indexes into the marks).</summary>
    public readonly record struct Gap(int A, int B, double DistanceM, double LandM, Coordinate LandMid);

    /// <summary>
    /// Candidate buoy gaps: two marks in water at most maxPairM apart whose straight line crosses land, at most maxLandM of it. Pairs
    /// across the same strip are clustered (shortest pair first; another pair whose land lies within clusterM of an accepted one is
    /// dropped). The router decides afterwards whether the graph lacks the connection. hitsLand: an optional quick segment test that
    /// spares the sampling of pairs in open water.
    /// </summary>
    public static List<Gap> BuoyGaps(IReadOnlyList<Coordinate> marks, Func<Coordinate, bool> onLand, double maxPairM = 1500, double maxLandM = 300, double clusterM = 300, double stepM = 10, Func<Coordinate, Coordinate, bool>? hitsLand = null)
    {
        var wet = marks.Select(m => !onLand(m)).ToArray();
        var index = new STRtree<int>();
        for (var i = 0; i < marks.Count; i++) if (wet[i]) index.Insert(new Envelope(marks[i]), i);
        index.Build();
        var candidates = new List<Gap>();
        for (var i = 0; i < marks.Count; i++)
        {
            if (!wet[i]) continue;
            var a = marks[i];
            var dLat = maxPairM / SeaGraph.MPerDeg; var dLng = dLat / Math.Max(0.2, Math.Cos(a.Y * Math.PI / 180));
            foreach (var j in index.Query(new Envelope(a.X - dLng, a.X + dLng, a.Y - dLat, a.Y + dLat)))
            {
                if (j <= i) continue;
                var b = marks[j];
                var d = SeaGraph.Haversine(a.Y, a.X, b.Y, b.X);
                if (d > maxPairM || d == 0 || hitsLand?.Invoke(a, b) == false) continue;
                var n = Math.Max(2, (int)Math.Ceiling(d / stepM));
                var land = 0; double mx = 0, my = 0;
                for (var k = 1; k < n; k++)
                {
                    var p = Lerp(a, b, (double)k / n);
                    if (!onLand(p)) continue;
                    land++; mx += p.X; my += p.Y;
                }
                var landM = land * d / n;
                if (land == 0 || landM > maxLandM) continue;
                candidates.Add(new Gap(i, j, d, landM, new Coordinate(mx / land, my / land)));
            }
        }
        var accepted = new List<Gap>();
        foreach (var c in candidates.OrderBy(c => c.DistanceM))
            if (!accepted.Any(x => SeaGraph.Haversine(x.LandMid.Y, x.LandMid.X, c.LandMid.Y, c.LandMid.X) <= clusterM)) accepted.Add(c);
        return accepted;
    }

    /// <summary>
    /// True when the route runs along the passage: at least minM of it (or 80 % of a shorter one) lies within tolM of the route.
    /// A route that merely crosses the line covers 2 × tolM. Used for stored routes (route cache, planned legs) that carry no graph flags.
    /// </summary>
    public static bool Follows(LineString route, IReadOnlyList<Coordinate> passage, double tolM = 30, double minM = 150)
    {
        if (!route.EnvelopeInternal.Intersects(LineEnvelope(passage, tolM))) return false;
        var near = new IndexedFacetDistance(route);
        var (pts, along) = Samples(passage, 10);
        var tolDeg = tolM / SeaGraph.MPerDeg;
        var f = route.Factory;
        var covered = 0.0;
        for (var k = 0; k + 1 < pts.Count; k++)
            if (near.Distance(f.CreatePoint(pts[k])) <= tolDeg) covered += along[k + 1] - along[k];
        return covered >= Math.Min(minM, 0.8 * along[^1]);
    }

    private static Envelope LineEnvelope(IReadOnlyList<Coordinate> line, double marginM)
    {
        var e = new Envelope(); foreach (var c in line) e.ExpandToInclude(c);
        e.ExpandBy(marginM / SeaGraph.MPerDeg / Math.Max(0.2, Math.Cos(e.Centre.Y * Math.PI / 180)), marginM / SeaGraph.MPerDeg);
        return e;
    }

    /// <summary>Points every ≤ stepM along the line (its vertices included) with their distance along it.</summary>
    private static (List<Coordinate> pts, double[] along) Samples(IReadOnlyList<Coordinate> line, double stepM)
    {
        var pts = new List<Coordinate> { line[0] }; var along = new List<double> { 0 };
        for (var k = 1; k < line.Count; k++)
        {
            var a = line[k - 1]; var b = line[k];
            var len = SeaGraph.Haversine(a.Y, a.X, b.Y, b.X);
            var n = Math.Max(1, (int)Math.Ceiling(len / stepM));
            for (var s = 1; s <= n; s++) { pts.Add(Lerp(a, b, (double)s / n)); along.Add(along[^1] + len / n); }
        }
        return (pts, along.ToArray());
    }

    private static Coordinate Lerp(Coordinate a, Coordinate b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
}
