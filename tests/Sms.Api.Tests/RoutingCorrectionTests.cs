using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sms.Api.Tests;

/// <summary>Spec 10 §10.4: admin routing corrections (blocks, passages) CRUD with GeoJSON geometries.</summary>
public class RoutingCorrectionTests(ApiFixture f) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Admin_creates_toggles_and_deletes_corrections_with_validation()
    {
        var (admin, _) = await f.AdminAsync();
        var passage = new { kind = "Passage", geometry = new { type = "LineString", coordinates = new[] { new[] { 10.15, 54.40 }, new[] { 10.20, 54.45 } } }, note = "Testpassage", active = true };
        var created = await admin.PostAsJsonAsync("/api/admin/routing/corrections", passage);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // a Block needs a polygon, a Passage a line
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/routing/corrections", new { kind = "Block", geometry = passage.geometry, note = "x", active = true })).StatusCode);
        var block = new { kind = "Block", geometry = new { type = "Polygon", coordinates = new[] { new[] { new[] { 10.0, 54.0 }, new[] { 10.1, 54.0 }, new[] { 10.1, 54.1 }, new[] { 10.0, 54.0 } } } }, note = "Sperrgebiet", active = true };
        (await admin.PostAsJsonAsync("/api/admin/routing/corrections", block)).EnsureSuccessStatusCode();
        // a ferry track is a line too
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/routing/corrections", new { kind = "Ferry", geometry = block.geometry, note = "Fährlinie", active = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/admin/routing/corrections", new { kind = "Ferry", geometry = passage.geometry, note = "Fährlinie", active = true })).StatusCode);

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/corrections");
        var mine = list.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
        Assert.Equal("Manual", mine.GetProperty("source").GetString());
        Assert.Equal("LineString", mine.GetProperty("geometry").GetProperty("type").GetString());
        Assert.True(mine.GetProperty("active").GetBoolean());

        (await admin.PutAsJsonAsync($"/api/admin/routing/corrections/{id}", new { kind = "Passage", geometry = passage.geometry, note = "Testpassage", active = false })).EnsureSuccessStatusCode();
        list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/routing/corrections");
        Assert.False(list.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id).GetProperty("active").GetBoolean());

        var (user, _) = await f.UserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/routing/corrections")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/admin/routing/corrections/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/routing/corrections/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/admin/routing/corrections/{id}")).StatusCode);
    }

    private static readonly NetTopologySuite.Geometries.GeometryFactory Gf = NetTopologySuite.Geometries.GeometryFactory.Default;
    private static NetTopologySuite.Geometries.Geometry Pt(double x, double y) { var p = Gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y)); p.SRID = 4326; return p; }
    private static NetTopologySuite.Geometries.Geometry Ln(double x1, double y1, double x2, double y2) { var l = Gf.CreateLineString([new(x1, y1), new(x2, y2)]); l.SRID = 4326; return l; }
    private static Sms.Domain.RoutingCorrection Row(NetTopologySuite.Geometries.Geometry g, double? clearance, Sms.Domain.CorrectionKind kind = Sms.Domain.CorrectionKind.Bridge) =>
        new() { Kind = kind, Source = Sms.Domain.CorrectionSource.Derived, Geometry = g, ClearanceM = clearance, Note = "Brücke" };

    [Fact]
    public async Task Dedupe_drops_an_unknown_bridge_only_next_to_a_known_one_of_the_same_shape()
    {
        // far from every other test's rows (the dedupe runs over the whole table)
        var knownPoint = Row(Pt(30.000, 70.000), 22);
        var lineAtPoint = Row(Ln(29.995, 69.996, 30.005, 70.004), null);   // the whole bridge, the point is its span (Fehmarnsund, Kvickbron): stays
        var pointAtPoint = Row(Pt(30.0005, 70.0001), null);               // ~25 m: the same crossing drawn again
        var knownLine = Row(Ln(31.000, 70.000, 31.000, 70.005), 12);
        var lineAtLine = Row(Ln(31.0005, 70.000, 31.0005, 70.005), null);  // ~20 m beside it: rail next to road
        var lineInSeries = Row(Ln(31.000, 70.005, 31.000, 70.015), null);  // on from its end: the approach viaduct, stays
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Sms.Infrastructure.SmsDbContext>();
        db.RoutingCorrections.AddRange(knownPoint, lineAtPoint, pointAtPoint, knownLine, lineAtLine, lineInSeries);
        await db.SaveChangesAsync();
        await Sms.Infrastructure.Routing.Waterways.DedupeUnknownBridgesAsync(db, dryRun: false, default);
        var active = await db.RoutingCorrections.AsNoTracking().Where(c => new[] { lineAtPoint.Id, pointAtPoint.Id, lineAtLine.Id, lineInSeries.Id }.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Active);
        Assert.True(active[lineAtPoint.Id]);
        Assert.False(active[pointAtPoint.Id]);
        Assert.False(active[lineAtLine.Id]);
        Assert.True(active[lineInSeries.Id]);
    }

    [Fact]
    public async Task Havnelods_deactivates_only_the_derived_rows_its_bridge_covers()
    {
        // fixed bridge: the main span's way (the entry point on it), a rail deck 19 m beside it, an approach viaduct in series (Øresundsbroen)
        var main = Row(Ln(32.000, 70.000, 32.000, 70.002), null);
        var beside = Row(Ln(32.0005, 70.000, 32.0005, 70.002), null);
        var approach = Row(Ln(32.000, 70.002, 32.000, 70.012), null);
        // opening bridge kept as a point by an earlier run (Guldborgbroen on OSM's node): the node it replaced goes, a bridge line drawn
        // since stays (the point is its span; alone the point puts the opening penalty on every edge within 1.5 km)
        var node = Row(Pt(33.000, 70.000), null, Sms.Domain.CorrectionKind.OpeningBridge);
        var line = Row(Ln(32.999, 69.9995, 33.001, 70.0005), null);
        var own = new Sms.Domain.RoutingCorrection { Kind = Sms.Domain.CorrectionKind.OpeningBridge, Source = Sms.Domain.CorrectionSource.Manual, Geometry = Pt(33.000, 70.000), Active = true, Note = "Havnelods: Testbro test-opening-0001" };
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Sms.Infrastructure.SmsDbContext>();
        db.RoutingCorrections.AddRange(main, beside, approach, node, line, own);
        await db.SaveChangesAsync();
        var fixedItem = await Sms.Infrastructure.Routing.Havnelods.ApplyAsync(db, new("test-fixed-0001", "Testbro fast", 70.001, 32.000), new("Fast bro", false, 5, null, 5, "5 m", null), null, default);
        var openItem = await Sms.Infrastructure.Routing.Havnelods.ApplyAsync(db, new("test-opening-0001", "Testbro", 70.000, 33.000), new("Dobbeltfløjet klapbro", true, 4, null, 4, "4 m", null), null, default);
        await db.SaveChangesAsync();
        Assert.Equal(("created", 2), (fixedItem.Action, fixedItem.Deactivated));
        Assert.Equal(("updated", 1), (openItem.Action, openItem.Deactivated));
        var active = await db.RoutingCorrections.AsNoTracking().Where(c => new[] { main.Id, beside.Id, approach.Id, node.Id, line.Id }.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Active);
        Assert.False(active[main.Id]);
        Assert.False(active[beside.Id]);
        Assert.True(active[approach.Id]);
        Assert.False(active[node.Id]);
        Assert.True(active[line.Id]);
    }
}
