using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// Ferry tracks (correction kind Ferry): an edge running along one - at least half of its 25 m samples within CorridorM, measured
/// in a local metric frame (cos φ) - is known-navigable water (SeaGraph.Ferry): no depth floor in the strict search.
/// </summary>
public sealed class FerryCorridors
{
    private const double MPerDeg = WaterGraphTiler.MPerDegLat;
    private readonly STRtree<(Coordinate a, Coordinate b)> _segments = new();
    private readonly double _corridorM;
    public int Count { get; }

    public static readonly FerryCorridors Empty = new([], 0);

    public FerryCorridors(IReadOnlyList<LineString> lines, double corridorM)
    {
        _corridorM = corridorM;
        if (corridorM > 0)
            foreach (var line in lines)
            {
                var cs = line.Coordinates;
                for (var k = 1; k < cs.Length; k++) _segments.Insert(new Envelope(cs[k - 1], cs[k]), (cs[k - 1], cs[k]));
                Count++;
            }
        _segments.Build();
    }

    /// <summary>Share of the edge's samples (segment midpoints every ~stepM, as ChannelPreference.ShareInside) within the corridor.</summary>
    public double Share(LineString edge, double stepM = 25)
    {
        if (Count == 0) return 0;
        // quick reject: the track segments near the edge's envelope (expanded by the corridor at its poleward side)
        var env = edge.EnvelopeInternal.Copy();
        env.ExpandBy(_corridorM / (MPerDeg * Cos(Math.Max(Math.Abs(env.MinY), Math.Abs(env.MaxY)))), _corridorM / MPerDeg);
        var near = _segments.Query(env);
        if (near.Count == 0) return 0;
        var inside = 0; var total = 0;
        for (var i = 1; i < edge.NumPoints; i++)
        {
            var a = edge.GetCoordinateN(i - 1); var b = edge.GetCoordinateN(i);
            var n = Math.Max(1, (int)Math.Ceiling(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / stepM));
            for (var k = 0; k < n; k++)
            {
                total++;
                if (Near(near, new Coordinate(a.X + (b.X - a.X) * (k + 0.5) / n, a.Y + (b.Y - a.Y) * (k + 0.5) / n))) inside++;
            }
        }
        return total == 0 ? 0 : (double)inside / total;
    }

    public bool Along(LineString edge) => Share(edge) >= 0.5;

    /// <summary>True when the point lies within the corridor of a track.</summary>
    public bool Contains(Coordinate p)
    {
        if (Count == 0) return false;
        var d = _corridorM / MPerDeg;
        return Near(_segments.Query(new Envelope(p.X - d / Cos(p.Y), p.X + d / Cos(p.Y), p.Y - d, p.Y + d)), p);
    }

    private static double Cos(double lat) => Math.Max(0.2, Math.Cos(lat * Math.PI / 180));

    private bool Near(IList<(Coordinate a, Coordinate b)> segments, Coordinate p)
    {
        var cos = Cos(p.Y);
        foreach (var (a, b) in segments)
        {
            // point → segment in the local metric frame (x scaled by cos φ), as SeaGraph.Project
            double ax = a.X * cos, ay = a.Y, bx = b.X * cos, by = b.Y, px = p.X * cos, py = p.Y;
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            var t = l2 == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / l2, 0, 1);
            double qx = ax + dx * t - px, qy = ay + dy * t - py;
            if (Math.Sqrt(qx * qx + qy * qy) * MPerDeg <= _corridorM) return true;
        }
        return false;
    }
}
