using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record IssueFix(string Key, string Action, Geometry? Geometry = null, double? ClearanceM = null, string? Note = null);

/// <summary>
/// Probleme (admin, spec-admin-issues): the routing issues of the latest detection (RoutingIssues) with the admin's decisions; fixes
/// create Manual corrections (they survive every re-import), "kein Problem" is stored, and both turn outdated when their basis changes.
/// </summary>
public static class IssueEndpoints
{
    /// <summary>Fix actions per issue type ("ignore" for every type).</summary>
    public static readonly IReadOnlyDictionary<IssueType, string[]> Actions = new Dictionary<IssueType, string[]>
    {
        [IssueType.UncertainWater] = ["known", "avoid", "block"],
        [IssueType.BridgeUnknown] = ["clearance", "opening", "closed"],
        [IssueType.FairwayGap] = ["passage", "known", "avoid"],
        [IssueType.FairwayDeadEnd] = ["passage", "known", "avoid"],
        [IssueType.FairwayIsolated] = ["passage", "known", "avoid"],
        [IssueType.HarbourOffWater] = [],
    };
    private static int _detecting;

    public static void MapIssues(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/admin/issues").WithTags("Admin").RequireAuthorization()
            .AddEndpointFilter(async (ctx, next) => await ctx.HttpContext.RequestServices.GetRequiredService<CurrentUser>().IsAdminAsync(ctx.HttpContext.RequestAborted) ? await next(ctx) : Results.Forbid());

        // status: open (no decision, default) | fixed | ignored | outdated | all; types: comma list; bbox: south,west,north,east (the region)
        g.MapGet("", async (string? status, string? types, string? bbox, int? limit, SmsDbContext db, CancellationToken ct) =>
        {
            var st = (status ?? "open").ToLowerInvariant();
            if (st is not ("open" or "fixed" or "ignored" or "outdated" or "all")) return Invalid("status", "status=open|fixed|ignored|outdated|all");
            var typeList = new List<IssueType>();
            foreach (var t in (types ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Enum.TryParse<IssueType>(t, true, out var v)) typeList.Add(v); else return Invalid("types", $"Unbekannter Typ {t}.");
            if (typeList.Count == 0) typeList = Enum.GetValues<IssueType>().ToList();
            Geometry? box = null;
            if (bbox is { Length: > 0 })
            {
                if (FeedbackEndpoints.Bbox(bbox) is not { } b) return Invalid("bbox", "bbox=south,west,north,east");
                box = Geo.Factory.ToGeometry(new Envelope(b.w, b.e, b.s, b.n));
            }
            var max = Math.Clamp(limit ?? 200, 1, 2000);
            var issues = db.RoutingIssues.AsNoTracking().Where(i => typeList.Contains(i.Type));
            var decisions = db.IssueDecisions.AsNoTracking().Where(d => typeList.Contains(d.Type));
            if (box is not null) { issues = issues.Where(i => i.Geometry.Intersects(box)); decisions = decisions.Where(d => d.Geometry.Intersects(box)); }
            var open = issues.Where(i => !db.IssueDecisions.Any(d => d.Key == i.Key));

            var items = new List<object>();
            if (st is "open" or "all")
                items.AddRange((await open.OrderByDescending(i => i.Priority).Take(max).ToListAsync(ct)).Select(i => Dto(i, null)));
            if (st != "open")
            {
                var ds = st switch
                {
                    "fixed" => decisions.Where(d => d.Status == IssueStatus.Fixed),
                    "ignored" => decisions.Where(d => d.Status == IssueStatus.Ignored),
                    "outdated" => decisions.Where(d => d.OutdatedAt != null),
                    _ => decisions,
                };
                var list = await ds.OrderByDescending(d => d.OutdatedAt).ThenByDescending(d => d.UpdatedAt).Take(max).ToListAsync(ct);
                var keys = list.Select(d => d.Key).ToList();
                var current = await db.RoutingIssues.AsNoTracking().Where(i => keys.Contains(i.Key)).ToDictionaryAsync(i => i.Key, ct);
                items.AddRange(list.Select(d => Dto(current.GetValueOrDefault(d.Key), d)));
            }
            var openCounts = await open.GroupBy(i => i.Type).Select(x => new { type = x.Key, n = x.Count() }).ToListAsync(ct);
            var decisionCounts = await decisions.GroupBy(d => new { d.Type, d.Status, outdated = d.OutdatedAt != null }).Select(x => new { x.Key.Type, x.Key.Status, x.Key.outdated, n = x.Count() }).ToListAsync(ct);
            var counts = typeList.ToDictionary(t => t.ToString(), t => new
            {
                open = openCounts.Where(c => c.type == t).Sum(c => c.n),
                @fixed = decisionCounts.Where(c => c.Type == t && c.Status == IssueStatus.Fixed).Sum(c => c.n),
                ignored = decisionCounts.Where(c => c.Type == t && c.Status == IssueStatus.Ignored).Sum(c => c.n),
                outdated = decisionCounts.Where(c => c.Type == t && c.outdated).Sum(c => c.n),
            });
            var last = await db.RoutingIssues.AsNoTracking().OrderByDescending(i => i.DetectedAt).Select(i => new { i.DetectedAt, i.GraphVersion }).FirstOrDefaultAsync(ct);
            return Results.Ok(new { detectedAt = last?.DetectedAt, graphVersion = last?.GraphVersion, counts, items, actions = Actions.ToDictionary(a => a.Key.ToString(), a => a.Value), minDepthM = RoutingIssues.MinDepthM });
        });

        // detection on the active graph + outdated check (runs after every rebuild too)
        g.MapPost("/detect", async (SmsDbContext db, CancellationToken ct) =>
        {
            if (Interlocked.CompareExchange(ref _detecting, 1, 0) != 0) return Results.Problem(statusCode: 409, title: "Die Erkennung läuft bereits.");
            try { return Results.Ok(await RoutingIssues.RunAsync(db, ct)); }
            finally { Interlocked.Exchange(ref _detecting, 0); }
        });
        g.MapPost("/check", async (SmsDbContext db, CancellationToken ct) =>
        {
            var (checkedCount, outdated) = await RoutingIssues.CheckAsync(db, ct);
            return Results.Ok(new { @checked = checkedCount, outdated });
        });

        g.MapPost("/fix", async (IssueFix req, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            var issue = await db.RoutingIssues.AsNoTracking().FirstOrDefaultAsync(i => i.Key == req.Key, ct);
            if (issue is null) return Results.NotFound();
            if (await db.IssueDecisions.AnyAsync(d => d.Key == req.Key, ct)) return Results.Problem(statusCode: 409, title: "Für dieses Problem gibt es schon eine Entscheidung.");
            var action = req.Action.ToLowerInvariant();
            if (action != "ignore" && !Actions[issue.Type].Contains(action)) return Invalid("action", $"Aktion {req.Action} passt nicht zu {issue.Type}.");
            if (req.Geometry is not null) req.Geometry.SRID = 4326;

            RoutingCorrection? correction = null;
            if (action != "ignore")
            {
                var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
                var made = await CorrectionAsync(issue, action, req, note, db, ct);
                if (made.error is not null) return made.error;
                correction = made.correction!;
                correction.Source = CorrectionSource.Manual; // imports replace Derived rows only: the fix survives them
                correction.CreatedById = current.Id;
                db.RoutingCorrections.Add(correction);
            }
            var basis = await RoutingIssues.BasisAsync(db, issue.Type, issue.Key, issue.Geometry, ct);
            var decision = new IssueDecision
            {
                Key = issue.Key, Type = issue.Type, Status = correction is null ? IssueStatus.Ignored : IssueStatus.Fixed, CorrectionId = correction?.Id,
                Geometry = issue.Geometry, Title = issue.Title, Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(), CreatedById = current.Id,
                BasisKind = basis.Kind, BasisRefs = basis.Refs, Fingerprint = basis.Fingerprint, BasedAt = DateTimeOffset.UtcNow, CheckedAt = DateTimeOffset.UtcNow,
            };
            db.IssueDecisions.Add(decision);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/issues/decisions/{decision.Id}", new { decision.Id, correctionId = correction?.Id, correction?.Kind, decision.Status });
        });
        // the admin reviewed an outdated decision: its basis becomes the data as it is now
        g.MapPost("/decisions/{id:guid}/confirm", async (Guid id, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            var d = await db.IssueDecisions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null) return Results.NotFound();
            var basis = await RoutingIssues.BasisAsync(db, d.Type, d.Key, d.Geometry, ct);
            d.BasisRefs = basis.Refs; d.Fingerprint = basis.Fingerprint; d.BasedAt = DateTimeOffset.UtcNow; d.CheckedAt = d.BasedAt; d.OutdatedAt = null; d.OutdatedReason = null; d.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        // reopens the issue; the correction a fix created stays (delete it in the corrections editor)
        g.MapDelete("/decisions/{id:guid}", async (Guid id, SmsDbContext db, CancellationToken ct) =>
            await db.IssueDecisions.Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : Results.NotFound());

        // the uncertain edges of a small viewport, as one MultiLineString (map overlay for drawing a fix)
        g.MapGet("/uncertain-edges", async (string bbox, SmsDbContext db, CancellationToken ct) =>
        {
            if (FeedbackEndpoints.Bbox(bbox) is not { } b) return Invalid("bbox", "bbox=south,west,north,east");
            if ((b.n - b.s) * (b.e - b.w) > 1) return Invalid("bbox", "Höchstens 1 Quadratgrad.");
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand($"""
                SELECT ST_AsGeoJSON(ST_Collect(geom), 6) FROM (SELECT e.geom FROM routing_edge e
                WHERE e.graph_version = (SELECT value::int FROM routing_meta WHERE key = 'active_version') AND e.geom && ST_MakeEnvelope(@w, @s, @e, @n, 4326) AND {RoutingIssues.UncertainSql} LIMIT 5000) x
                """, conn);
            cmd.Parameters.AddWithValue("w", b.w); cmd.Parameters.AddWithValue("s", b.s); cmd.Parameters.AddWithValue("e", b.e); cmd.Parameters.AddWithValue("n", b.n);
            return Results.Text(await cmd.ExecuteScalarAsync(ct) as string ?? """{"type":"MultiLineString","coordinates":[]}""", "application/json");
        });
    }

    private static object Dto(RoutingIssue? i, IssueDecision? d) => new
    {
        key = i?.Key ?? d!.Key, type = i?.Type ?? d!.Type, title = i?.Title ?? d!.Title, detail = i?.Detail, priority = i?.Priority, geometry = i?.Geometry ?? d!.Geometry, refId = i?.RefId,
        detected = i is not null,
        decision = d is null ? null : new { d.Id, d.Status, d.CorrectionId, d.Note, d.BasisKind, d.BasisRefs, d.BasedAt, d.CheckedAt, d.OutdatedAt, d.OutdatedReason, d.CreatedAt },
    };

    /// <summary>The Manual correction an action makes from an issue: the drawn geometry, or the issue's own (lines and points buffered for an area).</summary>
    private static async Task<(RoutingCorrection? correction, IResult? error)> CorrectionAsync(RoutingIssue issue, string action, IssueFix req, string? note, SmsDbContext db, CancellationToken ct)
    {
        var label = $"{issue.Title} (Problemliste)";
        if (issue.Type == IssueType.BridgeUnknown)
        {
            if (action == "clearance" && req.ClearanceM is not (> 0 and < 200)) return (null, Invalid("clearanceM", "Durchfahrtshöhe in Metern angeben."));
            var ids = (issue.RefId ?? "").Split(',').Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty).ToList();
            var derived = await db.RoutingCorrections.Where(c => ids.Contains(c.Id) && c.Source == CorrectionSource.Derived).ToListAsync(ct);
            foreach (var d in derived) d.Active = false; // the graph stops flagging them now; import-bridges does not recreate a way next to an active bridge
            var geometry = issue.Geometry.Copy();
            return action switch
            {
                "clearance" => (new RoutingCorrection { Kind = CorrectionKind.Bridge, Geometry = geometry, ClearanceM = req.ClearanceM, Note = note ?? label }, null),
                "opening" => (new RoutingCorrection { Kind = CorrectionKind.OpeningBridge, Geometry = geometry, ClearanceM = req.ClearanceM, Note = note ?? label }, null),
                _ => (new RoutingCorrection { Kind = CorrectionKind.Bridge, Geometry = geometry, ClearanceM = 0, Note = note ?? $"{label}: gesperrt" }, null),
            };
        }
        if (action == "passage")
        {
            var line = req.Geometry ?? (issue.Geometry is LineString l && issue.Type != IssueType.FairwayIsolated ? l : null);
            if (line is not LineString { NumPoints: >= 2 }) return (null, Invalid("geometry", "Eine Passage braucht eine Linie mit mindestens zwei Punkten."));
            return (new RoutingCorrection { Kind = CorrectionKind.Passage, Geometry = line.Copy(), Note = note ?? label }, null);
        }
        var area = req.Geometry;
        if (area is null)
        {
            // the issue's cell, or its line/point buffered by AreaBufferM (metres, geography)
            if (issue.Geometry is Polygon or MultiPolygon) area = issue.Geometry.Copy();
            else
            {
                var g = issue.Geometry.Copy(); g.SRID = 4326;
                area = (await db.Database.SqlQueryRaw<Geometry>("SELECT ST_Buffer({0}::geography, {1})::geometry AS \"Value\"", g, RoutingIssues.AreaBufferM).ToListAsync(ct)).Single();
            }
        }
        if (area is not (Polygon or MultiPolygon)) return (null, Invalid("geometry", "Eine Fläche braucht ein Polygon."));
        area.SRID = 4326;
        return action switch
        {
            "known" => (new RoutingCorrection { Kind = CorrectionKind.Fairway, Geometry = area, Note = $"{RoutingIssues.KnownDepthNote}{(note is null ? "" : ": " + note)}" }, null),
            "avoid" => (new RoutingCorrection { Kind = CorrectionKind.Avoid, Geometry = area, Note = note ?? label }, null),
            _ => (new RoutingCorrection { Kind = CorrectionKind.Block, Geometry = area, Note = note ?? label }, null),
        };
    }
}
