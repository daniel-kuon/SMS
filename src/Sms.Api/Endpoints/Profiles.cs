using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record ProfileUpdate(string DisplayName, string? Bio, Guid? HomeHarbourId, string? Experience, ProfileVisibility ProfileVisibility, Visibility DefaultContentVisibility, string Language, bool EmailNotifications);
public record PublicProfile(Guid UserId, string DisplayName, string? Bio, string? Experience, string? HomeHarbour, string? AvatarUrl, IReadOnlyList<TripSummary> Trips);
public record UserSearchResult(Guid UserId, string DisplayName);

public static class ProfileEndpoints
{
    public static void MapProfiles(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("").WithTags("Profiles");

        g.MapGet("/users/{userId:guid}", async (Guid userId, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var p = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (p is null) return Results.NotFound();
            if (p.ProfileVisibility == ProfileVisibility.Registered && !current.IsAuthenticated) return Results.Unauthorized();
            var harbour = p.HomeHarbourId is { } h ? await db.Harbours.Where(x => x.Id == h).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
            var viewer = await Viewer.ForAsync(current, access, ct);
            var trips = await db.Trips.AsNoTracking().Include(t => t.Ship)
                .Where(t => t.Crew.Any(c => c.UserId == userId && c.Status == CrewStatus.Accepted))
                .Where(viewer.Trips())
                .OrderByDescending(t => t.StartDate).Take(50)
                .Select(t => TripEndpoints.Summary(t)).ToListAsync(ct);
            return Results.Ok(new PublicProfile(p.UserId, p.DisplayName, p.Bio, p.Experience, harbour, p.AvatarMediaId is { } a ? $"/api/media/{a}/thumb" : null, trips));
        });

        g.MapGet("/users", async (string q, SmsDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2) return Results.Ok(Array.Empty<UserSearchResult>());
            // Display-name substring search, or an exact e-mail match; e-mail addresses are never returned.
            var pattern = "%" + q.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
            var normalizedEmail = q.Trim().ToUpperInvariant();
            var hits = await db.Profiles.AsNoTracking()
                .Join(db.Users, p => p.UserId, u => u.Id, (p, u) => new { p, u })
                .Where(x => EF.Functions.ILike(x.p.DisplayName, pattern, @"\") || x.u.NormalizedEmail == normalizedEmail)
                .OrderBy(x => x.p.DisplayName).Take(20)
                .Select(x => new UserSearchResult(x.p.UserId, x.p.DisplayName)).ToListAsync(ct);
            return Results.Ok(hits);
        }).RequireAuthorization();

        g.MapPut("/me/profile", async (ProfileUpdate req, SmsDbContext db, CurrentUser current, HtmlSanitizerService html, CancellationToken ct) =>
        {
            var p = await db.Profiles.FirstOrDefaultAsync(x => x.UserId == current.RequiredId, ct);
            if (p is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.DisplayName)) return Invalid("displayName", "Anzeigename fehlt.");
            p.DisplayName = req.DisplayName.Trim();
            p.Bio = html.Sanitize(req.Bio);
            p.HomeHarbourId = req.HomeHarbourId;
            p.Experience = req.Experience;
            p.ProfileVisibility = req.ProfileVisibility;
            p.DefaultContentVisibility = req.DefaultContentVisibility;
            p.Language = req.Language is "en" ? "en" : "de";
            p.EmailNotifications = req.EmailNotifications;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        }).RequireAuthorization();

        g.MapGet("/me/export", async (SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var uid = current.RequiredId;
            var export = new
            {
                profile = await db.Profiles.AsNoTracking().FirstAsync(p => p.UserId == uid, ct),
                ships = await db.ShipMembers.AsNoTracking().Where(m => m.UserId == uid).Select(m => new { m.ShipId, m.Role, m.Since }).ToListAsync(ct),
                crew = await db.CrewMembers.AsNoTracking().Where(c => c.UserId == uid).ToListAsync(ct),
                posts = await db.Posts.AsNoTracking().Where(p => p.AuthorId == uid).ToListAsync(ct),
                media = await db.Media.AsNoTracking().Where(m => m.OwnerId == uid).ToListAsync(ct),
                logbook = await db.LogbookEntries.AsNoTracking().Where(l => l.AuthorId == uid).ToListAsync(ct),
                expenses = await db.Expenses.AsNoTracking().Where(e => db.CrewMembers.Any(c => c.Id == e.PaidByCrewMemberId && c.UserId == uid)).ToListAsync(ct),
            };
            return Results.Json(export);
        }).RequireAuthorization();

        // Spec 07 A3: posts/photos deleted; crew entries, logbook crew and costs kept but anonymised.
        g.MapDelete("/me", async (SmsDbContext db, CurrentUser current, UserManager<ApplicationUser> users, MediaStorage storage, CancellationToken ct) =>
        {
            var uid = current.RequiredId;
            if (await db.ShipMembers.AnyAsync(m => m.UserId == uid && m.Role == ShipRole.Owner
                    && !db.ShipMembers.Any(o => o.ShipId == m.ShipId && o.UserId != uid && o.Role == ShipRole.Owner), ct))
                return Results.Problem(statusCode: 409, title: "Du bist einziger Eigner eines Schiffs. Übertrage zuerst die Eignerschaft oder lösche das Schiff.");
            var media = await db.Media.Where(m => m.OwnerId == uid).ToListAsync(ct);
            foreach (var m in media) storage.Delete(m.StorageKey);
            db.Media.RemoveRange(media);
            db.Posts.RemoveRange(db.Posts.Where(p => p.AuthorId == uid));
            await db.CrewMembers.Where(c => c.UserId == uid).ExecuteUpdateAsync(s => s.SetProperty(c => c.UserId, (Guid?)null).SetProperty(c => c.GuestName, "Ehemaliges Mitglied"), ct);
            await db.LogbookEntryCrew.Where(c => c.UserId == uid).ExecuteUpdateAsync(s => s.SetProperty(c => c.UserId, (Guid?)null).SetProperty(c => c.GuestName, "Ehemaliges Mitglied"), ct);
            db.ShipMembers.RemoveRange(db.ShipMembers.Where(m => m.UserId == uid));
            await db.SaveChangesAsync(ct);
            var user = await users.FindByIdAsync(uid.ToString());
            if (user is not null) await users.DeleteAsync(user);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
