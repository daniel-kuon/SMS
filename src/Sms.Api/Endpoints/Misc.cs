using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record TaskUpsert(string Title, string? Content, DateOnly? DueDate, Guid? ParentId, Guid? AssigneeCrewMemberId, Guid? AssigneeUserId, bool Done);
public record TaskDto(Guid Id, TaskScope Scope, Guid? TripId, Guid? ShipId, Guid? ParentId, string Title, string? Content, DateOnly? DueDate, Guid? AssigneeCrewMemberId, Guid? AssigneeUserId, string? Assignee, bool Done);
public record PageUpsert(string Slug, string Title, string Content, string Language, bool ShowInFooter);

public static class TaskEndpoints
{
    public static void MapTasks(this IEndpointRouteBuilder api)
    {
        api.MapGet("/trips/{tripId:guid}/tasks", async (Guid tripId, SmsDbContext db, Access access, CancellationToken ct) =>
            await access.IsCrewAsync(tripId, ct) ? Results.Ok(await List(db.Tasks.Where(t => t.TripId == tripId), db, ct)) : Results.Forbid()).RequireAuthorization().WithTags("Tasks");
        api.MapGet("/ships/{shipId:guid}/tasks", async (Guid shipId, SmsDbContext db, Access access, CancellationToken ct) =>
            await access.IsShipMemberAsync(shipId, null, ct) ? Results.Ok(await List(db.Tasks.Where(t => t.ShipId == shipId), db, ct)) : Results.Forbid()).RequireAuthorization().WithTags("Tasks");

        api.MapPost("/trips/{tripId:guid}/tasks", async (Guid tripId, TaskUpsert req, SmsDbContext db, Access access, CurrentUser current, HtmlSanitizerService html, CancellationToken ct) =>
        {
            if (!await access.IsCrewAsync(tripId, ct)) return Results.Forbid();
            var t = new TaskItem { Scope = TaskScope.Trip, TripId = tripId, CreatedById = current.Id };
            Apply(t, req, html); db.Tasks.Add(t); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/tasks/{t.Id}", new { t.Id });
        }).RequireAuthorization().WithTags("Tasks");
        api.MapPost("/ships/{shipId:guid}/tasks", async (Guid shipId, TaskUpsert req, SmsDbContext db, Access access, CurrentUser current, HtmlSanitizerService html, CancellationToken ct) =>
        {
            if (!await access.IsShipMemberAsync(shipId, null, ct)) return Results.Forbid();
            var t = new TaskItem { Scope = TaskScope.Ship, ShipId = shipId, CreatedById = current.Id };
            Apply(t, req, html); db.Tasks.Add(t); await db.SaveChangesAsync(ct);
            return Results.Created($"/api/tasks/{t.Id}", new { t.Id });
        }).RequireAuthorization().WithTags("Tasks");

        api.MapPut("/tasks/{id:guid}", async (Guid id, TaskUpsert req, SmsDbContext db, Access access, HtmlSanitizerService html, CancellationToken ct) =>
        {
            var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            if (!await CanAsync(t, access, ct)) return Results.Forbid();
            Apply(t, req, html); await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Tasks");

        api.MapDelete("/tasks/{id:guid}", async (Guid id, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            if (!await CanAsync(t, access, ct)) return Results.Forbid();
            await db.Tasks.Where(x => x.Id == id || x.ParentId == id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Tasks");
    }

    private static Task<bool> CanAsync(TaskItem t, Access access, CancellationToken ct) =>
        t.TripId is { } trip ? access.IsCrewAsync(trip, ct) : access.IsShipMemberAsync(t.ShipId!.Value, null, ct);

    private static void Apply(TaskItem t, TaskUpsert r, HtmlSanitizerService html)
    {
        t.Title = r.Title.Trim(); t.Content = html.Sanitize(r.Content); t.DueDate = r.DueDate; t.ParentId = r.ParentId;
        t.AssigneeCrewMemberId = r.AssigneeCrewMemberId; t.AssigneeUserId = r.AssigneeUserId; t.Done = r.Done;
    }

    private static async Task<List<TaskDto>> List(IQueryable<TaskItem> q, SmsDbContext db, CancellationToken ct)
    {
        var items = await q.AsNoTracking().OrderBy(t => t.Done).ThenBy(t => t.DueDate).ThenBy(t => t.CreatedAt).ToListAsync(ct);
        var users = await ShipEndpoints.DisplayNames(db, items.Where(t => t.AssigneeUserId != null).Select(t => t.AssigneeUserId!.Value), ct);
        var crew = await LogbookEndpoints.CrewNames(db, items.Where(t => t.TripId != null).Select(t => t.TripId!.Value).Distinct(), ct);
        return items.Select(t => new TaskDto(t.Id, t.Scope, t.TripId, t.ShipId, t.ParentId, t.Title, t.Content, t.DueDate, t.AssigneeCrewMemberId, t.AssigneeUserId,
            t.AssigneeCrewMemberId is { } c ? crew.GetValueOrDefault(c) : t.AssigneeUserId is { } u ? users.GetValueOrDefault(u) : null, t.Done)).ToList();
    }
}

public static class PageEndpoints
{
    public static void MapPages(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/pages").WithTags("Pages");
        g.MapGet("", async (string? lang, SmsDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ContentPages.AsNoTracking().Where(p => p.Language == (lang ?? "de")).OrderBy(p => p.Title).Select(p => new { p.Slug, p.Title, p.ShowInFooter, p.Language }).ToListAsync(ct)));
        g.MapGet("/{slug}", async (string slug, string? lang, SmsDbContext db, CancellationToken ct) =>
        {
            var p = await db.ContentPages.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug && x.Language == (lang ?? "de"), ct)
                    ?? await db.ContentPages.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, ct);
            return p is null ? Results.NotFound() : Results.Ok(new { p.Slug, p.Title, p.Content, p.Language, p.ShowInFooter });
        });
        g.MapPut("/{slug}", async (string slug, PageUpsert req, SmsDbContext db, CurrentUser current, HtmlSanitizerService html, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var p = await db.ContentPages.FirstOrDefaultAsync(x => x.Slug == slug && x.Language == req.Language, ct) ?? db.ContentPages.Add(new ContentPage { Slug = slug, Language = req.Language }).Entity;
            p.Title = req.Title; p.Content = html.Sanitize(req.Content); p.ShowInFooter = req.ShowInFooter; p.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
        g.MapDelete("/{slug}", async (string slug, string? lang, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            await db.ContentPages.Where(x => x.Slug == slug && (lang == null || x.Language == lang)).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}

/// <summary>iCal feeds per user and per ship (spec 07 C5) with secret tokens.</summary>
public static class CalendarEndpoints
{
    public static void MapCalendar(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/calendar").WithTags("Calendar");

        g.MapGet("/my-feeds", async (CurrentUser current, FeedTokens tokens, SmsDbContext db, HttpRequest req, CancellationToken ct) =>
        {
            var uid = current.RequiredId;
            var baseUrl = $"{req.Scheme}://{req.Host}/api/calendar";
            var ships = await db.ShipMembers.AsNoTracking().Where(m => m.UserId == uid).Select(m => new { m.ShipId, m.Ship.Name }).ToListAsync(ct);
            return Results.Ok(new
            {
                user = $"{baseUrl}/users/{tokens.Create("user", uid)}.ics",
                ships = ships.Select(s => new { s.ShipId, s.Name, url = $"{baseUrl}/ships/{tokens.Create("ship", s.ShipId)}.ics" }),
            });
        }).RequireAuthorization();

        g.MapGet("/{kind:regex(^(users|ships)$)}/{token}.ics", async (string kind, string token, FeedTokens tokens, SmsDbContext db, CancellationToken ct) =>
        {
            if (tokens.Parse(token) is not { } parsed || parsed.kind != kind.TrimEnd('s')) return Results.NotFound();
            var q = db.Trips.AsNoTracking().Include(t => t.Ship).Include(t => t.Crew).Where(t => t.Status != TripStatus.Cancelled);
            q = parsed.kind == "user" ? q.Where(t => t.Crew.Any(c => c.UserId == parsed.id && c.Status != CrewStatus.Declined)) : q.Where(t => t.ShipId == parsed.id);
            var trips = await q.OrderBy(t => t.StartDate).ToListAsync(ct);
            var cal = new Calendar { ProductId = "-//Sailing Portal//DE" };
            foreach (var t in trips)
            {
                cal.Events.Add(new CalendarEvent
                {
                    Uid = $"trip-{t.Id}@sailing-portal",
                    Summary = $"{t.Name} ({t.Ship.Name})",
                    Description = t.Description,
                    Start = new CalDateTime(t.StartDate.Year, t.StartDate.Month, t.StartDate.Day), // date-only values make the event all-day
                    End = new CalDateTime(t.EndDate.AddDays(1).Year, t.EndDate.AddDays(1).Month, t.EndDate.AddDays(1).Day),
                    Status = t.Status == TripStatus.Planned ? "TENTATIVE" : "CONFIRMED",
                });
            }
            var ics = new CalendarSerializer().SerializeToString(cal);
            return Results.Text(ics, "text/calendar");
        });
    }
}
