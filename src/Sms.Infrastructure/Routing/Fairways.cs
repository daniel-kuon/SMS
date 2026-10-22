using NetTopologySuite.Algorithm.Locate;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using Npgsql;

namespace Sms.Infrastructure.Routing;

/// <summary>Port/Starboard: lateral marks (IALA A); North…West: cardinal marks, safe water on that side; SafeWater: mid-channel/landfall marks.</summary>
public enum MarkSide { Port, Starboard, North, East, South, West, SafeWater }
/// <param name="Light">A red or green minor light (pier head): pairs only with another light, across a harbour entrance.</param>
public sealed record FairwayMark(double Lat, double Lng, MarkSide Side, bool Light = false);
/// <param name="Singles">Unpaired marks (single lateral, cardinal or safe-water marks) the chain runs past.</param>
/// <param name="Area">The water between the port and the starboard marks of the pairs (lines with ≥ 2 pairs, median pair width
/// ≥ <see cref="Fairways.AreaMinWidthM"/>): the fairway as an area,
/// red border on one side, green on the other; the routing passage is its centreline <paramref name="Line"/>.</param>
/// <param name="Wide">A corridor between two one-sided rows (a wide river): no ×3 channel zone round it in the graph build.</param>
/// <param name="Shoal">A single-mark line through charted shoal water (<see cref="Fairways.ShoalShare"/>): a channel zone line like a paired one.</param>
public sealed record Fairway(LineString Line, int Pairs, int Singles = 0, Polygon? Area = null, bool Wide = false, bool Shoal = false);
/// <summary>Water checks for a hop a → b (X = lng, Y = lat). Clear: no land between water on it. Deep: charted depth all along
/// (wide hops between single marks, straightened corners).
/// Path: a way a → b through charted deep water when the straight hop crosses land (the points between, or null).
/// Water: the point is not on land (a harbour entrance's inside). Floor: the floor (m) of the deepest charted depth band at the point, null = no band.</summary>
public sealed record FairwayWater(Func<Coordinate, Coordinate, bool> Clear, Func<Coordinate, Coordinate, bool> Deep, Func<Coordinate, Coordinate, Coordinate[]?>? Path = null, Func<Coordinate, bool>? Water = null, Func<Coordinate, double?>? Floor = null);

/// <summary>
/// Spec 10 §10.5 step 2: fairway centrelines from OpenSeaMap marks. Each port mark is paired with the nearest starboard mark
/// across the channel (≤ pairMaxM, not across land; IALA A: red/port left when entering). Points: pair midpoints, unpaired
/// lateral marks, cardinal marks (a point <see cref="CardinalOffsetM"/> off the mark on its safe side). Passes: (1) pairs only,
/// hops ≤ gapMaxM – the well-marked fairways; (2) those chains, most pairs first, go on through single marks and join other
/// chains' tips; (3) chains of the single marks left, joining the others. A chain grows from a tip to the cheapest point ahead:
/// hop length ×(1 + (turn − 45°)/15°) for turns over 45° against the heading over its last <see cref="HeadingBaseM"/>, ×4 for a
/// dead end; big turns only on short hops (pairs 90° within 1 km, else 45°; single marks 90°/60°/30° within 0.5/1/2 km); wide
/// hops up to wideGapM only to a lateral single mark that leads on, turn ≤ 30°, through charted depth (Storstrøm east of
/// Masnedø); a join needs both chains' courses to agree (≤ 45°, wide 30°). Every hop: no land between water (> <see cref="LandMaxM"/>),
/// never through a cardinal's danger quadrant within <see cref="CardinalDangerM"/>. Pairs buoyed away from each other are never
/// joined, nor is a single mark next to such a pair taken (Bogø/Stubbekøbing: two approaches leaving Grønsund). Safe-water
/// marks are never chain points: a chain ends at one ≤ <see cref="SafeAheadM"/> ahead (approach landfall, or a junction on a through
/// fairway, which may pass it on a straight course).
/// Cleanup: leading single marks after the last pair kept only ≥ 2 and ending lateral; out-and-back corners (> 90°) cut where
/// the straight hop is clear and deep; an end heading for another line ≤ gapMaxM ahead joins it
/// (T-junction); line ends running along a better line are cut back, lines mostly along one dropped; single lateral marks moved
/// half a pair width (≤ 150 m) towards the channel; stretches of ≥ 2 single marks, and single marks or junction hops at a line's
/// end, become lines of their own (no channel zone in the graph build, which takes lines with ≥ 2 pairs). Red/green minor lights
/// pair only with each other (pier heads at a harbour entrance); a lone pair counts when it joins a line. A wide gap between single lateral marks whose straight hop crosses ≥ 300 m of land may bend through deep
/// water (<see cref="FairwayWater.Path"/>); land inside a Water correction counts as water.
/// The fairway as an area: a paired line carries the water between its pairs' marks (<see cref="Fairway.Area"/>, red border on one
/// side, green on the other). A wide river whose rows are too far apart to pair (the Elbe) is two one-sided single-mark stretches
/// side by side: they are merged into one corridor, each mark paired with the foot of its perpendicular on the other row (widePairMaxM,
/// through charted deep water). A one-sided row without a pair within <see cref="PairReachM"/> collapses to a line
/// <see cref="RowOffsetM"/> (half the deepest band's run) off the marks towards the deeper charted band (<see cref="FairwayWater.Floor"/>).
/// Rows of ≥ 3 cardinals of one kind (a shoal's edge), or of north and south (east and west) cardinals on opposite sides of the
/// line (a channel between two shoals), are fairways of their own; a moved mark whose hop enters a cardinal's danger
/// quadrant goes back onto its mark.
/// ponytail: greedy chaining, no buoy sequence numbers; a one-sided row gets a line, not an area (its open side is the deeper band,
/// not a border); special-purpose marks are not used (none tagged as fairway marks); marks too sparse to chain without crossing
/// land (Grønsund between Bogø and Falster) stay unchained.
/// </summary>
public static class Fairways
{
    public const double CardinalOffsetM = 100, CardinalDangerM = 300, HeadingBaseM = 300, LandMaxM = 50, PathLandM = 300, EndLandM = 100, EntranceM = 150;
    /// <summary>A single lateral mark is moved at most this far towards the channel (half the width of the nearest pair, or of the charted deep water beside a row without pairs).</summary>
    public const double SingleOffsetMaxM = 600;
    /// <summary>A single lateral mark of a row without pairs nearby is moved at most this far towards the deeper water beside the row.</summary>
    public const double RowOffsetM = 150;
    /// <summary>A single lateral mark takes its offset from a pair of its line this close; farther from any pair it is a mark of a row (RowOffsetM).</summary>
    public const double PairReachM = 1000;
    /// <summary>A line ends at a safe-water mark this far ahead of its end: landfall buoys lie 1–3 km seaward of the channel.</summary>
    public const double SafeAheadM = 3000;
    /// <summary>Lines whose pairs are narrower than this (median) get no area: a narrow channel is its line, the area model is for
    /// wide water (Ærøskøbing: a 36 m wide harbour-entrance area carried no depth limit, the route made a spur to its entrance pair).</summary>
    public const double AreaMinWidthM = 200;
    /// <summary>In the note of every derived line with pairs; a stretch of single marks only ("Fahrwasser (k Einzeltonnen)") lacks
    /// it and gets no ×3 channel zone in the graph build (RoutingGraphBuilder, SeaRouter's smoother zone).</summary>
    public const string PairedNote = "Tonnenpaare";
    /// <summary>Start of the note of a derived Fairway area (the water between the marks of a paired line).</summary>
    public const string AreaNote = "Fahrwasserfläche (abgeleitet";
    /// <summary>The Passage note of a derived line.</summary>
    public static string Note(Fairway f) => f.Pairs == 0 ? $"Fahrwasser ({f.Singles} Einzeltonnen{(f.Shoal ? ", " + ShoalNote : "")})"
        : f.Singles > 0 ? $"Fahrwasser ({f.Pairs} {PairedNote}, {f.Singles} Einzeltonnen{(f.Wide ? ", " + WideNote : "")})" : $"Fahrwasser ({f.Pairs} {PairedNote}{(f.Wide ? ", " + WideNote : "")})";
    /// <summary>In the note of a wide corridor (Fairway.Wide) and its area: the graph build puts no ×3 channel zone round those.</summary>
    public const string WideNote = "breit";
    /// <summary>In the note of a single-mark line through charted shoal water (Fairway.Shoal): a channel zone line like a paired one.</summary>
    public const string ShoalNote = "Untiefen";
    /// <summary>The derived lines the graph build and the smoother put a channel zone round: paired or shoal single-mark lines, not the
    /// wide corridors (the stored note of a "Fahrwasser" passage).</summary>
    public static bool IsZoneLine(string? note) => note is not null && note.StartsWith("Fahrwasser") && !note.Contains(WideNote) && (note.Contains(PairedNote) || note.Contains(ShoalNote));

    /// <summary>
    /// Share of the water within bandM of the line that is charted shallower than shoalFloorM (<see cref="FairwayWater.Floor"/>): samples
    /// every stepM along the line at k × 250 m perpendicular on both sides (k = 1 … bandM / 250, local metric frame). Land
    /// (<see cref="FairwayWater.Water"/> false) is left out; a sample without a band counts as water but not as shoal (the coastal strip
    /// the raster leaves out - counting it would make every coast-hugging line a zone line). No floors → 0.
    /// </summary>
    /// <summary>
    /// The gaps of a fairway: straight chords between two line ends at most maxM apart where the second line continues the first: the chord
    /// turns from the first line's course out of its end, and into the second line's course on from its end (each over
    /// <see cref="HeadingBaseM"/>), by at most 90° within 1 km, 60° within 2 km, else 45° (the derivation's turn rule for single marks), and
    /// the fairway's course by at most 90° from one line to the other (two lines side by side are no gap).
    /// The derivation leaves such gaps where marks are missing or too far apart for a hop (Ulvsund, Stege Bugt, Bøgestrøm); a route
    /// through them still follows the fairway.
    /// </summary>
    public static List<LineString> GapChords(IReadOnlyList<LineString> lines, double maxM = 5000)
    {
        // each end with the course out of it: from the point HeadingBaseM back along the line (or its other end)
        var ends = new List<(Coordinate p, (double x, double y) dir, int line)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var cs = lines[i].Coordinates;
            if (cs.Length < 2) continue;
            foreach (var pts in new[] { cs, cs.Reverse().ToArray() })
            {
                var k = pts.Length - 2;
                while (k > 0 && RoutingGraphBuilder.Haversine(pts[k].Y, pts[k].X, pts[^1].Y, pts[^1].X) < HeadingBaseM) k--;
                ends.Add((pts[^1], V(pts[k], pts[^1]), i));
            }
        }
        var tree = new STRtree<int>();
        for (var k = 0; k < ends.Count; k++) tree.Insert(new Envelope(ends[k].p), k);
        tree.Build();
        var deg = maxM / WaterGraphTiler.MPerDegLat;
        var chords = new List<LineString>();
        for (var a = 0; a < ends.Count; a++)
        {
            var env = new Envelope(ends[a].p); env.ExpandBy(deg / Math.Cos(ends[a].p.Y * Math.PI / 180), deg);
            foreach (var b in tree.Query(env))
            {
                if (b <= a || ends[b].line == ends[a].line) continue;
                var d = RoutingGraphBuilder.Haversine(ends[a].p.Y, ends[a].p.X, ends[b].p.Y, ends[b].p.X);
                if (d < 1 || d > maxM) continue; // joined already, or too far
                var c = V(ends[a].p, ends[b].p); var maxTurn = d <= 1000 ? 90 : d <= 2000 ? 60 : 45;
                (double x, double y) on = (-ends[b].dir.x, -ends[b].dir.y);
                if (Turn(ends[a].dir, c) <= maxTurn && Turn(c, on) <= maxTurn && Turn(ends[a].dir, on) <= 90)
                    chords.Add(lines[ends[a].line].Factory.CreateLineString([ends[a].p.Copy(), ends[b].p.Copy()]));
            }
        }
        return chords;
    }

    public static double ShoalShare(LineString line, FairwayWater water, double bandM, double shoalFloorM, double stepM = 100)
    {
        if (water.Floor is not { } floor) return 0;
        int n = 0, shoal = 0, sides = (int)(bandM / 250);
        for (var i = 1; i < line.NumPoints; i++)
        {
            Coordinate a = line.GetCoordinateN(i - 1), b = line.GetCoordinateN(i);
            var (vx, vy) = V(a, b); var len = Math.Sqrt(vx * vx + vy * vy);
            if (len == 0) continue;
            var steps = Math.Max(1, (int)Math.Ceiling(len * WaterGraphTiler.MPerDegLat / stepM));
            for (var k = 0; k < steps; k++)
            {
                var c = new Coordinate(a.X + (b.X - a.X) * (k + 0.5) / steps, a.Y + (b.Y - a.Y) * (k + 0.5) / steps);
                for (var j = -sides; j <= sides; j++)
                {
                    if (j == 0) continue;
                    var p = Shift(c, -vy / len, vx / len, j * 250);
                    if (water.Water?.Invoke(p) == false) continue;
                    n++;
                    if (floor(p) is { } f && f < shoalFloorM) shoal++;
                }
            }
        }
        return n == 0 ? 0 : (double)shoal / n;
    }
    /// <summary>The seamark types <see cref="SideOf"/> maps.</summary>
    public static readonly string[] MarkTypes = ["buoy_lateral", "beacon_lateral", "buoy_cardinal", "beacon_cardinal", "buoy_safe_water", "beacon_safe_water", "light_minor"];

    /// <summary>Maps an OSM seamark type/category (lateral marks without a category: their colour, IALA A) to a side, or null.</summary>
    public static MarkSide? SideOf(string type, string? category, string? colour = null) => type switch
    {
        "buoy_lateral" or "beacon_lateral" => category switch
        {
            "port" or "preferred_channel_starboard" => MarkSide.Port,
            "starboard" or "preferred_channel_port" => MarkSide.Starboard,
            null or "" => colour switch { "red" => MarkSide.Port, "green" => MarkSide.Starboard, _ => null },
            _ => null,
        },
        "buoy_cardinal" or "beacon_cardinal" => category switch { "north" => MarkSide.North, "east" => MarkSide.East, "south" => MarkSide.South, "west" => MarkSide.West, _ => null },
        "buoy_safe_water" or "beacon_safe_water" => MarkSide.SafeWater,
        "light_minor" => colour switch { "red" => MarkSide.Port, "green" => MarkSide.Starboard, _ => null }, // colour: the light's
        _ => null,
    };

    private enum Step { Pairs, All }
    private sealed record Pt(Coordinate C, MarkSide? Side, FairwayMark? P = null, FairwayMark? S = null)
    {
        public bool Pair => Side is null && P is not null;
        /// <summary>A bend of a hop round land through deep water (Fairways.InputsAsync Path), no mark.</summary>
        public bool Via => Side is null && P is null;
        public bool Lateral => Side is null or MarkSide.Port or MarkSide.Starboard;
        /// <summary>Buoyage direction of a pair (port on the left), local metric frame.</summary>
        public (double x, double y) B => Pair ? (-V(At(P!), At(S!)).y, V(At(P!), At(S!)).x) : (0, 0);
    }

    public static List<Fairway> Derive(IReadOnlyList<FairwayMark> marks, GeometryFactory f, FairwayWater? water = null, double pairMaxM = 600, double gapMaxM = 2000, double wideGapM = 5000, double widePairMaxM = 2000)
    {
        Func<Coordinate, Coordinate, bool> clear = water?.Clear ?? ((_, _) => true), deep = water?.Deep ?? ((_, _) => true);
        var port = marks.Where(m => m.Side == MarkSide.Port).ToList();
        var starboard = marks.Where(m => m.Side == MarkSide.Starboard).ToList();
        // 1. pairs: closest port/starboard couples first, each mark used once, not across land
        var candidates = new List<(double d, int p, int s)>();
        for (var p = 0; p < port.Count; p++)
            for (var s = 0; s < starboard.Count; s++)
            {
                var d = RoutingGraphBuilder.Haversine(port[p].Lat, port[p].Lng, starboard[s].Lat, starboard[s].Lng);
                if (d <= pairMaxM && port[p].Light == starboard[s].Light) candidates.Add((d, p, s));
            }
        var usedP = new HashSet<int>(); var usedS = new HashSet<int>();
        var pts = new List<Pt>();
        foreach (var (_, p, s) in candidates.OrderBy(c => c.d))
        {
            if (usedP.Contains(p) || usedS.Contains(s) || !clear(At(port[p]), At(starboard[s]))) continue;
            usedP.Add(p); usedS.Add(s);
            pts.Add(new Pt(new Coordinate((port[p].Lng + starboard[s].Lng) / 2, (port[p].Lat + starboard[s].Lat) / 2), null, port[p], starboard[s]));
        }
        // 2. single points: unpaired lateral marks on the mark (moved towards the channel once chained), cardinals on their safe side
        // (a light without its partner is no fairway mark)
        foreach (var m in port.Where((_, i) => !usedP.Contains(i)).Concat(starboard.Where((_, i) => !usedS.Contains(i))).Where(m => !m.Light)) pts.Add(new Pt(At(m), m.Side));
        var dLat = CardinalOffsetM / WaterGraphTiler.MPerDegLat;
        foreach (var m in marks.Where(m => m.Side is not (MarkSide.Port or MarkSide.Starboard)))
        {
            var dLng = dLat / Math.Max(0.1, Math.Cos(m.Lat * Math.PI / 180));
            var (lng, lat) = m.Side switch { MarkSide.North => (m.Lng, m.Lat + dLat), MarkSide.South => (m.Lng, m.Lat - dLat), MarkSide.East => (m.Lng + dLng, m.Lat), MarkSide.West => (m.Lng - dLng, m.Lat), _ => (m.Lng, m.Lat) };
            pts.Add(new Pt(new Coordinate(lng, lat), m.Side, m)); // P: the mark itself
        }
        if (pts.Count == 0) return [];

        // 3. chains (grid of wideGapM cells for the neighbour search, water checks cached per point pair)
        var cell = wideGapM / WaterGraphTiler.MPerDegLat;
        var cellX = cell / Math.Max(0.1, Math.Cos(pts.Max(p => Math.Abs(p.C.Y)) * Math.PI / 180));
        (int, int) Key(Coordinate c) => ((int)Math.Floor(c.X / cellX), (int)Math.Floor(c.Y / cell));
        var grid = pts.Select((p, i) => (i, k: Key(p.C))).ToLookup(x => x.k, x => x.i);
        var clearCache = new Dictionary<(int, int), bool>(); var deepCache = new Dictionary<(int, int), bool>();
        bool Clear(int a, int b) => Cached(clearCache, a, b, () => clear(pts[a].C, pts[b].C) && CardinalsOk(pts[a].C, pts[b].C));
        // no hop comes within CardinalDangerM of a cardinal mark on its danger side (south of a north cardinal, …)
        bool CardinalsOk(Coordinate a, Coordinate b) => Near(a).Concat(Near(b)).All(c => pts[c].Side is not (MarkSide.North or MarkSide.East or MarkSide.South or MarkSide.West) || PassesCardinal(a, b, pts[c].P!));
        bool Deep(int a, int b) => Cached(deepCache, a, b, () => deep(pts[a].C, pts[b].C));
        double Dist(int a, int b) => RoutingGraphBuilder.Haversine(pts[a].C.Y, pts[a].C.X, pts[b].C.Y, pts[b].C.X);
        var used = Enumerable.Repeat(false, pts.Count).ToList();
        var chainOf = Enumerable.Repeat(-1, pts.Count).ToList();
        var chains = new List<List<int>?>();
        var tipOf = new Dictionary<int, int>(); // tip point → its chain
        var lonePairAxis = true; // pass 1: a lone pair runs along its buoyage axis; later a lone pair is too weak a hint (staggered marks)
        // pass 1: well-marked fairways, pair midpoints only, hops ≤ gapMaxM
        for (var seed = 0; seed < pts.Count && pts[seed].Pair; seed++)
            if (!used[seed] && !pts[seed].P!.Light) Grow(seed, Step.Pairs);
        lonePairAxis = false;
        // pass 2: those chains, most pairs first, go on through single marks and join other chains' tips, wider where the heading holds
        foreach (var ci in Enumerable.Range(0, chains.Count).OrderByDescending(i => chains[i]!.Count).ToList())
            while (chains[ci] is not null && Extend(ci, Step.All)) { }
        // pass 3: chains of single marks only, joining the others where they meet them (safe-water marks are never chain points)
        for (var seed = 0; seed < pts.Count; seed++)
            if (!used[seed] && pts[seed].Side != MarkSide.SafeWater) Grow(seed, Step.All);
        // the single marks the trimming will take off the chain ends go back into the pool, and passes 2 and 3 run once more:
        // a fairway's dangling tail must not keep the marks of the approach beside it (Masnedsund's beside Vordingborg's)
        for (var ci = 0; ci < chains.Count; ci++)
        {
            if (chains[ci] is not { } chain) continue;
            var before = chain.ToList();
            Untips(ci);
            Trim(chain); chain.Reverse(); Trim(chain); chain.Reverse();
            if (chain.Count == 1 && !pts[chain[0]].Pair) chain.Clear(); // a lone single mark is no chain to join
            foreach (var i in before.Except(chain)) { used[i] = false; chainOf[i] = -1; }
            if (chain.Count == 0) chains[ci] = null; else Tips(ci);
        }
        foreach (var ci in Enumerable.Range(0, chains.Count).Where(i => chains[i] is not null).OrderByDescending(i => chains[i]!.Count).ToList())
            while (chains[ci] is not null && Extend(ci, Step.All)) { }
        for (var seed = 0; seed < pts.Count; seed++)
            if (!used[seed] && !pts[seed].Via && pts[seed].Side != MarkSide.SafeWater) Grow(seed, Step.All);

        var cands = new List<List<int>>();
        foreach (var chain in chains)
        {
            if (chain is null) continue;
            Trim(chain); chain.Reverse(); Trim(chain);
            if (chain.Count == 0) continue;
            // a chain ends at a safe-water mark just ahead of it: the approach's landfall, or its junction with a through fairway
            foreach (var atEnd in (ReadOnlySpan<bool>)[true, false])
                if (SafeAhead(chain, atEnd, SafeAheadM) is { } s) { if (atEnd) chain.Add(s); else chain.Insert(0, s); }
            cands.Add(Straighten(chain.Select(i => Moved(i, chain)).ToList(), (a, b) => clear(a, b) && deep(a, b) && CardinalsOk(a, b)).Select(k => chain[k]).ToList());
        }
        // T-junctions: an end heading (within 45°) for a point of another line ≤ gapMaxM ahead ends there (a branch meeting a
        // through fairway between its tips); points: (index, coordinate), index -1 = the junction on the other line
        var owner = new Dictionary<int, int>();
        for (var k = 0; k < cands.Count; k++) foreach (var i in cands[k]) owner[i] = k;
        var lines = new List<List<(int i, Coordinate c)>>();
        for (var k = 0; k < cands.Count; k++)
        {
            var line = cands[k];
            var items = line.Select(i => (i, Moved(i, line))).ToList();
            foreach (var atEnd in (ReadOnlySpan<bool>)[true, false])
            {
                var tip = atEnd ? line[^1] : line[0];
                // a lone pair (a lit harbour entrance) looks both ways along its axis
                var axis = line.Count == 1 && pts[tip].Pair && pts[tip].P!.Light ? (atEnd ? pts[tip].B : (-pts[tip].B.x, -pts[tip].B.y)) : ((double x, double y)?)null;
                if (pts[tip].Side == MarkSide.SafeWater || (axis ?? Heading(line, atEnd)) is not { } h) continue;
                var ours = NearestPair(line, atEnd);
                // not into an approach buoyed away from ours (Bogø's end onto Stubbekøbing's approach)
                bool Away(int q) => ours >= 0 && cands[owner[q]].Where(j => pts[j].Pair).Select(j => (int?)j).MinBy(j => Dist(q, j!.Value)) is { } theirs && Diverges(ours, theirs);
                var q = Near(pts[tip].C).Where(q => owner.TryGetValue(q, out var o) && o != k && Dist(tip, q) <= gapMaxM && Turn(h, V(pts[tip].C, pts[q].C)) <= 45 && Clear(tip, q) && !Away(q))
                    .Select(q => (int?)q).MinBy(q => Dist(tip, q!.Value));
                if (q is not { } j) continue;
                var junction = (-1, Moved(j, cands[owner[j]]));
                if (atEnd) items.Add(junction); else items.Insert(0, junction);
            }
            // a lit harbour entrance joined to a fairway leads on through the entrance into the harbour (EntranceM along its axis,
            // where that is water): its passage then ends inside, not in the mouth (Glückstadt: the basin counted as reached water
            // and the route's last leg crossed the mole)
            if (line.Count == 1 && pts[line[0]].Pair && pts[line[0]].P!.Light && items.Count == 2 && water?.Water is { } isWater)
            {
                var mid = pts[line[0]].C; var outer = items[0].i < 0 ? items[0].Item2 : items[1].Item2;
                var b = pts[line[0]].B;
                var (vx, vy) = V(outer, mid);
                var sign = vx * b.x + vy * b.y >= 0 ? 1 : -1;
                var bl = Math.Sqrt(b.x * b.x + b.y * b.y);
                var step = sign * EntranceM / WaterGraphTiler.MPerDegLat / bl;
                var inner = new Coordinate(mid.X + step * b.x / Math.Cos(mid.Y * Math.PI / 180), mid.Y + step * b.y);
                if (isWater(inner) && clear(mid, inner)) { if (items[0].i < 0) items.Add((-1, inner)); else items.Insert(0, (-1, inner)); }
            }
            Unmove(items);
            if (Accepted(items)) lines.Add(items);
        }
        // cleanup: lines in order of quality; a line's ends running along a better line (within 150 m) are cut back to where it
        // leaves it, a line mostly (≥ 80 %) along better ones is dropped (unpaired marks of a channel chained beside its pairs)
        var result = new List<Fairway>(); var keptLines = new List<Coordinate[]>(); var singleRuns = new List<List<(int i, Coordinate c)>?>(); var merging = false;
        foreach (var items in lines.OrderByDescending(l => l.Count(x => x.i >= 0 && pts[x.i].Pair)).ThenByDescending(l => l.Count))
        {
            var near = items.Select(x => keptLines.Any(k => SegmentsM(x.c, k) <= 150)).ToArray();
            if (near.Count(x => x) >= 0.8 * items.Count) continue;
            int from = 0, to = items.Count - 1;
            while (from < to && near[from] && near[from + 1]) from++;
            while (to > from && near[to] && near[to - 1]) to--;
            var cut = items[from..(to + 1)];
            if (!Accepted(cut)) continue;
            var all = new List<Coordinate>();
            foreach (var (_, c) in cut)
                if (all.Count == 0 || !all[^1].Equals2D(c)) all.Add(c);
            if (all.Count < 2) continue;
            keptLines.Add([.. all]);
            // stretches of ≥ 2 single marks in a row, and any at an end of the line (a T-junction hop too), become lines of their
            // own (no pairs), sharing the pair at either end with the paired pieces: the graph build puts its ×3 channel zone round
            // lines with ≥ 2 pairs only (Fairways.PairedNote) – a junction hop or a lone pair would close the water round it
            var pair = cut.Select(x => x.i >= 0 && pts[x.i].Pair).ToArray();
            var start = 0;
            for (var k = 0; k < cut.Count;)
            {
                var run = 0;
                while (k + run < cut.Count && !pair[k + run]) run++;
                if (run >= 2 || run >= 1 && (k == 0 || k + run == cut.Count))
                {
                    int a = Math.Max(0, k - 1), b = Math.Min(cut.Count - 1, k + run);
                    if (a > start) Emit(cut[start..(a + 1)]);
                    Emit(cut[a..(b + 1)]);
                    start = b;
                }
                k += Math.Max(run, 1);
            }
            if (start < cut.Count - 1) Emit(cut[start..]);
        }
        // a stretch of single marks of one colour (a run of single marks split where the colour changes; cardinals and junction
        // points go with the stretch they are in) with single marks of the other colour beside it (≤ widePairMaxM, through charted
        // deep water; from any stretch, in a row along it): the two are the borders of one wide fairway (the Elbe: rows 1-2 km
        // apart chained as "two fairways instead of one", one run down the green row and back along the red; below Glückstadt the
        // red marks west of the Rhinplate sit in three different runs). They become one line of pairs, each mark paired with the
        // foot of its perpendicular on the other row (a synthetic partner; midpoints of the staggered marks themselves would zigzag),
        // the points without a foot (the rows' loose ends, the pairs shared with the paired pieces) kept, all in order along the
        // stretch. Runs with a merged or robbed stretch are emitted stretch by stretch without the robbed marks (consecutive
        // stretches share their boundary point), the others whole
        var stretches = new List<(int run, List<(int i, Coordinate c)> items)>();
        foreach (var (run, r) in singleRuns.Select((r, k) => (k, r!)))
        {
            var cur = new List<(int i, Coordinate c)>(); MarkSide? colour = null;
            foreach (var x in r)
            {
                var side = x.i >= 0 && !pts[x.i].Pair && pts[x.i].Side is MarkSide.Port or MarkSide.Starboard ? pts[x.i].Side : null;
                if (side is not null && colour is not null && side != colour) { cur.Add(x); stretches.Add((run, cur)); cur = [x]; colour = side; continue; }
                colour ??= side; cur.Add(x);
            }
            stretches.Add((run, cur));
        }
        var mergedRuns = new HashSet<int>(); var taken = new HashSet<int>(); var consumed = new HashSet<int>();
        var corridors = new List<List<(int i, Coordinate c)>>();
        var stretchOf = new Dictionary<int, int>();
        for (var k = 0; k < stretches.Count; k++) foreach (var x in stretches[k].items) if (x.i >= 0) stretchOf[x.i] = k;
        if (widePairMaxM > 0)
            for (var a = 0; a < stretches.Count; a++)
            {
                if (taken.Contains(a)) continue;
                var side = OneColour(stretches[a].items, MarkSide.Port) ? MarkSide.Port : OneColour(stretches[a].items, MarkSide.Starboard) ? MarkSide.Starboard : (MarkSide?)null;
                if (side is not { } colour) continue;
                var row = stretches[a].items.Where(x => x.i < 0 || !consumed.Contains(x.i)).ToList();
                var other = colour == MarkSide.Port ? MarkSide.Starboard : MarkSide.Port;
                // the other colour's single marks with a foot on this stretch, in order along it
                var beside = stretchOf.Keys.Where(i => !pts[i].Pair && pts[i].Side == other && !consumed.Contains(i) && !taken.Contains(stretchOf[i]))
                    .Select(i => (i, foot: Foot(pts[i].C, row))).Where(x => x.foot is not null).OrderBy(x => Along(x.foot!, row)).ToList();
                if (beside.Count < 2) continue;
                var b = beside.Select(x => (x.i, pts[x.i].C)).ToList();
                var bFeet = beside.Select(x => x.foot).ToList();
                var aFeet = row.Select(x => x.i >= 0 && pts[x.i].Side == colour ? Foot(pts[x.i].C, b) : null).ToList();
                // side by side: ≥ 2 marks of each row and ≥ 5 in all, or half of each row, face the other row (a sandbank between
                // the rows, the Rhinplate off Glückstadt, leaves the marks beside it without a foot)
                int af = aFeet.Count(x => x is not null), bf = bFeet.Count;
                var own = row.Count(x => x.i >= 0 && pts[x.i].Side == colour);
                if ((af < 2 || af + bf < 5) && af < 0.5 * own) continue;
                Corridor(row, aFeet, colour, b, bFeet);
                taken.Add(a); mergedRuns.Add(stretches[a].run);
                foreach (var (i, _) in beside) { consumed.Add(i); mergedRuns.Add(stretches[stretchOf[i]].run); }
            }
        merging = true;
        foreach (var c in corridors) Emit(c);
        // what is left of a robbed stretch without a lateral single mark (the cardinals along the corridor's bank) is no line
        for (var k = 0; k < stretches.Count; k++)
        {
            if (taken.Contains(k) || !mergedRuns.Contains(stretches[k].run)) continue;
            var left = stretches[k].items.Where(x => x.i < 0 || !consumed.Contains(x.i)).ToList();
            if (left.Any(x => x.i >= 0 && !pts[x.i].Pair && pts[x.i].Side is MarkSide.Port or MarkSide.Starboard)) Emit(left);
        }
        foreach (var (run, r) in singleRuns.Select((r, k) => (k, r!)))
            if (!mergedRuns.Contains(run)) Emit(r);
        return result;


        // the hops are checked for land and against the cardinals' danger quadrants with the marks moved (the chain checked them on
        // the marks): a moved mark whose hop crosses land or comes within CardinalDangerM on a cardinal's danger side goes back onto
        // its mark
        void Unmove(List<(int i, Coordinate c)> items)
        {
            for (var k = 1; k < items.Count; k++)
            {
                if (CardinalsOk(items[k - 1].c, items[k].c) && clear(items[k - 1].c, items[k].c)) continue;
                foreach (var j in (ReadOnlySpan<int>)[k - 1, k])
                    if (items[j].i >= 0 && !pts[items[j].i].Pair && !items[j].c.Equals2D(pts[items[j].i].C)) items[j] = (items[j].i, pts[items[j].i].C);
            }
        }
        // the corridor of two rows facing each other: pairs of each mark with its foot (a synthetic partner) on the other row, the
        // points without a foot kept, all in order along row a; a mark without a foot goes half the width of the corridor's nearest
        // pair towards the other row (as Moved does next to a pair; its run had none, so it sat on the row and the line would
        // zigzag); cardinals and safe-water marks of the rows (on the banks, a cable or more off the centre) stay out of the line;
        // a hop between two pair midpoints across land (rows bending round an island) or through a cardinal's danger quadrant
        // cuts the corridor there
        void Corridor(List<(int i, Coordinate c)> a, List<Coordinate?> aFeet, MarkSide aSide, List<(int i, Coordinate c)> b, List<Coordinate?> bFeet)
        {
            var merged = new List<(double at, int i, Coordinate c)>();
            var bSide = aSide == MarkSide.Port ? MarkSide.Starboard : MarkSide.Port;
            int Pair(FairwayMark m, Coordinate foot, MarkSide mSide) => mSide == MarkSide.Port ? AddPair(m, new FairwayMark(foot.Y, foot.X, MarkSide.Starboard)) : AddPair(new FairwayMark(foot.Y, foot.X, MarkSide.Port), m);
            for (var k = 0; k < a.Count; k++)
            {
                var (i, c) = a[k];
                if (aFeet[k] is { } foot) { var j = Pair(new FairwayMark(pts[i].C.Y, pts[i].C.X, aSide), foot, aSide); merged.Add((Along(pts[i].C, a), j, pts[j].C)); }
                else merged.Add((Along(i >= 0 ? pts[i].C : c, a), i, c));
            }
            for (var k = 0; k < b.Count; k++)
            {
                var (i, c) = b[k];
                if (bFeet[k] is { } foot) { var j = Pair(new FairwayMark(pts[i].C.Y, pts[i].C.X, bSide), foot, bSide); merged.Add((Along(foot, a), j, pts[j].C)); }
                else merged.Add((Along(i >= 0 ? pts[i].C : c, a), i, c));
            }
            var corridor = merged.OrderBy(x => x.at).Select(x => (x.i, x.c)).Where(x => x.i < 0 || pts[x.i].Lateral).ToList();
            for (var k = 0; k < corridor.Count; k++)
            {
                var (i, c) = corridor[k];
                if (i < 0 || pts[i].Pair || pts[i].Side is not (MarkSide.Port or MarkSide.Starboard)) continue;
                var near = corridor.Where(x => x.i >= 0 && pts[x.i].Pair && Dist(i, x.i) <= widePairMaxM).Select(x => (int?)x.i).MinBy(j => Dist(i, j!.Value));
                if (near is not { } j) continue;
                var sign = pts[i].Side == MarkSide.Port ? 0.5 : -0.5;
                corridor[k] = (i, new Coordinate(pts[i].C.X + sign * (pts[j].S!.Lng - pts[j].P!.Lng), pts[i].C.Y + sign * (pts[j].S.Lat - pts[j].P.Lat)));
            }
            Unmove(corridor);
            var from = 0;
            for (var k = 1; k < corridor.Count; k++)
                if (!clear(corridor[k - 1].c, corridor[k].c) || !CardinalsOk(corridor[k - 1].c, corridor[k].c)) { corridors.Add(corridor[from..k]); from = k; }
            corridors.Add(corridor[from..]);
        }
        // lateral marks of one colour only (≥ 2) between its end points (those are shared with the stretches or paired pieces on
        // either side: a pair, or the first mark of the next colour), no pairs of its own
        bool OneColour(List<(int i, Coordinate c)> run, MarkSide side)
        {
            var own = run.Where(x => x.i >= 0).Select(x => pts[x.i]).ToList();
            return own.Count(p => p.Side == side) >= 2 && own.Skip(1).SkipLast(1).All(p => !p.Pair && (p.Side == side || p.Side is not (MarkSide.Port or MarkSide.Starboard)));
        }
        int AddPair(FairwayMark pm, FairwayMark sm)
        {
            pts.Add(new Pt(new Coordinate((pm.Lng + sm.Lng) / 2, (pm.Lat + sm.Lat) / 2), null, pm, sm)); used.Add(true); chainOf.Add(-1);
            return pts.Count - 1;
        }
        // the foot of the perpendicular from c on a segment of the row (its marks' own positions; the end segments count half a
        // segment beyond their ends), ≤ widePairMaxM away through charted deep water, the nearest
        Coordinate? Foot(Coordinate c, List<(int i, Coordinate c)> row)
        {
            Coordinate? best = null; var bd = widePairMaxM;
            for (var k = 1; k < row.Count; k++)
            {
                Coordinate a = RowPt(row[k - 1]), b = RowPt(row[k]);
                var (bx, by) = V(a, b); var (cx, cy) = V(a, c);
                if (bx * bx + by * by == 0) continue;
                var t = (cx * bx + cy * by) / (bx * bx + by * by);
                if (t < (k == 1 ? -0.5 : 0) || t > (k == row.Count - 1 ? 1.5 : 1)) continue;
                var foot = new Coordinate(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
                var d = RoutingGraphBuilder.Haversine(c.Y, c.X, foot.Y, foot.X);
                if (d < 50 || d >= bd || !clear(c, foot) || !deep(c, foot)) continue;
                bd = d; best = foot;
            }
            return best;
        }
        Coordinate RowPt((int i, Coordinate c) x) => x.i >= 0 ? pts[x.i].C : x.c;
        // arc length (degrees of latitude) along the row to the point nearest c
        double Along(Coordinate c, List<(int i, Coordinate c)> row)
        {
            double best = double.MaxValue, at = 0, sum = 0;
            for (var k = 1; k < row.Count; k++)
            {
                Coordinate a = RowPt(row[k - 1]), b = RowPt(row[k]);
                var (bx, by) = V(a, b); var (cx, cy) = V(a, c);
                var len2 = bx * bx + by * by;
                var t = len2 > 0 ? Math.Clamp((cx * bx + cy * by) / len2, 0, 1) : 0;
                double dx = cx - t * bx, dy = cy - t * by, d = dx * dx + dy * dy;
                if (d < best) { best = d; at = sum + t * Math.Sqrt(len2); }
                sum += Math.Sqrt(len2);
            }
            return at;
        }

        void Emit(List<(int i, Coordinate c)> piece)
        {
            var coords = new List<Coordinate>();
            foreach (var (_, c) in piece)
                if (coords.Count == 0 || !coords[^1].Equals2D(c)) coords.Add(c);
            if (coords.Count < 2) return;
            var ls = f.CreateLineString(coords.ToArray());
            ls.SRID = 4326;
            // a single-mark stretch counts its single marks only (a lone pair: as one mark); its end pairs belong to the paired pieces
            int pairs = piece.Count(x => x.i >= 0 && pts[x.i].Pair), own = piece.Count(x => x.i >= 0 && !pts[x.i].Via);
            var singleRun = pairs < 2 || piece.Count > 2 && piece.Skip(1).SkipLast(1).All(x => x.i < 0 || !pts[x.i].Pair);
            if (singleRun && !merging) { singleRuns.Add(piece); return; }
            result.Add(singleRun ? new Fairway(ls, 0, Math.Max(1, own - pairs)) : new Fairway(ls, pairs, own - pairs, Area(piece), merging));
        }

        // the water between the marks of the piece's pairs: the convex hulls of each two pairs in a row, united (a quad of
        // staggered pairs may cross itself); singles between the pairs lie inside
        Polygon? Area(List<(int i, Coordinate c)> piece)
        {
            var pairs = piece.Where(x => x.i >= 0 && pts[x.i].Pair).Select(x => pts[x.i]).ToList();
            if (pairs.Count < 2) return null;
            var widths = pairs.Select(p => RoutingGraphBuilder.Haversine(p.P!.Lat, p.P.Lng, p.S!.Lat, p.S.Lng)).Order().ToList();
            if (widths[widths.Count / 2] < AreaMinWidthM) return null; // median: one wide or narrow pair does not decide
            var quads = new List<Geometry>();
            for (var k = 1; k < pairs.Count; k++)
                quads.Add(f.CreateMultiPointFromCoords([At(pairs[k - 1].P!), At(pairs[k - 1].S!), At(pairs[k].P!), At(pairs[k].S!)]).ConvexHull());
            Geometry union;
            try { union = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(quads); } catch (TopologyException) { union = f.CreateGeometryCollection(quads.ToArray()).Buffer(0); }
            var poly = union as Polygon ?? (union as MultiPolygon)?.Geometries.OfType<Polygon>().MaxBy(p => p.Area);
            if (poly is null || poly.IsEmpty) return null;
            poly.SRID = 4326;
            return poly;
        }

        // ≥ 2 pairs, 1 pair and 2 single marks, a lit pair (harbour entrance) with a junction, or ≥ 3 single marks (2 at a
        // junction or a safe-water end: short harbour approaches), at least half and ≥ 2 of them lateral, or ≥ 3 cardinals in a
        // row (CardinalRow)
        bool Accepted(List<(int i, Coordinate c)> items)
        {
            var own = items.Where(x => x.i >= 0 && !pts[x.i].Via).Select(x => pts[x.i]).ToList();
            int pairs = own.Count(p => p.Pair), singles = own.Count - pairs;
            var joined = items.Count > 0 && (items[0].i < 0 || items[^1].i < 0 || own[0].Side == MarkSide.SafeWater || own[^1].Side == MarkSide.SafeWater);
            var laterals = own.Count(p => p.Side is MarkSide.Port or MarkSide.Starboard);
            var cardinals = own.Where(p => p.Side is MarkSide.North or MarkSide.East or MarkSide.South or MarkSide.West).Select(p => p.Side).ToList();
            return pairs >= 2 || pairs == 1 && (singles >= 2 || own.Single(p => p.Pair).P!.Light && (items[0].i < 0 || items[^1].i < 0))
                || pairs == 0 && singles >= (joined ? 2 : 3) && (laterals >= 2 && 2 * laterals >= singles || cardinals.Count >= 3 && CardinalRow(items));
        }
        // ≥ 3 cardinals of one kind (a shoal's edge), or of two opposite kinds whose marks lie on opposite sides of the line (north
        // cardinals south of a channel between two shoals, south cardinals north of it); not a ring round a danger, nor east and
        // west cardinals alternating along one line
        bool CardinalRow(List<(int i, Coordinate c)> items)
        {
            var cs = items.Where(x => x.i >= 0 && pts[x.i].Side is MarkSide.North or MarkSide.East or MarkSide.South or MarkSide.West).ToList();
            var kinds = cs.Select(x => pts[x.i].Side!.Value).Distinct().ToList();
            if (kinds.Count == 1) return true;
            if (kinds.Count != 2 || !(kinds.Contains(MarkSide.North) && kinds.Contains(MarkSide.South) || kinds.Contains(MarkSide.East) && kinds.Contains(MarkSide.West))) return false;
            // side of the mark (P: the mark itself) relative to the line through the chain point's neighbours
            int Bank((int i, Coordinate c) x)
            {
                var at = items.IndexOf(x);
                var (ax, ay) = V(items[Math.Max(0, at - 1)].c, items[Math.Min(items.Count - 1, at + 1)].c);
                var (mx, my) = V(items[Math.Max(0, at - 1)].c, At(pts[x.i].P!));
                return Math.Sign(ax * my - ay * mx);
            }
            var sides = cs.GroupBy(x => pts[x.i].Side).Select(g => g.Select(Bank).Distinct().ToList()).ToList();
            return sides.All(g => g.Count == 1 && g[0] != 0) && sides[0][0] != sides[1][0];
        }

        // a new chain from a seed; its first hops had no heading to keep, so an end hop that turns more than a later hop of that
        // length may is given up again and the chain grows on from there (a lone mark's nearest neighbour is not always ahead)
        void Grow(int seed, Step step)
        {
            used[seed] = true; chainOf[seed] = chains.Count;
            var ci = chains.Count;
            chains.Add([seed]); Tips(ci);
            for (var round = 0; round < 4; round++)
            {
                while (Extend(ci, step)) { }
                var chain = chains[ci]!;
                var cut = false;
                foreach (var atEnd in (ReadOnlySpan<bool>)[true, false])
                {
                    if (chain.Count < 3) break;
                    int a = atEnd ? chain[^1] : chain[0], b = atEnd ? chain[^2] : chain[1], c = atEnd ? chain[^3] : chain[2];
                    var d = Dist(b, a);
                    if (Turn(V(pts[c].C, pts[b].C), V(pts[b].C, pts[a].C)) <= MaxTurn(d, pts[a].Pair, false)) continue;
                    Untips(ci); chain.Remove(a); used[a] = false; chainOf[a] = -1; Tips(ci); cut = true;
                }
                if (!cut) break;
            }
        }
        // big turns only on short hops: pairs 90° within 1 km, else 45°; single marks 90° within 500 m, 60° within 1 km, else 30°;
        // wide hops 30°, joins 60° within 500 m, 45° within 1 km, else 30°
        double MaxTurn(double d, bool pair, bool join) => d > gapMaxM ? 30 : join ? (d <= 500 ? 60 : d <= 1000 ? 45 : 30) : pair ? (d <= 1000 ? 90 : 45) : d <= 500 ? 90 : d <= 1000 ? 60 : 30;
        void Tips(int ci) { tipOf[chains[ci]![0]] = ci; tipOf[chains[ci]![^1]] = ci; }
        void Untips(int ci) { tipOf.Remove(chains[ci]![0]); tipOf.Remove(chains[ci]![^1]); }

        // the cheapest step at either tip: a free point (pass 1: pairs only), or another chain's tip whose own course goes on that way
        bool Extend(int ci, Step step)
        {
            var chain = chains[ci]!;
            var options = new List<(double cost, bool atEnd, int q, bool wide)>();
            foreach (var atEnd in (ReadOnlySpan<bool>)[true, false])
            {
                var tip = atEnd ? chain[^1] : chain[0];
                var h = Heading(chain, atEnd);
                foreach (var q in Near(pts[tip].C))
                {
                    if (q == tip || pts[q].Side == MarkSide.SafeWater) continue;
                    var join = used[q] ? tipOf.GetValueOrDefault(q, -1) : -1;
                    if (used[q] && (join < 0 || join == ci) || step != Step.All && (join >= 0 || !pts[q].Pair || pts[q].P!.Light)) continue; // lit pairs: not in pass 1
                    var d = Dist(tip, q);
                    var wide = d > gapMaxM;
                    if (d > wideGapM || wide && (step == Step.Pairs || h is null || !pts[q].Lateral || join < 0 && pts[tip].Pair && pts[q].Pair)) continue;
                    var max = MaxTurn(d, pts[q].Pair, join >= 0);
                    var turn = h is { } hh ? Turn(hh, V(pts[tip].C, pts[q].C)) : 0;
                    if (turn > max || join >= 0 && Onward(join, q, V(pts[tip].C, pts[q].C)) > max) continue;
                    options.Add((d * (turn > 45 ? 1 + (turn - 45) / 15 : 1), atEnd, q, wide));
                }
            }
            // a step to a dead end (no point to go on to from there) counts 4 times: a straight wide continuation beats a short turn
            // into a harbour mole (Grønsund at Hårbølle); a wide hop must lead somewhere (a join always does)
            var ranked = options.OrderBy(o => o.cost).Take(12).Select(o => (o, goesOn: step == Step.Pairs || used[o.q] || Continues(o.atEnd ? chain[^1] : chain[0], o.q)))
                .Where(x => x.goesOn || !x.o.wide).Select(x => (cost: x.goesOn ? x.o.cost : 4 * x.o.cost, x.o.atEnd, x.o.q, x.o.wide));
            foreach (var (_, atEnd, q, wide) in ranked.OrderBy(o => o.cost))
            {
                var tip = atEnd ? chain[^1] : chain[0];
                var join = used[q] ? tipOf[q] : -1;
                int ours = NearestPair(chain, atEnd), theirs = join >= 0 ? NearestPair(chains[join]!, chains[join]![^1] == q) : pts[q].Pair ? q : -1;
                if (ours < 0) ours = Near(pts[tip].C).Where(p => pts[p].Pair && chainOf[p] >= 0 && chainOf[p] != ci && chainOf[p] != join && Dist(p, tip) <= 300).Select(p => (int?)p).MinBy(p => Dist(p!.Value, tip)) ?? -1;
                if (ours >= 0 && theirs >= 0 && Diverges(ours, theirs)) continue;
                // a single mark next to a pair of a chain buoyed away from ours belongs to that approach (Stubbekøbing's marks for Bogø's)
                if (join < 0 && !pts[q].Pair && ours >= 0 && Near(pts[q].C).Any(p => pts[p].Pair && chainOf[p] >= 0 && chainOf[p] != ci && Dist(p, q) <= 300 && Diverges(ours, p))) continue;
                if (!PassesSafe(tip, q, Heading(chain, atEnd))) continue;
                // a wide gap from one single lateral mark to the next whose straight hop crosses land bends through charted deep
                // water round it (Grønsund past Falster)
                Coordinate[]? vias = null;
                if (!Clear(tip, q))
                {
                    if (water?.Path is null || join >= 0 || pts[q].Side is not (MarkSide.Port or MarkSide.Starboard) || pts[tip].Side is not (MarkSide.Port or MarkSide.Starboard) || !wide
                        || (vias = water.Path(pts[tip].C, pts[q].C)) is null) continue;
                    var way = vias.Prepend(pts[tip].C).Append(pts[q].C).ToList();
                    if (Enumerable.Range(1, way.Count - 1).Any(k => !CardinalsOk(way[k - 1], way[k]))) continue;
                }
                else if (wide && !Deep(tip, q)) continue;
                Untips(ci);
                if (join >= 0)
                {
                    var other = chains[join]!; Untips(join); chains[join] = null;
                    foreach (var i in other) chainOf[i] = ci;
                    if (other[0] != q) other.Reverse(); // other now starts at q
                    if (atEnd) chain.AddRange(other); else { other.Reverse(); chain.InsertRange(0, other); }
                }
                else
                {
                    var add = new List<int>();
                    foreach (var v in vias ?? []) { pts.Add(new Pt(v, null)); used.Add(true); chainOf.Add(ci); add.Add(pts.Count - 1); }
                    add.Add(q);
                    if (atEnd) chain.AddRange(add); else { add.Reverse(); chain.InsertRange(0, add); }
                    used[q] = true; chainOf[q] = ci;
                }
                Tips(ci);
                return true;
            }
            return false;
        }

        // from q (reached from tip) another free point, chain tip or safe-water mark lies ahead within the hop rules
        bool Continues(int tip, int q) => Near(pts[q].C).Any(r =>
        {
            if (r == q || r == tip || used[r] && !tipOf.ContainsKey(r)) return false;
            double d = Dist(q, r), turn = Turn(V(pts[tip].C, pts[q].C), V(pts[q].C, pts[r].C));
            return pts[r].Side == MarkSide.SafeWater ? d <= 1000 && turn <= 60 : d <= gapMaxM && turn <= 45 || d <= wideGapM && turn <= 30 && pts[r].Lateral;
        });

        IEnumerable<int> Near(Coordinate c)
        {
            var (kx, ky) = Key(c);
            for (var gx = kx - 1; gx <= kx + 1; gx++)
                for (var gy = ky - 1; gy <= ky + 1; gy++)
                    foreach (var q in grid[(gx, gy)]) yield return q;
        }

        // direction of travel at a tip over the last HeadingBaseM; a lone pair runs along its buoyage axis; null = too short to tell
        (double x, double y)? Heading(List<int> chain, bool atEnd)
        {
            if (chain.Count == 1) return pts[chain[0]].Pair && lonePairAxis ? (atEnd ? pts[chain[0]].B : (-pts[chain[0]].B.x, -pts[chain[0]].B.y)) : null;
            var tip = pts[atEnd ? chain[^1] : chain[0]].C;
            for (var k = 1; k < chain.Count; k++)
            {
                var back = pts[atEnd ? chain[^(k + 1)] : chain[k]].C;
                var d = RoutingGraphBuilder.Haversine(back.Y, back.X, tip.Y, tip.X);
                if (d >= HeadingBaseM || k == chain.Count - 1) return d < 100 ? null : V(back, tip);
            }
            return null;
        }

        // angle between a hop arriving at chain cj's tip q and that chain's own course on from q (a lone pair: either way along its axis)
        double Onward(int cj, int q, (double x, double y) hop)
        {
            var other = chains[cj]!;
            if (other.Count == 1) return pts[q].Pair && lonePairAxis ? Math.Min(Turn(hop, pts[q].B), Turn(hop, (-pts[q].B.x, -pts[q].B.y))) : 0;
            if (Heading(other, other[^1] == q) is not { } outward) return 0;
            return Turn(hop, (-outward.x, -outward.y));
        }

        int NearestPair(List<int> chain, bool fromEnd)
        {
            for (var k = 0; k < chain.Count; k++)
            {
                var i = fromEnd ? chain[^(k + 1)] : chain[k];
                if (pts[i].Pair) return i;
            }
            return -1;
        }

        // two pairs whose buoyage directions point away from each other: approaches leaving a common water in opposite directions
        bool Diverges(int a, int b)
        {
            var v = V(pts[a].C, pts[b].C);
            return Cos(pts[a].B, v) < -0.5 && Cos(pts[b].B, v) > 0.5;
        }

        // a safe-water mark ahead of the tip and nearer than q is passed only on a straight course close by (a through fairway at a
        // junction); otherwise the chain ends there (the approach's landfall) and never goes on beyond it
        bool PassesSafe(int tip, int q, (double x, double y)? h)
        {
            var dq = Dist(tip, q);
            foreach (var s in Near(pts[tip].C))
            {
                if (pts[s].Side != MarkSide.SafeWater) continue;
                var ds = Dist(tip, s);
                if (ds < 30 || ds >= dq || h is { } hh && Turn(hh, V(pts[tip].C, pts[s].C)) > 60) continue;
                if (Turn(V(pts[tip].C, pts[s].C), V(pts[s].C, pts[q].C)) > 30 || SegmentM(pts[s].C, pts[tip].C, pts[q].C) > 300) return false;
            }
            return true;
        }

        int? SafeAhead(List<int> chain, bool atEnd, double maxM)
        {
            var tip = atEnd ? chain[^1] : chain[0];
            var h = Heading(chain, atEnd);
            return Near(pts[tip].C).Where(s => pts[s].Side == MarkSide.SafeWater && !chain.Contains(s) && Dist(tip, s) <= maxM
                    && (h is not { } hh || Turn(hh, V(pts[tip].C, pts[s].C)) <= 60) && Clear(tip, s))
                .Select(s => (int?)s).MinBy(s => Dist(tip, s!.Value));
        }

        // drops the chain's leading single marks unless ≥ 2 of them come before the first pair and the end one is lateral
        void Trim(List<int> chain)
        {
            var firstPair = chain.FindIndex(i => pts[i].Pair);
            if (firstPair < 0) return;
            while (chain.Count > 0 && !pts[chain[0]].Lateral) { chain.RemoveAt(0); firstPair--; }
            if (firstPair == 1) chain.RemoveAt(0);
        }

        // a single lateral mark moved towards the channel by half the width of the chain's nearest pair (at most 150 m): within
        // 3 km along that pair's port → starboard direction; farther off (or without pairs: 50 m; at most 100 m) square to the line,
        // on the side the buoyage direction voted by the marks around it gives (port marks on the left when going with it)
        Coordinate Moved(int i, List<int> line)
        {
            var p = pts[i];
            if (p.Side is not (MarkSide.Port or MarkSide.Starboard)) return p.C;
            // a mark at the end of a bend through deep water stays put: the bend was found from the mark (Stockholm → Sandhamn)
            var at0 = line.IndexOf(i);
            if (at0 > 0 && pts[line[at0 - 1]].Via || at0 < line.Count - 1 && pts[line[at0 + 1]].Via) return p.C;
            var kn = Enumerable.Range(0, line.Count).Where(k => pts[line[k]].Pair).Select(k => (int?)k).MinBy(k => Dist(i, line[k!.Value]));
            var near = kn is { } n ? pts[line[n]] : null;
            var width = near is null ? 100 : RoutingGraphBuilder.Haversine(near.P!.Lat, near.P.Lng, near.S!.Lat, near.S.Lng);
            // a pair within PairReachM rules; farther off (≤ 3 km) only where the charted depth beside the row gives no side (Ulvsund:
            // the bridge gate's 100 m width would have kept the marks of the wide row before it on the row)
            Coordinate ByPair()
            {
                var k = (p.Side == MarkSide.Port ? 0.5 : -0.5) * Math.Min(1, 2 * (width > pairMaxM ? SingleOffsetMaxM : 150) / Math.Max(width, 1));
                return new Coordinate(p.C.X + k * (near!.S!.Lng - near.P!.Lng), p.C.Y + k * (near.S.Lat - near.P.Lat));
            }
            if (near is not null && Dist(i, line[kn!.Value]) <= PairReachM) return ByPair();
            // a row without pairs: the channel is the deepest charted band beside the row (probed square to it every 50 m up to
            // 2 RowOffsetM, on both sides); the mark goes half the width of that band's run towards it, 50-RowOffsetM, on the
            // side whose deepest band is deeper (Ulvsund: red marks along the north-west edge of the 5.5 m band, 300-500 m wide, the
            // expert's track ~150 m south-east of them; a narrow 200 m band: 100 m). The same floor both sides, or no band data:
            // the buoyage vote below
            if (water?.Floor is { } floorAt)
            {
                var t0 = V(pts[line[Math.Max(0, at0 - 1)]].C, pts[line[Math.Min(line.Count - 1, at0 + 1)]].C);
                var l0 = Math.Sqrt(t0.x * t0.x + t0.y * t0.y);
                if (l0 > 0)
                {
                    (double floor, double run) Probe(double sx, double sy)
                    {
                        var floors = new List<double>();
                        for (var d = 50.0; d <= 2 * RowOffsetM; d += 50)
                        {
                            var q = Shift(p.C, sx, sy, d);
                            if (!clear(p.C, q) || floorAt(q) is not { } fl) break;
                            floors.Add(fl);
                        }
                        return floors.Count == 0 ? (double.NaN, 0) : (floors.Max(), 50 * floors.Count(fl => fl == floors.Max()));
                    }
                    var left = Probe(-t0.y / l0, t0.x / l0); var right = Probe(t0.y / l0, -t0.x / l0);
                    if (!double.IsNaN(left.floor) && (double.IsNaN(right.floor) || left.floor > right.floor)) return Shift(p.C, -t0.y / l0, t0.x / l0, Math.Clamp(left.run / 2, 50, RowOffsetM));
                    if (!double.IsNaN(right.floor) && (double.IsNaN(left.floor) || right.floor > left.floor)) return Shift(p.C, t0.y / l0, -t0.x / l0, Math.Clamp(right.run / 2, 50, RowOffsetM));
                }
            }
            if (near is not null && Dist(i, line[kn!.Value]) <= 3000) return ByPair();
            // which way the buoyage runs along the line here: the marks around it vote (a port mark left of the line through its
            // neighbours says "with the line"); too few votes → no offset (a pair kilometres off may sit beyond a junction)
            var at = line.IndexOf(i);
            var vote = 0;
            for (var k = Math.Max(1, at - 4); k <= Math.Min(line.Count - 2, at + 4); k++)
            {
                var mk = pts[line[k]];
                if (mk.Side is not (MarkSide.Port or MarkSide.Starboard)) continue;
                var (ax, ay) = V(pts[line[k - 1]].C, pts[line[k + 1]].C); var (mx, my) = V(pts[line[k - 1]].C, mk.C);
                var cross = ax * my - ay * mx;
                if (Math.Abs(cross) > 1e-12) vote += (mk.Side == MarkSide.Port ? 1 : -1) * Math.Sign(cross);
            }
            if (Math.Abs(vote) < 2) return p.C;
            var t = V(pts[line[Math.Max(0, at - 1)]].C, pts[line[Math.Min(line.Count - 1, at + 1)]].C);
            var len = Math.Sqrt(t.x * t.x + t.y * t.y);
            if (len == 0) return p.C;
            double bx = Math.Sign(vote) * t.x / len, by = Math.Sign(vote) * t.y / len;
            var (nx, ny) = p.Side == MarkSide.Port ? (by, -bx) : (-by, bx); // right of the buoyage direction for a port mark
            var m = Math.Min(width / 2, 100) / WaterGraphTiler.MPerDegLat;
            return new Coordinate(p.C.X + nx * m / Math.Cos(p.C.Y * Math.PI / 180), p.C.Y + ny * m);
        }
    }

    /// <summary>
    /// Indices of the points kept when out-and-back corners are cut: an interior point where the line turns by more than 90° is
    /// dropped when <paramref name="straightOk"/> holds for the hop between its neighbours (clear of land, charted deep), repeatedly.
    /// </summary>
    public static List<int> Straighten(IReadOnlyList<Coordinate> line, Func<Coordinate, Coordinate, bool> straightOk)
    {
        var kept = Enumerable.Range(0, line.Count).ToList();
        for (var changed = true; changed;)
        {
            changed = false;
            for (var k = 1; k < kept.Count - 1; k++)
            {
                Coordinate a = line[kept[k - 1]], v = line[kept[k]], c = line[kept[k + 1]];
                if (Turn(V(a, v), V(v, c)) <= 90 || !straightOk(a, c)) continue;
                kept.RemoveAt(k); changed = true; break;
            }
        }
        return kept;
    }

    /// <summary>
    /// Marks and water checks for <see cref="Derive"/> in a box (lng/lat): the <see cref="MarkTypes"/> from the Seamarks table, the land
    /// of the routing graph (routing_land of the land version, Block corrections excluded as in the graph build) and the charted depth
    /// areas. Clear: a hop crosses at most <see cref="LandMaxM"/> of land between water (sampled every 10 m; land at its ends, a beacon
    /// on a mole, does not count). Deep: clear and ≥ 90 % of the hop (sampled every 50 m) inside depth areas; no depth data → never deep.
    /// Without the routing tables (no graph built yet) every hop is clear and none deep.
    /// </summary>
    public static async Task<(List<FairwayMark> marks, FairwayWater water)> InputsAsync(NpgsqlConnection conn, Envelope box, CancellationToken ct)
    {
        var marks = new List<FairwayMark>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT "Type", "Category", "Colour", ST_Y(g), ST_X(g) FROM (SELECT "Type", "Category",
              CASE WHEN "Type" = 'light_minor' THEN "TagsJson"->>'seamark:light:colour' ELSE "Colour" END "Colour", ST_Transform("Location", 4326) g FROM "Seamarks"
            WHERE "Type" = ANY(@types) AND "Location" && ST_Transform(ST_MakeEnvelope(@w, @s, @e, @n, 4326), 3857)) x
            """, conn))
        {
            cmd.Parameters.AddWithValue("types", MarkTypes); Box(cmd, box);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                if (SideOf(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)) is { } side)
                    marks.Add(new FairwayMark(r.GetDouble(3), r.GetDouble(4), side, r.GetString(0) == "light_minor"));
        }
        var land = new STRtree<IndexedPointInAreaLocator>(); var depth = new STRtree<IndexedPointInAreaLocator>();
        var floors = new STRtree<(IndexedPointInAreaLocator at, double floor)>();
        bool hasTables;
        await using (var cmd = new NpgsqlCommand("SELECT to_regclass('routing_land') IS NOT NULL AND to_regclass('routing_meta') IS NOT NULL AND to_regclass('routing_depth') IS NOT NULL", conn))
            hasTables = (bool)(await cmd.ExecuteScalarAsync(ct))!;
        if (hasTables)
        {
            async Task Load(string sql, STRtree<IndexedPointInAreaLocator> into, bool withFloor = false)
            {
                await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 600 };
                Box(cmd, box);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var g = r.GetFieldValue<Geometry>(0);
                    foreach (var poly in g is Polygon p ? [p] : g is MultiPolygon mp ? mp.Geometries.OfType<Polygon>() : g is GeometryCollection gc ? gc.Geometries.OfType<Polygon>() : [])
                    {
                        var at = new IndexedPointInAreaLocator(poly);
                        into.Insert(poly.EnvelopeInternal, at);
                        if (withFloor) floors.Insert(poly.EnvelopeInternal, (at, r.GetDouble(1)));
                    }
                }
            }
            await Load("""
                SELECT ST_ClipByBox2D(l.geom, ST_MakeEnvelope(@w, @s, @e, @n, 4326)) FROM routing_land l
                WHERE l.graph_version = (SELECT value::int FROM routing_meta WHERE key = 'land_version') AND l.geom && ST_MakeEnvelope(@w, @s, @e, @n, 4326)
                  AND NOT EXISTS (SELECT 1 FROM "RoutingCorrections" c WHERE c."Active" AND c."Kind" = 'Block' AND c."Geometry" ~= l.geom)
                """, land);
            await Load("SELECT geom, min_depth_m FROM routing_depth WHERE geom && ST_MakeEnvelope(@w, @s, @e, @n, 4326)", depth, withFloor: true);
        }
        // water that routing_land draws as land: inland Water corrections (Teltowkanal). Not the corridor of the imported OSM
        // waterways: "waterway=fairway" lines run through skerries, and land next to them is land (Stockholm → Sandhamn)
        var waterAreas = new STRtree<IndexedPointInAreaLocator>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT "Geometry" FROM "RoutingCorrections" WHERE "Active" AND "Kind" = 'Water' AND "Geometry" && ST_MakeEnvelope(@w, @s, @e, @n, 4326)
            """, conn) { CommandTimeout = 600 })
        {
            Box(cmd, box);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                foreach (var poly in r.GetFieldValue<Geometry>(0) is var g && g is Polygon p ? [p] : g is MultiPolygon mp ? mp.Geometries.OfType<Polygon>() : [])
                    waterAreas.Insert(poly.EnvelopeInternal, new IndexedPointInAreaLocator(poly));
        }
        land.Build(); depth.Build(); waterAreas.Build(); floors.Build();
        bool In(STRtree<IndexedPointInAreaLocator> tree, Coordinate c) => tree.Query(new Envelope(c)).Any(l => l.Locate(c) != Location.Exterior);
        bool OnLand(Coordinate c) => In(land, c) && !In(waterAreas, c);
        bool Clear(Coordinate a, Coordinate b)
        {
            var n = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / 10));
            int run = 0, start = -1;
            var water = false;
            for (var i = 0; i <= n; i++)
            {
                if (OnLand(new Coordinate(a.X + (b.X - a.X) * i / n, a.Y + (b.Y - a.Y) * i / n))) { if (run++ == 0) start = i; continue; }
                // a run between water: ≤ LandMaxM; from the start (a beacon on a mole): ≤ EndLandM
                if (run > 0 && run * 10.0 > (start > 0 ? LandMaxM : EndLandM)) return false;
                run = 0; water = true;
            }
            // up to the end ≤ EndLandM; a hop on land all along (a river the coastline draws as land) is left alone
            return !water || run * 10.0 <= EndLandM;
        }
        bool Deep(Coordinate a, Coordinate b)
        {
            var n = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / 50));
            var inDepth = Enumerable.Range(0, n + 1).Count(i => In(depth, new Coordinate(a.X + (b.X - a.X) * i / n, a.Y + (b.Y - a.Y) * i / n)));
            return inDepth >= 0.9 * (n + 1) && Clear(a, b);
        }
        // no land at all on it (20 m samples) and ≥ 90 % in charted depth
        bool OpenWay(Coordinate a, Coordinate b)
        {
            var n = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / 20));
            var inDepth = 0;
            for (var i = 0; i <= n; i++)
            {
                var c = new Coordinate(a.X + (b.X - a.X) * i / n, a.Y + (b.Y - a.Y) * i / n);
                if (OnLand(c)) return false;
                if (In(depth, c)) inDepth++;
            }
            return inDepth >= 0.9 * (n + 1);
        }
        // A* over 100 m cells of charted depth that are not land, within 1.5 km round the hop; only where the straight hop has
        // ≥ PathLandM of land on it, and only a gentle bend (≤ 3 points, ≤ 1.25 × the straight length), not a way through skerries
        // the cells' centres are pulled tight again (a point is dropped where the hop past it stays deep)
        Coordinate[]? Path(Coordinate a, Coordinate b)
        {
            const double cellM = 100;
            double dy = cellM / WaterGraphTiler.MPerDegLat, dx = dy / Math.Cos(a.Y * Math.PI / 180), m = 1500 / WaterGraphTiler.MPerDegLat;
            double x0 = Math.Min(a.X, b.X) - m / Math.Cos(a.Y * Math.PI / 180), y0 = Math.Min(a.Y, b.Y) - m;
            int nx = (int)((Math.Max(a.X, b.X) + m / Math.Cos(a.Y * Math.PI / 180) - x0) / dx) + 1, ny = (int)((Math.Max(a.Y, b.Y) + m - y0) / dy) + 1;
            if (nx * ny > 60000 || depth.Count == 0) return null;
            var straight = RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X);
            // only round a real obstacle (≥ PathLandM of land between water on the straight hop), not through skerries
            var samples = (int)Math.Ceiling(straight / 20);
            var interior = Enumerable.Range(1, samples - 1).Count(i => OnLand(new Coordinate(a.X + (b.X - a.X) * i / samples, a.Y + (b.Y - a.Y) * i / samples)));
            if (interior * 20 < PathLandM) return null;
            Coordinate Centre(int k) => new(x0 + (k % nx + 0.5) * dx, y0 + (k / nx + 0.5) * dy);
            int CellOf(Coordinate c) => Math.Clamp((int)((c.Y - y0) / dy), 0, ny - 1) * nx + Math.Clamp((int)((c.X - x0) / dx), 0, nx - 1);
            int from = CellOf(a), to = CellOf(b);
            var ok = new sbyte[nx * ny]; // 0 unknown, 1 water, -1 not
            bool Water(int k)
            {
                if (k == from || k == to) return true;
                if (ok[k] == 0) { var c = Centre(k); ok[k] = (sbyte)(In(depth, c) && !OnLand(c) ? 1 : -1); }
                return ok[k] > 0;
            }
            var g = new double[nx * ny]; Array.Fill(g, double.MaxValue); var prev = new int[nx * ny];
            var open = new PriorityQueue<int, double>();
            double H(int k) => Math.Sqrt(Math.Pow(k % nx - to % nx, 2) + Math.Pow(k / nx - to / nx, 2)) * cellM;
            g[from] = 0; open.Enqueue(from, H(from));
            while (open.TryDequeue(out var k, out var f))
            {
                if (k == to) break;
                if (f > 1.5 * straight) return null;
                for (var ddx = -1; ddx <= 1; ddx++)
                    for (var ddy = -1; ddy <= 1; ddy++)
                    {
                        int cx = k % nx + ddx, cy = k / nx + ddy;
                        if ((ddx | ddy) == 0 || cx < 0 || cy < 0 || cx >= nx || cy >= ny) continue;
                        var nk = cy * nx + cx;
                        var cost = g[k] + (ddx != 0 && ddy != 0 ? 1.4142 : 1) * cellM;
                        if (cost >= g[nk] || !Water(nk)) continue;
                        g[nk] = cost; prev[nk] = k; open.Enqueue(nk, cost + H(nk));
                    }
            }
            if (g[to] == double.MaxValue) return null;
            var cells = new List<Coordinate> { b };
            for (var k = prev[to]; k != from; k = prev[k]) cells.Add(Centre(k));
            cells.Add(a); cells.Reverse();
            var vias = new List<Coordinate>();
            for (var i = 0; i < cells.Count - 1;)
            {
                var j = cells.Count - 1;
                while (j > i + 1 && !OpenWay(cells[i], cells[j])) j--;
                if (j < cells.Count - 1) vias.Add(cells[j]);
                i = j;
            }
            var len = vias.Prepend(a).Append(b).Zip(vias.Append(b), (p, q) => RoutingGraphBuilder.Haversine(p.Y, p.X, q.Y, q.X)).Sum();
            return vias.Count is > 0 and <= 3 && len <= 1.25 * straight ? [.. vias] : null; // a gentle bend round one obstacle
        }
        double? Floor(Coordinate c)
        {
            double? best = null;
            foreach (var (at, floor) in floors.Query(new Envelope(c)))
                if (at.Locate(c) != Location.Exterior && (best is null || floor > best)) best = floor;
            return best;
        }
        return (marks, new FairwayWater(Clear, Deep, Path, c => !OnLand(c), floors.Count > 0 ? Floor : null));

        static void Box(NpgsqlCommand cmd, Envelope b)
        {
            cmd.Parameters.AddWithValue("w", b.MinX); cmd.Parameters.AddWithValue("s", b.MinY);
            cmd.Parameters.AddWithValue("e", b.MaxX); cmd.Parameters.AddWithValue("n", b.MaxY);
        }
    }

    private static Coordinate At(FairwayMark m) => new(m.Lng, m.Lat);
    private static bool Cached(Dictionary<(int, int), bool> cache, int a, int b, Func<bool> test)
    {
        var key = a < b ? (a, b) : (b, a);
        if (!cache.TryGetValue(key, out var ok)) cache[key] = ok = test();
        return ok;
    }
    /// <summary>
    /// The hop a → b stays out of the cardinal mark's danger quadrant within <see cref="CardinalDangerM"/> (the 90° sector round the
    /// direction opposite its name: south of a north cardinal, …), in a metric frame round the mark.
    /// </summary>
    public static bool PassesCardinal(Coordinate a, Coordinate b, FairwayMark m)
    {
        var o = new Coordinate(m.Lng, m.Lat);
        var (ax, ay) = V(o, a); var (bx, by) = V(o, b);
        double r = CardinalDangerM / WaterGraphTiler.MPerDegLat, dx = bx - ax, dy = by - ay;
        // t range where |a + t (b − a)| ≤ r
        double qa = dx * dx + dy * dy, qb = 2 * (ax * dx + ay * dy), qc = ax * ax + ay * ay - r * r;
        double t0, t1;
        if (qa == 0) { if (qc > 0) return true; t0 = t1 = 0; }
        else
        {
            var disc = qb * qb - 4 * qa * qc;
            if (disc < 0) return true;
            t0 = Math.Max(0, (-qb - Math.Sqrt(disc)) / (2 * qa)); t1 = Math.Min(1, (-qb + Math.Sqrt(disc)) / (2 * qa));
            if (t0 > t1) return true;
        }
        // the danger quadrant: within 45° of the direction opposite the named side (along ≤ −|across|); along and across are
        // linear in t, so the ends and the point where across changes sign decide
        var (ux, uy) = m.Side switch { MarkSide.North => (0.0, 1.0), MarkSide.South => (0.0, -1.0), MarkSide.East => (1.0, 0.0), _ => (-1.0, 0.0) };
        bool Danger(double t) { double x = ax + t * dx, y = ay + t * dy, along = x * ux + y * uy, across = -x * uy + y * ux; return -along > Math.Abs(across) + 1e-12; }
        var dAcross = -dx * uy + dy * ux;
        var tc = dAcross != 0 ? -(-ax * uy + ay * ux) / dAcross : double.NaN;
        return !(Danger(t0) || Danger(t1) || tc >= t0 && tc <= t1 && Danger(tc));
    }

    /// <summary>Metres from c to the nearest of the segments.</summary>
    private static double SegmentsM(Coordinate c, Coordinate[] line) => Enumerable.Range(1, line.Length - 1).Min(i => SegmentM(c, line[i - 1], line[i]));
    /// <summary>Metres from c to the segment a–b (local metric frame).</summary>
    private static double SegmentM(Coordinate c, Coordinate a, Coordinate b)
    {
        var (bx, by) = V(a, b); var (cx, cy) = V(a, c);
        var t = bx * bx + by * by > 0 ? Math.Clamp((cx * bx + cy * by) / (bx * bx + by * by), 0, 1) : 0;
        return Math.Sqrt((cx - t * bx) * (cx - t * bx) + (cy - t * by) * (cy - t * by)) * WaterGraphTiler.MPerDegLat;
    }
    /// <summary>a → b in a local metric frame (degrees of latitude).</summary>
    private static (double x, double y) V(Coordinate a, Coordinate b) => ((b.X - a.X) * Math.Cos(a.Y * Math.PI / 180), b.Y - a.Y);
    /// <summary>c moved metres along the unit direction (nx, ny) of the local metric frame.</summary>
    private static Coordinate Shift(Coordinate c, double nx, double ny, double metres)
    {
        var m = metres / WaterGraphTiler.MPerDegLat;
        return new Coordinate(c.X + nx * m / Math.Cos(c.Y * Math.PI / 180), c.Y + ny * m);
    }
    private static double Cos((double x, double y) u, (double x, double y) v)
    {
        double lu = Math.Sqrt(u.x * u.x + u.y * u.y), lv = Math.Sqrt(v.x * v.x + v.y * v.y);
        return lu > 0 && lv > 0 ? (u.x * v.x + u.y * v.y) / (lu * lv) : 1;
    }
    /// <summary>Angle between two directions in degrees (0 when one has no length).</summary>
    private static double Turn((double x, double y) h, (double x, double y) v) => Math.Acos(Math.Clamp(Cos(h, v), -1, 1)) * 180 / Math.PI;

    /// <summary>Web Mercator (EPSG:3857, the sea-mark storage projection) → WGS84.</summary>
    public static (double lat, double lng) FromMercator(double x, double y)
    {
        const double r = 6378137.0;
        var lng = x / r * 180 / Math.PI;
        var lat = (2 * Math.Atan(Math.Exp(y / r)) - Math.PI / 2) * 180 / Math.PI;
        return (lat, lng);
    }
}
