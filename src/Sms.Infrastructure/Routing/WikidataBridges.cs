using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Sms.Domain;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Plan C: bridge clearances (P2793) from Wikidata (CC0) for bridges in the routing region that cross a waterway (P177). An item is applied
/// only where it fixes something and the filters pass: 1.5 m ≤ clearance ≤ 80 m (road underpass heights like "575" out), no active bridge
/// with a clearance, opening bridge or manual bridge within <see cref="MatchM"/> already, and derived clearance-less bridge rows within
/// <see cref="MatchM"/> whose OSM name matches the label (or the only such row there). Movable bridges (P31 ⊂ Q787417) carry the closed
/// height: OpeningBridge with ClearanceM null, the height in the note.
/// </summary>
public static class WikidataBridges
{
    public const string SparqlUrl = "https://query.wikidata.org/sparql";
    public const double MatchM = 200, MinM = 1.5, MaxM = 80;
    public const string NotePrefix = "Wikidata ";
    public const string DeactivatedSuffix = " (Wikidata)";

    /// <summary>Region 4–20 E, 52–60 N; the columns of the saved review list (data-sources-wikidata-clearances.csv) plus movable.</summary>
    public const string Query = """
        SELECT ?item ?label ?clearance_m ?crosses ?lat ?lon ?movable WHERE {
          ?item wdt:P31/wdt:P279* wd:Q12280; wdt:P2793 ?clearance_m; wdt:P177 ?c; p:P625 ?s.
          ?s psv:P625 ?v. ?v wikibase:geoLatitude ?lat; wikibase:geoLongitude ?lon.
          FILTER(?lat > 52 && ?lat < 60 && ?lon > 4 && ?lon < 20)
          BIND(EXISTS { ?item wdt:P31/wdt:P279* wd:Q787417 } AS ?movable)
          SERVICE wikibase:label { bd:serviceParam wikibase:language "da,sv,nb,de,nl,en". ?item rdfs:label ?label. ?c rdfs:label ?crosses. }
        }
        """;

    public sealed record Item(string Qid, string Label, double ClearanceM, string? Crosses, double Lat, double Lng, bool Movable)
    {
        public string Url => $"https://www.wikidata.org/wiki/{Qid}";
        public bool InRange => ClearanceM is >= MinM and <= MaxM;
    }

    public sealed record ReviewItem(string Qid, string Label, double ClearanceM, string? Crosses, double Lat, double Lng, string Status, string? SuggestedKind = null,
        IReadOnlyList<string>? MatchedRows = null, double? KnownClearanceM = null, string? Known = null);
    public sealed record ImportResult(bool DryRun, int Items, int InRange, int Bridges, int Opening, int Updated, int Known, int NoUnknownRow, int NoNameMatch, int DeactivatedUnknown, IReadOnlyList<ReviewItem> Review);

    /// <summary>The SPARQL CSV (or the saved list): one item per Q-id (the query repeats an item per crossing and coordinate).</summary>
    public static List<Item> Parse(string csv)
    {
        var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return [];
        var head = Fields(lines[0]);
        int Col(string name) => head.IndexOf(name);
        int item = Col("item"), label = Col("label"), clearance = Col("clearance_m"), crosses = Col("crosses"), lat = Col("lat"), lon = Col("lon"), movable = Col("movable");
        if (item < 0 || clearance < 0 || lat < 0 || lon < 0) throw new FormatException("CSV ohne item/clearance_m/lat/lon.");
        var result = new List<Item>(); var seen = new HashSet<string>();
        foreach (var line in lines.Skip(1))
        {
            var f = Fields(line);
            var qid = f[item][(f[item].LastIndexOf('/') + 1)..];
            if (!seen.Add(qid)) continue;
            string? Get(int i) => i >= 0 && i < f.Count && f[i].Length > 0 ? f[i] : null;
            result.Add(new Item(qid, Get(label) ?? qid, Num(f[clearance]), Get(crosses), Num(f[lat]), Num(f[lon]), Get(movable) == "true"));
        }
        return result;
    }

    private static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static List<string> Fields(string line)
    {
        var result = new List<string>(); var sb = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch != '"') sb.Append(ch);
                else if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else quoted = false;
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }

    /// <summary>Letters and digits only, lower case, Nordic letters folded (ø → o, æ → ae, ß → ss, accents dropped).</summary>
    public static string Normalize(string s)
    {
        var d = s.ToLowerInvariant().Replace("ø", "o").Replace("æ", "ae").Replace("ß", "ss").Normalize(NormalizationForm.FormD);
        return new string(d.Where(char.IsLetterOrDigit).ToArray());
    }

    private static readonly System.Text.RegularExpressions.Regex BridgeWord = new("(broen|bron|brua|brucke|brug|bridge|bro|bru)$");

    /// <summary>The OSM name of a derived bridge row (after "Brücke ohne Höhenangabe – ") names the Wikidata bridge: without the bridge
    /// word at the end, one contains the other ("Storstrømsbroen" / "Storstrømsvej", "Rügenbron" / "Rügenbrücke"; not "Drammensbroen" /
    /// "Drammenbanen").</summary>
    public static bool NameMatches(string label, string? osmName)
    {
        if (osmName is null) return false;
        string a = BridgeWord.Replace(Normalize(label), ""), b = BridgeWord.Replace(Normalize(osmName), "");
        return a.Length >= 4 && b.Length >= 4 && (a.Contains(b) || b.Contains(a));
    }

    private static string? OsmName(RoutingCorrection r)
    {
        var n = r.Note ?? "";
        if (!n.StartsWith(Waterways.UnknownBridgeNote)) return null;
        n = n[Waterways.UnknownBridgeNote.Length..].TrimStart(' ', '–', '-');
        var cut = n.IndexOf(" (", StringComparison.Ordinal); // a suffix of an earlier run
        return (cut >= 0 ? n[..cut] : n) is { Length: > 0 } name ? name : null;
    }

    private static bool Unknown(RoutingCorrection r) => r.Source == CorrectionSource.Derived && r.Kind == CorrectionKind.Bridge && r.ClearanceM is null;

    public static async Task<ImportResult> ImportAsync(IReadOnlyList<Item> items, SmsDbContext db, Guid? userId, bool dryRun, CancellationToken ct)
    {
        var review = new List<ReviewItem>(); var deactivated = 0;
        foreach (var it in items)
        {
            ReviewItem R(string status, string? kind = null, IReadOnlyList<string>? rows = null, double? knownM = null, string? known = null) =>
                new(it.Qid, it.Label, it.ClearanceM, it.Crosses, it.Lat, it.Lng, status, kind, rows, knownM, known);
            if (!it.InRange) { review.Add(R("outOfRange")); continue; }
            var near = await Havnelods.NearbyBridgesAsync(db, it.Lat, it.Lng, MatchM, ct);
            var own = await db.RoutingCorrections.FirstOrDefaultAsync(c => c.Source == CorrectionSource.Manual && c.Note != null && c.Note.StartsWith(NotePrefix + it.Qid + " "), ct);
            if (near.FirstOrDefault(x => x.row != own && (x.row.Source == CorrectionSource.Manual || x.row.ClearanceM is not null || x.row.Kind == CorrectionKind.OpeningBridge)) is { row: { } k })
            { review.Add(R("known", knownM: k.ClearanceM, known: $"{k.Source} {k.Kind}: {k.Note}")); continue; }
            var unknown = near.Where(x => Unknown(x.row)).ToList();
            if (unknown.Count == 0 && own is null) { review.Add(R("noUnknownRow")); continue; }
            var matched = unknown.Where(x => NameMatches(it.Label, OsmName(x.row))).ToList();
            if (matched.Count == 0 && unknown.Count == 1) matched = unknown; // the only crossing there
            if (matched.Count == 0 && own is null) { review.Add(R("noNameMatch", rows: unknown.Select(x => $"{Math.Round(x.m)} m: {x.row.Note}").ToList())); continue; }
            var kind = it.Movable ? CorrectionKind.OpeningBridge : CorrectionKind.Bridge;
            var created = own is null;
            if (own is null)
            {
                var g = matched.Select(x => x.row.Geometry).FirstOrDefault(x => x is LineString) ?? matched[0].row.Geometry;
                own = new RoutingCorrection { Source = CorrectionSource.Manual, Geometry = g.Copy(), Active = true, CreatedById = userId };
                own.Geometry.SRID = 4326;
                db.RoutingCorrections.Add(own);
            }
            else own.UpdatedById = userId;
            var rows = matched.Select(x => $"{Math.Round(x.m)} m: {x.row.Note}").ToList();
            var h = it.ClearanceM.ToString("0.0#", CultureInfo.InvariantCulture);
            own.Kind = kind; own.ClearanceM = it.Movable ? null : it.ClearanceM;
            own.Note = Havnelods.Cut($"{NotePrefix}{it.Qid} (P2793): {it.Label} – {(it.Movable ? $"bewegliche Brücke, geschlossen {h} m, geöffnet ohne Höhenbegrenzung" : $"Durchfahrtshöhe {h} m")}{(it.Crosses is null ? "" : ", über " + it.Crosses)}. CC0, {it.Url}");
            foreach (var (row, _) in matched.Where(x => x.row.Active && Havnelods.Covers(own.Geometry, x.row.Geometry)))
            {
                row.Active = false; row.Note = Havnelods.Cut((row.Note ?? "") + DeactivatedSuffix); row.UpdatedById = userId; deactivated++;
            }
            review.Add(R(created ? "created" : "updated", kind.ToString(), rows));
        }
        if (!dryRun) await db.SaveChangesAsync(ct);
        bool Written(ReviewItem r) => r.Status is "created" or "updated";
        return new ImportResult(dryRun, items.Count, items.Count(i => i.InRange), review.Count(r => Written(r) && r.SuggestedKind == nameof(CorrectionKind.Bridge)),
            review.Count(r => Written(r) && r.SuggestedKind == nameof(CorrectionKind.OpeningBridge)), review.Count(r => r.Status == "updated"), review.Count(r => r.Status == "known"),
            review.Count(r => r.Status == "noUnknownRow"), review.Count(r => r.Status == "noNameMatch"), deactivated, review);
    }
}
