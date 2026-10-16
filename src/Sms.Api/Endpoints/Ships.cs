using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record ShipUpsert(string Name, string? Type, decimal? LengthM, decimal? BeamM, decimal? DraftM, decimal? AirDraftM, string? SailNumber,
    string? CallSign, string? Mmsi, Guid? HomeHarbourId, decimal CruisingSpeedKn, string? Description, Visibility Visibility);
public record ShipMemberDto(Guid UserId, string DisplayName, ShipRole Role, DateTimeOffset Since);
public record ShipDto(Guid Id, string Name, string? Type, decimal? LengthM, decimal? BeamM, decimal? DraftM, decimal? AirDraftM, string? SailNumber,
    string? CallSign, string? Mmsi, Guid? HomeHarbourId, string? HomeHarbourName, decimal CruisingSpeedKn, string? Description, Visibility Visibility,
    string? PhotoUrl, IReadOnlyList<ShipMemberDto> Members, ShipRole? MyRole);
public record ShipStats(decimal NauticalMiles, decimal EngineHours, int Entries, int Harbours, int Trips);
public record MemberUpsert(Guid UserId, ShipRole Role);
public record ShipInviteRequest(string Email, ShipRole Role);

public static class ShipEndpoints
{
    public static void MapShips(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/ships").WithTags("Ships");

        g.MapGet("", async (bool? mine, int? page, int? pageSize, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var (p, ps) = Paging(page, pageSize);
            var viewer = await Viewer.ForAsync(current, access, ct);
            var q = db.Ships.AsNoTracking().Include(s => s.Members).Include(s => s.HomeHarbour).Where(viewer.Ships());
            if (mine == true) q = q.Where(s => viewer.MemberShipIds.Contains(s.Id));
            var total = await q.CountAsync(ct);
            var items = await q.OrderBy(s => s.Name).Skip((p - 1) * ps).Take(ps).ToListAsync(ct);
            var names = await DisplayNames(db, items.SelectMany(s => s.Members.Select(m => m.UserId)), ct);
            return Results.Ok(new Paged<ShipDto>(items.Select(s => ToDto(s, names, current.Id)).ToList(), p, ps, total));
        });

        g.MapPost("", async (ShipUpsert req, SmsDbContext db, CurrentUser current, HtmlSanitizerService html, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Invalid("name", "Name fehlt.");
            var ship = new Ship { CreatedById = current.RequiredId };
            Apply(ship, req, html);
            ship.Members.Add(new ShipMember { UserId = current.RequiredId, Role = ShipRole.Owner });
            db.Ships.Add(ship);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/ships/{ship.Id}", new { ship.Id });
        }).RequireAuthorization();

        g.MapGet("/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var ship = await db.Ships.AsNoTracking().Include(s => s.Members).Include(s => s.HomeHarbour).Where(viewer.Ships()).FirstOrDefaultAsync(s => s.Id == id, ct);
            if (ship is null) return Results.NotFound();
            var names = await DisplayNames(db, ship.Members.Select(m => m.UserId), ct);
            return Results.Ok(ToDto(ship, names, current.Id));
        });

        g.MapGet("/{id:guid}/stats", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var entries = db.LogbookEntries.AsNoTracking().Where(l => l.ShipId == id).Where(viewer.Logbook());
            var stats = new ShipStats(
                await entries.SumAsync(l => l.SailedDistanceNm ?? ((l.LogEnd ?? 0) - (l.LogStart ?? 0)), ct),
                await entries.SumAsync(l => (l.EngineHoursEnd ?? 0) - (l.EngineHoursStart ?? 0), ct),
                await entries.CountAsync(ct),
                await entries.Select(l => l.EndHarbourId).Union(entries.Select(l => l.StartHarbourId)).CountAsync(ct),
                await db.Trips.AsNoTracking().Where(t => t.ShipId == id).Where(viewer.Trips()).CountAsync(ct));
            return Results.Ok(stats);
        });

        g.MapPut("/{id:guid}", async (Guid id, ShipUpsert req, SmsDbContext db, Access access, HtmlSanitizerService html, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(id, ct)) return Results.Forbid();
            var ship = await db.Ships.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (ship is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Name)) return Invalid("name", "Name fehlt.");
            Apply(ship, req, html);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapDelete("/{id:guid}", async (Guid id, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(id, ct)) return Results.Forbid();
            if (await db.Trips.AnyAsync(t => t.ShipId == id, ct) || await db.LogbookEntries.AnyAsync(l => l.ShipId == id, ct))
                return Results.Problem(statusCode: 409, title: "Schiff hat noch Törns oder Logbucheinträge.");
            await db.Ships.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // --- members
        g.MapPost("/{id:guid}/members", async (Guid id, MemberUpsert req, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(id, ct)) return Results.Forbid();
            if (!await db.Profiles.AnyAsync(p => p.UserId == req.UserId, ct)) return Invalid("userId", "Benutzer nicht gefunden.");
            var m = await db.ShipMembers.FirstOrDefaultAsync(x => x.ShipId == id && x.UserId == req.UserId, ct);
            if (m is null) db.ShipMembers.Add(new ShipMember { ShipId = id, UserId = req.UserId, Role = req.Role });
            else m.Role = req.Role;
            await db.SaveChangesAsync(ct);
            if (!await db.ShipMembers.AnyAsync(x => x.ShipId == id && x.Role == ShipRole.Owner, ct))
                return Results.Problem(statusCode: 409, title: "Mindestens ein Eigner muss bleiben.");
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, SmsDbContext db, Access access, CurrentUser current, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(id, ct) && current.Id != userId) return Results.Forbid();
            var owners = await db.ShipMembers.CountAsync(x => x.ShipId == id && x.Role == ShipRole.Owner, ct);
            var target = await db.ShipMembers.FirstOrDefaultAsync(x => x.ShipId == id && x.UserId == userId, ct);
            if (target is null) return Results.NotFound();
            if (target.Role == ShipRole.Owner && owners <= 1) return Results.Problem(statusCode: 409, title: "Der letzte Eigner kann nicht entfernt werden.");
            db.ShipMembers.Remove(target);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapPost("/{id:guid}/invitations", async (Guid id, ShipInviteRequest req, SmsDbContext db, Access access, CurrentUser current, MailSender mail, HttpRequest http, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(id, ct)) return Results.Forbid();
            var ship = await db.Ships.AsNoTracking().FirstAsync(s => s.Id == id, ct);
            var inv = new ShipInvitation { ShipId = id, Email = req.Email.Trim(), Role = req.Role, Token = NewToken(), ExpiresAt = DateTimeOffset.UtcNow.AddDays(14), CreatedById = current.Id };
            db.ShipInvitations.Add(inv);
            await db.SaveChangesAsync(ct);
            var link = $"{WebBaseUrl(http, cfg)}/invitations/ship/{inv.Token}";
            await mail.SendAsync(inv.Email, $"Einladung zum Schiff {ship.Name}", $"<p>Du wurdest als {req.Role} zum Schiff <b>{ship.Name}</b> eingeladen: <a href=\"{link}\">{link}</a></p>", ct);
            return Results.Ok(new { inv.Token, link });
        }).RequireAuthorization();

        api.MapGet("/ship-invitations/{token}", async (string token, SmsDbContext db, CancellationToken ct) =>
        {
            var inv = await db.ShipInvitations.AsNoTracking().Include(i => i.Ship).FirstOrDefaultAsync(i => i.Token == token, ct);
            return inv is null ? Results.NotFound() : Results.Ok(new { inv.Ship.Name, inv.Role, inv.Status, inv.ExpiresAt });
        }).WithTags("Ships");

        api.MapPost("/ship-invitations/{token}/accept", async (string token, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var inv = await db.ShipInvitations.FirstOrDefaultAsync(i => i.Token == token, ct);
            if (inv is null || inv.Status != InvitationStatus.Pending || inv.ExpiresAt < DateTimeOffset.UtcNow) return Results.Problem(statusCode: 410, title: "Einladung ungültig oder abgelaufen.");
            var uid = current.RequiredId;
            var existing = await db.ShipMembers.FirstOrDefaultAsync(m => m.ShipId == inv.ShipId && m.UserId == uid, ct);
            if (existing is null) db.ShipMembers.Add(new ShipMember { ShipId = inv.ShipId, UserId = uid, Role = inv.Role });
            else existing.Role = inv.Role;
            inv.Status = InvitationStatus.Accepted;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { inv.ShipId });
        }).RequireAuthorization().WithTags("Ships");
    }

    internal static async Task<Dictionary<Guid, string>> DisplayNames(SmsDbContext db, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return ids.Count == 0 ? [] : await db.Profiles.AsNoTracking().Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, p => p.DisplayName, ct);
    }

    private static void Apply(Ship s, ShipUpsert r, HtmlSanitizerService html)
    {
        s.Name = r.Name.Trim(); s.Type = r.Type; s.LengthM = r.LengthM; s.BeamM = r.BeamM; s.DraftM = r.DraftM; s.AirDraftM = r.AirDraftM;
        s.SailNumber = r.SailNumber; s.CallSign = r.CallSign; s.Mmsi = r.Mmsi; s.HomeHarbourId = r.HomeHarbourId;
        s.CruisingSpeedKn = r.CruisingSpeedKn > 0 ? r.CruisingSpeedKn : 5; s.Description = html.Sanitize(r.Description);
        s.Visibility = r.Visibility == Visibility.Private ? Visibility.Private : Visibility.Public;
    }

    internal static ShipDto ToDto(Ship s, Dictionary<Guid, string> names, Guid? me) => new(
        s.Id, s.Name, s.Type, s.LengthM, s.BeamM, s.DraftM, s.AirDraftM, s.SailNumber, s.CallSign, s.Mmsi, s.HomeHarbourId, s.HomeHarbour?.Name,
        s.CruisingSpeedKn, s.Description, s.Visibility, s.PhotoMediaId is { } p ? $"/api/media/{p}/preview" : null,
        s.Members.Select(m => new ShipMemberDto(m.UserId, names.GetValueOrDefault(m.UserId, "?"), m.Role, m.Since)).ToList(),
        s.Members.FirstOrDefault(m => m.UserId == me)?.Role);
}
