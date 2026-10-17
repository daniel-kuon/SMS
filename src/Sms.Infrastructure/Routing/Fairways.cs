using NetTopologySuite.Geometries;

namespace Sms.Infrastructure.Routing;

public enum LateralSide { Port, Starboard }
public sealed record LateralMark(double Lat, double Lng, LateralSide Side);
public sealed record Fairway(LineString Line, int Pairs);

/// <summary>
/// Spec 10 §10.5 step 2: fairway centrelines from OpenSeaMap lateral marks. Each port mark is paired with the
/// nearest starboard mark across the channel (IALA A: red/port left when entering), the midpoints are chained
/// along the channel and become passages that the routing graph prefers.
/// ponytail: greedy nearest-neighbour pairing and chaining; no use of buoy sequence numbers or fairway areas yet.
/// </summary>
public static class Fairways
{
    /// <summary>Maps OSM seamark type/category to a lateral side, or null for anything else.</summary>
    public static LateralSide? SideOf(string type, string? category) => type is "buoy_lateral" or "beacon_lateral"
        ? category switch { "port" or "preferred_channel_starboard" => LateralSide.Port, "starboard" or "preferred_channel_port" => LateralSide.Starboard, _ => null }
        : null;

    public static List<Fairway> Derive(IReadOnlyList<LateralMark> marks, GeometryFactory f, double pairMaxM = 600, double gapMaxM = 2000)
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
        var mids = new List<Coordinate>();
        foreach (var (_, p, s) in candidates.OrderBy(c => c.d))
        {
            if (usedP.Contains(p) || usedS.Contains(s)) continue;
            usedP.Add(p); usedS.Add(s);
            mids.Add(new Coordinate((port[p].Lng + starboard[s].Lng) / 2, (port[p].Lat + starboard[s].Lat) / 2));
        }
        // 2. chains: grow from an unvisited midpoint to the nearest unvisited one within the gap, at both ends
        var left = new List<Coordinate>(mids);
        var result = new List<Fairway>();
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
            if (chain.Count < 2) continue;
            var line = f.CreateLineString(chain.ToArray());
            line.SRID = 4326;
            result.Add(new Fairway(line, chain.Count));
        }
        return result;
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
