using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record EntryCrewRef(Guid? CrewMemberId, Guid? UserId, string? GuestName);
public record LogbookEntryUpsert(Guid? TripId, Guid StartHarbourId, Guid EndHarbourId, DateTimeOffset DepartedAt, DateTimeOffset ArrivedAt,
    decimal? EngineHoursStart, decimal? EngineHoursEnd, decimal? LogStart, decimal? LogEnd, string? WindSpeed, WindDirection? WindDirection,
    string? Weather, string? SeaState, decimal? SailedDistanceNm, string? SailsNotes, string? Notes, Visibility Visibility, List<EntryCrewRef>? Crew);
public record LogbookEntryDto(Guid Id, Guid ShipId, Guid? TripId, Guid StartHarbourId, string StartHarbour, Guid EndHarbourId, string EndHarbour,
    DateTimeOffset DepartedAt, DateTimeOffset ArrivedAt, string Duration, decimal? EngineHoursStart, decimal? EngineHoursEnd, decimal? LogStart, decimal? LogEnd,
    string? WindSpeed, WindDirection? WindDirection, string? Weather, string? SeaState, decimal? SailedDistanceNm, string? SailsNotes, string? Notes,
    Visibility Visibility, Guid AuthorId, IReadOnlyList<EntryCrewDto> Crew, string CrewList, bool CanEdit);
public record EntryCrewDto(Guid? CrewMemberId, Guid? UserId, string Name);
public record LogbookDefaults(Guid? StartHarbourId, string? StartHarbourName, decimal? EngineHoursStart, decimal? LogStart, IReadOnlyList<EntryCrewDto> Crew);

public static class LogbookEndpoints
{
    public static void MapLogbook(this IEndpointRouteBuilder api)
    {
        var ships = api.MapGroup("/ships/{shipId:guid}/logbook").WithTags("Logbook");
        var g = api.MapGroup("/logbook").WithTags("Logbook");

        ships.MapGet("", async (Guid shipId, Guid? trip, int? year, int? page, int? pageSize, string? sort, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var (p, ps) = Paging(page, pageSize);
            var q = db.LogbookEntries.AsNoTracking().Include(l => l.StartHarbour).Include(l => l.EndHarbour).Include(l => l.Crew).Include(l => l.Ship).Include(l => l.Trip)
                .Where(l => l.ShipId == shipId).Where(viewer.Logbook());
            if (trip is { } t) q = q.Where(l => l.TripId == t);
            if (year is { } y) q = q.Where(l => l.DepartedAt.Year == y);
            q = sort switch
            {
                "departedAt" => q.OrderBy(l => l.DepartedAt),
                "start" => q.OrderBy(l => l.StartHarbour.Name),
                "end" => q.OrderBy(l => l.EndHarbour.Name),
                _ => q.OrderByDescending(l => l.DepartedAt),
            };
            var total = await q.CountAsync(ct);
            var items = await q.Skip((p - 1) * ps).Take(ps).ToListAsync(ct);
            var names = await ShipEndpoints.DisplayNames(db, items.SelectMany(e => e.Crew.Where(c => c.UserId != null).Select(c => c.UserId!.Value)), ct);
            var crewNames = await CrewNames(db, items.Select(i => i.TripId).Where(i => i != null).Select(i => i!.Value).Distinct(), ct);
            return Results.Ok(new Paged<LogbookEntryDto>(items.Select(e => ToDto(e, names, crewNames, current.Id)).ToList(), p, ps, total));
        });

        // Spec 07 D2 / 05 §5.4: defaults from the ship's previous entry regardless of its visibility (only these three values).
        ships.MapGet("/new-defaults", async (Guid shipId, Guid? trip, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            if (trip is { } t ? !await access.CanWriteLogbookAsync(t, ct) : !await access.IsShipMemberAsync(shipId, null, ct)) return Results.Forbid();
            var last = await db.LogbookEntries.AsNoTracking().Include(l => l.EndHarbour).Where(l => l.ShipId == shipId).OrderByDescending(l => l.ArrivedAt).FirstOrDefaultAsync(ct);
            var crew = new List<EntryCrewDto>();
            if (trip is { } tid)
            {
                var tz = await db.Trips.Where(x => x.Id == tid).Select(x => x.TimeZone).FirstAsync(ct);
                var today = TodayIn(tz);
                var members = await db.CrewMembers.AsNoTracking().Where(c => c.TripId == tid && c.Status == CrewStatus.Accepted).ToListAsync(ct);
                var names = await ShipEndpoints.DisplayNames(db, members.Where(m => m.UserId != null).Select(m => m.UserId!.Value), ct);
                crew = members.Where(m => m.IsAboardOn(today)).Select(m => new EntryCrewDto(m.Id, m.UserId, m.UserId is { } u ? names.GetValueOrDefault(u, "?") : m.GuestName ?? "Gast")).ToList();
            }
            return Results.Ok(new LogbookDefaults(last?.EndHarbourId, last?.EndHarbour.Name, last?.EngineHoursEnd, last?.LogEnd, crew));
        }).RequireAuthorization();

        ships.MapPost("", async (Guid shipId, LogbookEntryUpsert req, SmsDbContext db, CurrentUser current, Access access, HtmlSanitizerService html, SeaRouter router, CancellationToken ct) =>
        {
            if (req.TripId is { } t)
            {
                if (!await db.Trips.AnyAsync(x => x.Id == t && x.ShipId == shipId, ct)) return Invalid("tripId", "Törn gehört nicht zum Schiff.");
                if (!await access.CanWriteLogbookAsync(t, ct)) return Results.Forbid();
            }
            else if (!await access.IsShipMemberAsync(shipId, null, ct)) return Results.Forbid();
            if (Validate(req) is { } problem) return problem;
            var entry = new LogbookEntry { ShipId = shipId, AuthorId = current.RequiredId, CreatedById = current.Id };
            Apply(entry, req, html);
            await CalculateRouteAsync(entry, router, await AirDraftAsync(db, shipId, ct), ct);
            db.LogbookEntries.Add(entry);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/logbook/{entry.Id}", new { entry.Id });
        }).RequireAuthorization();

        g.MapGet("/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var e = await db.LogbookEntries.AsNoTracking().Include(l => l.StartHarbour).Include(l => l.EndHarbour).Include(l => l.Crew).Include(l => l.Ship).Include(l => l.Trip).ThenInclude(t => t!.Ship)
                .Where(viewer.Logbook()).FirstOrDefaultAsync(l => l.Id == id, ct);
            if (e is null) return Results.NotFound();
            var names = await ShipEndpoints.DisplayNames(db, e.Crew.Where(c => c.UserId != null).Select(c => c.UserId!.Value), ct);
            var crewNames = await CrewNames(db, e.TripId is { } t ? [t] : [], ct);
            var dto = ToDto(e, names, crewNames, current.Id);
            // Spec 07 E2: the day's posts and photos become part of the entry's detail page.
            List<DayDto> days = [];
            if (e.Trip is not null)
                days = await TripEndpoints.Timeline(e.Trip, DayIn(e.DepartedAt, e.Trip.TimeZone), DayIn(e.ArrivedAt, e.Trip.TimeZone), db, viewer, ct);
            return Results.Ok(new { entry = dto, days });
        });

        g.MapPut("/{id:guid}", async (Guid id, LogbookEntryUpsert req, SmsDbContext db, CurrentUser current, Access access, HtmlSanitizerService html, SeaRouter router, CancellationToken ct) =>
        {
            var e = await db.LogbookEntries.Include(l => l.Crew).FirstOrDefaultAsync(l => l.Id == id, ct);
            if (e is null) return Results.NotFound();
            if (!await CanEditAsync(e, current, access, ct)) return Results.Forbid();
            if (Validate(req) is { } problem) return problem;
            Apply(e, req with { TripId = e.TripId }, html);
            await CalculateRouteAsync(e, router, await AirDraftAsync(db, e.ShipId, ct), ct);
            e.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapDelete("/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var e = await db.LogbookEntries.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (e is null) return Results.NotFound();
            if (!await CanEditAsync(e, current, access, ct)) return Results.Forbid();
            e.DeletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    /// <summary>Spec 12 §12.1 A: cache the sea route between start and end harbour on the entry (null while no graph exists).</summary>
    private static async Task<double?> AirDraftAsync(SmsDbContext db, Guid shipId, CancellationToken ct) => (double?)await db.Ships.AsNoTracking().Where(s => s.Id == shipId).Select(s => s.AirDraftM).FirstOrDefaultAsync(ct);

    private static async Task CalculateRouteAsync(LogbookEntry e, SeaRouter router, double? airDraftM, CancellationToken ct)
    {
        try
        {
            var route = e.StartHarbourId == e.EndHarbourId ? null : await router.RouteAsync(e.StartHarbourId, e.EndHarbourId, ct, airDraftM);
            e.CalculatedRoute = route?.Geometry; e.CalculatedDistanceNm = route?.DistanceNm;
        }
        catch (Exception) { e.CalculatedRoute = null; e.CalculatedDistanceNm = null; }
    }

    internal static async Task<Dictionary<Guid, string>> CrewNames(SmsDbContext db, Guid tripId, CancellationToken ct) => await CrewNames(db, [tripId], ct);

    /// <summary>CrewMemberId → display name (guest name or profile name) for the given trips.</summary>
    internal static async Task<Dictionary<Guid, string>> CrewNames(SmsDbContext db, IEnumerable<Guid> tripIds, CancellationToken ct)
    {
        var ids = tripIds.ToList();
        if (ids.Count == 0) return [];
        var members = await db.CrewMembers.AsNoTracking().Where(c => ids.Contains(c.TripId)).ToListAsync(ct);
        var names = await ShipEndpoints.DisplayNames(db, members.Where(m => m.UserId != null).Select(m => m.UserId!.Value), ct);
        return members.ToDictionary(m => m.Id, m => m.UserId is { } u ? names.GetValueOrDefault(u, "?") : m.GuestName ?? "Gast");
    }

    private static async Task<bool> CanEditAsync(LogbookEntry e, CurrentUser current, Access access, CancellationToken ct) =>
        e.AuthorId == current.Id
        || (e.TripId is { } t ? await access.CanWriteLogbookAsync(t, ct) : await access.IsShipMemberAsync(e.ShipId, null, ct))
        || await current.IsAdminAsync(ct);

    private static IResult? Validate(LogbookEntryUpsert r)
    {
        if (r.ArrivedAt < r.DepartedAt) return Invalid("arrivedAt", "Ankunft liegt vor dem Ablegen.");
        if (r.StartHarbourId == Guid.Empty || r.EndHarbourId == Guid.Empty) return Invalid("startHarbourId", "Start- und Zielhafen angeben.");
        return null;
    }

    private static void Apply(LogbookEntry e, LogbookEntryUpsert r, HtmlSanitizerService html)
    {
        e.TripId = r.TripId; e.StartHarbourId = r.StartHarbourId; e.EndHarbourId = r.EndHarbourId; e.DepartedAt = r.DepartedAt; e.ArrivedAt = r.ArrivedAt;
        e.EngineHoursStart = r.EngineHoursStart; e.EngineHoursEnd = r.EngineHoursEnd; e.LogStart = r.LogStart; e.LogEnd = r.LogEnd;
        e.WindSpeed = r.WindSpeed; e.WindDirection = r.WindDirection; e.Weather = r.Weather; e.SeaState = r.SeaState;
        e.SailedDistanceNm = r.SailedDistanceNm ?? (r.LogEnd is { } le && r.LogStart is { } ls && le >= ls ? le - ls : null);
        e.SailsNotes = r.SailsNotes; e.Notes = html.Sanitize(r.Notes); e.Visibility = r.Visibility;
        e.Crew.Clear();
        foreach (var c in r.Crew ?? [])
            if (c.CrewMemberId is not null || c.UserId is not null || !string.IsNullOrWhiteSpace(c.GuestName))
                e.Crew.Add(new LogbookEntryCrew { LogbookEntryId = e.Id, CrewMemberId = c.CrewMemberId, UserId = c.UserId, GuestName = c.GuestName?.Trim() });
    }

    internal static LogbookEntryDto ToDto(LogbookEntry e, Dictionary<Guid, string> userNames, Dictionary<Guid, string> crewNames, Guid? me)
    {
        var crew = e.Crew.Select(c => new EntryCrewDto(c.CrewMemberId, c.UserId,
            c.CrewMemberId is { } cm && crewNames.TryGetValue(cm, out var n) ? n : c.UserId is { } u ? userNames.GetValueOrDefault(u, "?") : c.GuestName ?? "Gast")).ToList();
        var d = e.Duration;
        return new LogbookEntryDto(e.Id, e.ShipId, e.TripId, e.StartHarbourId, e.StartHarbour.Name, e.EndHarbourId, e.EndHarbour.Name, e.DepartedAt, e.ArrivedAt,
            $"{(int)d.TotalHours}:{d.Minutes:00}", e.EngineHoursStart, e.EngineHoursEnd, e.LogStart, e.LogEnd, e.WindSpeed, e.WindDirection, e.Weather, e.SeaState,
            e.SailedDistanceNm, e.SailsNotes, e.Notes, e.Visibility, e.AuthorId, crew, string.Join(", ", crew.Select(c => c.Name)), e.AuthorId == me);
    }
}
