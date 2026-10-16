using System.Linq.Expressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Sms.Domain;
using Sms.Infrastructure;

namespace Sms.Api;

public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public static class ApiHelpers
{
    public static ValidationProblem Invalid(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static (int page, int pageSize) Paging(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 25, 1, 200));

    /// <summary>Base URL of the SPA for links in e-mails: configured, else the caller's origin.</summary>
    public static string WebBaseUrl(HttpRequest req, IConfiguration cfg)
    {
        if (cfg["Web:BaseUrl"] is { Length: > 0 } configured) return configured.TrimEnd('/');
        if (req.Headers.Origin.FirstOrDefault() is { Length: > 0 } origin) return origin.TrimEnd('/');
        if (Uri.TryCreate(req.Headers.Referer.FirstOrDefault(), UriKind.Absolute, out var referer)) return $"{referer.Scheme}://{referer.Authority}";
        return $"{req.Scheme}://{req.Host}";
    }

    public static string NewToken() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    /// <summary>Trip-local "today" for grouping items into days.</summary>
    public static DateOnly TodayIn(string timeZone)
    {
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var z) ? z : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).DateTime);
    }

    public static DateOnly DayIn(DateTimeOffset instant, string timeZone)
    {
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var z) ? z : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, tz).DateTime);
    }
}

/// <summary>Viewer context for visibility filters (spec 08 §8.3), resolved once per request.</summary>
public sealed record Viewer(Guid? UserId, bool IsAdmin, HashSet<Guid> CrewTripIds, HashSet<Guid> MemberShipIds)
{
    public static async Task<Viewer> ForAsync(CurrentUser user, Access access, CancellationToken ct) =>
        new(user.Id, await user.IsAdminAsync(ct), await access.CrewTripIdsAsync(ct), await access.MemberShipIdsAsync(ct));

    // Effective visibility = min(item, trip, ship cap). Crew members see everything non-private of their trips;
    // the public sees items that are Public on all three levels; authors and admins see their own / everything.
    public Expression<Func<Post, bool>> Posts() => p =>
        (UserId != null && p.AuthorId == UserId) || IsAdmin
        || (CrewTripIds.Contains(p.TripId) && p.Visibility != Visibility.Private)
        || (p.Visibility == Visibility.Public && p.Trip.Visibility == Visibility.Public && p.Trip.Ship.Visibility == Visibility.Public);

    public Expression<Func<Media, bool>> Media(Trip trip, Ship ship)
    {
        var tripPublic = trip.Visibility == Visibility.Public && ship.Visibility == Visibility.Public;
        var isCrew = CrewTripIds.Contains(trip.Id);
        return m =>
            (UserId != null && m.OwnerId == UserId) || IsAdmin
            || (isCrew && m.Visibility != Visibility.Private)
            || (tripPublic && m.Visibility == Visibility.Public);
    }

    public Expression<Func<LogbookEntry, bool>> Logbook() => l =>
        (UserId != null && l.AuthorId == UserId) || IsAdmin
        // entries inside a trip: crew of that trip, or public on all levels
        || (l.TripId != null && CrewTripIds.Contains(l.TripId.Value) && l.Visibility != Visibility.Private)
        || (l.TripId != null && l.Visibility == Visibility.Public && l.Trip!.Visibility == Visibility.Public && l.Ship.Visibility == Visibility.Public)
        // entries without trip: ship members, or public on a public ship
        || (l.TripId == null && MemberShipIds.Contains(l.ShipId) && l.Visibility != Visibility.Private)
        || (l.TripId == null && l.Visibility == Visibility.Public && l.Ship.Visibility == Visibility.Public);

    public Expression<Func<Trip, bool>> Trips() => t =>
        IsAdmin || CrewTripIds.Contains(t.Id) || MemberShipIds.Contains(t.ShipId)
        || (t.Visibility == Visibility.Public && t.Ship.Visibility == Visibility.Public);

    public Expression<Func<Ship, bool>> Ships() => s =>
        IsAdmin || MemberShipIds.Contains(s.Id) || s.Visibility == Visibility.Public || s.Trips.Any(t => CrewTripIds.Contains(t.Id));
}

/// <summary>Opaque tokens for calendar feeds (spec 11.4) without extra columns.</summary>
public sealed class FeedTokens(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("sms.calendar");
    public string Create(string kind, Guid id) => _protector.Protect($"{kind}:{id}");
    public (string kind, Guid id)? Parse(string token)
    {
        try
        {
            var parts = _protector.Unprotect(token).Split(':');
            return (parts[0], Guid.Parse(parts[1]));
        }
        catch { return null; }
    }
}
