using NetTopologySuite.Densify;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Operation.Distance;
using NetTopologySuite.Operation.Linemerge;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;
using NetTopologySuite.Triangulate;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Narrow natural channels (archipelago sounds, river mouths, gaps between islands) are closed by the coast buffer of the
/// area graph: water within CoastBufferM of land is not open water there. Where such water lies more than OpenMarginM
/// away from any open water – a sound narrower than 2 × CoastBufferM, a bay behind a gap – its medial axis (Voronoi
/// skeleton of the densified shoreline) becomes a derived passage, with its ends extended to the open water so the
/// graph build anchors them. The coastal fringe along an open shore never qualifies: it is within OpenMarginM of open
/// water along its whole length.
/// </summary>
public static class NarrowChannels
{
    public sealed record Options(double FineBufferM = 20, double CoastBufferM = 150, double OpenMarginM = 100, double MinClearanceM = 20, double DensifyM = 15, double FillHoleKm2 = 3, double MinCoreAreaM2 = 20_000, double MinChannelM = 300);

    /// <summary>Channel centrelines (WGS84) inside <paramref name="tile"/> for the raw land polygons that touch it.</summary>
    public static List<LineString> Derive(IReadOnlyList<Geometry> rawLand, Envelope tile, Options o, GeometryFactory f)
    {
        var cos = Math.Cos((tile.MinY + tile.MaxY) / 2 * Math.PI / 180);
        double Deg(double m) => m / WaterGraphTiler.MPerDegLat;
        var box = f.ToGeometry(new Envelope(tile.MinX * cos, tile.MaxX * cos, tile.MinY, tile.MaxY));
        var pieces = rawLand.Where(g => g.EnvelopeInternal.Intersects(tile)).Select(g => Scale(g, cos)).ToList();
        if (pieces.Count == 0) return [];
        Geometry land;
        try { land = UnaryUnionOp.Union(pieces); } catch (TopologyException) { land = new GeometryCollection(pieces.ToArray()).Buffer(0); }
        var fine = TopologyPreservingSimplifier.Simplify(land.Buffer(Deg(o.FineBufferM)), Deg(5));
        var water = Diff(box, fine);
        if (water.IsEmpty) return [];
        var coarse = fine.Buffer(Deg(o.CoastBufferM - o.FineBufferM));
        coarse = Polygons(coarse).Select(p => (Geometry)VisibilityGraph.FillSmallHoles(p, o.FillHoleKm2)).Aggregate((Geometry)f.CreateEmpty(Dimension.Surface), (acc, p) => OverlayNGRobust.Overlay(acc, p, SpatialFunction.Union));
        var open = Diff(water, coarse);
        var narrow = OverlayNGRobust.Overlay(water, coarse, SpatialFunction.Intersection);
        if (narrow.IsEmpty) return [];
        // the part of the narrow water that is not merely the fringe of an open shore
        var core = open.IsEmpty ? narrow : Diff(narrow, open.Buffer(Deg(o.OpenMarginM)));
        core = core.Buffer(-Deg(15)).Buffer(Deg(15)); // slivers along the shore
        var minArea = Deg(1) * Deg(1) * o.MinCoreAreaM2;
        var waterPrepared = PreparedGeometryFactory.Prepare(water);
        var openBoundary = open.IsEmpty ? null : open.Boundary;
        var segments = new List<LineString>();
        foreach (var piece in Polygons(core).Where(p => p.Area >= minArea))
        {
            var inner = piece.Buffer(-Deg(o.MinClearanceM));
            if (inner.IsEmpty) continue;
            var innerPrepared = PreparedGeometryFactory.Prepare(inner);
            // sites snapped to ~1 cm and deduplicated: near-coincident sites make the diagram's clipping overlay fail
            var sites = Densifier.Densify(piece, Deg(o.DensifyM)).Coordinates.Select(c => new Coordinate(Math.Round(c.X, 7), Math.Round(c.Y, 7))).Distinct().ToList();
            if (sites.Count < 4) continue;
            var builder = new VoronoiDiagramBuilder { Tolerance = 1e-9 };
            builder.SetSites(sites);
            var env = new Envelope(piece.EnvelopeInternal); env.ExpandBy(Deg(50));
            builder.ClipEnvelope = env;
            GeometryCollection diagram;
            try { diagram = (GeometryCollection)builder.GetDiagram(f); }
            catch (TopologyException) { builder.ClipEnvelope = null; diagram = (GeometryCollection)builder.GetDiagram(f); }
            var seen = new HashSet<(long, long, long, long)>();
            var lines = new List<LineString>();
            foreach (var cell in Polygons(diagram))
            {
                var ring = cell.ExteriorRing.Coordinates;
                for (var i = 1; i < ring.Length; i++)
                {
                    var a = ring[i - 1]; var b = ring[i];
                    var key = Key(a, b);
                    if (!seen.Add(key)) continue;
                    var seg = f.CreateLineString([a, b]);
                    if (innerPrepared.Covers(seg)) lines.Add(seg);
                }
            }
            if (lines.Count == 0) continue;
            // leaves of the skeleton reach for the open water: a short straight extension through the fine water
            var degree = new Dictionary<(long, long), int>();
            foreach (var l in lines) foreach (var c in new[] { l.StartPoint.Coordinate, l.EndPoint.Coordinate }) { var k = Key(c); degree[k] = degree.GetValueOrDefault(k) + 1; }
            if (openBoundary is not null)
                foreach (var leaf in lines.SelectMany(l => new[] { l.StartPoint.Coordinate, l.EndPoint.Coordinate }).Where(c => degree[Key(c)] == 1).DistinctBy(Key).ToList())
                {
                    var near = DistanceOp.NearestPoints(openBoundary, f.CreatePoint(leaf));
                    var link = f.CreateLineString([leaf, near[0]]);
                    if (link.Length <= Deg(o.OpenMarginM + 80) && waterPrepared.Covers(link)) lines.Add(link);
                }
            segments.AddRange(lines);
        }
        if (segments.Count == 0) return [];
        var merger = new LineMerger();
        merger.Add(segments);
        var merged = merger.GetMergedLineStrings().OfType<LineString>().ToList();
        // the skeleton of a pond's fringe is a stub nobody can use: keep a line when it is long or joins another one
        var ends = new Dictionary<(long, long), int>();
        foreach (var l in merged) foreach (var c in new[] { l.StartPoint.Coordinate, l.EndPoint.Coordinate }) { var k = Key(c); ends[k] = ends.GetValueOrDefault(k) + 1; }
        var minLen = Deg(o.MinChannelM);
        return merged.Where(l => l.Length >= minLen || ends[Key(l.StartPoint.Coordinate)] > 1 || ends[Key(l.EndPoint.Coordinate)] > 1)
            .Select(l => DouglasPeuckerSimplifier.Simplify(l, Deg(8)) as LineString ?? l)
            .Select(l => (LineString)Scale(l, 1 / cos)).Where(l => l.NumPoints >= 2).ToList();
    }

    private static Geometry Diff(Geometry a, Geometry b) => OverlayNGRobust.Overlay(a, b, SpatialFunction.Difference);

    private static (long, long, long, long) Key(Coordinate a, Coordinate b)
    {
        var (ka, kb) = (Key(a), Key(b));
        return ka.CompareTo(kb) <= 0 ? (ka.Item1, ka.Item2, kb.Item1, kb.Item2) : (kb.Item1, kb.Item2, ka.Item1, ka.Item2);
    }
    private static (long, long) Key(Coordinate c) => ((long)Math.Round(c.X * 1e7), (long)Math.Round(c.Y * 1e7));

    private static IEnumerable<Polygon> Polygons(Geometry g) => g switch { Polygon p => p.IsEmpty ? [] : [p], GeometryCollection c => c.Geometries.SelectMany(Polygons), _ => [] };

    private static Geometry Scale(Geometry g, double sx) { var c = g.Copy(); c.Apply(new ScaleX(sx)); c.GeometryChanged(); return c; }
    private sealed class ScaleX(double sx) : ICoordinateSequenceFilter
    {
        public bool Done => false; public bool GeometryChanged => true;
        public void Filter(CoordinateSequence seq, int i) => seq.SetX(i, seq.GetX(i) * sx);
    }
}
