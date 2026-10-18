using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Union;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Spec 10 §10.5 step 3: where a marked channel exists, the water next to it is usually shallow or restricted
/// (Wadden Sea, Schlei, South Funen archipelago). The band within <c>distanceM</c> of a derived fairway line (minus
/// charted fairway areas) is the "channel zone"; the part of an edge that runs inside the zone costs ×factor. Passage
/// edges (the fairway lines themselves) are never touched, so following the buoys stays the cheapest way through.
/// Priced by length, not by the midpoint: a 20 km visibility leg that cuts across a buoyed shallow pays for the cut.
/// </summary>
public static class ChannelPreference
{
    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> gridEdges, IReadOnlyList<LineString> fairways, double distanceM, double factor) => Apply(gridEdges, fairways, [], distanceM, factor);

    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> gridEdges, IReadOnlyList<LineString> fairways, IReadOnlyList<Polygon> areas, double distanceM, double factor)
    {
        if ((fairways.Count == 0 && areas.Count == 0) || factor <= 1 || distanceM <= 0) return gridEdges.ToList();
        var zone = Zone(fairways, areas, distanceM);
        if (zone.Count == 0) return gridEdges.ToList();
        var index = new STRtree<IPreparedGeometry>();
        foreach (var z in zone) index.Insert(z.EnvelopeInternal, PreparedGeometryFactory.Prepare(z));
        index.Build();
        var result = new List<GraphEdge>(gridEdges.Count);
        foreach (var e in gridEdges)
        {
            var hits = index.Query(e.Geom.EnvelopeInternal);
            var share = hits.Count == 0 ? 0 : ShareInside(e.Geom, hits);
            result.Add(share <= 0 ? e : e with { Cost = e.Cost * (1 + share * (factor - 1)) });
        }
        return result;
    }

    /// <summary>Share of the line inside any of the polygons, sampled every ~200 m (an exact overlay per edge is far slower for a million legs).</summary>
    public static double ShareInside(LineString line, IList<IPreparedGeometry> polygons, double stepM = 200)
    {
        var inside = 0; var total = 0;
        for (var i = 1; i < line.NumPoints; i++)
        {
            var a = line.GetCoordinateN(i - 1); var b = line.GetCoordinateN(i);
            var n = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / stepM));
            for (var k = 0; k < n; k++)
            {
                var p = line.Factory.CreatePoint(new Coordinate(a.X + (b.X - a.X) * (k + 0.5) / n, a.Y + (b.Y - a.Y) * (k + 0.5) / n));
                total++;
                foreach (var poly in polygons) if (poly.Contains(p)) { inside++; break; }
            }
        }
        return total == 0 ? 0 : (double)inside / total;
    }

    /// <summary>Buffer band around the fairway lines, with the charted fairway areas (marked channels) cut out: inside those the water is the channel.</summary>
    public static List<Geometry> Zone(IReadOnlyList<LineString> fairways, IReadOnlyList<Polygon> areas, double distanceM)
    {
        var bands = new List<Geometry>();
        // a charted fairway area counts like a buoy chain along its edge: water next to it is penalised, water inside it is cut out below
        var lines = fairways.Concat(areas.Where(a => !a.IsEmpty).Select(a => (LineString)a.ExteriorRing)).ToList();
        foreach (var f in lines)
        {
            var lat = f.Centroid.Y;
            var degLat = distanceM / WaterGraphTiler.MPerDegLat;
            // buffer in a locally scaled frame so the band is distanceM wide in both axes
            var cos = Math.Cos(lat * Math.PI / 180);
            var scaled = (LineString)f.Copy(); scaled.Apply(new Scale(cos));
            var band = scaled.Buffer(degLat, 4); band.Apply(new Scale(1 / cos));
            bands.Add(band);
        }
        if (bands.Count == 0) return [];
        Geometry union;
        try { union = UnaryUnionOp.Union(bands); } catch (TopologyException) { union = new GeometryCollection(bands.ToArray()).Buffer(0); }
        if (areas.Count > 0)
        {
            Geometry areaUnion;
            try { areaUnion = UnaryUnionOp.Union(areas.Cast<Geometry>().ToList()); } catch (TopologyException) { areaUnion = new GeometryCollection(areas.Cast<Geometry>().ToArray()).Buffer(0); }
            union = union.Difference(areaUnion);
        }
        return union is GeometryCollection gc ? gc.Geometries.Where(g => !g.IsEmpty && g.Dimension == Dimension.Surface).ToList() : union.IsEmpty ? [] : [union];
    }

    private sealed class Scale(double sx) : ICoordinateSequenceFilter
    {
        public bool Done => false; public bool GeometryChanged => true;
        public void Filter(CoordinateSequence seq, int i) => seq.SetX(i, seq.GetX(i) * sx);
    }

}
