using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;

namespace Sms.Domain;

public enum FeedbackStatus { Open, Resolved }
/// <summary>CorrectedObject: a derived line redrawn by the admin (Geometry = how it should run, the snapshot keeps the original; the original row is never changed).</summary>
public enum FeedbackKind { WrongObject, MissingFairway, Comment, CorrectedObject }

/// <summary>Admin feedback on the fairway detection (Fairways.Derive): a named set of examples that belong together, e.g. "Bøgestrøm".</summary>
public class FeedbackGroup : Entity
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public FeedbackStatus Status { get; set; } = FeedbackStatus.Open;
    public List<FeedbackItem> Items { get; set; } = [];
}

/// <summary>One example: a derived object marked wrong (with a snapshot that survives re-derivation), a drawn fairway that should exist, or a comment.</summary>
public class FeedbackItem : Entity
{
    public Guid GroupId { get; set; }
    public FeedbackGroup? Group { get; set; }
    public FeedbackKind Kind { get; set; }
    /// <summary>"correction:&lt;guid&gt;" or "seamark:&lt;id&gt;"; null for drawn lines and comments.</summary>
    public string? ObjectRef { get; set; }
    /// <summary>The object as it was when marked (kind, source, note, tags, geometry as GeoJSON), jsonb.</summary>
    public string? ObjectSnapshot { get; set; }
    /// <summary>EPSG:4326: the marked object's geometry, the drawn line or the comment point.</summary>
    public Geometry? Geometry { get; set; }
    public string? Note { get; set; }
    /// <summary>What should be true instead: "sollte nicht existieren", "sollte an Tonne X enden", "sollte hier verbinden".</summary>
    public string? Expectation { get; set; }
}

/// <summary>
/// Compares feedback with the current derived passages, so a change to the fairway detection can be checked against the collected examples.
/// Distances are metres in a local equirectangular projection around the feedback geometry (exact enough over a few km).
/// </summary>
public static class FeedbackRegression
{
    public const double MissingToleranceM = 50, MissingSatisfiedShare = 0.8;
    public const double WrongToleranceM = 25, WrongPresentShare = 0.8, WrongGoneShare = 0.2;

    /// <summary>Passage category from the derived note (the derive endpoints write these prefixes); also the map styling in the web.</summary>
    public static string PassageCategory(string? note, CorrectionSource source) =>
        source == CorrectionSource.Manual ? "manual"
        : note is null ? "osm"
        : note.StartsWith("Fahrwasser (", StringComparison.Ordinal) ? "fairway"
        : note.StartsWith("Annahme", StringComparison.Ordinal) ? "assumed"
        : note.StartsWith("Enge", StringComparison.Ordinal) ? "narrow"
        : "osm";

    /// <summary>
    /// Share (0..1) of the geometry's length within <paramref name="toleranceM"/> of any candidate; a point counts 0 or 1. With
    /// <paramref name="except"/>, only the parts farther than <paramref name="exceptM"/> from it count (0 when there are none).
    /// </summary>
    public static double Coverage(Geometry g, IReadOnlyList<Geometry> candidates, double toleranceM, double stepM = 10, Geometry? except = null, double exceptM = 0)
    {
        if (g.IsEmpty || candidates.Count == 0) return 0;
        var t = LocalMetres(g.Centroid.Coordinate);
        var local = candidates.Select(t.Transform).ToList();
        var samples = Samples(t.Transform(g), stepM);
        if (except is not null) { var x = t.Transform(except); samples = samples.Where(s => !x.IsWithinDistance(new Point(s.p), exceptM)).ToList(); }
        var total = samples.Sum(s => s.w);
        if (total == 0) return 0;
        var covered = 0.0;
        foreach (var (p, w) in samples)
        {
            var env = new Envelope(p); env.ExpandBy(toleranceM);
            var pt = new Point(p);
            if (local.Any(c => c.EnvelopeInternal.Intersects(env) && c.IsWithinDistance(pt, toleranceM))) covered += w;
        }
        return covered / total;
    }

    public sealed record WrongCheck(string Status, bool? Satisfied, double Coverage, int? Best, bool Identical);

    /// <summary>
    /// A wrong object (a line): "present" while ≥ 80 % of it is still covered by current passages of its category (identical when the best
    /// match also lies ≥ 90 % along it), "gone" below 20 % (satisfied), "partly" in between (the expectation decides, review by hand).
    /// Points (buoys) are not judged.
    /// </summary>
    public static WrongCheck CheckWrong(Geometry wrong, IReadOnlyList<Geometry> current)
    {
        if (wrong.Dimension == Dimension.Point) return new("unchecked", null, 0, null, false);
        var coverage = Coverage(wrong, current, WrongToleranceM);
        int? best = null; var bestShare = 0.0;
        for (var i = 0; i < current.Count; i++)
        {
            var s = Coverage(wrong, [current[i]], WrongToleranceM);
            if (s > bestShare) { bestShare = s; best = i; }
        }
        var identical = best is { } b && bestShare >= 0.9 && Coverage(current[b], [wrong], WrongToleranceM) >= 0.9;
        return coverage >= WrongPresentShare ? new("present", false, coverage, best, identical)
            : coverage < WrongGoneShare ? new("gone", true, coverage, best, false)
            : new("partly", null, coverage, best, false);
    }

    public sealed record MissingCheck(string Status, bool Satisfied, double Coverage);

    /// <summary>A drawn fairway is satisfied when current passages run within 50 m of it along ≥ 80 % of its length.</summary>
    public static MissingCheck CheckMissing(Geometry drawn, IReadOnlyList<Geometry> current)
    {
        var coverage = Coverage(drawn, current, MissingToleranceM);
        var ok = coverage >= MissingSatisfiedShare;
        return new(ok ? "present" : coverage > 0 ? "partly" : "missing", ok, coverage);
    }

    public sealed record CorrectedCheck(string Status, bool Satisfied, double Coverage, double WrongPartsCoverage, double WrongPartsM);

    /// <summary>
    /// A corrected line: satisfied when current passages run within 50 m along ≥ 80 % of the corrected line ("corrected") and the wrong parts of
    /// the original – its parts farther than 50 m from the corrected line – are no longer covered by passages of its category (&lt; 20 % within
    /// 25 m). Otherwise "wrongPartsRemain" (the new course is there, the old detour too), "partly" or "missing".
    /// </summary>
    public static CorrectedCheck CheckCorrected(Geometry original, Geometry corrected, IReadOnlyList<Geometry> current, IReadOnlyList<Geometry> currentOfCategory)
    {
        var coverage = Coverage(corrected, current, MissingToleranceM);
        var wrong = Coverage(original, currentOfCategory, WrongToleranceM, except: corrected, exceptM: MissingToleranceM);
        var wrongM = (1 - Coverage(original, [corrected], MissingToleranceM)) * LengthM(original); // the original's length off the corrected line
        var present = coverage >= MissingSatisfiedShare;
        var gone = wrong < WrongGoneShare;
        return new(present && gone ? "corrected" : present ? "wrongPartsRemain" : coverage > 0 ? "partly" : "missing", present && gone, coverage, wrong, Math.Round(wrongM));
    }

    /// <summary>Length of the linear parts in metres.</summary>
    public static double LengthM(Geometry g) => g.IsEmpty ? 0 : LocalMetres(g.Centroid.Coordinate).Transform(g).Length;

    /// <summary>Degrees → metres around <paramref name="at"/> (equirectangular).</summary>
    private static AffineTransformation LocalMetres(Coordinate at)
    {
        const double mPerDegLat = 111_320;
        var kx = mPerDegLat * Math.Cos(at.Y * Math.PI / 180);
        return new AffineTransformation().Translate(-at.X, -at.Y).Scale(kx, mPerDegLat);
    }

    /// <summary>Points every ≈ stepM along the linear parts (polygon rings included), each weighted with the length it stands for.</summary>
    private static List<(Coordinate p, double w)> Samples(Geometry g, double stepM)
    {
        if (g.Dimension == Dimension.Point) return g.Coordinates.Select(c => (c, 1.0)).ToList();
        var result = new List<(Coordinate, double)>();
        foreach (LineString l in LinearComponentExtracter.GetLines(g))
            for (var i = 1; i < l.NumPoints; i++)
            {
                var a = l.GetCoordinateN(i - 1); var b = l.GetCoordinateN(i);
                var len = a.Distance(b); if (len == 0) continue;
                var n = (int)Math.Ceiling(len / stepM);
                for (var k = 0; k < n; k++) { var f = (k + 0.5) / n; result.Add((new Coordinate(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f), len / n)); }
            }
        return result;
    }
}
