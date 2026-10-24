using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.Geometries;
using Npgsql;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

/// <summary>Probleme (spec-admin-issues): detection, priorities and the outdated reasons without a database.</summary>
public class RoutingIssueRulesTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static LineString Line(params double[] xy) => F.CreateLineString(Enumerable.Range(0, xy.Length / 2).Select(i => new Coordinate(xy[2 * i], xy[2 * i + 1])).ToArray());

    [Fact]
    public void Water_priority_ranks_fairway_harbour_and_traffic_above_plain_shallows()
    {
        var plain = RoutingIssues.WaterPriority(5, false, false, 0);
        Assert.Equal(10, RoutingIssues.WaterPriority(9, false, false, 0)); // 10 · log10(1 + km): edge km are damped
        Assert.True(RoutingIssues.WaterPriority(50, false, false, 0) < RoutingIssues.WaterPriority(5, true, false, 0)); // a fairway beats dense graph edges
        Assert.True(RoutingIssues.WaterPriority(5, true, false, 0) > RoutingIssues.WaterPriority(5, false, true, 0));
        Assert.True(RoutingIssues.WaterPriority(5, false, false, 99) > plain);
        Assert.True(RoutingIssues.GapPriority(1, 1) > RoutingIssues.GapPriority(1, 0));
    }

    [Fact]
    public void Both_dead_ends_of_a_line_make_one_isolated_fairway_one_end_a_dead_end_to_known_water()
    {
        var a = Line(10, 54, 10.01, 54); var b = Line(11, 54, 11.01, 54);
        Guid ia = Guid.NewGuid(), ib = Guid.NewGuid();
        var issues = RoutingIssues.FairwayEndIssues([
            new(ia, 0, a.StartPoint, a, true, null), new(ia, 1, a.EndPoint, a, false, F.CreatePoint(new Coordinate(10.02, 54))),
            new(ib, 1, b.EndPoint, b, false, F.CreatePoint(new Coordinate(11.02, 54)))]);
        var isolated = Assert.Single(issues, i => i.Type == IssueType.FairwayIsolated);
        Assert.Equal(a, isolated.Geometry);
        Assert.Contains("nicht ans Wassernetz angebunden", isolated.Detail);
        var dead = Assert.Single(issues, i => i.Type == IssueType.FairwayDeadEnd);
        Assert.Equal("deadend:11.01000,54.00000", dead.Key);
        var stretch = Assert.IsType<LineString>(dead.Geometry); // end → nearest certain water: the uncertain stretch
        Assert.Equal(new Coordinate(11.02, 54), stretch.EndPoint.Coordinate);
        Assert.Contains("m bis sicherem Wasser", dead.Detail);
    }

    [Fact]
    public void Keys_are_stable_and_reasons_say_what_changed()
    {
        Assert.Equal(RoutingIssues.BridgeKey(Line(10.123456, 54.1, 10.2, 54.2)), RoutingIssues.BridgeKey(Line(10.1234561, 54.1, 10.2, 54.2)));
        Assert.StartsWith("Neue Brückendaten", RoutingIssues.OutdatedReason("bridge", "keine", new("bridge", "x", "Bridge 12 m: 1; ids a", 1)));
        Assert.StartsWith("Fahrwasser nicht mehr abgeleitet", RoutingIssues.OutdatedReason("fairway", "Fahrwasser: 1", new("fairway", "x", "keine", 0)));
        Assert.StartsWith("Tiefendaten geändert", RoutingIssues.OutdatedReason("depth", "ft: 1", new("depth", "x", "ft: 2", 2)));
        Assert.Contains(Fairways.WideNote, RoutingIssues.KnownDepthNote); // buoyed water, no channel zone
    }
}

/// <summary>Probleme against a real database: detection on a small hand-made graph, fixes as Manual rows, ignore, outdated after a "re-import".</summary>
public class RoutingIssuesTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    private const int Version = 1;
    private const long Grid = 1000; // node ids above are passage vertices
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static LineString Line(params double[] xy) => F.CreateLineString(Enumerable.Range(0, xy.Length / 2).Select(i => new Coordinate(xy[2 * i], xy[2 * i + 1])).ToArray());
    private static Polygon Box(double w, double s, double e, double n) => (Polygon)F.ToGeometry(new Envelope(w, e, s, n));

    private static async Task Sql(SmsDbContext db, string sql, params object[] args)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < args.Length; i++) cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task Meta(SmsDbContext db) => Sql(db, """
        INSERT INTO routing_meta (key, value) VALUES ('active_version', '1'), ('grid_nodes', '1000'), ('region', '9,53,13,56')
        ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value
        """);

    private static Task Edge(SmsDbContext db, long s, long t, LineString g, double? depth = null, bool traffic = false, bool unknownBridge = false) => Sql(db, """
        INSERT INTO routing_edge (graph_version, source, target, cost, reverse_cost, x1, y1, x2, y2, geom, min_depth_m, traffic, unknown_bridge)
        VALUES (@p0, @p1, @p2, 1, 1, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10)
        """, Version, s, t, g.StartPoint.X, g.StartPoint.Y, g.EndPoint.X, g.EndPoint.Y, g, (object?)depth ?? DBNull.Value, traffic, unknownBridge);

    private static RoutingCorrection Fairway(LineString l) => new() { Kind = CorrectionKind.Passage, Source = CorrectionSource.Derived, Geometry = l, Note = "Fahrwasser (3 Tonnenpaare)" };

    [Fact]
    public async Task Detection_finds_uncertain_water_unknown_bridges_gaps_dead_ends_and_isolated_fairways()
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        await Meta(db);
        // uncertain water: three 1.2 km edges in the 1-2 fathom band in cell (333, 1800); the same in dense traffic does not count
        for (var k = 0; k < 3; k++) await Edge(db, 1 + k, 10 + k, Line(10.0, 54.005 + k * 0.005, 10.019, 54.005 + k * 0.005), depth: 1.8288);
        await Edge(db, 20, 21, Line(10.10, 54.01, 10.119, 54.01), depth: 1.8288, traffic: true);
        // a derived bridge without clearance the graph passes under
        var bridge = Line(10.30, 54.000, 10.30, 54.003);
        db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Bridge, Source = CorrectionSource.Derived, Geometry = bridge, Note = "Brücke ohne Höhenangabe – Test" });
        await Edge(db, 30, 31, Line(10.29, 54.0015, 10.31, 54.0015), unknownBridge: true);
        // L1: start not linked, end linked to water whose only edge is uncertain → isolated
        var l1 = Line(10.50, 54.30, 10.52, 54.30);
        await Edge(db, 1001, 1002, l1);
        await Edge(db, 1002, 40, Line(10.52, 54.30, 10.53, 54.30));
        await Edge(db, 40, 41, Line(10.53, 54.30, 10.54, 54.30), depth: 0);
        // L2: start linked to known water, end linked to uncertain water only → a dead end, stretch to the known edge
        var l2 = Line(10.50, 54.40, 10.52, 54.40);
        await Edge(db, 1003, 1004, l2);
        await Edge(db, 1003, 50, Line(10.50, 54.40, 10.49, 54.40));
        await Edge(db, 50, 51, Line(10.49, 54.40, 10.48, 54.40), depth: 9.144);
        await Edge(db, 1004, 52, Line(10.52, 54.40, 10.53, 54.40));
        await Edge(db, 52, 53, Line(10.53, 54.40, 10.54, 54.40), depth: 1.8288);
        // L3 → 1 km gap → L4, straight on
        var l3 = Line(10.70, 54.50, 10.75, 54.50); var l4 = Line(10.765, 54.50, 10.80, 54.50);
        db.RoutingCorrections.AddRange(Fairway(l1), Fairway(l2), Fairway(l3), Fairway(l4));
        await db.SaveChangesAsync();

        var result = await RoutingIssues.RunAsync(db, default);
        Assert.Equal(Version, result.Version);
        var issues = await db.RoutingIssues.AsNoTracking().ToListAsync();

        var water = Assert.Single(issues, i => i.Key == "water:333:1800");
        Assert.True(water.Priority >= 3); // ≈ 3.7 km
        Assert.DoesNotContain(issues, i => i.Key == "water:336:1800"); // the traffic cell
        var b = Assert.Single(issues, i => i.Type == IssueType.BridgeUnknown);
        Assert.Equal(RoutingIssues.BridgeKey(bridge), b.Key);
        Assert.Equal("Brücke ohne Höhenangabe – Test", b.Title);
        Assert.Single(issues, i => i.Type == IssueType.FairwayIsolated && i.Geometry.EqualsExact(l1));
        var dead = Assert.Single(issues, i => i.Type == IssueType.FairwayDeadEnd && i.Key == "deadend:10.52000,54.40000");
        Assert.Equal(new Coordinate(10.49, 54.40), ((LineString)dead.Geometry).EndPoint.Coordinate); // nearest certain water: beyond L2's other end
        var gap = Assert.Single(issues, i => i.Type == IssueType.FairwayGap);
        Assert.Equal(2, gap.RefId!.Split(',').Length);
    }

    [Fact]
    public async Task Fixes_are_manual_ignore_persists_and_decisions_turn_outdated_when_their_data_changes()
    {
        var (admin, _) = await f.AdminAsync();
        Guid harbourId, bridgeId;
        var bridge = Line(11.30, 55.000, 11.30, 55.003);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
            await Meta(db);
            for (var k = 0; k < 3; k++) await Edge(db, 101 + k, 110 + k, Line(11.105, 55.205 + k * 0.005, 11.125, 55.205 + k * 0.005), depth: 1.8288);
            await Sql(db, "INSERT INTO routing_depth (geom, min_depth_m, max_depth_m, source) VALUES (@p0, 1.8288, 3.6576, 'ft')", Box(11.10, 55.20, 11.13, 55.23));
            var derived = new RoutingCorrection { Kind = CorrectionKind.Bridge, Source = CorrectionSource.Derived, Geometry = bridge, Note = "Brücke ohne Höhenangabe – Fix" };
            db.RoutingCorrections.Add(derived); bridgeId = derived.Id;
            await Edge(db, 130, 131, Line(11.29, 55.0015, 11.31, 55.0015), unknownBridge: true);
            var harbour = new Harbour { Name = "Testhafen ohne Zugang", Location = Geo.Point(55.5, 11.5) };
            db.Harbours.Add(harbour); harbourId = harbour.Id;
            await db.SaveChangesAsync();
        }
        (await admin.PostAsync("/api/admin/issues/detect", null)).EnsureSuccessStatusCode();
        var waterKey = "water:370:1840"; // 11.10 / 0.03, 55.20 / 0.03
        var bridgeKey = RoutingIssues.BridgeKey(bridge);
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/issues?types=UncertainWater,BridgeUnknown,HarbourOffWater&bbox=54.9,11,55.6,11.6");
        var keys = list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()).ToList();
        Assert.Contains(waterKey, keys); Assert.Contains(bridgeKey, keys); Assert.Contains($"harbour:{harbourId}", keys);

        // fixes: Manual rows; a bridge fix deactivates the derived row; one decision per key
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/issues/fix", new { key = bridgeKey, action = "clearance" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/issues/fix", new { key = waterKey, action = "clearance", clearanceM = 5 })).StatusCode);
        var water = await (await admin.PostAsJsonAsync("/api/admin/issues/fix", new { key = waterKey, action = "known" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Fairway", water.GetProperty("kind").GetString());
        var bridgeFix = await admin.PostAsJsonAsync("/api/admin/issues/fix", new { key = bridgeKey, action = "clearance", clearanceM = 12.5 });
        Assert.Equal(HttpStatusCode.Created, bridgeFix.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/issues/fix", new { key = bridgeKey, action = "closed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/issues/fix", new { key = $"harbour:{harbourId}", action = "ignore", note = "Winterlager" })).StatusCode);
        var bridgeCorrection = (await bridgeFix.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("correctionId").GetGuid();
        var waterCorrection = water.GetProperty("correctionId").GetGuid();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
            var made = await db.RoutingCorrections.AsNoTracking().Where(c => c.Id == bridgeCorrection || c.Id == waterCorrection).ToListAsync();
            Assert.All(made, c => Assert.Equal(CorrectionSource.Manual, c.Source));
            Assert.Equal(12.5, made.Single(c => c.Id == bridgeCorrection).ClearanceM);
            Assert.Equal(bridge, made.Single(c => c.Id == bridgeCorrection).Geometry);
            Assert.Contains(Fairways.WideNote, made.Single(c => c.Id == waterCorrection).Note);
            Assert.False((await db.RoutingCorrections.AsNoTracking().SingleAsync(c => c.Id == bridgeId)).Active);

            // a re-import with the same data: derived rows replaced, depth rows re-inserted identically → nothing outdated
            await db.RoutingCorrections.Where(c => c.Source == CorrectionSource.Derived && c.Kind == CorrectionKind.Bridge).ExecuteDeleteAsync();
            await Sql(db, "DELETE FROM routing_depth WHERE source = 'ft' AND geom && @p0", Box(11.10, 55.20, 11.13, 55.23));
            await Sql(db, "INSERT INTO routing_depth (geom, min_depth_m, max_depth_m, source) VALUES (@p0, 1.8288, 3.6576, 'ft')", Box(11.10, 55.20, 11.13, 55.23));
        }
        (await admin.PostAsync("/api/admin/issues/detect", null)).EnsureSuccessStatusCode(); // as after a rebuild: decisions checked
        var outdated = await admin.GetFromJsonAsync<JsonElement>("/api/admin/issues?status=outdated&bbox=54.9,11,55.6,11.6");
        Assert.Equal(0, outdated.GetProperty("items").GetArrayLength());
        var ignored = await admin.GetFromJsonAsync<JsonElement>("/api/admin/issues?status=ignored&types=HarbourOffWater&bbox=54.9,11,55.6,11.6");
        var harbourItem = Assert.Single(ignored.GetProperty("items").EnumerateArray());
        Assert.True(harbourItem.GetProperty("detected").GetBoolean()); // still detected, but decided: not in the open list
        Assert.DoesNotContain((await admin.GetFromJsonAsync<JsonElement>("/api/admin/issues?types=HarbourOffWater&bbox=54.9,11,55.6,11.6")).GetProperty("items").EnumerateArray(),
            i => i.GetProperty("key").GetString() == $"harbour:{harbourId}");

        // the data changes: a shallower band, a charted clearance for the bridge, the harbour pin moved
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
            await Sql(db, "INSERT INTO routing_depth (geom, min_depth_m, max_depth_m, source) VALUES (@p0, 0, 1.8288, 'ft')", Box(11.11, 55.21, 11.12, 55.22));
            db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Bridge, Source = CorrectionSource.Derived, Geometry = bridge, ClearanceM = 8, Note = "Brücke" });
            var h = await db.Harbours.SingleAsync(x => x.Id == harbourId); h.Location = Geo.Point(55.51, 11.5);
            await db.SaveChangesAsync();
        }
        (await admin.PostAsync("/api/admin/issues/check", null)).EnsureSuccessStatusCode();
        outdated = await admin.GetFromJsonAsync<JsonElement>("/api/admin/issues?status=outdated&bbox=54.9,11,55.6,11.6");
        var reasons = outdated.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("key").GetString()!, i => i.GetProperty("decision").GetProperty("outdatedReason").GetString()!);
        Assert.StartsWith("Tiefendaten geändert", reasons[waterKey]);
        Assert.StartsWith("Neue Brückendaten", reasons[bridgeKey]);
        Assert.Contains("8 m", reasons[bridgeKey]);
        Assert.StartsWith("Hafen geändert", reasons[$"harbour:{harbourId}"]);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
            Assert.Equal(12.5, (await db.RoutingCorrections.AsNoTracking().SingleAsync(c => c.Id == bridgeCorrection)).ClearanceM); // the fix is never changed
        }

        // reviewed: the basis is the data as it is now
        var waterDecision = outdated.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("key").GetString() == waterKey).GetProperty("decision").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/issues/decisions/{waterDecision}/confirm", null)).StatusCode);
        (await admin.PostAsync("/api/admin/issues/check", null)).EnsureSuccessStatusCode();
        outdated = await admin.GetFromJsonAsync<JsonElement>("/api/admin/issues?status=outdated&bbox=54.9,11,55.6,11.6");
        Assert.DoesNotContain(outdated.GetProperty("items").EnumerateArray(), i => i.GetProperty("key").GetString() == waterKey);

        var edges = await admin.GetFromJsonAsync<JsonElement>("/api/admin/issues/uncertain-edges?bbox=55.19,11.09,55.24,11.14");
        Assert.Equal(3, edges.GetProperty("coordinates").GetArrayLength());

        var (user, _) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/issues")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/admin/issues/fix", new { key = waterKey, action = "ignore" })).StatusCode);
    }
}
