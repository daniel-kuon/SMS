using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Distance;

namespace Sms.Domain;

/// <summary>
/// Piers, jetties, pontoons, breakwaters and groynes are not part of the coastline land the graph is built on. After the
/// string pulling, every leg that crosses one (buffered by clearM) is rerouted locally: a small visibility graph over the
/// corners of the buffered obstacles around the crossing, searched with A*, the smoother's blockers (land, low bridges,
/// shallow areas, channel zone) still blocking. A route end (harbour point) on a pier is reached from the nearest water next
/// to it. Never fails: a leg without a detour stays as it was.
/// </summary>
public static class PierAvoidance
{
    private const double MPerDeg = 111_320.0;

    /// <param name="piers">Pier geometries (lines or polygons, EPSG:4326), indexed by envelope.</param>
    /// <param name="blocked">True when the straight leg between two points crosses another obstacle (land etc.).</param>
    /// <param name="clearM">Distance kept from a pier; legs are tested against 0.6 × clearM, so detours along the buffer's corners stay legal.</param>
    /// <param name="windowM">The rerouted stretch reaches this far before the first and after the last crossing of a leg.</param>
    public static List<Coordinate> Apply(IReadOnlyList<Coordinate> route, STRtree<Geometry> piers, Func<Coordinate, Coordinate, bool> blocked, double clearM = 5, double windowM = 300) =>
        Apply(route, null, piers, blocked, clearM, windowM).points;

    /// <summary>Apply with per-segment passage flags carried through: a leg kept as it was keeps its flag, detour legs are free.</summary>
    public static (List<Coordinate> points, List<bool> fixedSeg) Apply(IReadOnlyList<Coordinate> route, IReadOnlyList<bool>? fixedSeg, STRtree<Geometry> piers, Func<Coordinate, Coordinate, bool> blocked, double clearM = 5, double windowM = 300)
    {
        if (route.Count < 2) return (route.ToList(), fixedSeg?.ToList() ?? []);
        var buffered = new Dictionary<Geometry, (Geometry outer, IPreparedGeometry core)>(ReferenceEqualityComparer.Instance);
        (Geometry outer, IPreparedGeometry core) Buffered(Geometry g) => Buffer(g, buffered, clearM);
        List<(Geometry outer, IPreparedGeometry core)> Near(Envelope env, double m)
        {
            var e = new Envelope(env); var lat = env.Centre.Y;
            e.ExpandBy(m / (MPerDeg * Math.Cos(lat * Math.PI / 180)), m / MPerDeg);
            return piers.Query(e).Select(Buffered).ToList();
        }
        bool CrossesPier(Coordinate p, Coordinate q) => Crosses(piers, buffered, p, q, clearM);
        // nearest water next to a point inside a pier: the closest point on the boundary of the merged buffers around it, seen
        // from 1 m towards the neighbouring route point (a harbour point on a jetty's centreline: the side the boat comes from)
        Coordinate? Water(Coordinate p, Coordinate toward)
        {
            var d = Dist(p, toward);
            if (!Near(new Envelope(p), clearM).Any(o => o.core.Contains(p.Factory().CreatePoint(p)))) return null;
            var near = Near(new Envelope(p), 200);
            var pt = p.Factory().CreatePoint(d < 1 ? p : Lerp(p, toward, 1 / d));
            var union = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(near.Select(o => o.outer).ToList());
            return DistanceOp.NearestPoints(union.Boundary, pt)[0].Copy();
        }

        var start = route[0]; var end = route[^1];
        var startWater = Water(start, route[1]); var endWater = Water(end, route[^2]);
        var plan = new List<Coordinate> { startWater ?? start };
        for (var i = 1; i < route.Count - 1; i++) plan.Add(Water(route[i], route[i - 1]) ?? route[i]);
        plan.Add(endWater ?? end);

        var result = new List<Coordinate> { start }; var flags = new List<bool>();
        if (startWater is not null) { result.Add(startWater); flags.Add(false); }
        for (var k = 0; k < plan.Count - 1; k++)
        {
            var leg = Detour(plan[k], plan[k + 1], k == 0, k == plan.Count - 2);
            result.AddRange(leg);
            for (var q = 0; q < leg.Count; q++) flags.Add(leg.Count == 1 && fixedSeg is not null && fixedSeg[k]);
        }
        if (endWater is not null) { result.Add(end); flags.Add(false); }
        return (result, flags);

        // the leg a→b, rerouted where it crosses a pier; returns the points after a (ending with b)
        List<Coordinate> Detour(Coordinate a, Coordinate b, bool exemptA, bool exemptB)
        {
            var seg = a.Factory().CreateLineString([a, b]);
            var crossed = Near(seg.EnvelopeInternal, 2 * clearM).Where(o => o.core.Intersects(seg)).ToList();
            if (crossed.Count == 0) return [b];
            double t0 = 1, t1 = 0;
            foreach (var o in crossed)
                foreach (var c in o.core.Geometry.Intersection(seg).Coordinates) { var t = Param(a, b, c); t0 = Math.Min(t0, t); t1 = Math.Max(t1, t); }
            var pad = windowM / Math.Max(Dist(a, b), 1e-6);
            var ta = Math.Max(0, t0 - pad); var tb = Math.Min(1, t1 + pad);
            var wa = ta == 0 ? a : Lerp(a, b, ta); var wb = tb == 1 ? b : Lerp(a, b, tb);
            // the leg's ends stand on land at a harbour (the smoother lets the first and last leg cross it), so only there land is not checked
            var exempt0 = exemptA && ta == 0; var exempt1 = exemptB && tb == 1;
            var box = new Envelope(wa, wb);
            foreach (var o in crossed) box.ExpandToInclude(o.outer.EnvelopeInternal);
            var verts = new List<Coordinate> { wa, wb };
            // a shortest path bends only at convex corners of the merged buffers (a finger pier's root inside the main jetty's buffer never)
            var merged = NetTopologySuite.Operation.Union.UnaryUnionOp.Union(Near(box, 100).Select(o => o.outer).ToList());
            for (var i = 0; i < merged.NumGeometries; i++)
                if (merged.GetGeometryN(i) is Polygon poly)
                {
                    Corners(poly.ExteriorRing.Coordinates, true, verts);
                    foreach (var hole in poly.InteriorRings) Corners(hole.Coordinates, false, verts);
                }
            // ponytail: O(n²) visibility over all corners in the box; a marina has a few hundred, enough
            if (verts.Count > 3000) return [b];
            bool Visible(int u, int v)
            {
                if (CrossesPier(verts[u], verts[v])) return false;
                var exempt = (exempt0 && (u == 0 || v == 0)) || (exempt1 && (u == 1 || v == 1));
                return exempt || !blocked(verts[u], verts[v]);
            }
            // A* from wa (0) to wb (1)
            var g = new double[verts.Count]; Array.Fill(g, double.MaxValue); g[0] = 0;
            var prev = new int[verts.Count]; Array.Fill(prev, -1);
            var closed = new bool[verts.Count];
            var open = new PriorityQueue<int, double>(); open.Enqueue(0, Dist(wa, wb));
            while (open.TryDequeue(out var u, out _))
            {
                if (closed[u]) continue;
                closed[u] = true;
                if (u == 1) break;
                for (var v = 1; v < verts.Count; v++)
                {
                    if (closed[v]) continue;
                    var d = g[u] + Dist(verts[u], verts[v]);
                    // not shorter than the best way to the target found so far: no visibility test needed
                    if (d >= g[v] || d + Dist(verts[v], wb) >= g[1] || !Visible(u, v)) continue;
                    g[v] = d; prev[v] = u; open.Enqueue(v, d + Dist(verts[v], wb));
                }
            }
            if (!closed[1]) return [b]; // no way around: keep the leg
            var path = new List<Coordinate>();
            for (var v = 1; v != 0; v = prev[v]) path.Add(verts[v]);
            path.Reverse();
            if (ta > 0) path.Insert(0, wa);
            if (tb < 1) path.Add(b);
            return path;
        }
    }

    /// <summary>A test for "the leg p→q crosses a pier" (its buffer core, 0.6 × clearM), the same the rerouting uses: for the final vertex removal.</summary>
    public static Func<Coordinate, Coordinate, bool> Crossing(STRtree<Geometry> piers, double clearM = 5)
    {
        var buffered = new Dictionary<Geometry, (Geometry outer, IPreparedGeometry core)>(ReferenceEqualityComparer.Instance);
        return (p, q) => { lock (buffered) return Crosses(piers, buffered, p, q, clearM); };
    }

    private static bool Crosses(STRtree<Geometry> piers, Dictionary<Geometry, (Geometry outer, IPreparedGeometry core)> buffered, Coordinate p, Coordinate q, double clearM)
    {
        var seg = p.Factory().CreateLineString([p, q]);
        var tol = 2 * clearM / (MPerDeg * Math.Cos(p.Y * Math.PI / 180)); // in degrees, generous in latitude
        foreach (var g in piers.Query(Expand(seg.EnvelopeInternal, tol)))
            if (Buffer(g, buffered, clearM).core.Intersects(seg)) return true;
        return false;
    }

    private static (Geometry outer, IPreparedGeometry core) Buffer(Geometry g, Dictionary<Geometry, (Geometry outer, IPreparedGeometry core)> buffered, double clearM)
    {
        if (buffered.TryGetValue(g, out var b)) return b;
        // buffer in a local metric frame (a buffer in degrees would be an ellipse)
        var c = g.EnvelopeInternal.Centre; var cos = Math.Cos(c.Y * Math.PI / 180);
        var toM = new AffineTransformation().Translate(-c.X, -c.Y).Scale(cos * MPerDeg, MPerDeg);
        var toDeg = toM.GetInverse();
        var m = toM.Transform(g);
        return buffered[g] = (toDeg.Transform(m.Buffer(clearM, 1)), PreparedGeometryFactory.Prepare(toDeg.Transform(m.Buffer(clearM * 0.6, 1))));
    }

    /// <summary>Corners of a ring that bulge into the water: convex ones of an outer ring, reflex ones of a hole.</summary>
    private static void Corners(Coordinate[] ring, bool shell, List<Coordinate> into)
    {
        var n = ring.Length - 1; // closed
        if (n < 3) return;
        var turn = NetTopologySuite.Algorithm.Orientation.IsCCW(ring) == shell ? NetTopologySuite.Algorithm.OrientationIndex.CounterClockwise : NetTopologySuite.Algorithm.OrientationIndex.Clockwise;
        for (var i = 0; i < n; i++)
            if (NetTopologySuite.Algorithm.Orientation.Index(ring[(i - 1 + n) % n], ring[i], ring[(i + 1) % n]) == turn) into.Add(ring[i]);
    }

    private static GeometryFactory Factory(this Coordinate _) => Shared;
    private static readonly GeometryFactory Shared = new(new PrecisionModel(), 4326);
    private static Envelope Expand(Envelope e, double d) { var r = new Envelope(e); r.ExpandBy(d); return r; }
    private static Coordinate Lerp(Coordinate a, Coordinate b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    private static double Param(Coordinate a, Coordinate b, Coordinate c)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l = dx * dx + dy * dy;
        return l == 0 ? 0 : Math.Clamp(((c.X - a.X) * dx + (c.Y - a.Y) * dy) / l, 0, 1);
    }
    /// <summary>Metres, equirectangular (legs here are short).</summary>
    public static double Dist(Coordinate a, Coordinate b)
    {
        var dx = (b.X - a.X) * MPerDeg * Math.Cos((a.Y + b.Y) / 2 * Math.PI / 180); var dy = (b.Y - a.Y) * MPerDeg;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
