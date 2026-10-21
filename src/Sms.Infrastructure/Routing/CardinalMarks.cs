using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Cardinal marks as routing obstacles: the danger quadrant of every north/east/south/west cardinal (the 90° sector opposite its
/// name, radius <see cref="Fairways.CardinalDangerM"/>) is a wedge polygon. Edges through a wedge cost ×factor by the share
/// inside (<see cref="Apply"/>, like ChannelPreference); the smoothing never pulls a leg through one (SeaRouter.SmoothAsync).
/// </summary>
public static class CardinalMarks
{
    private sealed class Row { public double X { get; set; } public double Y { get; set; } public string Category { get; set; } = ""; }

    /// <summary>All N/E/S/W cardinal buoys and beacons of the Seamarks table (EPSG:3857 → 4326).</summary>
    public static async Task<List<(Coordinate p, MarkSide side)>> LoadAsync(SmsDbContext db, CancellationToken ct = default)
    {
        var rows = await db.Database.SqlQueryRaw<Row>("""
            SELECT ST_X(ST_Transform("Location", 4326)) AS "X", ST_Y(ST_Transform("Location", 4326)) AS "Y", "Category"
            FROM "Seamarks" WHERE "Type" IN ('buoy_cardinal', 'beacon_cardinal') AND "Category" IN ('north', 'east', 'south', 'west')
            """).ToListAsync(ct);
        return rows.Select(r => (new Coordinate(r.X, r.Y), r.Category switch { "north" => MarkSide.North, "east" => MarkSide.East, "south" => MarkSide.South, _ => MarkSide.West })).ToList();
    }

    /// <summary>The danger wedge of a cardinal: centre, the arc of radius <paramref name="radiusM"/> within ±45° of the direction opposite the
    /// name (south of a north cardinal, …), in a local metric frame (cos φ).</summary>
    public static Polygon Wedge(Coordinate p, MarkSide side, double radiusM = Fairways.CardinalDangerM)
    {
        var az = side switch { MarkSide.North => 180.0, MarkSide.East => 270.0, MarkSide.South => 0.0, _ => 90.0 };
        var dLat = radiusM / WaterGraphTiler.MPerDegLat; var dLng = dLat / Math.Max(0.2, Math.Cos(p.Y * Math.PI / 180));
        var ring = new List<Coordinate> { p };
        for (var a = az - 45; a <= az + 45.001; a += 15) ring.Add(new Coordinate(p.X + dLng * Math.Sin(a * Math.PI / 180), p.Y + dLat * Math.Cos(a * Math.PI / 180)));
        ring.Add(p);
        return Geo.Factory.CreatePolygon(ring.ToArray());
    }

    /// <summary>A route keeps this far off an isolated-danger mark (the rock or wreck lies under it).</summary>
    public const double IsolatedDangerM = 60;

    /// <summary>All isolated-danger buoys and beacons of the Seamarks table (EPSG:3857 → 4326).</summary>
    public static async Task<List<Coordinate>> LoadIsolatedAsync(SmsDbContext db, CancellationToken ct = default) =>
        (await db.Database.SqlQueryRaw<Row>("""
            SELECT ST_X(ST_Transform("Location", 4326)) AS "X", ST_Y(ST_Transform("Location", 4326)) AS "Y", '' AS "Category"
            FROM "Seamarks" WHERE "Type" IN ('buoy_isolated_danger', 'beacon_isolated_danger')
            """).ToListAsync(ct)).Select(r => new Coordinate(r.X, r.Y)).ToList();

    /// <summary>The danger disc round an isolated-danger mark: a 24-gon round the circle of radius <paramref name="radiusM"/> (its edges
    /// touch the circle, nothing inside the radius is outside the polygon), in a local metric frame (cos φ).</summary>
    public static Polygon Disc(Coordinate p, double radiusM = IsolatedDangerM)
    {
        var r = radiusM / Math.Cos(Math.PI / 24);
        var dLat = r / WaterGraphTiler.MPerDegLat; var dLng = dLat / Math.Max(0.2, Math.Cos(p.Y * Math.PI / 180));
        var ring = new Coordinate[25];
        for (var k = 0; k < 24; k++) ring[k] = new Coordinate(p.X + dLng * Math.Sin(k * Math.PI / 12), p.Y + dLat * Math.Cos(k * Math.PI / 12));
        ring[24] = ring[0];
        return Geo.Factory.CreatePolygon(ring);
    }

    public static STRtree<Polygon> Index(IEnumerable<(Coordinate p, MarkSide side)> marks, double radiusM = Fairways.CardinalDangerM)
    {
        var tree = new STRtree<Polygon>();
        foreach (var (p, side) in marks) { var w = Wedge(p, side, radiusM); tree.Insert(w.EnvelopeInternal, w); }
        tree.Build();
        return tree;
    }

    /// <summary>Edges that run through a danger wedge cost ×factor by the share inside (sampled every 25 m; a passage edge's cost is
    /// multiplied too, so the search leaves an "Enge" or waterway line through a wedge where the grid offers a way round). factor ≤ 1 = off.</summary>
    public static List<GraphEdge> Apply(IReadOnlyList<GraphEdge> edges, IEnumerable<(Coordinate p, MarkSide side)> marks, double factor)
    {
        if (factor <= 1) return edges.ToList();
        var index = new STRtree<IPreparedGeometry>();
        foreach (var (p, side) in marks) { var w = Wedge(p, side); index.Insert(w.EnvelopeInternal, PreparedGeometryFactory.Prepare(w)); }
        index.Build();
        var result = new List<GraphEdge>(edges.Count);
        foreach (var e in edges)
        {
            var hits = index.Query(e.Geom.EnvelopeInternal);
            var share = hits.Count == 0 ? 0 : ChannelPreference.ShareInside(e.Geom, hits, 25);
            result.Add(share <= 0 ? e : e with { Cost = e.Cost * (1 + share * (factor - 1)) });
        }
        return result;
    }
}
