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

    [Fact]
    public async Task Dedupe_drops_an_unknown_bridge_only_next_to_a_known_one_of_the_same_shape()
    {
        // far from every other test's rows (the dedupe runs over the whole table)
        var gf = NetTopologySuite.Geometries.GeometryFactory.Default;
        NetTopologySuite.Geometries.Geometry Pt(double x, double y) { var p = gf.CreatePoint(new NetTopologySuite.Geometries.Coordinate(x, y)); p.SRID = 4326; return p; }
        NetTopologySuite.Geometries.Geometry Ln(double x1, double y1, double x2, double y2) { var l = gf.CreateLineString([new(x1, y1), new(x2, y2)]); l.SRID = 4326; return l; }
        Sms.Domain.RoutingCorrection Row(NetTopologySuite.Geometries.Geometry g, double? clearance) =>
            new() { Kind = Sms.Domain.CorrectionKind.Bridge, Source = Sms.Domain.CorrectionSource.Derived, Geometry = g, ClearanceM = clearance, Note = "Brücke" };
        var knownPoint = Row(Pt(30.000, 70.000), 22);
        var lineAtPoint = Row(Ln(29.995, 69.996, 30.005, 70.004), null);   // the whole bridge, the point is its span (Fehmarnsund, Kvickbron): stays
        var pointAtPoint = Row(Pt(30.0005, 70.0001), null);               // ~25 m: the same crossing drawn again
        var knownLine = Row(Ln(31.000, 70.000, 31.000, 70.005), 12);
        var lineAtLine = Row(Ln(31.0005, 70.000, 31.0005, 70.005), null);  // ~20 m beside it: rail next to road
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Sms.Infrastructure.SmsDbContext>();
        db.RoutingCorrections.AddRange(knownPoint, lineAtPoint, pointAtPoint, knownLine, lineAtLine);
        await db.SaveChangesAsync();
        await Sms.Infrastructure.Routing.Waterways.DedupeUnknownBridgesAsync(db, dryRun: false, default);
        var active = await db.RoutingCorrections.AsNoTracking().Where(c => new[] { lineAtPoint.Id, pointAtPoint.Id, lineAtLine.Id }.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Active);
        Assert.True(active[lineAtPoint.Id]);
        Assert.False(active[pointAtPoint.Id]);
        Assert.False(active[lineAtLine.Id]);
    }
}
