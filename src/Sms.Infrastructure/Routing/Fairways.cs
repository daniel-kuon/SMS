using NetTopologySuite.Geometries;

namespace Sms.Infrastructure.Routing;

public enum LateralSide { Port, Starboard }
public sealed record LateralMark(double Lat, double Lng, LateralSide Side);
/// <param name="Singles">Unpaired marks the chain runs past where it bridges a stretch marked on one side only.</param>
public sealed record Fairway(LineString Line, int Pairs, int Singles = 0);

/// <summary>
/// Spec 10 §10.5 step 2: fairway centrelines from OpenSeaMap lateral marks. Each port mark is paired with the
/// nearest starboard mark across the channel (IALA A: red/port left when entering), the midpoints are chained
/// along the channel and become passages that the routing graph prefers. Two chains whose tips lie up to <c>bridgeMaxM</c> apart
/// are joined through the unpaired marks between them (a stretch marked on one side only: single starboard marks past
/// Karshamnsgrund, 3 km between the paired stretches of the Mönsterås fairway, so the route went through Öknöström and the
/// skerries): each such mark stands for a point half the nearest pair's width off it towards the channel, the walk goes on
/// within 60° of its heading, hops ≤ gapMaxM, and adds nothing unless it reaches the other chain.
/// ponytail: greedy nearest-neighbour pairing and chaining; no use of buoy sequence numbers or fairway areas yet.
/// </summary>
public static class Fairways
{
    /// <summary>Maps OSM seamark type/category to a lateral side, or null for anything else.</summary>
    public static LateralSide? SideOf(string type, string? category) => type is "buoy_lateral" or "beacon_lateral"
        ? category switch { "port" or "preferred_channel_starboard" => LateralSide.Port, "starboard" or "preferred_channel_port" => LateralSide.Starboard, _ => null }
        : null;

    public static List<Fairway> Derive(IReadOnlyList<LateralMark> marks, GeometryFactory f, double pairMaxM = 600, double gapMaxM = 2000, double bridgeMaxM = 6000)
    {
        var port = marks.Where(m => m.Side == LateralSide.Port).ToList();
        var starboard = marks.Where(m => m.Side == LateralSide.Starboard).ToList();
        // 1. pairs: closest port/starboard couples first, each mark used once
        var candidates = new List<(double d, int p, int s)>();
        for (var p = 0; p < port.Count; p++)
            for (var s = 0; s < starboard.Count; s++)
            {
                var d = RoutingGraphBuilder.Haversine(port[p].Lat, port[p].Lng, starboard[s].Lat, starboard[s].Lng);
                if (d <= pairMaxM) candidates.Add((d, p, s));
            }
        var usedP = new HashSet<int>(); var usedS = new HashSet<int>();
        var mids = new List<Coordinate>(); var pairs = new List<(LateralMark p, LateralMark s)>();
        foreach (var (_, p, s) in candidates.OrderBy(c => c.d))
        {
            if (usedP.Contains(p) || usedS.Contains(s)) continue;
            usedP.Add(p); usedS.Add(s);
            mids.Add(new Coordinate((port[p].Lng + starboard[s].Lng) / 2, (port[p].Lat + starboard[s].Lat) / 2));
            pairs.Add((port[p], starboard[s]));
        }
        // 2. chains: grow from an unvisited midpoint to the nearest unvisited one within the gap, at both ends
        var left = new List<Coordinate>(mids);
        var chains = new List<List<Coordinate>>();
        while (left.Count > 0)
        {
            var chain = new LinkedList<Coordinate>();
            chain.AddFirst(left[0]); left.RemoveAt(0);
            while (true)
            {
                // extend whichever tip is closest to a remaining midpoint, so the chain stays ordered along the channel
                var best = -1; var bestD = gapMaxM; var atEnd = true;
                var last = chain.Last!.Value; var first = chain.First!.Value;
                for (var i = 0; i < left.Count; i++)
                {
                    var dEnd = RoutingGraphBuilder.Haversine(last.Y, last.X, left[i].Y, left[i].X);
                    if (dEnd < bestD) { bestD = dEnd; best = i; atEnd = true; }
                    var dStart = RoutingGraphBuilder.Haversine(first.Y, first.X, left[i].Y, left[i].X);
                    if (dStart < bestD) { bestD = dStart; best = i; atEnd = false; }
                }
                if (best < 0) break;
                if (atEnd) chain.AddLast(left[best]); else chain.AddFirst(left[best]);
                left.RemoveAt(best);
            }
            chains.Add([.. chain]);
        }
        // 3. one-sided stretches: unpaired marks as points half the nearest pair's port→starboard offset towards the channel
        var singles = new List<Coordinate>();
        foreach (var m in port.Where((_, i) => !usedP.Contains(i)).Concat(starboard.Where((_, i) => !usedS.Contains(i))))
        {
            if (pairs.Count == 0) break;
            var (pp, ss) = pairs.MinBy(x => RoutingGraphBuilder.Haversine(m.Lat, m.Lng, x.p.Lat, x.p.Lng));
            var sign = m.Side == LateralSide.Port ? 0.5 : -0.5;
            singles.Add(new Coordinate(m.Lng + sign * (ss.Lng - pp.Lng), m.Lat + sign * (ss.Lat - pp.Lat)));
        }
        var bridged = new int[chains.Count]; // single marks per chain
        for (var joined = true; joined && singles.Count > 0;)
        {
            joined = false;
            for (var a = 0; a < chains.Count && !joined; a++)
                foreach (var tail in (ReadOnlySpan<bool>)[true, false])
                {
                    var ca = chains[a];
                    if (ca.Count < 2) break;
                    Coordinate tip = tail ? ca[^1] : ca[0], prev = tail ? ca[^2] : ca[1];
                    if (Walk(tip, prev, a) is not { } w) continue;
                    var cb = chains[w.chain];
                    var other = w.head ? cb : Enumerable.Reverse(cb).ToList(); // b from its joined tip on
                    var merged = (tail ? ca : Enumerable.Reverse(ca)).Concat(w.path).Concat(other).ToList();
                    foreach (var p in w.path) singles.Remove(p);
                    chains[a] = merged; bridged[a] += bridged[w.chain] + w.path.Count;
                    chains.RemoveAt(w.chain); bridged = [.. bridged.Where((_, i) => i != w.chain)];
                    joined = true; break;
                }
        }
        var result = new List<Fairway>();
        for (var i = 0; i < chains.Count; i++)
        {
            if (chains[i].Count < 2) continue;
            var line = f.CreateLineString(chains[i].ToArray());
            line.SRID = 4326;
            result.Add(new Fairway(line, chains[i].Count - bridged[i], bridged[i]));
        }
        return result;

        // from a chain tip onwards (heading prev → tip) through single points to another chain's tip, or null
        (int chain, bool head, List<Coordinate> path)? Walk(Coordinate tip, Coordinate prev, int own)
        {
            var path = new List<Coordinate>(); var at = tip; var from = prev; var walked = 0.0;
            while (walked <= bridgeMaxM)
            {
                (int chain, bool head, double d) best = (-1, false, gapMaxM);
                for (var c = 0; c < chains.Count; c++)
                {
                    if (c == own) continue;
                    foreach (var head in (ReadOnlySpan<bool>)[true, false])
                    {
                        var t = head ? chains[c][0] : chains[c][^1];
                        var d = RoutingGraphBuilder.Haversine(at.Y, at.X, t.Y, t.X);
                        if (d < best.d && Ahead(from, at, t)) best = (c, head, d);
                    }
                }
                var next = singles.Where(s => !path.Contains(s) && Ahead(from, at, s)).Select(s => (s, d: RoutingGraphBuilder.Haversine(at.Y, at.X, s.Y, s.X))).Where(x => x.d <= gapMaxM).OrderBy(x => x.d).FirstOrDefault();
                // the other chain when it is nearer than the next single mark (a plain gap is the chaining's business)
                if (best.chain >= 0 && (next.s is null || best.d <= next.d)) return path.Count > 0 && walked + best.d <= bridgeMaxM ? (best.chain, best.head, path) : null;
                if (next.s is null) return null;
                path.Add(next.s); walked += next.d; from = at; at = next.s;
            }
            return null;
        }
    }

    /// <summary>q lies within 60° of the heading a → b, seen from b (local metric frame).</summary>
    private static bool Ahead(Coordinate a, Coordinate b, Coordinate q)
    {
        var cos = Math.Cos(b.Y * Math.PI / 180);
        double hx = (b.X - a.X) * cos, hy = b.Y - a.Y, qx = (q.X - b.X) * cos, qy = q.Y - b.Y;
        double hl = Math.Sqrt(hx * hx + hy * hy), ql = Math.Sqrt(qx * qx + qy * qy);
        return hl > 0 && ql > 0 && (hx * qx + hy * qy) / (hl * ql) >= 0.5;
    }

    /// <summary>Web Mercator (EPSG:3857, the sea-mark storage projection) → WGS84.</summary>
    public static (double lat, double lng) FromMercator(double x, double y)
    {
        const double r = 6378137.0;
        var lng = x / r * 180 / Math.PI;
        var lat = (2 * Math.Atan(Math.Exp(y / r)) - Math.PI / 2) * 180 / Math.PI;
        return (lat, lng);
    }
}
