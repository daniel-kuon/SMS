using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Domain.Social;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;
using Sms.Infrastructure.Tracking;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record TripUpsert(string Name, string? Description, DateOnly StartDate, DateOnly EndDate, string? TimeZone, Guid? PlannedStartHarbourId, Guid? PlannedEndHarbourId,
    Visibility Visibility, TripStatus Status, string? Currency, Guid? SkipperUserId, Visibility? TrackVisibility = null, int? PublicPositionDelayMinutes = null);
public record CrewDto(Guid Id, Guid? UserId, string Name, CrewRole Role, bool CanWriteLogbook, CrewStatus Status, DateOnly? FromDate, DateOnly? ToDate, bool IsGuest);
public record CrewUpsert(Guid? UserId, string? GuestName, CrewRole Role = CrewRole.Crew, bool CanWriteLogbook = false, DateOnly? FromDate = null, DateOnly? ToDate = null);
public record TripSummary(Guid Id, Guid ShipId, string ShipName, string Name, DateOnly StartDate, DateOnly EndDate, TripStatus Status, Visibility Visibility, int CrewCount);
public record TripDto(Guid Id, Guid ShipId, string ShipName, string Name, string? Description, DateOnly StartDate, DateOnly EndDate, string TimeZone,
    Guid? PlannedStartHarbourId, Guid? PlannedEndHarbourId, Visibility Visibility, TripStatus Status, string Currency, DateTimeOffset? CostsLockedAt,
    IReadOnlyList<CrewDto> Crew, CrewDto? Me, bool CanEdit, bool CanWriteLogbook, bool IsCrew, bool CostsVisible, Visibility TrackVisibility, int PublicPositionDelayMinutes);
/// <summary>Order null = position in the list (single line, no alternatives).</summary>
public record LegUpsert(Guid FromHarbourId, Guid ToHarbourId, DateOnly? PlannedDate, int? Order = null, int Alternative = 0);
public record LegDto(Guid Id, int Order, int Alternative, Guid FromHarbourId, string FromName, Guid ToHarbourId, string ToName, DateOnly? PlannedDate, decimal? DistanceNm, decimal? EtaHours, IReadOnlyList<RouteWarning> Warnings);
public record TripInviteRequest(string? Email, CrewRole Role = CrewRole.Crew);
public record DayDto(DateOnly Date, IReadOnlyList<LogbookEntryDto> Logbook, IReadOnlyList<PostDto> Posts, IReadOnlyList<MediaDto> Photos);

public static class TripEndpoints
{
    public static TripSummary Summary(Trip t) => new(t.Id, t.ShipId, t.Ship.Name, t.Name, t.StartDate, t.EndDate, t.Status, t.Visibility, t.Crew.Count(c => c.Status == CrewStatus.Accepted));

    public static void MapTrips(this IEndpointRouteBuilder api)
    {
        var ships = api.MapGroup("/ships/{shipId:guid}/trips").WithTags("Trips");
        var g = api.MapGroup("/trips").WithTags("Trips");

        ships.MapGet("", async (Guid shipId, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var trips = await db.Trips.AsNoTracking().Include(t => t.Ship).Include(t => t.Crew).Where(t => t.ShipId == shipId).Where(viewer.Trips())
                .OrderByDescending(t => t.StartDate).ToListAsync(ct);
            return Results.Ok(trips.Select(Summary));
        });

        g.MapGet("", async (SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var uid = current.RequiredId;
            var trips = await db.Trips.AsNoTracking().Include(t => t.Ship).Include(t => t.Crew)
                .Where(t => t.Crew.Any(c => c.UserId == uid && c.Status != CrewStatus.Declined))
                .OrderByDescending(t => t.StartDate).ToListAsync(ct);
            return Results.Ok(trips.Select(t => new { Summary = Summary(t), MyStatus = t.Crew.First(c => c.UserId == uid).Status }));
        }).RequireAuthorization();

        ships.MapPost("", async (Guid shipId, TripUpsert req, SmsDbContext db, CurrentUser current, Access access, HtmlSanitizerService html, CancellationToken ct) =>
        {
            if (!await access.CanCreateTripAsync(shipId, ct)) return Results.Forbid();
            if (Validate(req) is { } problem) return problem;
            var trip = new Trip { ShipId = shipId, CreatedById = current.Id };
            Apply(trip, req, html);
            var skipperId = req.SkipperUserId ?? current.RequiredId;
            trip.Crew.Add(new CrewMember { UserId = skipperId, Role = CrewRole.Skipper, CanWriteLogbook = true, Status = CrewStatus.Accepted });
            db.Trips.Add(trip);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/trips/{trip.Id}", new { trip.Id });
        }).RequireAuthorization();

        g.MapGet("/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).Include(t => t.Crew).Where(viewer.Trips()).FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return Results.NotFound();
            return Results.Ok(await ToDto(trip, db, current, ct));
        });

        g.MapPut("/{id:guid}", async (Guid id, TripUpsert req, SmsDbContext db, Access access, HtmlSanitizerService html, CancellationToken ct) =>
        {
            var trip = await db.Trips.Include(t => t.Crew).FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return Results.NotFound();
            var isSkipper = await access.IsTripSkipperAsync(id, ct);
            var isOwner = await access.IsShipOwnerAsync(trip.ShipId, ct);
            if (!isSkipper && !isOwner) return Results.Forbid();
            if (Validate(req) is { } problem) return problem;
            if (trip.CostsLockedAt is not null && req.Currency is { } cur && !cur.Equals(trip.Currency, StringComparison.OrdinalIgnoreCase))
                return Results.Problem(statusCode: 409, title: "Währung kann nach Abschluss der Abrechnung nicht geändert werden.");
            Apply(trip, req, html);
            if (req.SkipperUserId is { } newSkipper && isOwner)
            {
                foreach (var c in trip.Crew.Where(c => c.Role == CrewRole.Skipper)) c.Role = CrewRole.Crew;
                var member = trip.Crew.FirstOrDefault(c => c.UserId == newSkipper);
                if (member is null) trip.Crew.Add(new CrewMember { UserId = newSkipper, Role = CrewRole.Skipper, CanWriteLogbook = true, Status = CrewStatus.Accepted });
                else { member.Role = CrewRole.Skipper; member.CanWriteLogbook = true; member.Status = CrewStatus.Accepted; }
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapDelete("/{id:guid}", async (Guid id, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return Results.NotFound();
            var hasContent = await db.Posts.AnyAsync(p => p.TripId == id, ct) || await db.Media.AnyAsync(m => m.TripId == id, ct)
                             || await db.LogbookEntries.AnyAsync(l => l.TripId == id, ct) || await db.Expenses.AnyAsync(e => e.TripId == id, ct);
            var allowed = await access.IsTripSkipperAsync(id, ct) || (!hasContent && await access.IsShipOwnerAsync(trip.ShipId, ct));
            if (!allowed) return Results.Forbid();
            if (hasContent) { trip.Status = TripStatus.Cancelled; await db.SaveChangesAsync(ct); return Results.NoContent(); }
            db.Trips.Remove(trip);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // --- crew (spec 07 C2)
        g.MapGet("/{id:guid}/crew", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            if (!await db.Trips.AsNoTracking().Where(viewer.Trips()).AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            return Results.Ok(await CrewDtos(id, db, ct));
        });

        g.MapPost("/{id:guid}/crew", async (Guid id, CrewUpsert req, SmsDbContext db, Access access, CurrentUser current, MailSender mail, HttpRequest http, IConfiguration cfg, Notifier notifier, CancellationToken ct) =>
        {
            if (!await access.IsTripSkipperAsync(id, ct)) return Results.Forbid();
            if (req.UserId is null && string.IsNullOrWhiteSpace(req.GuestName)) return Invalid("guestName", "Benutzer oder Gastname angeben.");
            if (req.UserId is { } uid && await db.CrewMembers.AnyAsync(c => c.TripId == id && c.UserId == uid && c.Status != CrewStatus.Declined && c.Status != CrewStatus.Left, ct))
                return Results.Problem(statusCode: 409, title: "Bereits in der Crew.");
            var member = new CrewMember
            {
                TripId = id, UserId = req.UserId, GuestName = req.UserId is null ? req.GuestName!.Trim() : null, Role = CrewRole.Crew,
                CanWriteLogbook = req.CanWriteLogbook, Status = req.UserId is null ? CrewStatus.Accepted : CrewStatus.Invited, FromDate = req.FromDate, ToDate = req.ToDate, CreatedById = current.Id,
            };
            db.CrewMembers.Add(member);
            await db.SaveChangesAsync(ct);
            if (req.UserId is { } invited)
            {
                var email = await db.Users.Where(u => u.Id == invited).Select(u => u.Email).FirstOrDefaultAsync(ct);
                var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).FirstAsync(t => t.Id == id, ct);
                if (email is not null) await mail.SendAsync(email, $"Einladung zum Törn {trip.Name}", $"<p>Du bist zum Törn <b>{H(trip.Name)}</b> ({H(trip.Ship.Name)}) eingeladen: <a href=\"{WebBaseUrl(http, cfg)}/trips/{id}\">Einladung ansehen</a></p>", ct);
                await notifier.NotifyAsync(invited, NotificationType.TripInvitation, $"Einladung zum Törn {trip.Name} ({trip.Ship.Name})", $"/trips/{id}", ct);
                await db.SaveChangesAsync(ct);
            }
            return Results.Created($"/api/crew/{member.Id}", new { member.Id });
        }).RequireAuthorization();

        api.MapPut("/crew/{id:guid}", async (Guid id, CrewUpsert req, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            var member = await db.CrewMembers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (member is null) return Results.NotFound();
            if (!await access.IsTripSkipperAsync(member.TripId, ct)) return Results.Forbid();
            if (member.Role == CrewRole.Skipper && req.Role != CrewRole.Skipper) return Results.Problem(statusCode: 409, title: "Skipper über die Törn-Einstellungen ändern.");
            member.CanWriteLogbook = req.CanWriteLogbook; member.FromDate = req.FromDate; member.ToDate = req.ToDate;
            if (member.UserId is null && req.UserId is { } link) { member.UserId = link; member.GuestName = null; member.Status = CrewStatus.Accepted; } // link guest to account
            else if (member.UserId is null && !string.IsNullOrWhiteSpace(req.GuestName)) member.GuestName = req.GuestName.Trim();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Trips");

        api.MapDelete("/crew/{id:guid}", async (Guid id, SmsDbContext db, Access access, CurrentUser current, CancellationToken ct) =>
        {
            var member = await db.CrewMembers.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (member is null) return Results.NotFound();
            var self = member.UserId == current.Id;
            if (!self && !await access.IsTripSkipperAsync(member.TripId, ct)) return Results.Forbid();
            if (member.Role == CrewRole.Skipper) return Results.Problem(statusCode: 409, title: "Der Skipper kann die Crew nicht verlassen.");
            // keep historic shares/logbook references: mark as Left instead of deleting when referenced
            var referenced = await db.ExpenseShares.AnyAsync(s => s.CrewMemberId == id, ct) || await db.Expenses.AnyAsync(e => e.PaidByCrewMemberId == id, ct) || await db.LogbookEntryCrew.AnyAsync(c => c.CrewMemberId == id, ct);
            if (referenced) member.Status = CrewStatus.Left; else db.CrewMembers.Remove(member);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Trips");

        api.MapPost("/crew/{id:guid}/{action:regex(^(accept|decline)$)}", async (Guid id, string action, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var member = await db.CrewMembers.FirstOrDefaultAsync(c => c.Id == id && c.UserId == current.RequiredId, ct);
            if (member is null) return Results.NotFound();
            member.Status = action == "accept" ? CrewStatus.Accepted : CrewStatus.Declined;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Trips");

        g.MapPost("/{id:guid}/invitations", async (Guid id, TripInviteRequest req, SmsDbContext db, Access access, CurrentUser current, MailSender mail, HttpRequest http, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!await access.IsTripSkipperAsync(id, ct)) return Results.Forbid();
            if (req.Email is { Length: > 0 } && !IsEmail(req.Email)) return Invalid("email", "Ungültige E-Mail-Adresse.");
            var inv = new TripInvitation { TripId = id, Email = req.Email?.Trim(), Role = CrewRole.Crew, Token = NewToken(), ExpiresAt = DateTimeOffset.UtcNow.AddDays(30), CreatedById = current.Id };
            db.TripInvitations.Add(inv);
            await db.SaveChangesAsync(ct);
            var link = $"{WebBaseUrl(http, cfg)}/invitations/trip/{inv.Token}";
            if (inv.Email is not null)
            {
                var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).FirstAsync(t => t.Id == id, ct);
                await mail.SendAsync(inv.Email, $"Einladung zum Törn {trip.Name}", $"<p>Komm mit auf <b>{H(trip.Name)}</b> ({H(trip.Ship.Name)}): <a href=\"{link}\">{link}</a></p>", ct);
            }
            return Results.Ok(new { inv.Token, link });
        }).RequireAuthorization();

        api.MapGet("/trip-invitations/{token}", async (string token, SmsDbContext db, CancellationToken ct) =>
        {
            var inv = await db.TripInvitations.AsNoTracking().Include(i => i.Trip).ThenInclude(t => t.Ship).FirstOrDefaultAsync(i => i.Token == token, ct);
            return inv is null ? Results.NotFound() : Results.Ok(new { inv.Trip.Name, ShipName = inv.Trip.Ship.Name, inv.Trip.StartDate, inv.Trip.EndDate, inv.Status, inv.ExpiresAt });
        }).WithTags("Trips");

        api.MapPost("/trip-invitations/{token}/accept", async (string token, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var inv = await db.TripInvitations.FirstOrDefaultAsync(i => i.Token == token, ct);
            if (inv is null || inv.Status == InvitationStatus.Declined || inv.ExpiresAt < DateTimeOffset.UtcNow) return Results.Problem(statusCode: 410, title: "Einladung ungültig oder abgelaufen.");
            var uid = current.RequiredId;
            var member = await db.CrewMembers.FirstOrDefaultAsync(c => c.TripId == inv.TripId && c.UserId == uid, ct);
            if (member is null) db.CrewMembers.Add(new CrewMember { TripId = inv.TripId, UserId = uid, Role = CrewRole.Crew, Status = CrewStatus.Accepted });
            else member.Status = CrewStatus.Accepted;
            if (inv.Email is not null) inv.Status = InvitationStatus.Accepted; // link invitations stay reusable
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { inv.TripId });
        }).RequireAuthorization().WithTags("Trips");

        // --- planner legs (spec 07 C3)
        g.MapGet("/{id:guid}/legs", async (Guid id, SmsDbContext db, CurrentUser current, Access access, SeaRouter router, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).Where(viewer.Trips()).FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return Results.NotFound();
            return Results.Ok(await LegDtos(trip, db, router, ct));
        });

        g.MapPut("/{id:guid}/legs", async (Guid id, List<LegUpsert> legs, SmsDbContext db, Access access, SeaRouter router, CancellationToken ct) =>
        {
            if (!await access.IsTripSkipperAsync(id, ct)) return Results.Forbid();
            var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return Results.NotFound();
            var refs = legs.Select((l, i) => new PlannedLegRules.LegRef(l.Order ?? i, l.Alternative, l.FromHarbourId, l.ToHarbourId)).ToList();
            if (PlannedLegRules.Validate(refs, trip.PlannedStartHarbourId) is { } error) return Invalid("legs", error);
            // keep geometry/distance already computed for a harbour pair; route only new pairs
            var known = (await db.PlannedLegs.AsNoTracking().Where(l => l.TripId == id && l.RouteGeometry != null).ToListAsync(ct))
                .DistinctBy(l => (l.FromHarbourId, l.ToHarbourId)).ToDictionary(l => (l.FromHarbourId, l.ToHarbourId));
            await db.PlannedLegs.Where(l => l.TripId == id).ExecuteDeleteAsync(ct);
            foreach (var (l, r) in legs.Zip(refs).OrderBy(x => x.Second.Order).ThenBy(x => x.Second.Alternative))
            {
                var leg = new PlannedLeg { TripId = id, Order = r.Order, Alternative = r.Alternative, FromHarbourId = l.FromHarbourId, ToHarbourId = l.ToHarbourId, PlannedDate = l.PlannedDate };
                if (known.TryGetValue((l.FromHarbourId, l.ToHarbourId), out var k)) { leg.RouteGeometry = k.RouteGeometry; leg.DistanceNm = k.DistanceNm; }
                else
                {
                    var route = await router.RouteAsync(l.FromHarbourId, l.ToHarbourId, ct, (double?)trip.Ship.AirDraftM, router.MinDepth((double?)trip.Ship.DraftM, (double?)trip.Ship.MinDepthM)); // low bridges and shallows stay closed for this ship
                    var meters = route is null ? await db.Harbours.Where(h => h.Id == l.FromHarbourId).Select(h => h.Location.Distance(db.Harbours.First(x => x.Id == l.ToHarbourId).Location)).FirstOrDefaultAsync(ct) : 0;
                    leg.DistanceNm = route?.DistanceNm ?? Math.Round((decimal)meters / 1852m, 1); leg.RouteGeometry = route?.Geometry;
                }
                db.PlannedLegs.Add(leg);
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(await LegDtos(trip, db, router, ct));
        }).RequireAuthorization();

        // --- day timeline (spec 07 E2)
        g.MapGet("/{id:guid}/timeline", async (Guid id, DateOnly? from, DateOnly? to, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).Where(viewer.Trips()).FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return Results.NotFound();
            var days = await Timeline(trip, from, to, db, viewer, ct);
            return Results.Ok(days);
        });

        // --- overview: stats + map data (spec 12, calculated/straight legs only in this phase)
        g.MapGet("/{id:guid}/overview", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).Where(viewer.Trips()).FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return Results.NotFound();
            var entries = await db.LogbookEntries.AsNoTracking().Include(l => l.StartHarbour).Include(l => l.EndHarbour).Include(l => l.Ship).Include(l => l.Trip)
                .Where(l => l.TripId == id).Where(viewer.Logbook()).OrderBy(l => l.DepartedAt).ToListAsync(ct);
            var isCrewForTrack = viewer.CrewTripIds.Contains(id) || viewer.IsAdmin;
            var trackVisible = isCrewForTrack || await access.CanViewAsync(trip.TrackVisibility, trip, trip.Ship, null, ct);
            var cutoff = isCrewForTrack ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(-trip.PublicPositionDelayMinutes);
            var segments = trackVisible ? await db.TrackSegments.AsNoTracking().Where(s => s.TripId == id && s.Source != Sms.Domain.Tracking.TrackSource.Calculated && s.EndTime <= cutoff).ToListAsync(ct) : [];
            var zones = isCrewForTrack ? [] : await db.PrivacyZones.AsNoTracking().Where(z => z.ShipId == trip.ShipId).ToListAsync(ct);
            // recorded geometries shown to non-crew viewers have privacy zones removed (spec 12 §12.5)
            var visibleGeometry = segments.ToDictionary(s => s.Id, s => TrackProcessor.StripZones(zones.Count > 0 ? s.Geometry : s.GeometryCoarse, zones)); // fine geometry when zones cut points
            segments = segments.Where(s => visibleGeometry[s.Id] is not null).ToList();
            var legs = entries.Select(l =>
            {
                var seg = segments.FirstOrDefault(s => s.LogbookEntryId == l.Id && (s.SourceOverride ?? s.Source) != Sms.Domain.Tracking.TrackSource.Calculated);
                return new
                {
                    l.Id, Day = DayIn(l.DepartedAt, trip.TimeZone), From = l.StartHarbour.Name, To = l.EndHarbour.Name,
                    Coordinates = seg is not null ? visibleGeometry[seg.Id]!.Coordinates.Select(c => new[] { c.X, c.Y }).ToArray()
                        : l.CalculatedRoute is not null ? l.CalculatedRoute.Coordinates.Select(c => new[] { c.X, c.Y }).ToArray()
                        : [[l.StartHarbour.Location.X, l.StartHarbour.Location.Y], [l.EndHarbour.Location.X, l.EndHarbour.Location.Y]],
                    DistanceNm = seg?.DistanceNm ?? l.SailedDistanceNm ?? ((l.LogEnd ?? 0) - (l.LogStart ?? 0)), CalculatedDistanceNm = l.CalculatedDistanceNm,
                    Source = seg is not null ? (seg.SourceOverride ?? seg.Source).ToString() : l.CalculatedRoute is not null ? "calculated" : "straight-line",
                };
            }).ToList();
            // days with recorded track but no logbook entry (spec 12 §12.2)
            legs.AddRange(segments.Where(s => s.LogbookEntryId == null || entries.All(e => e.Id != s.LogbookEntryId)).Select(s => new
            {
                Id = s.Id, Day = s.Day, From = "", To = "", Coordinates = visibleGeometry[s.Id]!.Coordinates.Select(c => new[] { c.X, c.Y }).ToArray(),
                DistanceNm = s.DistanceNm, CalculatedDistanceNm = (decimal?)null, Source = (s.SourceOverride ?? s.Source).ToString(),
            }));
            var photos = (await db.Media.AsNoTracking().Where(m => m.TripId == id && m.Location != null && m.Kind == MediaKind.Photo).Where(viewer.Media(trip, trip.Ship))
                .Select(m => new { m.Id, m.Location, m.Caption, m.Day }).ToListAsync(ct))
                .Select(m => new { type = "photo", m.Id, lat = m.Location!.Y, lng = m.Location.X, thumbUrl = (string?)$"/api/media/{m.Id}/thumb", title = m.Caption, m.Day }).ToList();
            var posts = (await db.Posts.AsNoTracking().Include(p => p.Trip).ThenInclude(t => t.Ship).Where(p => p.TripId == id && p.Location != null).Where(viewer.Posts())
                .Select(p => new { p.Id, p.Location, p.Title, p.Day }).ToListAsync(ct))
                .Select(p => new { type = "post", p.Id, lat = p.Location!.Y, lng = p.Location.X, thumbUrl = (string?)null, title = p.Title, Day = (DateOnly?)p.Day }).ToList();
            var crew = await access.IsCrewAsync(id, ct);
            var stats = new
            {
                distanceNm = legs.Sum(l => l.DistanceNm),
                engineHours = entries.Sum(l => (l.EngineHoursEnd ?? 0) - (l.EngineHoursStart ?? 0)),
                underwayHours = Math.Round((decimal)entries.Sum(l => l.Duration.TotalHours), 1),
                harbours = entries.Select(l => l.EndHarbourId).Union(entries.Select(l => l.StartHarbourId)).Distinct().Count(),
                nights = Math.Max(0, trip.EndDate.DayNumber - trip.StartDate.DayNumber),
                photos = await db.Media.AsNoTracking().Where(m => m.TripId == id && m.Kind == MediaKind.Photo).Where(viewer.Media(trip, trip.Ship)).CountAsync(ct),
                posts = await db.Posts.AsNoTracking().Where(p => p.TripId == id).Where(viewer.Posts()).CountAsync(ct),
                costsTotal = crew ? await db.Expenses.Where(e => e.TripId == id).SumAsync(e => e.AmountInTripCurrency, ct) : (decimal?)null,
            };
            var stops = entries.GroupBy(l => l.EndHarbourId).Select(gp => new { harbourId = gp.Key, name = gp.First().EndHarbour.Name, lat = gp.First().EndHarbour.Location.Y, lng = gp.First().EndHarbour.Location.X, nights = gp.Count() });
            trip.Crew = await db.CrewMembers.AsNoTracking().Where(c => c.TripId == id).ToListAsync(ct);
            return Results.Ok(new { trip = Summary(trip), stats, legs, stops, items = photos.Concat<object>(posts), attribution = HarbourEndpoints.Attribution });
        });

        // --- map payload for the Törn section (docs/14): planned legs incl. alternatives, logbook routes, harbours, located photos
        g.MapGet("/{id:guid}/map", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).Where(viewer.Trips()).FirstOrDefaultAsync(t => t.Id == id, ct);
            if (trip is null) return Results.NotFound();
            var legs = await db.PlannedLegs.AsNoTracking().Where(l => l.TripId == id).OrderBy(l => l.Order).ThenBy(l => l.Alternative).ToListAsync(ct);
            var entries = await db.LogbookEntries.AsNoTracking().Include(l => l.Ship).Include(l => l.Trip)
                .Where(l => l.TripId == id).Where(viewer.Logbook()).OrderBy(l => l.DepartedAt).ToListAsync(ct);
            // imported tracks follow the same visibility, delay and privacy-zone rules as /overview (spec 12 §12.5)
            var isCrewForTrack = viewer.CrewTripIds.Contains(id) || viewer.IsAdmin;
            var trackVisible = isCrewForTrack || await access.CanViewAsync(trip.TrackVisibility, trip, trip.Ship, null, ct);
            var cutoff = isCrewForTrack ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(-trip.PublicPositionDelayMinutes);
            var segments = trackVisible ? await db.TrackSegments.AsNoTracking()
                .Where(s => s.TripId == id && s.LogbookEntryId != null && (s.SourceOverride ?? s.Source) != Sms.Domain.Tracking.TrackSource.Calculated && s.EndTime <= cutoff).ToListAsync(ct) : [];
            var zones = isCrewForTrack ? [] : await db.PrivacyZones.AsNoTracking().Where(z => z.ShipId == trip.ShipId).ToListAsync(ct);
            var tracks = segments.GroupBy(s => s.LogbookEntryId!.Value)
                .Select(gp => (gp.Key, Line: TrackProcessor.StripZones(zones.Count > 0 ? gp.First().Geometry : gp.First().GeometryCoarse, zones)))
                .Where(x => x.Line is not null).ToDictionary(x => x.Key, x => x.Line);
            var harbourIds = legs.SelectMany(l => new[] { l.FromHarbourId, l.ToHarbourId }).Concat(entries.SelectMany(e => new[] { e.StartHarbourId, e.EndHarbourId }))
                .Concat(new[] { trip.PlannedStartHarbourId, trip.PlannedEndHarbourId }.Where(h => h != null).Select(h => h!.Value)).Distinct().ToList();
            var harbours = await db.Harbours.AsNoTracking().Where(h => harbourIds.Contains(h.Id)).Select(h => new { h.Id, h.Name, h.Location }).ToListAsync(ct); // geography has no ST_Y, so the coordinates are read in memory
            var photos = await db.Media.AsNoTracking().Where(m => m.TripId == id && m.Location != null && m.Kind == MediaKind.Photo).Where(viewer.Media(trip, trip.Ship))
                .OrderBy(m => m.TakenAt ?? m.CreatedAt).Select(m => new { m.Id, m.Location, m.TakenAt, m.LogbookEntryId }).ToListAsync(ct);
            return Results.Ok(new
            {
                plannedLegs = legs.Select(l => new { l.Id, l.Order, l.Alternative, l.FromHarbourId, l.ToHarbourId, l.PlannedDate, l.DistanceNm, geometry = l.RouteGeometry }),
                logbook = entries.Select(e => new { e.Id, e.StartHarbourId, e.EndHarbourId, e.DepartedAt, e.ArrivedAt, geometry = e.RouteGeometry ?? tracks.GetValueOrDefault(e.Id) }),
                harbours = harbours.Select(h => new { h.Id, h.Name, lat = h.Location.Y, lng = h.Location.X }),
                photos = photos.Select(m => new { m.Id, thumbUrl = $"/api/media/{m.Id}/thumb", lat = m.Location!.Y, lng = m.Location.X, m.TakenAt, m.LogbookEntryId }),
            });
        });
    }

    internal static async Task<List<DayDto>> Timeline(Trip trip, DateOnly? from, DateOnly? to, SmsDbContext db, Viewer viewer, CancellationToken ct)
    {
        var start = from ?? trip.StartDate;
        var end = to ?? trip.EndDate;
        var entries = await db.LogbookEntries.AsNoTracking().Include(l => l.StartHarbour).Include(l => l.EndHarbour).Include(l => l.Crew).Include(l => l.Ship).Include(l => l.Trip)
            .Where(l => l.TripId == trip.Id).Where(viewer.Logbook()).OrderBy(l => l.DepartedAt).ToListAsync(ct);
        var posts = await db.Posts.AsNoTracking().Include(p => p.Media).Include(p => p.Trip).ThenInclude(t => t.Ship)
            .Where(p => p.TripId == trip.Id && p.Day >= start && p.Day <= end).Where(viewer.Posts()).OrderBy(p => p.PublishedAt).ToListAsync(ct);
        var photos = await db.Media.AsNoTracking().Where(m => m.TripId == trip.Id && m.PostId == null && m.Kind == MediaKind.Photo && m.Day >= start && m.Day <= end)
            .Where(viewer.Media(trip, trip.Ship)).OrderBy(m => m.TakenAt ?? m.CreatedAt).ToListAsync(ct);
        var names = await ShipEndpoints.DisplayNames(db, posts.Select(p => p.AuthorId).Concat(photos.Select(m => m.OwnerId)).Concat(entries.SelectMany(e => e.Crew.Where(c => c.UserId != null).Select(c => c.UserId!.Value))).Concat(entries.Select(e => e.AuthorId)), ct);
        var crewNames = await LogbookEndpoints.CrewNames(db, trip.Id, ct);
        var days = new List<DayDto>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var dayEntries = entries.Where(e => DayIn(e.DepartedAt, trip.TimeZone) <= d && DayIn(e.ArrivedAt, trip.TimeZone) >= d).Select(e => LogbookEndpoints.ToDto(e, names, crewNames, viewer.UserId)).ToList();
            var dayPosts = posts.Where(p => p.Day == d).Select(p => PostEndpoints.ToDto(p, names, p.Media.AsQueryable().Where(viewer.Media(trip, trip.Ship)).ToList())).ToList();
            var dayPhotos = photos.Where(m => m.Day == d).Select(m => MediaEndpoints.ToDto(m, names)).ToList();
            if (dayEntries.Count + dayPosts.Count + dayPhotos.Count > 0 || (d >= trip.StartDate && d <= trip.EndDate))
                days.Add(new DayDto(d, dayEntries, dayPosts, dayPhotos));
        }
        return days;
    }

    private static async Task<List<CrewDto>> CrewDtos(Guid tripId, SmsDbContext db, CancellationToken ct)
    {
        var crew = await db.CrewMembers.AsNoTracking().Where(c => c.TripId == tripId).OrderBy(c => c.Role).ThenBy(c => c.CreatedAt).ToListAsync(ct);
        var names = await ShipEndpoints.DisplayNames(db, crew.Where(c => c.UserId != null).Select(c => c.UserId!.Value), ct);
        return crew.Select(c => ToCrewDto(c, names)).ToList();
    }

    internal static CrewDto ToCrewDto(CrewMember c, Dictionary<Guid, string> names) =>
        new(c.Id, c.UserId, c.UserId is { } u ? names.GetValueOrDefault(u, "?") : c.GuestName ?? "Gast", c.Role, c.Role == CrewRole.Skipper || c.CanWriteLogbook, c.Status, c.FromDate, c.ToDate, c.UserId is null);

    private static async Task<List<LegDto>> LegDtos(Trip trip, SmsDbContext db, SeaRouter router, CancellationToken ct)
    {
        var legs = await db.PlannedLegs.AsNoTracking().Where(l => l.TripId == trip.Id).OrderBy(l => l.Order).ThenBy(l => l.Alternative).ToListAsync(ct);
        var ids = legs.SelectMany(l => new[] { l.FromHarbourId, l.ToHarbourId }).Distinct().ToList();
        var names = await db.Harbours.AsNoTracking().Where(h => ids.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.Name, ct);
        var warnings = await router.WarningsAlongAsync(legs.Select(l => l.RouteGeometry).ToList(), ct, (double?)trip.Ship.WarnDepthM); // assumed passages and shallows below the ship's warning depth the stored route runs through
        return legs.Select((l, i) => new LegDto(l.Id, l.Order, l.Alternative, l.FromHarbourId, names.GetValueOrDefault(l.FromHarbourId, "?"), l.ToHarbourId, names.GetValueOrDefault(l.ToHarbourId, "?"), l.PlannedDate, l.DistanceNm,
            l.DistanceNm is { } nm && trip.Ship.CruisingSpeedKn > 0 ? Math.Round(nm / trip.Ship.CruisingSpeedKn, 1) : null, warnings[i])).ToList();
    }

    private static async Task<TripDto> ToDto(Trip t, SmsDbContext db, CurrentUser current, CancellationToken ct)
    {
        var crew = await CrewDtos(t.Id, db, ct);
        var me = current.Id is { } uid ? crew.FirstOrDefault(c => c.UserId == uid) : null;
        var isCrew = me is { Status: CrewStatus.Accepted };
        var isOwner = current.Id is { } o && await db.ShipMembers.AnyAsync(m => m.ShipId == t.ShipId && m.UserId == o && m.Role == ShipRole.Owner, ct);
        return new TripDto(t.Id, t.ShipId, t.Ship.Name, t.Name, t.Description, t.StartDate, t.EndDate, t.TimeZone, t.PlannedStartHarbourId, t.PlannedEndHarbourId,
            t.Visibility, t.Status, t.Currency, t.CostsLockedAt, crew, me, me is { Role: CrewRole.Skipper, Status: CrewStatus.Accepted } || isOwner,
            isCrew && (me!.Role == CrewRole.Skipper || me.CanWriteLogbook), isCrew, isCrew, t.TrackVisibility, t.PublicPositionDelayMinutes);
    }

    private static IResult? Validate(TripUpsert r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return Invalid("name", "Name fehlt.");
        if (r.EndDate < r.StartDate) return Invalid("endDate", "Ende liegt vor dem Start.");
        if (r.Currency is { Length: not 3 }) return Invalid("currency", "ISO-4217-Code (3 Buchstaben).");
        if (r.TimeZone is { Length: > 0 } tz && !TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _)) return Invalid("timeZone", "Unbekannte Zeitzone.");
        return null;
    }

    private static void Apply(Trip t, TripUpsert r, HtmlSanitizerService html)
    {
        t.Name = r.Name.Trim(); t.Description = html.Sanitize(r.Description); t.StartDate = r.StartDate; t.EndDate = r.EndDate;
        t.TimeZone = string.IsNullOrWhiteSpace(r.TimeZone) ? "Europe/Berlin" : r.TimeZone; t.PlannedStartHarbourId = r.PlannedStartHarbourId; t.PlannedEndHarbourId = r.PlannedEndHarbourId;
        t.Visibility = r.Visibility == Visibility.Public ? Visibility.Public : Visibility.Crew; t.Status = r.Status; t.Currency = (r.Currency ?? "EUR").ToUpperInvariant();
        t.TrackVisibility = r.TrackVisibility ?? t.Visibility; t.PublicPositionDelayMinutes = Math.Clamp(r.PublicPositionDelayMinutes ?? 120, 0, 100_000);
    }
}
