using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;

namespace Sms.Api;

/// <summary>The signed-in user (or anonymous) for the current request.</summary>
public sealed class CurrentUser(IHttpContextAccessor http, SmsDbContext db)
{
    private UserProfile? _profile;
    private bool _profileLoaded;

    public ClaimsPrincipal Principal => http.HttpContext?.User ?? new ClaimsPrincipal();
    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;
    public Guid? Id => Guid.TryParse(Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public Guid RequiredId => Id ?? throw new UnauthorizedAccessException();

    public async Task<UserProfile?> ProfileAsync(CancellationToken ct = default)
    {
        if (_profileLoaded) return _profile;
        _profileLoaded = true;
        if (Id is { } id) _profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == id, ct);
        return _profile;
    }

    public async Task<bool> IsAdminAsync(CancellationToken ct = default) => (await ProfileAsync(ct))?.IsAdmin == true;
}

/// <summary>
/// Permission checks of spec 08 §8.4, evaluated against the database.
/// ponytail: one scoped service instead of ASP.NET policy/resource handlers — same rules, far less plumbing.
/// </summary>
public sealed class Access(SmsDbContext db, CurrentUser user)
{
    public async Task<bool> IsShipMemberAsync(Guid shipId, ShipRole? role = null, CancellationToken ct = default)
    {
        if (user.Id is not { } uid) return false;
        return await db.ShipMembers.AnyAsync(m => m.ShipId == shipId && m.UserId == uid && (role == null || m.Role == role), ct);
    }

    public Task<bool> IsShipOwnerAsync(Guid shipId, CancellationToken ct = default) => IsShipMemberAsync(shipId, ShipRole.Owner, ct);

    /// <summary>Accepted crew member record of the current user in a trip, or null.</summary>
    public async Task<CrewMember?> CrewAsync(Guid tripId, CancellationToken ct = default)
    {
        if (user.Id is not { } uid) return null;
        return await db.CrewMembers.AsNoTracking().FirstOrDefaultAsync(c => c.TripId == tripId && c.UserId == uid && c.Status == CrewStatus.Accepted, ct);
    }

    public async Task<bool> IsCrewAsync(Guid tripId, CancellationToken ct = default) => await CrewAsync(tripId, ct) is not null;

    public async Task<bool> IsTripSkipperAsync(Guid tripId, CancellationToken ct = default) =>
        await CrewAsync(tripId, ct) is { Role: CrewRole.Skipper };

    public async Task<bool> CanWriteLogbookAsync(Guid tripId, CancellationToken ct = default) =>
        await CrewAsync(tripId, ct) is { } c && (c.Role == CrewRole.Skipper || c.CanWriteLogbook);

    /// <summary>Ship owners and ship skippers may create trips and write entries without trip.</summary>
    public async Task<bool> CanCreateTripAsync(Guid shipId, CancellationToken ct = default) => await IsShipMemberAsync(shipId, null, ct);

    /// <summary>Whether the current viewer may see an item with the given raw visibility inside a trip (spec 08 §8.3).</summary>
    public async Task<bool> CanViewAsync(Visibility itemVisibility, Trip trip, Ship ship, Guid? authorId, CancellationToken ct = default)
    {
        var effective = VisibilityRules.Effective(itemVisibility, trip.Visibility, ship.Visibility);
        return VisibilityRules.CanView(effective, authorId is not null && authorId == user.Id, await IsCrewAsync(trip.Id, ct), await user.IsAdminAsync(ct));
    }

    /// <summary>Ids of trips in which the current user is accepted crew (for query filters).</summary>
    public async Task<HashSet<Guid>> CrewTripIdsAsync(CancellationToken ct = default)
    {
        if (user.Id is not { } uid) return [];
        return (await db.CrewMembers.Where(c => c.UserId == uid && c.Status == CrewStatus.Accepted).Select(c => c.TripId).ToListAsync(ct)).ToHashSet();
    }

    public async Task<HashSet<Guid>> MemberShipIdsAsync(CancellationToken ct = default)
    {
        if (user.Id is not { } uid) return [];
        return (await db.ShipMembers.Where(m => m.UserId == uid).Select(m => m.ShipId).ToListAsync(ct)).ToHashSet();
    }
}
