using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record PostUpsert(string? Title, string Content, DateOnly? Day, Guid? HarbourId, double? Lat, double? Lng, Visibility Visibility, List<Guid>? MediaIds);
public record PostDto(Guid Id, Guid TripId, Guid AuthorId, string Author, DateOnly Day, string? Title, string Content, Guid? HarbourId, double? Lat, double? Lng,
    LocationSource? LocationSource, Visibility Visibility, DateTimeOffset PublishedAt, IReadOnlyList<MediaDto> Media, bool CanEdit);

public static class PostEndpoints
{
    public static void MapPosts(this IEndpointRouteBuilder api)
    {
        var trips = api.MapGroup("/trips/{tripId:guid}/posts").WithTags("Posts");
        var g = api.MapGroup("/posts").WithTags("Posts");

        trips.MapPost("", async (Guid tripId, PostUpsert req, SmsDbContext db, CurrentUser current, Access access, HtmlSanitizerService html, CancellationToken ct) =>
        {
            if (!await access.IsCrewAsync(tripId, ct)) return Results.Forbid();
            var trip = await db.Trips.AsNoTracking().FirstAsync(t => t.Id == tripId, ct);
            if (string.IsNullOrWhiteSpace(req.Content) && (req.MediaIds is null || req.MediaIds.Count == 0)) return Invalid("content", "Inhalt fehlt.");
            var post = new Post { TripId = tripId, AuthorId = current.RequiredId, CreatedById = current.Id };
            await Apply(post, req, trip, db, html, ct);
            db.Posts.Add(post);
            await db.SaveChangesAsync(ct);
            await AttachMedia(post, req, db, current.RequiredId, ct); // after the insert: the FK needs the post row
            return Results.Created($"/api/posts/{post.Id}", new { post.Id });
        }).RequireAuthorization();

        g.MapGet("/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var post = await db.Posts.AsNoTracking().Include(p => p.Media).Include(p => p.Trip).ThenInclude(t => t.Ship).Where(viewer.Posts()).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (post is null) return Results.NotFound();
            var names = await ShipEndpoints.DisplayNames(db, [post.AuthorId, .. post.Media.Select(m => m.OwnerId)], ct);
            var media = post.Media.AsQueryable().Where(viewer.Media(post.Trip, post.Trip.Ship)).ToList();
            return Results.Ok(ToDto(post, names, media, current.Id));
        });

        g.MapPut("/{id:guid}", async (Guid id, PostUpsert req, SmsDbContext db, CurrentUser current, HtmlSanitizerService html, CancellationToken ct) =>
        {
            var post = await db.Posts.Include(p => p.Trip).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (post is null) return Results.NotFound();
            if (post.AuthorId != current.Id && !await current.IsAdminAsync(ct)) return Results.Forbid();
            await Apply(post, req, post.Trip, db, html, ct);
            await db.SaveChangesAsync(ct);
            await AttachMedia(post, req, db, post.AuthorId, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapDelete("/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (post is null) return Results.NotFound();
            if (post.AuthorId != current.Id && !await access.IsTripSkipperAsync(post.TripId, ct) && !await current.IsAdminAsync(ct)) return Results.Forbid();
            post.DeletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static async Task Apply(Post p, PostUpsert r, Trip trip, SmsDbContext db, HtmlSanitizerService html, CancellationToken ct)
    {
        p.Title = r.Title?.Trim();
        p.Content = html.Sanitize(r.Content);
        var day = r.Day ?? TodayIn(trip.TimeZone);
        p.Day = day < trip.StartDate ? trip.StartDate : day > trip.EndDate ? trip.EndDate : day;
        p.Visibility = r.Visibility;
        p.HarbourId = r.HarbourId;
        if (r.Lat is { } lat && r.Lng is { } lng) { p.Location = Geo.Point(lat, lng); p.LocationSource = LocationSource.Manual; }
        else if (r.HarbourId is { } h && await db.Harbours.AsNoTracking().FirstOrDefaultAsync(x => x.Id == h, ct) is { } harbour) { p.Location = harbour.Location; p.LocationSource = LocationSource.Harbour; }
        else if (await db.LogbookEntries.AsNoTracking().Include(l => l.EndHarbour).Where(l => l.TripId == trip.Id).OrderByDescending(l => l.ArrivedAt)
                     .FirstOrDefaultAsync(l => l.ArrivedAt <= DateTimeOffset.UtcNow, ct) is { } last && DayIn(last.ArrivedAt, trip.TimeZone) <= p.Day)
        { p.Location = last.EndHarbour.Location; p.LocationSource = LocationSource.Harbour; }
    }

    /// <summary>Attach / detach the author's photos; runs after the post row exists.</summary>
    private static async Task AttachMedia(Post p, PostUpsert r, SmsDbContext db, Guid owner, CancellationToken ct)
    {
        var ids = r.MediaIds ?? [];
        await db.Media.Where(m => m.PostId == p.Id && !ids.Contains(m.Id)).ExecuteUpdateAsync(s => s.SetProperty(m => m.PostId, (Guid?)null), ct);
        if (ids.Count > 0)
            await db.Media.Where(m => ids.Contains(m.Id) && m.OwnerId == owner && m.TripId == p.TripId).ExecuteUpdateAsync(s => s.SetProperty(m => m.PostId, p.Id).SetProperty(m => m.Day, p.Day), ct);
    }

    internal static PostDto ToDto(Post p, Dictionary<Guid, string> names, List<Media> media, Guid? me = null) => new(
        p.Id, p.TripId, p.AuthorId, names.GetValueOrDefault(p.AuthorId, "?"), p.Day, p.Title, p.Content, p.HarbourId, p.Location?.Y, p.Location?.X, p.LocationSource,
        p.Visibility, p.PublishedAt, media.Select(m => MediaEndpoints.ToDto(m, names)).ToList(), me is not null && p.AuthorId == me);
}
