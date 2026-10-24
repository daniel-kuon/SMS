using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Distance;
using Sms.Domain;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Den danske Havnelods (Geodatastyrelsen, https://www.danskehavnelods.dk): the Danish bridge regulations (broreglementer, public
/// regulations - cited, not licensed) become Manual Bridge/OpeningBridge corrections. The list is <c>data/broer.js</c>, each bridge
/// <c>Havnelodsen/bridges/{id}.html</c> (label/value sections: Brotype, Gennemsejlingshøjde, Åbningstider). An opening bridge keeps
/// <see cref="RoutingCorrection.ClearanceM"/> null (unlimited when open, as the waterway import's) and names the closed clearance in the
/// note; a fixed one gets its clearance. Harbours (<c>data/havne.js</c>) are not imported: the licence of the harbour text is open (docs/13).
/// </summary>
public static class Havnelods
{
    public const string BaseUrl = "https://www.danskehavnelods.dk/";
    public const string NotePrefix = "Havnelods: ";
    public const string Attribution = "Kilde: Den danske Havnelods, Geodatastyrelsen";
    public const string UserAgent = "sailing-portal/0.1 (+https://github.com/daniel-kuon/SMS)";
    /// <summary>Derived clearance-less bridge rows within this distance of a Havnelods point belong to its crossing (snap, deactivate).</summary>
    public const double CrossingM = 300;
    /// <summary>... and only those within this distance of the chosen bridge line (rail + road + footway at one crossing); a canal bridge
    /// 250 m away across a side arm stays (the dedupe radius).</summary>
    public const double SameCrossingM = 100;
    public const string DeactivatedSuffix = " (Havnelods)";
    public const string UnverifiedNote = " – unverifiziert: Havnelods nennt keine Durchfahrtshöhe";

    public sealed record ListEntry(string Id, string Name, double Lat, double Lng)
    {
        public string Url => $"{BaseUrl}Havnelodsen/bridges/{Id}.html";
    }

    /// <param name="ClearanceM">Fixed bridge: the navigation span's clearance; opening bridge: closed (under the flap).</param>
    /// <param name="FixedSpanM">Opening bridge: the clearance under its fixed spans, when the page gives one.</param>
    /// <param name="LowestM">The lowest clearance on the page (an entry of two bridges, "Kalvebodbroen og Sorterendebroen").</param>
    public sealed record BridgePage(string? Type, bool Opening, double? ClearanceM, double? FixedSpanM, double? LowestM, string? ClearanceText, string? OpeningHours);

    public sealed record ImportItem(string Name, string Url, string Action, string? Kind = null, double? ClearanceM = null, double? FixedSpanM = null, int Deactivated = 0, IReadOnlyList<string>? Nearby = null);
    public sealed record ImportResult(bool DryRun, int Listed, int Bridges, int Opening, int Updated, int DeactivatedUnknown, int NoClearance, int NoLine, int FlaggedUnverified, int Failed, IReadOnlyList<ImportItem> Items);

    /// <summary>The DK entries of broer.js (<c>let broer = [...];</c>).</summary>
    public static List<ListEntry> ParseList(string js)
    {
        using var doc = JsonDocument.Parse(js[js.IndexOf('[')..(js.LastIndexOf(']') + 1)]);
        return doc.RootElement.EnumerateArray()
            .Where(e => e.GetProperty("Nationalitet").GetString() == "DK" && e.TryGetProperty("location", out var l) && l.GetArrayLength() == 2)
            .Select(e => new ListEntry(e.GetProperty("Id").GetString()!, e.GetProperty("Name").GetString()!, e.GetProperty("location")[0].GetDouble(), e.GetProperty("location")[1].GetDouble()))
            .ToList();
    }

    private static readonly Regex Section = new(@"<div class='overskrift1'><a name='[^']*'>(.*?)</a></div>\s*<div class='brod'[^>]*>(.*?)</div>", RegexOptions.Singleline);
    private static readonly Regex Tag = new("<[^>]+>");
    private static readonly Regex LineBreak = new(@"<br\s*/?>|</p>", RegexOptions.IgnoreCase);

    /// <summary>The page's label → text sections (paragraphs and line breaks as newlines).</summary>
    public static Dictionary<string, string> Sections(string html)
    {
        var result = new Dictionary<string, string>();
        foreach (Match m in Section.Matches(html))
        {
            var text = WebUtility.HtmlDecode(Tag.Replace(LineBreak.Replace(m.Groups[2].Value, "\n"), ""));
            var lines = text.Split('\n').Select(l => Regex.Replace(l, @"\s+", " ").Trim()).Where(l => l.Length > 0);
            result.TryAdd(WebUtility.HtmlDecode(m.Groups[1].Value).Trim(), string.Join("\n", lines));
        }
        return result;
    }

    private static readonly Regex OpeningType = new("klap|sving|drejebro|skydebro|løftebro", RegexOptions.IgnoreCase);

    public static BridgePage ParseBridge(string html)
    {
        var s = Sections(html);
        var type = s.GetValueOrDefault("Brotype");
        var text = s.GetValueOrDefault("Gennemsejlingshøjde");
        var (clearance, fixedSpan, lowest) = text is null ? (null, null, null) : Clearance(text);
        return new BridgePage(type, type is not null && OpeningType.IsMatch(type), clearance, fixedSpan, lowest, text, s.GetValueOrDefault("Åbningstider"));
    }

    // "4 m", "3,6 m", "4,4 - 8,2 m", "4,3 meter"
    private static readonly Regex Metres = new(@"(\d+(?:,\d+)?)(?:\s*-\s*\d+(?:,\d+)?)?\s*(?:m|meter)\b", RegexOptions.IgnoreCase);
    // a number of metres that is a width ("i en bredde på 10 m", "De midterste 80 m af") or the open clearance ("ved åben bro er 20 m")
    private static readonly Regex NotClearance = new("bredde|midterste|åben", RegexOptions.IgnoreCase);

    /// <summary>The clearance of a Gennemsejlingshøjde text: the first number of metres that is no width and no open clearance; the fixed
    /// spans' clearance: a later one with "fast" next to it; the lowest of all.</summary>
    public static (double? clearance, double? fixedSpan, double? lowest) Clearance(string text)
    {
        double? first = null, fixedSpan = null, lowest = null;
        var prev = 0;
        foreach (Match m in Metres.Matches(text))
        {
            var before = text[prev..m.Index];
            var end = m.Index + m.Length;
            var after = text[end..Math.Min(text.Length, end + 30)];
            prev = end;
            if (NotClearance.IsMatch(before) || after.TrimStart().StartsWith("af ", StringComparison.OrdinalIgnoreCase)) continue;
            var v = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            lowest = lowest is { } l ? Math.Min(l, v) : v;
            if (first is null) first = v;
            else if (fixedSpan is null && (before + after).Contains("fast", StringComparison.OrdinalIgnoreCase)) fixedSpan = v;
        }
        return (first, fixedSpan, lowest);
    }

    private static string F(double v) => v.ToString("0.0#", CultureInfo.InvariantCulture);
    private static string Flat(string? s) => s is null ? "" : s.Replace("\n", " / ");

    /// <summary>The correction note: kind and clearance in German, then the regulation's own text, the attribution and the page URL (the
    /// re-run key, never cut).</summary>
    public static string Note(ListEntry e, BridgePage p, double? clearanceM)
    {
        var cl = clearanceM is { } c ? F(c) + " m" : "ohne Angabe";
        var kind = p.Opening
            ? $"bewegliche Brücke ({p.Type}), geschlossen {cl}{(p.FixedSpanM is { } f ? $", feste Felder {F(f)} m" : "")}, geöffnet ohne Höhenbegrenzung"
            : $"feste Brücke, Durchfahrtshöhe {cl}";
        var head = $"{NotePrefix}{e.Name} – {kind}. Gennemsejlingshøjde: {Flat(p.ClearanceText)}";
        var tail = $" {Attribution}, {e.Url}";
        var hours = p.OpeningHours is null ? "" : $" Åbningstider: {Flat(p.OpeningHours)}";
        var room = 4000 - head.Length - tail.Length;
        if (head.Length + tail.Length > 4000) head = head[..(4000 - tail.Length)];
        else if (hours.Length > room) hours = hours[..Math.Max(0, room - 1)] + "…";
        return head + hours + tail;
    }

    /// <summary>Active bridge rows whose geometry lies within <paramref name="m"/> metres of the point, with that distance.</summary>
    public static async Task<List<(RoutingCorrection row, double m)>> NearbyBridgesAsync(SmsDbContext db, double lat, double lng, double m, CancellationToken ct)
    {
        var dLat = m / WaterGraphTiler.MPerDegLat; var dLng = dLat / Math.Cos(lat * Math.PI / 180);
        var box = Geo.Factory.ToGeometry(new Envelope(lng - dLng, lng + dLng, lat - dLat, lat + dLat)); box.SRID = 4326;
        var rows = await db.RoutingCorrections.Where(c => c.Active && (c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.OpeningBridge) && c.Geometry.Intersects(box)).ToListAsync(ct);
        var p = Geo.Point(lat, lng);
        return rows.Select(r => (r, DistanceM(r.Geometry, p))).Where(x => x.Item2 <= m).OrderBy(x => x.Item2).ToList();
    }

    /// <summary>Metres between two geometries a few hundred metres apart (nearest points in degrees, then haversine).</summary>
    public static double DistanceM(Geometry a, Geometry b)
    {
        var n = DistanceOp.NearestPoints(a, b);
        return RoutingGraphBuilder.Haversine(n[0].Y, n[0].X, n[1].Y, n[1].X);
    }

    /// <summary>A known (Manual) bridge stands for a derived row only where it covers it: a point covers points within
    /// <see cref="SameCrossingM"/> (the OSM node it replaces), a line covers rows lying within <see cref="SameCrossingM"/> of it along their
    /// whole length (rail + road + footway beside it). A line next to a point stays: the point is its span (Obstacles.WithSpans), alone it
    /// matches every edge within the area build's 1.5 km. A segment in series stays too: Øresundsbroen's 14 km of approach viaducts,
    /// Limfjordsbroen's fixed spans next to the 46 m flap were deactivated with the main span's way, so edges under them met no bridge.
    /// ponytail: vertices only (a straight segment between two covered vertices is covered unless the known line bends sharply).</summary>
    public static bool Covers(Geometry known, Geometry row) =>
        (known is not Point || row is Point) && row.Coordinates.All(c => DistanceM(known, Geo.Point(c.Y, c.X)) <= SameCrossingM);

    private static bool UnknownOf(RoutingCorrection r, bool opening) =>
        r.Source == CorrectionSource.Derived && r.ClearanceM is null && (r.Kind == CorrectionKind.Bridge || (opening && r.Kind == CorrectionKind.OpeningBridge));

    /// <summary>Plan A: fetches broer.js and every DK bridge page (200 ms apart), writes one Manual correction per bridge with a clearance
    /// or an opening (updated in place on a re-run, found by its page URL), snapped to the nearest derived clearance-less bridge line of
    /// the crossing (<see cref="CrossingM"/>); those rows (and an opening bridge's derived opening rows without a clearance) it
    /// <see cref="Covers"/> are deactivated (note suffix <see cref="DeactivatedSuffix"/>). A crossing without such a line is
    /// left alone ("charted" when OSM has a bridge there already, else "noLine": a point would match edges 1.5 km round). A fixed bridge
    /// without a clearance (Masnedsund) writes nothing and marks the manual bridge rows of the crossing unverified. Changes are saved once
    /// at the end, so a dry run reports exactly what the real run does.</summary>
    public static async Task<ImportResult> ImportBridgesAsync(HttpClient http, SmsDbContext db, Guid? userId, bool dryRun, CancellationToken ct)
    {
        var list = ParseList(await http.GetStringAsync(BaseUrl + "data/broer.js", ct));
        var items = new List<ImportItem>();
        foreach (var e in list)
        {
            await Task.Delay(200, ct);
            string html;
            try { html = await http.GetStringAsync(e.Url, ct); }
            catch (HttpRequestException ex) { items.Add(new ImportItem(e.Name, e.Url, $"failed ({(int?)ex.StatusCode})")); continue; }
            items.Add(await ApplyAsync(db, e, ParseBridge(html), userId, ct));
        }
        if (!dryRun) await db.SaveChangesAsync(ct);
        return new ImportResult(dryRun, list.Count, items.Count(i => i.Action is "created" or "updated" && i.Kind == nameof(CorrectionKind.Bridge)),
            items.Count(i => i.Action is "created" or "updated" && i.Kind == nameof(CorrectionKind.OpeningBridge)), items.Count(i => i.Action == "updated"),
            items.Sum(i => i.Deactivated), items.Count(i => i.Action == "noClearance"), items.Count(i => i.Action == "noLine"),
            items.Count(i => i.Action == "flaggedUnverified"), items.Count(i => i.Action.StartsWith("failed")), items);
    }

    public static async Task<ImportItem> ApplyAsync(SmsDbContext db, ListEntry e, BridgePage p, Guid? userId, CancellationToken ct)
    {
        var near = await NearbyBridgesAsync(db, e.Lat, e.Lng, CrossingM, ct);
        var others = near.Where(x => !(x.row.Note ?? "").Contains(e.Id) && (x.row.Source == CorrectionSource.Manual || x.row.ClearanceM is not null))
            .Select(x => $"{x.row.Source} {x.row.Kind} {(x.row.ClearanceM is { } c ? F(c) + " m" : "ohne Höhe")} {Math.Round(x.m)} m: {Trim(x.row.Note)}").ToList();
        var clearance = e.Name.Contains(" og ") ? p.LowestM : p.ClearanceM; // two bridges in one entry: the lower governs
        if (!p.Opening && clearance is null)
        {
            var flagged = 0;
            foreach (var (row, _) in near.Where(x => x.row.Source == CorrectionSource.Manual && x.row.Kind == CorrectionKind.Bridge && x.row.ClearanceM is not null && !(x.row.Note ?? "").Contains("unverifiziert")))
            {
                row.Note = Cut((row.Note ?? "") + UnverifiedNote + $" ({e.Url})"); row.UpdatedById = userId; flagged++;
            }
            return new ImportItem(e.Name, e.Url, flagged > 0 ? "flaggedUnverified" : "noClearance", Nearby: others);
        }
        var kind = p.Opening ? CorrectionKind.OpeningBridge : CorrectionKind.Bridge;
        var own = await db.RoutingCorrections.FirstOrDefaultAsync(c => c.Source == CorrectionSource.Manual && c.Note != null && c.Note.Contains(e.Id), ct);
        // an opening bridge may take the place of OSM's opening-bridge point (Guldborgbroen): a point opening bridge matches as before; a
        // fixed one with a clearance needs a line (a point would close every edge within the area build's 1.5 km)
        var geometry = own?.Geometry ?? near.Where(x => UnknownOf(x.row, p.Opening) && x.row.Geometry is LineString).Select(x => x.row.Geometry).FirstOrDefault()
            ?? (p.Opening ? near.Where(x => UnknownOf(x.row, true)).Select(x => x.row.Geometry).FirstOrDefault() : null);
        if (geometry is null)
            return new ImportItem(e.Name, e.Url, near.Count > 0 ? "charted" : "noLine", kind.ToString(), clearance, p.FixedSpanM, Nearby: others);
        var note = Note(e, p, clearance);
        if (own is null)
        {
            own = new RoutingCorrection { Kind = kind, Source = CorrectionSource.Manual, Geometry = geometry.Copy(), Active = true, CreatedById = userId };
            own.Geometry.SRID = 4326;
            db.RoutingCorrections.Add(own);
        }
        else own.UpdatedById = userId;
        var created = own.Note is null;
        own.Kind = kind; own.ClearanceM = p.Opening ? null : clearance; own.Note = note;
        var deactivated = 0;
        foreach (var (row, _) in near.Where(x => x.row.Active && UnknownOf(x.row, p.Opening) && Covers(own.Geometry, x.row.Geometry)))
        {
            row.Active = false; row.Note = Cut((row.Note ?? "") + DeactivatedSuffix); row.UpdatedById = userId; deactivated++;
        }
        return new ImportItem(e.Name, e.Url, created ? "created" : "updated", kind.ToString(), clearance, p.FixedSpanM, deactivated, others);
    }

    private static string Trim(string? s) => s is null ? "" : s.Length <= 80 ? s : s[..80] + "…";
    internal static string Cut(string s) => s.Length <= 4000 ? s : s[..4000];
}
