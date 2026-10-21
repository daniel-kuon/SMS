using NetTopologySuite.Geometries;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Merges chains of passage edges through degree-2 vertices into one edge per junction-to-junction stretch. A vertex is a
/// junction when it is kept explicitly (passage ends, vertices with a link), belongs to the water graph, or has a number of
/// passage edges other than two. The merged edge carries the concatenated geometry, the summed cost and the strictest depth
/// and clearance (and channel floor) of its parts, and whether any part passes under a bridge of unknown clearance. A vertex where an assumed (or ferry, or traffic) stretch meets an ordinary one stays a junction (the flags are per edge). The DTM depth
/// of the merged edge is the shallowest of its parts' known depths (a part's band floor or its DTM depth, whichever is deeper): uncertain
/// for a minimum depth exactly when one of its parts was.
/// </summary>
public static class PassageContraction
{
    public static List<GraphEdge> Contract(IReadOnlyList<GraphEdge> edges, IReadOnlySet<long> keep, Func<long, bool> isWaterNode)
    {
        var incident = new Dictionary<long, List<int>>();
        for (var i = 0; i < edges.Count; i++)
        {
            Add(incident, edges[i].Source, i); Add(incident, edges[i].Target, i);
        }
        bool Junction(long n) => keep.Contains(n) || isWaterNode(n) || !incident.TryGetValue(n, out var l) || l.Count != 2 || edges[l[0]].Assumed != edges[l[1]].Assumed || edges[l[0]].Ferry != edges[l[1]].Ferry || edges[l[0]].Traffic != edges[l[1]].Traffic;
        var consumed = new bool[edges.Count];
        var result = new List<GraphEdge>();
        foreach (var start in incident.Keys.Where(Junction))
        {
            foreach (var first in incident[start])
            {
                if (consumed[first]) continue;
                // walk from start through degree-2 vertices until the next junction
                var coords = new List<Coordinate>(); var cost = 0.0; double? clearance = null, depth = null, floor = null; var unknownBridge = false; var known = double.PositiveInfinity; var anyDtm = false;
                var node = start; var idx = first;
                while (true)
                {
                    consumed[idx] = true;
                    var e = edges[idx];
                    var forward = e.Source == node;
                    var cs = e.Geom.Coordinates; if (!forward) Array.Reverse(cs);
                    if (coords.Count == 0) coords.AddRange(cs); else coords.AddRange(cs.Skip(1));
                    cost += e.Cost;
                    if (e.ClearanceM is { } c) clearance = clearance is { } cur ? Math.Min(cur, c) : c;
                    if (e.MinDepthM is { } d) depth = depth is { } cd ? Math.Min(cd, d) : d;
                    if (e.FloorM is { } f) floor = floor is { } cf ? Math.Min(cf, f) : f;
                    unknownBridge |= e.UnknownBridge;
                    anyDtm |= e.DtmMinM is not null;
                    if ((e.MinDepthM ?? e.FloorM) is not null) known = Math.Min(known, Math.Max(Math.Min(e.MinDepthM ?? double.MaxValue, e.FloorM ?? double.MaxValue), e.DtmMinM ?? double.MinValue));
                    node = forward ? e.Target : e.Source;
                    if (Junction(node)) break;
                    var next = incident[node].First(j => j != idx);
                    if (consumed[next]) break; // a loop back onto itself
                    idx = next;
                }
                if (coords.Count >= 2) result.Add(new GraphEdge(start, node, cost, edges[0].Geom.Factory.CreateLineString(coords.ToArray()), clearance, depth, edges[first].Assumed, FloorM: floor, UnknownBridge: unknownBridge, Ferry: edges[first].Ferry, Traffic: edges[first].Traffic, DtmMinM: anyDtm && !double.IsPositiveInfinity(known) ? known : null));
            }
        }
        // cycles without any junction (a closed ring of channel pieces): kept as they are
        for (var i = 0; i < edges.Count; i++) if (!consumed[i]) result.Add(edges[i]);
        return result;
    }

    private static void Add(Dictionary<long, List<int>> d, long key, int i) { if (!d.TryGetValue(key, out var l)) d[key] = l = []; l.Add(i); }
}
