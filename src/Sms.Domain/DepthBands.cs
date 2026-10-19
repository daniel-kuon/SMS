namespace Sms.Domain;

/// <summary>
/// The charted depth comes in bands (EMODnet fathom steps: 0–1.83, 1.83–3.66, 3.66–5.49 m …), stored by their lower bound. Asking
/// for lower bound ≥ draft + margin closed every 1.83–3.66 m band to a 1.8 m boat: the whole Schlei, the Ærøskøbing and Bogestrøm
/// channels and the shore water of the Kiel Förde, all sailed by the user's 1.8 m boat according to the old logbook. A band now
/// counts as deep enough when its middle reaches draft + margin.
/// </summary>
public static class DepthBands
{
    /// <summary>The lowest band floor that still passes for <paramref name="limitM"/> (draft + margin), given the bands as
    /// (min, max) pairs; areas and edges with min_depth ≥ the result are navigable. Without a passing band: the limit itself.</summary>
    public static double EffectiveFloor(double limitM, IEnumerable<(double MinM, double MaxM)> bands)
    {
        var passing = bands.Where(b => b.MaxM > b.MinM && (b.MinM + b.MaxM) / 2 >= limitM).Select(b => b.MinM).ToList();
        return passing.Count == 0 ? limitM : Math.Min(limitM, passing.Min());
    }
}
