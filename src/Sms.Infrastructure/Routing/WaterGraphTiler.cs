using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;

namespace Sms.Infrastructure.Routing;

public sealed record GraphNode(long Id, double X, double Y, bool IsMin);
/// <summary>MinDepthM: shallowest charted depth area the edge touches (null = no data or a marked passage, which is never depth-limited).</summary>
/// <param name="Assumed">An assumed passage (AssumedPassages): routes over it carry a warning.</param>
public sealed record GraphEdge(long Source, long Target, double Cost, LineString Geom, double? ClearanceM = null, double? MinDepthM = null, bool Assumed = false);
public sealed record GraphTile(Envelope Envelope, double StartDegLng, double StartDegLat);

/// <summary>
/// One processed tile: its water cells as nodes, every edge that starts in it (including links into already
/// processed neighbouring tiles) and a nearest-node lookup over the tile plus the neighbours' border cells.
/// </summary>
public sealed class TileResult
{
    private readonly STRtree<WaterGraphTiler.Cell> _lookup;
    internal TileResult(GraphTile tile, List<GraphNode> nodes, List<GraphEdge> edges, STRtree<WaterGraphTiler.Cell> lookup) { Tile = tile; Nodes = nodes; Edges = edges; _lookup = lookup; }
    public GraphTile Tile { get; }
    public IReadOnlyList<GraphNode> Nodes { get; }
    public IReadOnlyList<GraphEdge> Edges { get; }

    /// <summary>Nearest water node (this tile or a processed neighbour) within maxDeg of the point, or null.</summary>
    public GraphNode? Nearest(double x, double y, double maxDeg)
    {
        var env = new Envelope(x - maxDeg, x + maxDeg, y - maxDeg, y + maxDeg);
        WaterGraphTiler.Cell? best = null; var bestD = double.MaxValue;
        foreach (var c in _lookup.Query(env))
        {
            var d = (c.Cx - x) * (c.Cx - x) + (c.Cy - y) * (c.Cy - y);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best is not null && Math.Sqrt(bestD) <= maxDeg ? new GraphNode(best.Id, best.Cx, best.Cy, best.IsMin) : null;
    }
}

/// <summary>
/// Spec 10 §10.2 graph construction without holding the whole region in memory: the region is cut into tiles of
/// <c>tileCells × tileCells</c> start cells; each tile gets only the land polygons around it (landFor), builds an
/// adaptive quadtree over water, connects neighbouring cells whose connecting segment stays on water and links to
/// the border cells of neighbouring tiles that were processed before. Memory is bounded by one tile plus the
/// retained border cells. No database access: the caller supplies land and consumes TileResults.
/// </summary>
public sealed class WaterGraphTiler
{
    public const double MPerDegLat = 111_320.0;
    private readonly GeometryFactory _f;
    private readonly double _minDeg, _coastPenalty;
    private readonly List<(Envelope Env, List<Cell> Cells)> _borders = [];

    public WaterGraphTiler(GeometryFactory factory, Envelope region, double startCellM, double minCellM, double coastPenaltyFactor, int tileCells = 10)
    {
        _f = factory; Region = region; _coastPenalty = coastPenaltyFactor;
        _minDeg = minCellM / MPerDegLat;
        var startDegLat = startCellM / MPerDegLat;
        var tileDegLat = Math.Max(1, tileCells) * startDegLat;
        // ponytail: one longitude scale (region centre) keeps all tile rows on the same column grid so diagonal
        // neighbours touch exactly; cells get wider in metres towards the north (Europe: ×2 at 70° N) – split the
        // region into latitude bands with their own build if that ever matters.
        var startDegLng = startCellM / (MPerDegLat * Math.Cos((region.MinY + region.MaxY) / 2 * Math.PI / 180));
        var tileDegLng = Math.Max(1, tileCells) * startDegLng;
        var tiles = new List<GraphTile>();
        for (var lat = region.MinY; lat < region.MaxY; lat += tileDegLat)
            for (var lng = region.MinX; lng < region.MaxX; lng += tileDegLng)
                tiles.Add(new GraphTile(new Envelope(lng, Math.Min(lng + tileDegLng, region.MaxX), lat, Math.Min(lat + tileDegLat, region.MaxY)), startDegLng, startDegLat));
        Tiles = tiles;
    }

    public Envelope Region { get; }
    public IReadOnlyList<GraphTile> Tiles { get; }
    /// <summary>Next free node id; ids are assigned sequentially across tiles (passages etc. continue from here).</summary>
    public long NextId { get; private set; } = 1;
    public long NodeCount { get; private set; }
    public long EdgeCount { get; private set; }

    /// <summary>Margin around a tile that landFor must cover so that cross-tile edges can be checked against land.</summary>
    public static Envelope WithMargin(GraphTile t) { var e = new Envelope(t.Envelope); e.ExpandBy(t.StartDegLng, t.StartDegLat); return e; }

    public async Task RunAsync(Func<GraphTile, CancellationToken, Task<IReadOnlyList<Geometry>>> landFor, Func<TileResult, CancellationToken, Task> sink, CancellationToken ct)
    {
        foreach (var tile in Tiles)
        {
            ct.ThrowIfCancellationRequested();
            var result = Process(tile, await landFor(tile, ct), ct);
            NodeCount += result.Nodes.Count; EdgeCount += result.Edges.Count;
            await sink(result, ct);
        }
    }

    /// <summary>Synchronous core for one tile (also used by tests).</summary>
    public TileResult Process(GraphTile tile, IReadOnlyList<Geometry> land, CancellationToken ct = default)
    {
        var landIndex = new STRtree<LandPiece>();
        foreach (var g in land) if (!g.IsEmpty) landIndex.Insert(g.EnvelopeInternal, new LandPiece(PreparedGeometryFactory.Prepare(g), g));
        landIndex.Build();

        // 1. adaptive quadtree over water
        var env = tile.Envelope;
        var leaves = new List<Cell>();
        for (var y = env.MinY; y < env.MaxY - 1e-12; y += tile.StartDegLat)
            for (var x = env.MinX; x < env.MaxX - 1e-12; x += tile.StartDegLng)
                Split(new Cell(tile, x, y, Math.Min(tile.StartDegLng, env.MaxX - x), Math.Min(tile.StartDegLat, env.MaxY - y)), landIndex, leaves, ct);
        foreach (var c in leaves) c.Id = NextId++;

        // 2. lookup over own cells + border cells of processed neighbours
        var eps = _minDeg / 10;
        var grown = new Envelope(env); grown.ExpandBy(eps);
        var lookup = new STRtree<Cell>();
        foreach (var c in leaves) lookup.Insert(c.Envelope, c);
        foreach (var (bEnv, cells) in _borders)
            if (bEnv.Intersects(grown)) foreach (var c in cells) if (grown.Intersects(c.Envelope)) lookup.Insert(c.Envelope, c);
        lookup.Build();

        // 3. edges between neighbouring cells (own ↔ own once, own → neighbour tile always: the neighbour never saw us)
        var edges = new List<GraphEdge>();
        foreach (var a in leaves)
        {
            ct.ThrowIfCancellationRequested();
            var q = new Envelope(a.Envelope); q.ExpandBy(eps);
            foreach (var b in lookup.Query(q))
            {
                if (ReferenceEquals(a, b) || (b.Tile == tile && b.Id <= a.Id)) continue;
                var line = _f.CreateLineString([new Coordinate(a.Cx, a.Cy), new Coordinate(b.Cx, b.Cy)]);
                if (CrossesLand(line, landIndex)) continue;
                var metres = RoutingGraphBuilder.Haversine(a.Cy, a.Cx, b.Cy, b.Cx);
                if (a.IsMin || b.IsMin) metres *= 1 + _coastPenalty; // keep routes off the coast
                edges.Add(new GraphEdge(a.Id, b.Id, metres, line));
            }
        }

        // 4. remember this tile's border cells for the tiles that follow
        var border = leaves.Where(c => c.X - eps <= env.MinX || c.X + c.W + eps >= env.MaxX || c.Y - eps <= env.MinY || c.Y + c.H + eps >= env.MaxY).ToList();
        if (border.Count > 0) _borders.Add((env, border));

        return new TileResult(tile, leaves.Select(c => new GraphNode(c.Id, c.Cx, c.Cy, c.IsMin)).ToList(), edges, lookup);
    }

    internal sealed class Cell(GraphTile tile, double x, double y, double w, double h)
    {
        public readonly GraphTile Tile = tile;
        public readonly double X = x, Y = y, W = w, H = h; public long Id; public bool IsMin;
        public double Cx => X + W / 2; public double Cy => Y + H / 2;
        public Envelope Envelope => new(X, X + W, Y, Y + H);
    }
    private sealed record LandPiece(IPreparedGeometry Prepared, Geometry Geometry);

    private void Split(Cell c, STRtree<LandPiece> land, List<Cell> leaves, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var poly = _f.ToGeometry(c.Envelope);
        var touching = new List<LandPiece>();
        foreach (var p in land.Query(c.Envelope)) if (p.Prepared.Intersects(poly)) touching.Add(p);
        if (touching.Count == 0) { leaves.Add(c); return; }
        if (CoveredByLand(poly, touching)) return; // inland: drop the whole subtree instead of descending to the minimum cell size
        if (c.H > _minDeg)
        {
            var hw = c.W / 2; var hh = c.H / 2;
            Split(new Cell(c.Tile, c.X, c.Y, hw, hh), land, leaves, ct);
            Split(new Cell(c.Tile, c.X + hw, c.Y, hw, hh), land, leaves, ct);
            Split(new Cell(c.Tile, c.X, c.Y + hh, hw, hh), land, leaves, ct);
            Split(new Cell(c.Tile, c.X + hw, c.Y + hh, hw, hh), land, leaves, ct);
            return;
        }
        // minimum size: keep as water node if the centre is not on land
        var centre = _f.CreatePoint(new Coordinate(c.Cx, c.Cy));
        foreach (var p in touching) if (p.Prepared.Contains(centre)) return;
        c.IsMin = true;
        leaves.Add(c);
    }

    private bool CoveredByLand(Geometry cell, List<LandPiece> touching)
    {
        foreach (var p in touching) if (p.Prepared.Covers(cell)) return true;
        if (touching.Count == 1) return false;
        // several pieces (the OSM file splits land on a grid): covered when their union fills the cell
        var pieces = touching.Select(p => p.Geometry.Intersection(cell)).Where(g => !g.IsEmpty && g.Dimension == Dimension.Surface).ToList();
        if (pieces.Count == 0) return false;
        var area = pieces.Count == 1 ? pieces[0].Area : _f.BuildGeometry(pieces).Union().Area;
        return area >= cell.Area * (1 - 1e-6);
    }

    private static bool CrossesLand(LineString line, STRtree<LandPiece> land)
    {
        foreach (var p in land.Query(line.EnvelopeInternal)) if (p.Prepared.Intersects(line)) return true;
        return false;
    }
}
