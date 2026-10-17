using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using Sms.Api;

namespace Sms.Api.Tests;

/// <summary>Spec 10 §10.1: imports from an OSM PBF extract (streamed twice, converted to Overpass-style batches).</summary>
public class PbfImportTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private static Node N(long id, double lat, double lon, params (string k, string v)[] tags) => new() { Id = id, Latitude = lat, Longitude = lon, Version = 1, Tags = tags.Length == 0 ? null : new TagsCollection(tags.Select(t => new Tag(t.k, t.v))) };
    private static Way W(long id, long[] nodes, params (string k, string v)[] tags) => new() { Id = id, Nodes = nodes, Version = 1, Tags = new TagsCollection(tags.Select(t => new Tag(t.k, t.v))) };

    /// <summary>A tiny extract: two sea marks, a marina (way), a canal (way) with a bridge, and noise.</summary>
    private static byte[] BuildPbf()
    {
        var geos = new OsmGeo[]
        {
            N(1, 54.60, 9.99, ("seamark:type", "buoy_lateral"), ("seamark:buoy_lateral:category", "port")),
            N(2, 54.60, 10.01, ("seamark:type", "buoy_lateral"), ("seamark:buoy_lateral:category", "starboard")),
            N(3, 54.70, 10.10, ("amenity", "cafe")),
            N(10, 54.50, 9.80), N(11, 54.50, 9.81), N(12, 54.51, 9.81), N(13, 54.51, 9.80),
            N(20, 54.40, 9.60), N(21, 54.40, 9.62), N(22, 54.40, 9.64),
            N(30, 54.4001, 9.61, ("seamark:type", "bridge"), ("seamark:bridge:clearance_height", "6")),
            N(40, 54.80, 10.30, ("leisure", "marina"), ("name", "Marina Node")),
            W(100, [10, 11, 12, 13, 10], ("leisure", "marina"), ("name", "Testmarina PBF"), ("fuel", "yes")),
            W(200, [20, 21, 22], ("waterway", "canal"), ("name", "PBF-Kanal"), ("CEMT", "IV")),
            W(300, [20, 21], ("highway", "residential")),
        };
        using var ms = new MemoryStream();
        var target = new PBFOsmStreamTarget(ms);
        target.Initialize();
        foreach (var g in geos) if (g is Node n) target.AddNode(n); else target.AddWay((Way)g);
        target.Flush(); target.Close();
        return ms.ToArray();
    }

    [Fact]
    public async Task Streams_a_pbf_and_feeds_the_three_importers()
    {
        var job = f.Services.GetRequiredService<PbfImportJob>();
        var (admin, adminId) = await f.AdminAsync();
        using var stream = new MemoryStream(BuildPbf());
        var totals = await job.ImportStreamAsync(new PBFOsmStreamSource(stream), new PbfImportRequest("memory"), adminId, CancellationToken.None);
        Assert.Equal(3, totals["seamarksCreated"]); // two buoys + the bridge (seamark:type=bridge is a sea mark as well)
        Assert.Equal(2, totals["harboursCreated"]);
        Assert.Equal(1, totals["waterways"]);
        Assert.Equal(1, totals["bridges"]);

        var harbours = await admin.GetFromJsonAsync<JsonElement>("/api/harbours?q=Testmarina");
        var marina = Assert.Single(harbours.EnumerateArray());
        Assert.Equal(54.505, marina.GetProperty("lat").GetDouble(), 2); // centre of the way (closed ring: first node counted twice)
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/harbours/{marina.GetProperty("id").GetGuid()}");
        Assert.True(detail.GetProperty("facilities").GetProperty("fuel").GetBoolean());
        var corrections = (await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/corrections")).EnumerateArray().ToList();
        Assert.Contains(corrections, c => c.GetProperty("note").GetString()!.StartsWith("PBF-Kanal"));
        Assert.Contains(corrections, c => c.GetProperty("kind").GetString() == "Bridge" && c.GetProperty("clearanceM").GetDouble() == 6);
        Assert.Equal(3, (await admin.GetFromJsonAsync<JsonElement>("/api/seamarks/status")).GetProperty("count").GetInt32());

        // second run: updates, no duplicates
        using var again = new MemoryStream(BuildPbf());
        var t2 = await job.ImportStreamAsync(new PBFOsmStreamSource(again), new PbfImportRequest("memory", Harbours: false, Waterways: false), adminId, CancellationToken.None);
        Assert.Equal(3, t2["seamarksUpdated"]);
        Assert.False(t2.ContainsKey("harboursCreated"));

        // bbox restricts nodes and way centres
        using var boxed = new MemoryStream(BuildPbf());
        var t3 = await job.ImportStreamAsync(new PBFOsmStreamSource(boxed), new PbfImportRequest("memory", Bbox: "54.55,9.9,54.65,10.1"), adminId, CancellationToken.None);
        Assert.Equal(2, t3["seamarksUpdated"]); // the bridge lies outside the box
        Assert.Equal(0, t3.GetValueOrDefault("harboursCreated") + t3.GetValueOrDefault("harboursUpdated"));

        var status = await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/import-pbf/status");
        Assert.False(status.GetProperty("running").GetBoolean());
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/routing/import-pbf", new { url = "" })).StatusCode);
    }
}
