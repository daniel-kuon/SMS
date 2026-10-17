using System.Globalization;
using System.Text;

namespace Sms.Api;

/// <summary>
/// Light characteristics the way a chart writes them (OpenSeaMap convention): "Fl(2)WR.10s12m9-7M" = flashing, group of 2,
/// white and red sectors, period 10 s, 12 m above datum, ranges 9 and 7 miles. Sectored lights use seamark:light:N:* tags.
/// </summary>
public static class Lights
{
    private static readonly Dictionary<string, string> ColourLetter = new() { ["white"] = "W", ["red"] = "R", ["green"] = "G", ["yellow"] = "Y", ["blue"] = "Bu", ["orange"] = "Or", ["amber"] = "Am", ["violet"] = "Vi" };

    /// <summary>Returns the charted label and the sector list ("colour:start:end:range;…"), or (null, null) without a light.</summary>
    public static (string? label, string? sectors) Describe(IReadOnlyDictionary<string, string> tags)
    {
        var numbered = new SortedDictionary<int, Dictionary<string, string>>();
        var plain = new Dictionary<string, string>();
        foreach (var (k, v) in tags)
        {
            if (!k.StartsWith("seamark:light:")) continue;
            var rest = k["seamark:light:".Length..];
            var colon = rest.IndexOf(':');
            if (colon > 0 && int.TryParse(rest[..colon], out var n)) { if (!numbered.TryGetValue(n, out var d)) numbered[n] = d = []; d[rest[(colon + 1)..]] = v; }
            else plain[rest] = v;
        }
        var sectors = numbered.Count > 0 ? numbered.Values.ToList() : plain.Count > 0 ? [plain] : [];
        if (sectors.Count == 0) return (null, null);
        string? First(string key) => sectors.Select(s => s.GetValueOrDefault(key)).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        var sb = new StringBuilder();
        var character = First("character") ?? "";
        sb.Append(character);
        if (First("group") is { Length: > 0 } g) sb.Append('(').Append(g).Append(')');
        var colours = sectors.SelectMany(s => (s.GetValueOrDefault("colour") ?? "").Split(';')).Select(c => ColourLetter.GetValueOrDefault(c.Trim(), "")).Where(c => c != "").Distinct().ToList();
        if (colours is not ["W"] && colours.Count > 0) { if (!tags.Keys.Any(k => k.EndsWith(":group")) ) sb.Append('.'); sb.Append(string.Join("", colours)); } // "Fl.G.4s" but "Fl(2)G.10s"; a white-only light is charted without its colour
        if (First("period") is { Length: > 0 } p) { if (sb.Length > 0) sb.Append('.'); sb.Append(Num(p)).Append('s'); }
        if (First("height") is { Length: > 0 } h) sb.Append(Num(h)).Append('m');
        var ranges = sectors.Select(s => s.GetValueOrDefault("range")).Where(r => !string.IsNullOrEmpty(r)).Select(r => Num(r!)).Distinct().ToList();
        if (ranges.Count > 0)
        {
            var ordered = ranges.OrderByDescending(r => double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0).ToList();
            sb.Append(ordered.Count == 1 ? ordered[0] : $"{ordered[0]}-{ordered[^1]}").Append('M'); // charts give the largest and the smallest range
        }
        var label = sb.Length > 0 ? sb.ToString() : null;
        var sectorText = string.Join(";", sectors.Select(s => $"{ColourLetter.GetValueOrDefault((s.GetValueOrDefault("colour") ?? "").Split(';')[0].Trim(), "")}:{Num(s.GetValueOrDefault("sector_start") ?? "")}:{Num(s.GetValueOrDefault("sector_end") ?? "")}:{Num(s.GetValueOrDefault("range") ?? "")}"));
        return (label, sectorText.Replace(":", ":").Trim(';').Length > 0 && sectorText.Any(char.IsDigit) ? sectorText : null);
    }

    /// <summary>"10", "10.5 m", "12,5" → "10", "10.5", "12.5"; anything else unchanged.</summary>
    public static string Num(string v)
    {
        var m = System.Text.RegularExpressions.Regex.Match(v.Replace(',', '.'), @"-?\d+(\.\d+)?");
        return m.Success ? m.Value : v.Trim();
    }
}
