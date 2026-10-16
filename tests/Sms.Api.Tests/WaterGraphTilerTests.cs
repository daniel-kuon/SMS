using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

/// <summary>Tile-wise water graph construction without a database (spec 10 §10.2).</summary>
public class WaterGraphTilerTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    // 0.4° × 0.2° region at 54° N, 5 km start cells, 2×2 start cells per tile → several tiles in both directions
    private static readonly Envelope Region = new(10.0, 10.4, 54.0, 54.2);
    private static WaterGraphTiler Tiler(int tileCells = 2) => new(F, Region, 5000, 500, 0.2, tileCells);

    private static async Task<List<TileResult>> Run(WaterGraphTiler t, IReadOnlyList<Geometry> land)
    {
        var results = new List<TileResult>();
        await t.RunAsync((tile, _) => Task.FromResult<IReadOnlyList<Geometry>>(land.Where(g => g.EnvelopeInternal.Intersects(WaterGraphTiler.WithMargin(tile))).ToList()), (r, _) => { results.Add(r); return Task.CompletedTask; }, CancellationToken.None);
        return results;
    }

    [Fact]
    public void Tiles_cover_the_region_exactly_once()
    {
        var t = Tiler();
        Assert.True(t.Tiles.Count > 4);
        var area = t.Tiles.Sum(x => x.Envelope.Area);
        Assert.Equal(Region.Area, area, 1e-9);
        for (var i = 0; i < t.Tiles.Count; i++)
            for (var j = i + 1; j < t.Tiles.Count; j++)
                Assert.True(t.Tiles[i].Envelope.Intersection(t.Tiles[j].Envelope).Area < 1e-12, "tiles overlap");
        Assert.All(t.Tiles, x => Assert.True(Region.Contains(x.Envelope)));
    }

    [Fact]
    public async Task Open_water_yields_one_node_per_start_cell_and_a_connected_grid()
    {
        var t = Tiler();
        var results = await Run(t, []);
        var nodes = results.SelectMany(r => r.Nodes).ToList();
        var edges = results.SelectMany(r => r.Edges).ToList();
        var columns = (int)Math.Ceiling(Region.Width / t.Tiles[0].StartDegLng);
        var rows = (int)Math.Ceiling(Region.Height / t.Tiles[0].StartDegLat);
        Assert.Equal(columns * rows, nodes.Count);
        Assert.Equal(nodes.Count, nodes.Select(n => n.Id).Distinct().Count());
        Assert.Equal(nodes.Count, t.NodeCount);
        // 8-neighbourhood of a grid, each pair once
        var expected = (columns - 1) * rows + columns * (rows - 1) + 2 * (columns - 1) * (rows - 1);
        Assert.Equal(expected, edges.Count);
        Assert.Equal(edges.Count, edges.Select(e => (Math.Min(e.Source, e.Target), Math.Max(e.Source, e.Target))).Distinct().Count());
        Assert.All(edges, e => Assert.NotEqual(e.Source, e.Target));
        // every node is reachable from the first one
        var adj = nodes.ToDictionary(n => n.Id, _ => new List<long>());
        foreach (var e in edges) { adj[e.Source].Add(e.Target); adj[e.Target].Add(e.Source); }
        var seen = new HashSet<long> { nodes[0].Id }; var stack = new Stack<long>([nodes[0].Id]);
        while (stack.TryPop(out var id)) foreach (var n in adj[id]) if (seen.Add(n)) stack.Push(n);
        Assert.Equal(nodes.Count, seen.Count);
    }

    [Fact]
    public async Task Edges_cross_tile_borders_but_never_land()
    {
        // an island straddling a tile border in the middle of the region, split into two pieces like the OSM file does
        var west = F.ToGeometry(new Envelope(10.15, 10.20, 54.08, 54.12));
        var east = F.ToGeometry(new Envelope(10.20, 10.25, 54.08, 54.12));
        var results = await Run(Tiler(), [west, east]);
        var edges = results.SelectMany(r => r.Edges).ToList();
        var tileOf = results.SelectMany(r => r.Nodes.Select(n => (n.Id, r.Tile))).ToDictionary(x => x.Id, x => x.Tile);
        Assert.Contains(edges, e => tileOf[e.Source] != tileOf[e.Target]);
        Assert.All(edges, e => Assert.False(e.Geom.Intersects(west) || e.Geom.Intersects(east), "edge crosses land"));
        // the coast is refined down to small cells and penalised
        var nodes = results.SelectMany(r => r.Nodes).ToList();
        Assert.Contains(nodes, n => n.IsMin);
        Assert.Contains(edges, e => e.Cost > RoutingGraphBuilder.Haversine(0, 0, 0, 0) && (nodes.First(n => n.Id == e.Source).IsMin || nodes.First(n => n.Id == e.Target).IsMin));
        Assert.All(nodes, n => Assert.False(west.Contains(F.CreatePoint(new Coordinate(n.X, n.Y))) || east.Contains(F.CreatePoint(new Coordinate(n.X, n.Y)))));
    }

    [Fact]
    public async Task Land_covered_tiles_produce_no_nodes_and_are_not_subdivided()
    {
        var t = Tiler();
        // land covers the western half of the region as two grid pieces
        var land = new List<Geometry> { F.ToGeometry(new Envelope(9.9, 10.1, 53.9, 54.1)), F.ToGeometry(new Envelope(9.9, 10.1, 54.1, 54.3)) };
        var results = await Run(t, land);
        foreach (var r in results)
        {
            if (r.Tile.Envelope.MaxX <= 10.1) Assert.Empty(r.Nodes);
            else if (r.Tile.Envelope.MinX >= 10.1) Assert.All(r.Nodes, n => Assert.False(n.IsMin)); // open water: start cells only
        }
        Assert.True(t.NodeCount > 0);
    }

    [Fact]
    public async Task Nearest_finds_nodes_of_processed_neighbours_and_respects_the_limit()
    {
        var results = await Run(Tiler(), []);
        var last = results[^1];
        var corner = last.Tile.Envelope;
        var near = last.Nearest(corner.MinX - 0.001, corner.MinY - 0.001, 0.2); // just outside, in an earlier tile
        Assert.NotNull(near);
        Assert.DoesNotContain(last.Nodes, n => n.Id == near!.Id);
        Assert.Null(last.Nearest(corner.MinX, corner.MinY, 0.0001));
    }
}
