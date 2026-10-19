using System.Collections.Concurrent;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Distance;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Area routing (spec 10, "open bodies of water"): instead of a grid over the water, the graph consists of the convex
/// corners of the (buffered, simplified) land, a sparse lattice for the open sea, anchors for harbours and passage ends,
/// and the corners of shallow banks. Two nodes are joined when the straight line between them does not cross land, so a
/// route on open water is a handful of straight legs; canals, fairways and buoyed channels stay precomputed passage
/// lines (RoutingGraphBuilder step 4) that connect the areas.
/// </summary>
public static class VisibilityGraph
{
    /// <param name="NarrowLegM">Legs up to this length that fail the coast-buffer visibility but pass against the fine obstacles are added at ×NarrowLegFactor (short constrictions: a sound entrance, a harbour mouth).</param>
    public sealed record Options(double LatticeM = 20_000, double LatticeClearM = 3_000, double MaxVisibilityM = 40_000, int VisibleNeighbours = 24, double NarrowLegM = 1_000, double NarrowLegFactor = 1.5);

    /// <summary>Nodes sit this far outside the obstacle boundary, so a leg along a straight stretch of coast touches nothing and a leg into land is a plain intersection.</summary>
    public const double OffsetM = 1.0;
    private static double OffsetDeg => OffsetM / WaterGraphTiler.MPerDegLat;

    /// <summary>p moved by OffsetM in the direction of (dx, dy) (in degrees, longitude scaled by the latitude).</summary>
    public static Coordinate Offset(Coordinate p, double dx, double dy)
    {
        var cos = Math.Cos(p.Y * Math.PI / 180);
        var len = Math.Sqrt(dx * cos * dx * cos + dy * dy);
        return len == 0 ? p : new Coordinate(p.X + dx * cos / len * OffsetDeg / cos, p.Y + dy / len * OffsetDeg);
    }

    /// <summary>Convex corners of the obstacle rings (the points a shortest path can bend at); holes contribute the corners of the land that pokes into them.</summary>
    /// <summary>True when the point lies on an edge of the z<paramref name="zoom"/> tile grid (within ~10 m): the depth areas come per
    /// tile, so their polygons have artificial corners along every tile edge.</summary>
    public static bool OnTileEdge(Coordinate c, int zoom = 12)
    {
        var n = 1 << zoom;
        var tx = (c.X + 180) / 360 * n;
        var latR = c.Y * Math.PI / 180;
        var ty = (1 - Math.Log(Math.Tan(latR) + 1 / Math.Cos(latR)) / Math.PI) / 2 * n;
        static bool Near(double v) { var f = v - Math.Floor(v); return f < 1e-3 || f > 1 - 1e-3; }
        return Near(tx) || Near(ty);
    }

    public static List<Coordinate> Corners(IEnumerable<Polygon> obstacles, Envelope region)
    {
        var result = new List<Coordinate>();
        foreach (var poly in obstacles)
        {
            AddCorners(poly.ExteriorRing, true, region, result);
            for (var i = 0; i < poly.NumInteriorRings; i++) AddCorners(poly.GetInteriorRingN(i), false, region, result);
        }
        return result;
    }

    private static void AddCorners(LineString ring, bool exterior, Envelope region, List<Coordinate> into)
    {
        var cs = ring.Coordinates;
        if (cs.Length < 4) return;
        var ccw = Orientation.IsCCW(cs);
        var n = cs.Length - 1; // closed ring: last == first
        for (var i = 0; i < n; i++)
        {
            var prev = cs[(i - 1 + n) % n]; var cur = cs[i]; var next = cs[(i + 1) % n];
            var idx = Orientation.Index(prev, cur, next);
            if (idx == OrientationIndex.Collinear || !region.Contains(cur)) continue;
            var convex = idx == (ccw ? OrientationIndex.CounterClockwise : OrientationIndex.Clockwise);
            if (convex != exterior) continue;
            // outward bisector: away from both neighbours (for a convex corner that points out of the obstacle; for a hole corner into the hole)
            var (ax, ay) = Unit(cur.X - prev.X, cur.Y - prev.Y); var (bx, by) = Unit(cur.X - next.X, cur.Y - next.Y);
            into.Add(Offset(cur, ax + bx, ay + by));
        }
    }

    /// <summary>Holes (enclosed water pockets) smaller than maxKm2 are removed from the polygon.</summary>
    public static Polygon FillSmallHoles(Polygon poly, double maxKm2)
    {
        if (poly.NumInteriorRings == 0) return poly;
        var keep = new List<LinearRing>();
        for (var i = 0; i < poly.NumInteriorRings; i++)
        {
            var ring = (LinearRing)poly.GetInteriorRingN(i);
            var areaKm2 = Math.Abs(Area.OfRing(ring.CoordinateSequence)) * WaterGraphTiler.MPerDegLat * WaterGraphTiler.MPerDegLat * Math.Cos(ring.Centroid.Y * Math.PI / 180) / 1e6;
            if (areaKm2 > maxKm2) keep.Add(ring);
        }
        return keep.Count == poly.NumInteriorRings ? poly : poly.Factory.CreatePolygon((LinearRing)poly.ExteriorRing, keep.ToArray());
    }

    private static (double x, double y) Unit(double dx, double dy) { var l = Math.Sqrt(dx * dx + dy * dy); return l == 0 ? (0, 0) : (dx / l, dy / l); }

    /// <summary>Lattice points over the region that keep LatticeClearM from every obstacle (bend candidates for long open-sea legs).</summary>
    public static List<Coordinate> Lattice(Envelope region, STRtree<IPreparedGeometry> obstacles, Options o)
    {
        var result = new List<Coordinate>();
        var dLat = o.LatticeM / WaterGraphTiler.MPerDegLat;
        var clearDeg = o.LatticeClearM / WaterGraphTiler.MPerDegLat;
        for (var lat = region.MinY + dLat / 2; lat < region.MaxY; lat += dLat)
        {
            var dLng = dLat / Math.Cos(lat * Math.PI / 180);
            for (var lng = region.MinX + dLng / 2; lng < region.MaxX; lng += dLng)
            {
                var p = new Point(lng, lat);
                var env = new Envelope(lng - clearDeg / Math.Cos(lat * Math.PI / 180), lng + clearDeg / Math.Cos(lat * Math.PI / 180), lat - clearDeg, lat + clearDeg);
                var probe = Geo.Factory.ToGeometry(env);
                if (obstacles.Query(env).Any(ob => ob.Intersects(probe))) continue;
                result.Add(p.Coordinate);
            }
        }
        return result;
    }

    /// <summary>A point in water for each harbour or passage end: the point itself when it lies outside every obstacle, else the nearest
    /// point on the boundary of the obstacles containing it, a step into the water. Runs in parallel (prepared geometries per thread, see
    /// Edges); the nearest boundary point comes from a facet index per polygon (a plain DistanceOp walked every vertex of a coast polygon
    /// for each of the 700 k passage ends: 4 min in v60).</summary>
    public static Coordinate[] Anchors(IReadOnlyList<Coordinate> points, IReadOnlyList<Polygon> polygons, Action? tick = null)
    {
        using var perThread = new ThreadLocal<STRtree<IPreparedGeometry>>(() => Index(polygons));
        var byEnvelope = new STRtree<int>();
        for (var k = 0; k < polygons.Count; k++) byEnvelope.Insert(polygons[k].EnvelopeInternal, k);
        byEnvelope.Build();
        var facets = new ConcurrentDictionary<int, Lazy<IndexedFacetDistance>>();
        var result = new Coordinate[points.Count];
        Parallel.For(0, points.Count, i =>
        {
            tick?.Invoke();
            var p = points[i]; var pt = Geo.Factory.CreatePoint(p);
            if (!perThread.Value!.Query(new Envelope(p)).Any(ob => ob.Contains(pt))) { result[i] = p; return; }
            var best = p; var bestD = double.MaxValue;
            foreach (var k in byEnvelope.Query(new Envelope(p)).Order()) // polygon order: on a tie the first polygon wins
            {
                if (!polygons[k].EnvelopeInternal.Contains(p)) continue;
                var near = facets.GetOrAdd(k, k => new Lazy<IndexedFacetDistance>(() => new IndexedFacetDistance(polygons[k].Boundary))).Value.NearestPoints(pt)[0];
                var d = near.Distance(p);
                if (d < bestD) { bestD = d; best = near; }
            }
            result[i] = Offset(best, best.X - p.X, best.Y - p.Y); // a step past the boundary into the water
        });
        return result;
    }

    /// <summary>Visibility edges: each node to its nearest visible neighbours within MaxVisibilityM (both directions deduplicated).</summary>
    /// <summary>Visibility edges. Prepared geometries are not safe to share between threads (their lazily built indexes answered
    /// wrong under contention), so every worker thread prepares its own copy of the obstacle polygons.</summary>
    public static List<GraphEdge> Edges(IReadOnlyList<GraphNode> nodes, IReadOnlyList<Polygon> polygons, Options o) => Edges(nodes, polygons, null, o);

    /// <param name="finePolygons">Land with a small buffer: a short leg that the coast buffer closes (a constriction narrower than twice the buffer)
    /// is still added when it clears these, at ×NarrowLegFactor. Null = off.</param>
    public static List<GraphEdge> Edges(IReadOnlyList<GraphNode> nodes, IReadOnlyList<Polygon> polygons, IReadOnlyList<Polygon>? finePolygons, Options o, Action? tick = null)
    {
        using var perThread = new ThreadLocal<STRtree<IPreparedGeometry>>(() => Index(polygons));
        using var perThreadFine = new ThreadLocal<STRtree<IPreparedGeometry>?>(() => finePolygons is null ? null : Index(finePolygons));
        var narrow = new ConcurrentDictionary<(long, long), bool>();
        var index = new STRtree<GraphNode>();
        foreach (var n in nodes) index.Insert(new Envelope(n.X, n.X, n.Y, n.Y), n);
        index.Build();
        var maxDeg = o.MaxVisibilityM / WaterGraphTiler.MPerDegLat;
        var pairs = new ConcurrentBag<(long a, long b, GraphNode na, GraphNode nb)>();
        Parallel.ForEach(nodes, n =>
        {
            tick?.Invoke();
            var cos = Math.Cos(n.Y * Math.PI / 180);
            var env = new Envelope(n.X - maxDeg / cos, n.X + maxDeg / cos, n.Y - maxDeg, n.Y + maxDeg);
            var candidates = index.Query(env).Where(m => m.Id != n.Id)
                .Select(m => (m, d: RoutingGraphBuilder.Haversine(n.Y, n.X, m.Y, m.X))).Where(x => x.d <= o.MaxVisibilityM).OrderBy(x => x.d);
            // the nearest visible nodes, but spread over 8 direction sectors: a node on a dense chain (bank corners along a
            // contour, a jagged coast) would otherwise fill every slot with chain neighbours and no leg would ever cross the chain
            var found = 0; var examined = 0;
            var perSector = Math.Max(2, o.VisibleNeighbours / 8);
            var sectors = new int[8];
            foreach (var (m, _) in candidates)
            {
                if (found >= o.VisibleNeighbours || examined >= 8 * o.VisibleNeighbours) break;
                var sector = (int)Math.Floor((Math.Atan2(m.Y - n.Y, (m.X - n.X) * cos) + Math.PI) / (2 * Math.PI) * 8) & 7;
                if (sectors[sector] >= perSector) continue;
                examined++;
                if (!Visible(n, m, perThread.Value!))
                {
                    // a short leg through a constriction the coast buffer closes: allowed against the fine land, priced
                    var fine = perThreadFine.Value;
                    if (fine is null || RoutingGraphBuilder.Haversine(n.Y, n.X, m.Y, m.X) > o.NarrowLegM || !Visible(n, m, fine)) continue;
                    narrow[(Math.Min(n.Id, m.Id), Math.Max(n.Id, m.Id))] = true;
                }
                found++; sectors[sector]++;
                if (n.Id < m.Id) pairs.Add((n.Id, m.Id, n, m)); else pairs.Add((m.Id, n.Id, m, n));
            }
        });
        var result = new List<GraphEdge>();
        foreach (var (a, b, na, nb) in pairs.Distinct(new PairKey()))
        {
            var e = new GraphEdge(a, b, RoutingGraphBuilder.Haversine(na.Y, na.X, nb.Y, nb.X), Geo.Factory.CreateLineString([new Coordinate(na.X, na.Y), new Coordinate(nb.X, nb.Y)]));
            result.Add(narrow.ContainsKey((a, b)) ? e with { Cost = e.Cost * o.NarrowLegFactor } : e);
        }
        return result;
    }

    private sealed class PairKey : IEqualityComparer<(long a, long b, GraphNode na, GraphNode nb)>
    {
        public bool Equals((long a, long b, GraphNode na, GraphNode nb) x, (long a, long b, GraphNode na, GraphNode nb) y) => x.a == y.a && x.b == y.b;
        public int GetHashCode((long a, long b, GraphNode na, GraphNode nb) x) => HashCode.Combine(x.a, x.b);
    }

    /// <summary>Nodes lie OffsetM outside every obstacle, so touching an obstacle at all means the leg runs into land.</summary>
    public static bool Visible(GraphNode a, GraphNode b, STRtree<IPreparedGeometry> obstacles)
    {
        var seg = Geo.Factory.CreateLineString([new Coordinate(a.X, a.Y), new Coordinate(b.X, b.Y)]);
        foreach (var ob in obstacles.Query(seg.EnvelopeInternal))
            if (ob.Intersects(seg)) return false;
        return true;
    }

    public static STRtree<IPreparedGeometry> Index(IEnumerable<Polygon> polygons)
    {
        var index = new STRtree<IPreparedGeometry>();
        foreach (var p in polygons)
        {
            var prepared = PreparedGeometryFactory.Prepare(p);
            // the prepared predicates build their point-in-area and segment indexes lazily; warm them up here, single-threaded,
            // before Edges() hammers them from all cores (a racing lazy init answered wrong for a few edges)
            var c = p.ExteriorRing.Coordinates;
            prepared.Contains(p.Factory.CreatePoint(c[0]));
            prepared.Intersects(p.Factory.CreateLineString([c[0], c[1]]));
            index.Insert(p.EnvelopeInternal, prepared);
        }
        index.Build();
        return index;
    }
}
