using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class VisibilityGraphTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static Polygon Box(double x1, double y1, double x2, double y2) => F.CreatePolygon([new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2), new(x1, y1)]);

    [Fact]
    public void Corners_are_the_convex_vertices_of_islands_and_the_land_poking_into_bays()
    {
        var island = Box(10.0, 54.0, 10.1, 54.1);
        var region = new Envelope(9, 11, 53, 55);
        Assert.Equal(4, VisibilityGraph.Corners([island], region).Count);
        // an L-shaped island: 5 convex corners, the inner (reflex) one is no bend candidate
        var l = F.CreatePolygon([new(10, 54), new(10.3, 54), new(10.3, 54.1), new(10.1, 54.1), new(10.1, 54.3), new(10, 54.3), new(10, 54)]);
        Assert.Equal(5, VisibilityGraph.Corners([l], region).Count);
        // a lake (hole) inside land: the hole's reflex corners are where land pokes in – a square hole has none
        var withLake = F.CreatePolygon(F.CreateLinearRing([new(10, 54), new(11, 54), new(11, 55), new(10, 55), new(10, 54)]), [F.CreateLinearRing([new(10.4, 54.4), new(10.6, 54.4), new(10.6, 54.6), new(10.4, 54.6), new(10.4, 54.4)])]);
        Assert.Equal(4, VisibilityGraph.Corners([withLake], region).Count);
        Assert.Empty(VisibilityGraph.Corners([island], new Envelope(12, 13, 53, 55))); // outside the region
    }

    [Fact]
    public void Edges_join_nodes_that_see_each_other_and_may_run_along_a_coast_but_not_through_land()
    {
        var island = Box(10.0, 54.0, 10.1, 54.1);
        var obstacles = VisibilityGraph.Index([island]);
        var nodes = VisibilityGraph.Corners([island], new Envelope(9, 11, 53, 55)).Select((c, i) => new GraphNode(i + 1, c.X, c.Y, false)).ToList();
        nodes.Add(new GraphNode(5, 9.9, 54.05, false)); // west of the island
        nodes.Add(new GraphNode(6, 10.2, 54.05, false)); // east of the island
        var edges = VisibilityGraph.Edges(nodes, [island], new VisibilityGraph.Options(MaxVisibilityM: 60_000, VisibleNeighbours: 10));
        bool Has(long a, long b) => edges.Any(e => (e.Source == a && e.Target == b) || (e.Source == b && e.Target == a));
        Assert.False(Has(5, 6)); // straight through the island
        Assert.True(Has(1, 2) && Has(2, 3) && Has(3, 4) && Has(4, 1)); // along the coast
        Assert.False(Has(1, 3) && Has(2, 4)); // diagonals cross the interior
        Assert.Equal(2, edges.Count(e => e.Source == 5 || e.Target == 5)); // the west point sees the two western corners only
        Assert.All(edges, e => Assert.True(e.Cost > 0));
    }

    [Fact]
    public void Lattice_keeps_clear_of_obstacles_and_anchors_move_points_out_of_the_land_buffer()
    {
        var island = Box(10.0, 54.0, 10.4, 54.4);
        var polygons = new List<Polygon> { island };
        var obstacles = VisibilityGraph.Index(polygons);
        var lattice = VisibilityGraph.Lattice(new Envelope(9.5, 11, 53.5, 55), obstacles, new VisibilityGraph.Options(LatticeM: 20_000, LatticeClearM: 3_000));
        Assert.NotEmpty(lattice);
        Assert.DoesNotContain(lattice, p => island.EnvelopeInternal.Contains(p));
        var inside = VisibilityGraph.Anchor(new Coordinate(10.01, 54.2), obstacles, polygons);
        Assert.Equal(10.0, inside.X, 4); Assert.Equal(54.2, inside.Y, 4); Assert.True(inside.X < 10.0); // nearest boundary point, west edge, a metre out
        var outside = new Coordinate(10.6, 54.2);
        Assert.Equal(outside, VisibilityGraph.Anchor(outside, obstacles, polygons));
    }

    [Fact]
    public void Small_enclosed_pockets_are_filled_so_anchors_land_on_the_outer_coast()
    {
        var land = F.CreatePolygon(F.CreateLinearRing([new(10, 54), new(11, 54), new(11, 55), new(10, 55), new(10, 54)]),
            [F.CreateLinearRing([new(10.02, 54.05), new(10.03, 54.05), new(10.03, 54.06), new(10.02, 54.06), new(10.02, 54.05)]), // ≈ 0.7 km² basin near the west coast
             F.CreateLinearRing([new(10.6, 54.6), new(10.9, 54.6), new(10.9, 54.9), new(10.6, 54.9), new(10.6, 54.6)])]); // ≈ 650 km² lake
        var filled = VisibilityGraph.FillSmallHoles(land, 3);
        Assert.Equal(1, filled.NumInteriorRings);
        var obstacles = VisibilityGraph.Index([filled]);
        var anchor = VisibilityGraph.Anchor(new Coordinate(10.025, 54.055), obstacles, [filled]);
        Assert.Equal(10.0, anchor.X, 4); Assert.Equal(54.055, anchor.Y, 4); Assert.True(anchor.X < 10.0); // nearest outer coast (a metre into the water), not the basin wall
    }

    [Fact]
    public void A_dense_chain_of_nodes_does_not_wall_off_the_nodes_across_it()
    {
        // 40 nodes 100 m apart along a line, one node 3 km north and one 3 km south of it, no land at all
        var nodes = new List<GraphNode>();
        for (var i = 0; i < 40; i++) nodes.Add(new GraphNode(i + 1, 10.0 + i * 0.0016, 54.0, false));
        nodes.Add(new GraphNode(101, 10.03, 54.027, false));
        nodes.Add(new GraphNode(102, 10.03, 53.973, false));
        var edges = VisibilityGraph.Edges(nodes, [], new VisibilityGraph.Options(MaxVisibilityM: 60_000, VisibleNeighbours: 8));
        Assert.Contains(edges, e => e.Source == 101 || e.Target == 101);
        Assert.Contains(edges, e => e.Source == 102 || e.Target == 102);
        // the two far nodes see each other across the chain only via the chain nodes; the chain itself stays connected
        Assert.True(edges.Count(e => e.Source <= 40 && e.Target <= 40) >= 39);
    }

    [Fact]
    public void Tile_edge_corners_are_recognised()
    {
        Assert.True(VisibilityGraph.OnTileEdge(new Coordinate(10.546875, 54.5)));  // 10.546875 = tile 2168 / 4096 * 360 - 180
        Assert.False(VisibilityGraph.OnTileEdge(new Coordinate(10.50, 54.50)));
    }

    [Fact]
    public void An_open_sea_lattice_connects_across_rows()
    {
        // 3 rows x 4 columns, 20 km apart, no obstacles: every node must reach the row above and below
        var nodes = new List<GraphNode>(); long id = 1;
        for (var r = 0; r < 3; r++) for (var c = 0; c < 4; c++) nodes.Add(new GraphNode(id++, 17.0 + c * 0.3375, 57.66 + r * 0.17967, false));
        var edges = VisibilityGraph.Edges(nodes, [], new VisibilityGraph.Options(MaxVisibilityM: 40_000, VisibleNeighbours: 24));
        var n5 = nodes[5]; // middle row, second column
        var nbrs = edges.Where(e => e.Source == n5.Id || e.Target == n5.Id).Select(e => e.Source == n5.Id ? e.Target : e.Source).Select(i => nodes.First(n => n.Id == i)).ToList();
        Assert.Contains(nbrs, n => n.Y > n5.Y + 0.1);
        Assert.Contains(nbrs, n => n.Y < n5.Y - 0.1);
    }
}
