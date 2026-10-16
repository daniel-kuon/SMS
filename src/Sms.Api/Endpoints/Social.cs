using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Domain.Social;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record CommentDto(Guid Id, Guid AuthorId, string Author, string? AvatarUrl, string Text, DateTimeOffset CreatedAt, bool CanDelete);
public record CommentCreate(string Text);
public record SocialInfo(IReadOnlyList<CommentDto> Comments, int Likes, bool Liked);
public record FollowRequest(TargetType TargetType, Guid TargetId);
public record ReportCreate(TargetType TargetType, Guid TargetId, string Reason);
public record FeedItem(string Kind, Guid Id, Guid TripId, string TripName, string ShipName, Guid AuthorId, string Author, string? Title, string? Excerpt, string? ThumbUrl, DateTimeOffset At, string Link);
public record NotificationDto(Guid Id, NotificationType Type, string Title, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

/// <summary>Creates in-app notifications and (optionally) e-mails (spec 07 E4).</summary>
public sealed class Notifier(SmsDbContext db, MailSender mail, IConfiguration cfg)
{
    public async Task NotifyAsync(Guid userId, NotificationType type, string title, string? link, CancellationToken ct, bool email = false)
    {
        db.Notifications.Add(new Notification { UserId = userId, Type = type, Title = title, Link = link });
        if (email && await db.Profiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.EmailNotifications).FirstOrDefaultAsync(ct))
        {
            var address = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);
            var baseUrl = cfg["Web:BaseUrl"]?.TrimEnd('/') ?? "";
            if (address is not null) await mail.SendAsync(address, title, $"<p>{System.Net.WebUtility.HtmlEncode(title)}</p>" + (link is null ? "" : $"<p><a href=\"{baseUrl}{link}\">{baseUrl}{link}</a></p>"), ct);
        }
    }
}

public static class SocialEndpoints
{
    private static readonly Dictionary<string, TargetType> Routes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["posts"] = TargetType.Post, ["media"] = TargetType.Media, ["logbook"] = TargetType.LogbookEntry, ["trips"] = TargetType.Trip, ["reviews"] = TargetType.HarbourReview,
    };

    public static void MapSocial(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("").WithTags("Social");

        // --- comments & likes on any visible item
        g.MapGet("/{targetType:regex(^(posts|media|logbook|trips|reviews)$)}/{id:guid}/social", async (string targetType, Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var type = Routes[targetType];
            if (!await CanViewTargetAsync(type, id, db, current, access, ct)) return Results.NotFound();
            var comments = await db.Comments.AsNoTracking().Where(c => c.TargetType == type && c.TargetId == id && !c.Hidden).OrderBy(c => c.CreatedAt).ToListAsync(ct);
            var profiles = await db.Profiles.AsNoTracking().Where(p => comments.Select(c => c.AuthorId).Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
            var likes = await db.Reactions.CountAsync(r => r.TargetType == type && r.TargetId == id, ct);
            var liked = current.Id is { } uid && await db.Reactions.AnyAsync(r => r.TargetType == type && r.TargetId == id && r.UserId == uid, ct);
            var isAdmin = await current.IsAdminAsync(ct);
            return Results.Ok(new SocialInfo(comments.Select(c => new CommentDto(c.Id, c.AuthorId, profiles.GetValueOrDefault(c.AuthorId)?.DisplayName ?? "?",
                profiles.GetValueOrDefault(c.AuthorId)?.AvatarMediaId is { } a ? $"/api/media/{a}/thumb" : null, c.Text, c.CreatedAt, c.AuthorId == current.Id || isAdmin)).ToList(), likes, liked));
        });

        g.MapPost("/{targetType:regex(^(posts|media|logbook|trips|reviews)$)}/{id:guid}/comments", async (string targetType, Guid id, CommentCreate req, SmsDbContext db, CurrentUser current, Access access, Notifier notifier, CancellationToken ct) =>
        {
            var type = Routes[targetType];
            if (!await CanViewTargetAsync(type, id, db, current, access, ct)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Text) || req.Text.Length > 4000) return Invalid("text", "Kommentar fehlt oder zu lang.");
            if (await db.UserBlocks.AnyAsync(b => b.UserId == current.RequiredId, ct)) return Results.Forbid();
            var c = new Comment { TargetType = type, TargetId = id, AuthorId = current.RequiredId, Text = req.Text.Trim(), CreatedById = current.Id };
            db.Comments.Add(c);
            var (ownerId, link, label) = await TargetOwnerAsync(type, id, db, ct);
            if (ownerId is { } owner && owner != current.Id)
                await notifier.NotifyAsync(owner, NotificationType.Comment, $"{(await current.ProfileAsync(ct))?.DisplayName} hat {label} kommentiert", link, ct, email: true);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/comments/{c.Id}", new { c.Id });
        }).RequireAuthorization();

        g.MapDelete("/comments/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (c.AuthorId != current.Id && !await current.IsAdminAsync(ct)) return Results.Forbid();
            db.Comments.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapPut("/{targetType:regex(^(posts|media|logbook|trips|reviews)$)}/{id:guid}/reactions", async (string targetType, Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var type = Routes[targetType];
            if (!await CanViewTargetAsync(type, id, db, current, access, ct)) return Results.NotFound();
            if (!await db.Reactions.AnyAsync(r => r.TargetType == type && r.TargetId == id && r.UserId == current.RequiredId, ct))
            {
                db.Reactions.Add(new Reaction { TargetType = type, TargetId = id, UserId = current.RequiredId });
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapDelete("/{targetType:regex(^(posts|media|logbook|trips|reviews)$)}/{id:guid}/reactions", async (string targetType, Guid id, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var type = Routes[targetType];
            await db.Reactions.Where(r => r.TargetType == type && r.TargetId == id && r.UserId == current.RequiredId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // --- follows
        g.MapGet("/me/follows", async (SmsDbContext db, CurrentUser current, CancellationToken ct) =>
            Results.Ok(await db.Follows.AsNoTracking().Where(f => f.FollowerId == current.RequiredId).Select(f => new { f.TargetType, f.TargetId }).ToListAsync(ct))).RequireAuthorization();
        g.MapPost("/follows", async (FollowRequest req, SmsDbContext db, CurrentUser current, Notifier notifier, CancellationToken ct) =>
        {
            if (req.TargetType is not (TargetType.User or TargetType.Ship)) return Invalid("targetType", "User oder Ship.");
            var exists = req.TargetType == TargetType.User ? await db.Profiles.AnyAsync(p => p.UserId == req.TargetId, ct) : await db.Ships.AnyAsync(s => s.Id == req.TargetId, ct);
            if (!exists) return Results.NotFound();
            if (!await db.Follows.AnyAsync(f => f.FollowerId == current.RequiredId && f.TargetType == req.TargetType && f.TargetId == req.TargetId, ct))
            {
                db.Follows.Add(new Follow { FollowerId = current.RequiredId, TargetType = req.TargetType, TargetId = req.TargetId });
                if (req.TargetType == TargetType.User && req.TargetId != current.Id)
                    await notifier.NotifyAsync(req.TargetId, NotificationType.Follow, $"{(await current.ProfileAsync(ct))?.DisplayName} folgt dir jetzt", $"/u/{current.Id}", ct);
                await db.SaveChangesAsync(ct);
            }
            return Results.NoContent();
        }).RequireAuthorization();
        g.MapDelete("/follows/{targetType}/{targetId:guid}", async (TargetType targetType, Guid targetId, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            await db.Follows.Where(f => f.FollowerId == current.RequiredId && f.TargetType == targetType && f.TargetId == targetId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // --- feed (my trips + followed users/ships) and public explore
        g.MapGet("/feed", async (DateTimeOffset? cursor, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var uid = current.RequiredId;
            var followedUsers = await db.Follows.Where(f => f.FollowerId == uid && f.TargetType == TargetType.User).Select(f => f.TargetId).ToListAsync(ct);
            var followedShips = await db.Follows.Where(f => f.FollowerId == uid && f.TargetType == TargetType.Ship).Select(f => f.TargetId).ToListAsync(ct);
            var before = cursor ?? DateTimeOffset.UtcNow.AddMinutes(1);
            var posts = await db.Posts.AsNoTracking().Include(p => p.Trip).ThenInclude(t => t.Ship).Include(p => p.Media)
                .Where(viewer.Posts()).Where(p => p.PublishedAt < before)
                .Where(p => viewer.CrewTripIds.Contains(p.TripId) || followedUsers.Contains(p.AuthorId) || followedShips.Contains(p.Trip.ShipId))
                .OrderByDescending(p => p.PublishedAt).Take(30).ToListAsync(ct);
            var entries = await db.LogbookEntries.AsNoTracking().Include(l => l.Trip).ThenInclude(t => t!.Ship).Include(l => l.StartHarbour).Include(l => l.EndHarbour)
                .Where(viewer.Logbook()).Where(l => l.TripId != null && l.CreatedAt < before)
                .Where(l => viewer.CrewTripIds.Contains(l.TripId!.Value) || followedUsers.Contains(l.AuthorId) || followedShips.Contains(l.ShipId))
                .OrderByDescending(l => l.CreatedAt).Take(30).ToListAsync(ct);
            return Results.Ok(await ToFeed(posts, entries, db, ct));
        }).RequireAuthorization();

        g.MapGet("/explore", async (DateTimeOffset? cursor, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var before = cursor ?? DateTimeOffset.UtcNow.AddMinutes(1);
            var posts = await db.Posts.AsNoTracking().Include(p => p.Trip).ThenInclude(t => t.Ship).Include(p => p.Media)
                .Where(p => p.Visibility == Visibility.Public && p.Trip.Visibility == Visibility.Public && p.Trip.Ship.Visibility == Visibility.Public && p.PublishedAt < before)
                .OrderByDescending(p => p.PublishedAt).Take(30).ToListAsync(ct);
            var entries = await db.LogbookEntries.AsNoTracking().Include(l => l.Trip).ThenInclude(t => t!.Ship).Include(l => l.StartHarbour).Include(l => l.EndHarbour)
                .Where(l => l.TripId != null && l.Visibility == Visibility.Public && l.Trip!.Visibility == Visibility.Public && l.Ship.Visibility == Visibility.Public && l.CreatedAt < before)
                .OrderByDescending(l => l.CreatedAt).Take(30).ToListAsync(ct);
            var trips = await db.Trips.AsNoTracking().Include(t => t.Ship).Include(t => t.Crew).Where(t => t.Visibility == Visibility.Public && t.Ship.Visibility == Visibility.Public && t.Status != TripStatus.Cancelled)
                .OrderByDescending(t => t.StartDate).Take(20).Select(t => TripEndpoints.Summary(t)).ToListAsync(ct);
            return Results.Ok(new { items = await ToFeed(posts, entries, db, ct), trips });
        });

        // --- notifications
        g.MapGet("/notifications", async (SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var items = await db.Notifications.AsNoTracking().Where(n => n.UserId == current.RequiredId).OrderByDescending(n => n.CreatedAt).Take(50)
                .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Link, n.CreatedAt, n.ReadAt)).ToListAsync(ct);
            return Results.Ok(new { unread = items.Count(i => i.ReadAt == null), items });
        }).RequireAuthorization();
        g.MapPost("/notifications/read-all", async (SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            await db.Notifications.Where(n => n.UserId == current.RequiredId && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);
            return Results.NoContent();
        }).RequireAuthorization();
        g.MapPost("/notifications/{id:guid}/read", async (Guid id, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            await db.Notifications.Where(n => n.Id == id && n.UserId == current.RequiredId).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // --- reports & moderation (spec 07 E5)
        g.MapPost("/reports", async (ReportCreate req, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Reason)) return Invalid("reason", "Grund fehlt.");
            db.Reports.Add(new Report { TargetType = req.TargetType, TargetId = req.TargetId, ReporterId = current.RequiredId, Reason = req.Reason.Trim(), CreatedById = current.Id });
            foreach (var admin in await db.Profiles.Where(p => p.IsAdmin).Select(p => p.UserId).ToListAsync(ct))
                db.Notifications.Add(new Notification { UserId = admin, Type = NotificationType.Report, Title = $"Neue Meldung: {req.TargetType}", Link = "/admin" });
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        }).RequireAuthorization();

        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization();
        admin.MapGet("/reports", async (SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var reports = await db.Reports.AsNoTracking().OrderBy(r => r.Status).ThenByDescending(r => r.CreatedAt).Take(200).ToListAsync(ct);
            var names = await ShipEndpoints.DisplayNames(db, reports.Select(r => r.ReporterId), ct);
            return Results.Ok(reports.Select(r => new { r.Id, r.TargetType, r.TargetId, r.ReporterId, Reporter = names.GetValueOrDefault(r.ReporterId, "?"), r.Reason, r.Status, r.Resolution, r.CreatedAt }));
        });
        admin.MapPost("/reports/{id:guid}/resolve", async (Guid id, ResolveRequest req, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var r = await db.Reports.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (req.Hide)
            {
                var now = DateTimeOffset.UtcNow;
                switch (r.TargetType)
                {
                    case TargetType.Post: await db.Posts.Where(p => p.Id == r.TargetId).ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, now), ct); break;
                    case TargetType.Media: await db.Media.Where(m => m.Id == r.TargetId).ExecuteUpdateAsync(s => s.SetProperty(m => m.DeletedAt, now), ct); break;
                    case TargetType.LogbookEntry: await db.LogbookEntries.Where(l => l.Id == r.TargetId).ExecuteUpdateAsync(s => s.SetProperty(l => l.DeletedAt, now), ct); break;
                    case TargetType.HarbourReview: await db.HarbourReviews.Where(x => x.Id == r.TargetId).ExecuteDeleteAsync(ct); break;
                }
                await db.Comments.Where(c => c.TargetType == r.TargetType && c.TargetId == r.TargetId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Hidden, true), ct);
            }
            r.Status = req.Hide ? ReportStatus.Resolved : ReportStatus.Dismissed; r.Resolution = req.Resolution; r.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        admin.MapPost("/users/{userId:guid}/block", async (Guid userId, BlockRequest req, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (req.Blocked) { if (!await db.UserBlocks.AnyAsync(b => b.UserId == userId, ct)) db.UserBlocks.Add(new UserBlock { UserId = userId, BlockedById = current.RequiredId, Reason = req.Reason }); }
            else await db.UserBlocks.Where(b => b.UserId == userId).ExecuteDeleteAsync(ct);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public record ResolveRequest(bool Hide, string? Resolution);
    public record BlockRequest(bool Blocked, string? Reason);

    private static async Task<bool> CanViewTargetAsync(TargetType type, Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct)
    {
        var viewer = await Viewer.ForAsync(current, access, ct);
        switch (type)
        {
            case TargetType.Post: return await db.Posts.AsNoTracking().Include(p => p.Trip).ThenInclude(t => t.Ship).Where(viewer.Posts()).AnyAsync(p => p.Id == id, ct);
            case TargetType.LogbookEntry: return await db.LogbookEntries.AsNoTracking().Include(l => l.Ship).Include(l => l.Trip).Where(viewer.Logbook()).AnyAsync(l => l.Id == id, ct);
            case TargetType.Trip: return await db.Trips.AsNoTracking().Include(t => t.Ship).Where(viewer.Trips()).AnyAsync(t => t.Id == id, ct);
            case TargetType.HarbourReview: return await db.HarbourReviews.AnyAsync(r => r.Id == id, ct);
            case TargetType.Media:
                var m = await db.Media.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                if (m is null) return false;
                if (m.TripId is not { } tripId) return m.Visibility == Visibility.Public || m.OwnerId == current.Id;
                var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).FirstAsync(t => t.Id == tripId, ct);
                return await access.CanViewAsync(m.Visibility, trip, trip.Ship, m.OwnerId, ct);
            default: return false;
        }
    }

    private static async Task<(Guid? ownerId, string? link, string label)> TargetOwnerAsync(TargetType type, Guid id, SmsDbContext db, CancellationToken ct) => type switch
    {
        TargetType.Post => (await db.Posts.Where(p => p.Id == id).Select(p => (Guid?)p.AuthorId).FirstOrDefaultAsync(ct), $"/posts/{id}", "deinen Beitrag"),
        TargetType.Media => (await db.Media.Where(m => m.Id == id).Select(m => (Guid?)m.OwnerId).FirstOrDefaultAsync(ct), null, "dein Foto"),
        TargetType.LogbookEntry => (await db.LogbookEntries.Where(l => l.Id == id).Select(l => (Guid?)l.AuthorId).FirstOrDefaultAsync(ct), $"/logbook/{id}", "deinen Logbucheintrag"),
        TargetType.HarbourReview => (await db.HarbourReviews.Where(r => r.Id == id).Select(r => (Guid?)r.AuthorId).FirstOrDefaultAsync(ct), null, "deine Bewertung"),
        _ => (null, $"/trips/{id}", "den Törn"),
    };

    private static async Task<List<FeedItem>> ToFeed(List<Post> posts, List<LogbookEntry> entries, SmsDbContext db, CancellationToken ct)
    {
        var names = await ShipEndpoints.DisplayNames(db, posts.Select(p => p.AuthorId).Concat(entries.Select(e => e.AuthorId)), ct);
        var items = posts.Select(p => new FeedItem("post", p.Id, p.TripId, p.Trip.Name, p.Trip.Ship.Name, p.AuthorId, names.GetValueOrDefault(p.AuthorId, "?"), p.Title,
                Excerpt(p.Content), p.Media.FirstOrDefault(m => m.DeletedAt == null)?.Id is { } mid ? $"/api/media/{mid}/thumb" : null, p.PublishedAt, $"/posts/{p.Id}"))
            .Concat(entries.Select(e => new FeedItem("logbook", e.Id, e.TripId!.Value, e.Trip!.Name, e.Trip.Ship.Name, e.AuthorId, names.GetValueOrDefault(e.AuthorId, "?"),
                $"{e.StartHarbour.Name} → {e.EndHarbour.Name}", $"{e.SailedDistanceNm ?? 0} sm · {e.Duration.TotalHours:0.#} h {(e.WindSpeed is null ? "" : "· " + e.WindDirection + " " + e.WindSpeed)}", null, e.CreatedAt, $"/logbook/{e.Id}")))
            .OrderByDescending(i => i.At).Take(40).ToList();
        return items;
    }

    private static string Excerpt(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ")).Trim();
        return text.Length > 240 ? text[..240] + "…" : text;
    }
}
