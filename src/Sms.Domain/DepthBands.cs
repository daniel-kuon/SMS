using NetTopologySuite.Geometries;

namespace Sms.Domain;

/// <summary>
/// The charted depth comes in bands (EMODnet fathom steps: 0–1.83, 1.83–3.66, 3.66–5.49 m …), stored by their lower bound. Asking
/// for lower bound ≥ draft + margin closed every 1.83–3.66 m band to a 1.8 m boat: the whole Schlei, the Ærøskøbing and Bogestrøm
/// channels and the shore water of the Kiel Förde, all sailed by the user's 1.8 m boat according to the old logbook. A band now
/// counts as deep enough when its middle reaches the ship's minimum depth.
/// The ship has two settings (the owner picks the margin): the minimum depth (routes avoid bands whose middle lies below it, outside
/// buoyed channels and the harbour approach) and the warning depth (the route warns "Tiefe prüfen" where a band's floor lies below
/// it). A ship (or an old client) with a draft only gets minimum depth = draft + <see cref="DraftMarginM"/> and no warnings.
/// </summary>
public static class DepthBands
{
    /// <summary>Margin added to a bare draft when no minimum depth is given (the rule before the two settings).</summary>
    public const double DraftMarginM = 0.5;

    /// <summary>The minimum depth a route keeps: the owner's own value as given (no margin on top), else draft + margin, else null (no limit).</summary>
    public static double? MinDepth(double? draftM, double? minDepthM, double marginM = DraftMarginM) =>
        minDepthM is > 0 ? minDepthM : draftM is > 0 ? draftM + marginM : null;

    /// <summary>The lowest band floor that still passes for <paramref name="minDepthM"/>, given the bands as (min, max) pairs: a band
    /// passes when its middle ≥ the minimum depth; areas and edges with min_depth ≥ the result are navigable. Without a passing band: the limit itself.</summary>
    public static double EffectiveFloor(double minDepthM, IEnumerable<(double MinM, double MaxM)> bands)
    {
        var passing = bands.Where(b => b.MaxM > b.MinM && (b.MinM + b.MaxM) / 2 >= minDepthM).Select(b => b.MinM).ToList();
        return passing.Count == 0 ? minDepthM : Math.Min(minDepthM, passing.Min());
    }

    /// <summary>A band (by its floor) the route should be checked in: the floor lies below the warning depth.</summary>
    public static bool Warns(double floorM, double? warnDepthM) => warnDepthM is { } w && floorM < w;

    /// <summary>"Tonnen first" with a bound: the strict route (uncertain water closed) is kept when it is no more than factor × the uncertain
    /// water of the relaxed route longer than that route; factor ≤ 0 keeps it always (the rule before).</summary>
    public static bool KeepsStrict(double strictM, double relaxedM, double uncertainM, double factor) => factor <= 0 || strictM <= relaxedM + factor * uncertainM;

    /// <summary>
    /// The depth an edge counts with: its charted floor (the shallowest depth area it crosses), else – water without depth data in the
    /// coastal strip, which the raster leaves out because it is shallower than its first band – the floor of the shallowest band within
    /// reach of it (the builder stores that per edge), else unknown (open water without data: no limit, no warning).
    /// </summary>
    public static float Effective(float chartedM, float adjacentM) => float.IsNaN(chartedM) ? adjacentM : chartedM;

    /// <summary>A stretch of a route in water shallower than the warning depth: its midpoint, band floor (and top, NaN = unknown), length;
    /// Inferred = no charted depth, counted as the band next to it.</summary>
    public sealed record Piece(Coordinate At, double FloorM, double MaxM, double LengthM, bool Inferred = false);

    /// <summary>
    /// "checkDepth" warnings of a route from its pieces: in route order, a piece starting within mergeM (along the route) after the end of
    /// the previous stretch joins it; a stretch keeps its shallowest piece (the longest of equally shallow ones) as location and band and
    /// sums the lengths. At most max stretches - the shallowest, then the longest - listed in route order.
    /// </summary>
    public static List<Piece> Stretches(LineString route, IEnumerable<Piece> pieces, double mergeM = 1000, int max = 5)
    {
        var line = new NetTopologySuite.LinearReferencing.LengthIndexedLine(route);
        var cs = route.Coordinates; var metres = 0.0;
        for (var i = 1; i < cs.Length; i++) metres += SeaGraph.Haversine(cs[i - 1].Y, cs[i - 1].X, cs[i].Y, cs[i].X);
        var mPerUnit = route.Length > 0 ? metres / route.Length : 0; // ponytail: one scale for the whole line, fine for merging pieces
        var stretches = new List<(Piece shallowest, double length, double at, double end)>();
        foreach (var (p, at) in pieces.Select(p => (p, at: line.Project(p.At) * mPerUnit)).OrderBy(x => x.at))
        {
            if (stretches.Count > 0 && at - p.LengthM / 2 - stretches[^1].end <= mergeM)
            {
                var s = stretches[^1];
                stretches[^1] = (p.FloorM < s.shallowest.FloorM || (p.FloorM == s.shallowest.FloorM && p.LengthM > s.shallowest.LengthM) ? p : s.shallowest, s.length + p.LengthM, s.at, Math.Max(s.end, at + p.LengthM / 2));
            }
            else stretches.Add((p, p.LengthM, at, at + p.LengthM / 2));
        }
        return stretches.OrderBy(s => s.shallowest.FloorM).ThenByDescending(s => s.length).Take(max).OrderBy(s => s.at)
            .Select(s => s.shallowest with { LengthM = s.length }).ToList();
    }
}
