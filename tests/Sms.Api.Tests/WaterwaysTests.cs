using System.Text.Json;
using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class WaterwaysTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static Dictionary<string, string> Tags(params (string k, string v)[] t) => t.ToDictionary(x => x.k, x => x.v);

    [Fact]
    public void Navigability_follows_the_osm_tags()
    {
        Assert.True(Waterways.IsNavigable(Tags(("waterway", "canal"), ("CEMT", "Vb"))));
        Assert.True(Waterways.IsNavigable(Tags(("waterway", "river"), ("boat", "yes"))));
        Assert.True(Waterways.IsNavigable(Tags(("waterway", "fairway"))));
        Assert.False(Waterways.IsNavigable(Tags(("waterway", "river"))));            // no permission known
        Assert.False(Waterways.IsNavigable(Tags(("waterway", "canal"), ("CEMT", "I"), ("motorboat", "no"))));
        Assert.False(Waterways.IsNavigable(Tags(("waterway", "stream"), ("boat", "yes"))));
    }

    [Fact]
    public void Parses_overpass_json_and_merges_connected_ways()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "waterways.json")));
        var ways = Waterways.Parse(doc, F);
        // canal (2 ways) + Eider (joined at the shared end point) merge into one chain; the others are dropped
        var chain = Assert.Single(ways);
        Assert.Equal(3, chain.Ways);
        Assert.Equal("Nord-Ostsee-Kanal", chain.Name);
        Assert.Equal(6, chain.Line.NumPoints);
        Assert.Equal(4326, chain.Line.SRID);
    }

    [Fact]
    public void Junctions_stay_separate_lines_sharing_a_coordinate()
    {
        var a = F.CreateLineString([new Coordinate(10, 54), new Coordinate(10.1, 54)]);
        var b = F.CreateLineString([new Coordinate(10.1, 54), new Coordinate(10.2, 54)]);
        var c = F.CreateLineString([new Coordinate(10.1, 54), new Coordinate(10.1, 54.1)]); // branch at the junction
        var merged = Waterways.Merge([(a, "Main"), (b, "Main"), (c, "Branch")], F);
        Assert.Equal(3, merged.Count); // LineMerger does not merge through a degree-3 node
        Assert.Contains(merged, m => m.Name == "Branch");
        Assert.All(merged, m => Assert.Contains(m.Line.Coordinates, co => co.Equals2D(new Coordinate(10.1, 54))));
    }

    [Fact]
    public void Plain_road_bridges_are_the_unknown_clearance_candidates()
    {
        Assert.True(Waterways.IsPlainBridge(Tags(("bridge", "yes"), ("highway", "secondary"), ("ref", "H 635"))));   // Öknöström
        Assert.True(Waterways.IsPlainBridge(Tags(("bridge", "viaduct"), ("railway", "rail"))));
        Assert.False(Waterways.IsPlainBridge(Tags(("bridge", "no"), ("highway", "secondary"))));
        Assert.False(Waterways.IsPlainBridge(Tags(("bridge", "yes"), ("waterway", "canal"))));                       // an aqueduct
        Assert.False(Waterways.IsPlainBridge(Tags(("bridge", "yes"), ("highway", "primary"), ("seamark:bridge:clearance_height", "18"))));   // the waterway import's
        Assert.False(Waterways.IsPlainBridge(Tags(("bridge", "movable"), ("highway", "primary"), ("bridge:movable", "bascule"))));
        Assert.False(Waterways.IsPlainBridge(Tags(("bridge", "yes"), ("highway", "primary"), ("seamark:type", "bridge"))));
    }

    [Fact]
    public void Movable_bridges_are_opening()
    {
        Assert.True(Waterways.IsOpening(Tags(("bridge", "movable"), ("bridge:movable", "bascule"), ("highway", "primary"))));   // Frederik IX Bro, Nykøbing F
        Assert.True(Waterways.IsOpening(Tags(("bridge", "movable"), ("highway", "primary"))));                                  // without the subtype
        Assert.True(Waterways.IsOpening(Tags(("bridge", "yes"), ("seamark:bridge:category", "swing"))));
        Assert.False(Waterways.IsOpening(Tags(("bridge", "yes"), ("bridge:movable", "no"))));
        Assert.False(Waterways.IsOpening(Tags(("bridge", "yes"), ("highway", "primary"))));
        Assert.False(Waterways.IsPlainBridge(Tags(("bridge", "movable"), ("highway", "primary"))));
    }

    [Fact]
    public void Unknown_bridge_warnings_name_the_bridge()
    {
        Assert.Equal("Brovejen", SeaRouter.BridgeName("Brücke ohne Höhenangabe – Brovejen"));
        Assert.Equal("Brovejen", SeaRouter.BridgeName("Brücke Brovejen"));
        Assert.Equal("Masnedsundbroen", SeaRouter.BridgeName("Brücke Masnedsundbroen (Quelle: https://ing.dk/x)"));
        Assert.Null(SeaRouter.BridgeName("Brücke ohne Höhenangabe"));
        Assert.Null(SeaRouter.BridgeName("Brücke"));
        Assert.Equal("Brücke Brovejen mit unbekannter Durchfahrtshöhe – vor Ort prüfen", SeaRouter.UnknownBridgeNoteFor("Brovejen"));
        Assert.Equal(SeaRouter.UnknownBridgeNote, SeaRouter.UnknownBridgeNoteFor(null));
    }

    [Fact]
    public void Ferry_lines_skip_cable_ferries_and_take_the_relation_name()
    {
        using var doc = JsonDocument.Parse("""
        {"elements":[
          {"type":"way","id":1,"tags":{"route":"ferry","name":"Färja 18 Utö–Ornö–Saltsjöbaden–Stockholm"},"geometry":[{"lat":59.30,"lon":18.22},{"lat":59.31,"lon":18.22}]},
          {"type":"way","id":2,"tags":{"route":"ferry","ferry:cable":"yes","name":"Linfärja"},"geometry":[{"lat":59.40,"lon":18.30},{"lat":59.401,"lon":18.30}]},
          {"type":"way","id":3,"tags":{"route":"ferry"},"geometry":[{"lat":59.29,"lon":18.40},{"lat":59.29,"lon":18.50},{"lat":59.28,"lon":18.60}]},
          {"type":"relation","id":10,"tags":{"route":"ferry","name":"Strömma kanalturen till Sandhamn"},"members":[{"type":"way","ref":3,"role":""}]}
        ]}
        """);
        var lines = Waterways.ParseFerryLines(doc, F);
        Assert.Equal(2, lines.Count);
        Assert.Equal("Färja 18 Utö–Ornö–Saltsjöbaden–Stockholm", lines[0].name);
        Assert.Equal("Strömma kanalturen till Sandhamn", lines[1].name);
        Assert.Equal(3, lines[1].line.NumPoints);
        Assert.All(lines, l => Assert.Equal(4326, l.line.SRID));
    }
}
