using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record FeedbackGroupUpsert(string Name, string? Description, FeedbackStatus Status = FeedbackStatus.Open);
public record FeedbackItemUpsert(FeedbackKind Kind, string? ObjectRef, Geometry? Geometry, string? Note, string? Expectation, Guid? GroupId = null);

/// <summary>
/// Fahrwasser-Feedback (admin): groups of examples where the fairway detection (Fairways.Derive) is wrong – derived objects marked wrong
/// with a snapshot, drawn fairways that should exist, comments – plus an export for review and a regression check against the current
/// derived passages. Only collects feedback; the routing never reads it.
/// </summary>
public static class FeedbackEndpoints
{
    public const string ExportFormat = "sms-fairway-feedback/1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(), new GeoJsonConverterFactory() } };

    public static void MapFeedback(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/admin/feedback").WithTags("Admin").RequireAuthorization()
            .AddEndpointFilter(async (ctx, next) => await ctx.HttpContext.RequestServices.GetRequiredService<CurrentUser>().IsAdminAsync(ctx.HttpContext.RequestAborted) ? await next(ctx) : Results.Forbid());

        // the map: passages (derived fairways, assumptions, narrows, OSM waterways, manual) and lateral marks of the viewport
        g.MapGet("/layers", async (string bbox, int? limit, SmsDbContext db, CancellationToken ct) =>
        {
            if (Bbox(bbox) is not { } b) return Invalid("bbox", "bbox=south,west,north,east");
            var max = Math.Clamp(limit ?? 4000, 1, 20000);
            var box = Geo.Factory.ToGeometry(new Envelope(b.w, b.e, b.s, b.n));
            var passages = await db.RoutingCorrections.AsNoTracking().Where(c => c.Kind == CorrectionKind.Passage && c.Geometry.Intersects(box))
                .OrderBy(c => c.Source).Take(max).Select(c => new { c.Id, c.Source, c.Note, c.Active, c.Geometry, c.CreatedAt }).ToListAsync(ct);
            var sw = SeamarkEndpoints.ToMercator(b.s, b.w); var ne = SeamarkEndpoints.ToMercator(b.n, b.e);
            var mbox = Geo.Factory.ToGeometry(new Envelope(sw.X, ne.X, sw.Y, ne.Y)); mbox.SRID = 3857;
            var marks = await db.Seamarks.AsNoTracking().Where(m => (m.Type == "buoy_lateral" || m.Type == "beacon_lateral") && m.Location.Within(mbox))
                .Take(max).Select(m => new { m.Id, m.Type, m.Category, m.Name, m.Colour, m.Location, m.TagsJson }).ToListAsync(ct);
            return Results.Ok(new
            {
                passages = passages.Select(c => new { c.Id, c.Source, c.Note, c.Active, c.Geometry, c.CreatedAt, category = FeedbackRegression.PassageCategory(c.Note, c.Source) }),
                buoys = marks.Select(m =>
                {
                    var (lat, lng) = Fairways.FromMercator(m.Location.X, m.Location.Y);
                    return new { m.Id, m.Type, m.Category, m.Name, m.Colour, side = Fairways.SideOf(m.Type, m.Category), lat, lng, tags = Parse(m.TagsJson) };
                }),
                truncated = passages.Count >= max || marks.Count >= max,
            });
        });

        g.MapGet("/groups", async (SmsDbContext db, CancellationToken ct) => Results.Ok(await db.FeedbackGroups.AsNoTracking().OrderBy(x => x.Status).ThenByDescending(x => x.UpdatedAt)
            .Select(x => new { x.Id, x.Name, x.Description, x.Status, x.CreatedAt, x.CreatedById, x.UpdatedAt, items = x.Items.Count,
                wrong = x.Items.Count(i => i.Kind == FeedbackKind.WrongObject), missing = x.Items.Count(i => i.Kind == FeedbackKind.MissingFairway) }).ToListAsync(ct)));
        g.MapPost("/groups", async (FeedbackGroupUpsert req, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Invalid("name", "Name fehlt.");
            var grp = new FeedbackGroup { Name = req.Name.Trim(), Description = Blank(req.Description), Status = req.Status, CreatedById = current.Id };
            db.FeedbackGroups.Add(grp);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/feedback/groups/{grp.Id}", new { grp.Id });
        });
        g.MapPut("/groups/{id:guid}", async (Guid id, FeedbackGroupUpsert req, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Invalid("name", "Name fehlt.");
            var grp = await db.FeedbackGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (grp is null) return Results.NotFound();
            grp.Name = req.Name.Trim(); grp.Description = Blank(req.Description); grp.Status = req.Status; grp.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        g.MapDelete("/groups/{id:guid}", async (Guid id, SmsDbContext db, CancellationToken ct) =>
            await db.FeedbackGroups.Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : Results.NotFound()); // items cascade

        g.MapGet("/groups/{id:guid}/items", async (Guid id, SmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.FeedbackGroups.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var items = await db.FeedbackItems.AsNoTracking().Where(i => i.GroupId == id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
            return Results.Ok(items.Select(ItemDto));
        });
        g.MapPost("/groups/{id:guid}/items", async (Guid id, FeedbackItemUpsert req, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.FeedbackGroups.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var item = new FeedbackItem { GroupId = id, Kind = req.Kind, Note = Blank(req.Note), Expectation = Blank(req.Expectation), Geometry = req.Geometry, CreatedById = current.Id };
            if (req.ObjectRef is { Length: > 0 } r)
            {
                // the snapshot is taken here, from the database: the example survives re-derivation (which replaces the derived rows)
                if (await SnapshotAsync(r, db, ct) is not { } snap) return Invalid("objectRef", $"Objekt {r} nicht gefunden.");
                item.ObjectRef = r; item.ObjectSnapshot = snap.json; item.Geometry ??= snap.geometry;
            }
            if (Validate(item) is { } error) return error;
            await TouchAsync(db, id, ct);
            db.FeedbackItems.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/feedback/items/{item.Id}", ItemDto(item));
        });
        g.MapPut("/items/{id:guid}", async (Guid id, FeedbackItemUpsert req, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            var item = await db.FeedbackItems.FirstOrDefaultAsync(i => i.Id == id, ct);
            if (item is null) return Results.NotFound();
            if (req.GroupId is { } gid && gid != item.GroupId)
            {
                if (!await db.FeedbackGroups.AnyAsync(x => x.Id == gid, ct)) return Invalid("groupId", "Gruppe nicht gefunden.");
                item.GroupId = gid;
            }
            item.Kind = req.Kind; item.Note = Blank(req.Note); item.Expectation = Blank(req.Expectation); item.UpdatedById = current.Id;
            if (req.Geometry is not null && item.ObjectRef is null) item.Geometry = req.Geometry; // a marked object keeps its geometry
            if (Validate(item) is { } error) return error;
            await TouchAsync(db, item.GroupId, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ItemDto(item));
        });
        g.MapDelete("/items/{id:guid}", async (Guid id, SmsDbContext db, CancellationToken ct) =>
            await db.FeedbackItems.Where(i => i.Id == id).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : Results.NotFound());

        g.MapGet("/groups/{id:guid}/export", async (Guid id, SmsDbContext db, CancellationToken ct) =>
        {
            var grp = await db.FeedbackGroups.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
            return grp is null ? Results.NotFound() : Results.Ok(new { format = ExportFormat, exportedAt = DateTimeOffset.UtcNow, group = ExportGroup(grp) });
        });
        g.MapGet("/export", async (string? status, SmsDbContext db, CancellationToken ct) =>
        {
            if (!TryStatus(status, out var st)) return Invalid("status", "status=open|resolved|all");
            var groups = await Groups(db, st).Include(x => x.Items).AsNoTracking().ToListAsync(ct);
            return Results.Ok(new { format = ExportFormat, exportedAt = DateTimeOffset.UtcNow, status = status ?? "open", groups = groups.Select(ExportGroup) });
        });

        // after a change to Fairways.cs + derive-fairways: which examples does the current derivation satisfy?
        g.MapGet("/regression", async (string? status, Guid? group, SmsDbContext db, CancellationToken ct) =>
        {
            if (!TryStatus(status, out var st)) return Invalid("status", "status=open|resolved|all");
            var q = group is { } gid ? db.FeedbackGroups.Where(x => x.Id == gid) : Groups(db, st);
            var groups = await q.Include(x => x.Items).AsNoTracking().ToListAsync(ct);
            var result = new List<object>();
            foreach (var grp in groups)
            {
                var items = new List<RegressionRow>();
                foreach (var i in grp.Items.OrderBy(i => i.CreatedAt)) items.Add(await CheckAsync(i, db, ct));
                result.Add(new { grp.Id, grp.Name, grp.Status, satisfied = items.Count(r => r.Satisfied == true), failed = items.Count(r => r.Satisfied == false),
                    unknown = items.Count(r => r.Satisfied is null), items });
            }
            return Results.Ok(new { checkedAt = DateTimeOffset.UtcNow,
                rules = new { FeedbackRegression.MissingToleranceM, FeedbackRegression.MissingSatisfiedShare, FeedbackRegression.WrongToleranceM, FeedbackRegression.WrongPresentShare, FeedbackRegression.WrongGoneShare },
                groups = result });
        });
    }

    public sealed record RegressionMatch(Guid Id, string? Note, string Category, double Share);
    public sealed record RegressionRow(Guid Id, FeedbackKind Kind, string? ObjectRef, string? Note, string? Expectation, string Status, bool? Satisfied, double? Coverage,
        bool? Identical, bool? RefExists, IReadOnlyList<RegressionMatch> Matches);

    private static async Task<RegressionRow> CheckAsync(FeedbackItem i, SmsDbContext db, CancellationToken ct)
    {
        RegressionRow Row(string status, bool? ok = null, double? coverage = null, bool? identical = null, bool? refExists = null, IReadOnlyList<RegressionMatch>? m = null) =>
            new(i.Id, i.Kind, i.ObjectRef, i.Note, i.Expectation, status, ok, coverage is { } c ? Math.Round(c, 3) : null, identical, refExists, m ?? []);
        bool? refExists = null;
        if (i.ObjectRef?.Split(':') is [var type, var key])
            refExists = type == "correction" && Guid.TryParse(key, out var cid) ? await db.RoutingCorrections.AnyAsync(c => c.Id == cid, ct)
                : type == "seamark" && long.TryParse(key, out var sid) ? await db.Seamarks.AnyAsync(s => s.Id == sid, ct) : null;
        if (i.Kind == FeedbackKind.Comment || i.Geometry is null || i.Geometry.Dimension == Dimension.Point) return Row("unchecked", refExists: refExists);

        var env = i.Geometry.EnvelopeInternal.Copy(); env.ExpandBy(0.003); // ≈ 200 m: more than both tolerances
        var box = Geo.Factory.ToGeometry(env);
        var current = (await db.RoutingCorrections.AsNoTracking().Where(c => c.Kind == CorrectionKind.Passage && c.Active && c.Geometry.Intersects(box))
            .Select(c => new { c.Id, c.Note, c.Source, c.Geometry }).ToListAsync(ct))
            .Select(c => new { c.Id, c.Note, c.Geometry, category = FeedbackRegression.PassageCategory(c.Note, c.Source) }).ToList();

        if (i.Kind == FeedbackKind.MissingFairway)
        {
            var m = FeedbackRegression.CheckMissing(i.Geometry, current.Select(c => c.Geometry).ToList());
            var matches = current.Select(c => new RegressionMatch(c.Id, c.Note, c.category, Math.Round(FeedbackRegression.Coverage(i.Geometry, [c.Geometry], FeedbackRegression.MissingToleranceM), 3)))
                .Where(x => x.Share > 0).OrderByDescending(x => x.Share).Take(5).ToList();
            return Row(m.Status, m.Satisfied, m.Coverage, null, refExists, matches);
        }
        // a wrong object is compared with the current passages of its own category (a derived fairway is not "still there" because an OSM way runs along it)
        var category = SnapshotString(i.ObjectSnapshot, "category");
        var same = category is null ? current : current.Where(c => c.category == category).ToList();
        var w = FeedbackRegression.CheckWrong(i.Geometry, same.Select(c => c.Geometry).ToList());
        var best = w.Best is { } b ? new List<RegressionMatch> { new(same[b].Id, same[b].Note, same[b].category, Math.Round(FeedbackRegression.Coverage(i.Geometry, [same[b].Geometry], FeedbackRegression.WrongToleranceM), 3)) } : [];
        return Row(w.Status, w.Satisfied, w.Coverage, w.Identical, refExists, best);
    }

    private static async Task<(string json, Geometry geometry)?> SnapshotAsync(string objectRef, SmsDbContext db, CancellationToken ct)
    {
        switch (objectRef.Split(':'))
        {
            case ["correction", var key] when Guid.TryParse(key, out var id):
                var c = await db.RoutingCorrections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                if (c is null) return null;
                return (JsonSerializer.Serialize(new { @ref = objectRef, c.Kind, c.Source, c.Note, c.Active, c.ClearanceM, category = FeedbackRegression.PassageCategory(c.Note, c.Source), c.CreatedAt, c.Geometry }, Json), c.Geometry);
            case ["seamark", var key] when long.TryParse(key, out var id):
                var m = await db.Seamarks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                if (m is null) return null;
                var (lat, lng) = Fairways.FromMercator(m.Location.X, m.Location.Y);
                var point = Geo.Point(lat, lng);
                return (JsonSerializer.Serialize(new { @ref = objectRef, m.Type, m.Category, m.Name, m.Colour, side = Fairways.SideOf(m.Type, m.Category), m.OsmType, m.OsmId, lat, lng, tags = Parse(m.TagsJson), geometry = point }, Json), point);
            default: return null;
        }
    }

    private static IResult? Validate(FeedbackItem i)
    {
        if (i.Geometry is not null) i.Geometry.SRID = 4326;
        return i.Kind switch
        {
            FeedbackKind.MissingFairway when i.Geometry is not LineString { NumPoints: >= 2 } => Invalid("geometry", "Ein Fahrwasser braucht eine Linie mit mindestens zwei Punkten."),
            FeedbackKind.WrongObject when i.ObjectRef is null && i.Geometry is null => Invalid("objectRef", "Objekt oder Geometrie fehlt."),
            FeedbackKind.Comment when i.Note is null => Invalid("note", "Kommentar fehlt."),
            _ => null,
        };
    }

    private static object ItemDto(FeedbackItem i) => new { i.Id, i.GroupId, i.Kind, i.ObjectRef, snapshot = Parse(i.ObjectSnapshot), i.Geometry, i.Note, i.Expectation, i.CreatedAt, i.CreatedById };

    /// <summary>A group as a GeoJSON FeatureCollection with its metadata as foreign members (opens in geojson.io / QGIS as is).</summary>
    private static object ExportGroup(FeedbackGroup grp) => new
    {
        type = "FeatureCollection", id = grp.Id, name = grp.Name, description = grp.Description, status = grp.Status, createdAt = grp.CreatedAt, createdById = grp.CreatedById, updatedAt = grp.UpdatedAt,
        features = grp.Items.OrderBy(i => i.CreatedAt).Select(i => new
        {
            type = "Feature", id = i.Id, geometry = i.Geometry,
            properties = new { kind = i.Kind, objectRef = i.ObjectRef, note = i.Note, expectation = i.Expectation, createdAt = i.CreatedAt, snapshot = Parse(i.ObjectSnapshot) },
        }),
    };

    private static IQueryable<FeedbackGroup> Groups(SmsDbContext db, FeedbackStatus? status) =>
        (status is { } s ? db.FeedbackGroups.Where(x => x.Status == s) : db.FeedbackGroups).OrderBy(x => x.CreatedAt);
    private static bool TryStatus(string? s, out FeedbackStatus? status)
    {
        status = FeedbackStatus.Open;
        if (s is null || s.Equals("open", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("all", StringComparison.OrdinalIgnoreCase)) { status = null; return true; }
        if (s.Equals("resolved", StringComparison.OrdinalIgnoreCase)) { status = FeedbackStatus.Resolved; return true; }
        return false;
    }
    /// <summary>The group moves to the top of the list when its items change.</summary>
    private static Task TouchAsync(SmsDbContext db, Guid groupId, CancellationToken ct) =>
        db.FeedbackGroups.Where(x => x.Id == groupId).ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
    private static (double s, double w, double n, double e)? Bbox(string bbox)
    {
        var v = bbox.Split(',').Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
        return v.Length == 4 && v.All(double.IsFinite) && v[0] < v[2] && v[1] < v[3] ? (v[0], v[1], v[2], v[3]) : null;
    }
    private static JsonElement? Parse(string? json) => json is null ? null : JsonSerializer.Deserialize<JsonElement>(json);
    private static string? SnapshotString(string? json, string key) =>
        json is not null && Parse(json) is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
