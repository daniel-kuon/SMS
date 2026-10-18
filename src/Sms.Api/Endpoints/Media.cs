using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Sms.Domain;
using Sms.Domain.Tracking;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record MediaDto(Guid Id, Guid OwnerId, string Owner, Guid? TripId, Guid? PostId, DateOnly? Day, DateTimeOffset? TakenAt, double? Lat, double? Lng,
    LocationSource? LocationSource, string? Caption, Visibility Visibility, int? Width, int? Height, string ThumbUrl, string PreviewUrl, string OriginalUrl);
public record MediaPick(Guid Id, string ThumbUrl, string Url, string FileName, string? Caption);
public record MediaUpdate(string? Caption, Visibility Visibility, DateOnly? Day, Guid? PostId, double? Lat, double? Lng);

public static class MediaEndpoints
{
    private static readonly HashSet<string> Allowed = ["image/jpeg", "image/png", "image/webp", "image/heic"];

    public static void MapMedia(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/media").WithTags("Media");

        // Spec 07 E3: multi-file upload into a trip; day from EXIF (trip time zone) when inside the trip, else today; location from EXIF, else harbour of that day.
        api.MapPost("/trips/{tripId:guid}/media", async (Guid tripId, HttpRequest http, SmsDbContext db, CurrentUser current, Access access, MediaStorage storage, CancellationToken ct) =>
        {
            if (!await access.IsCrewAsync(tripId, ct)) return Results.Forbid();
            var trip = await db.Trips.AsNoTracking().FirstAsync(t => t.Id == tripId, ct);
            if (!http.HasFormContentType) return Invalid("file", "multipart/form-data erwartet.");
            var form = await http.ReadFormAsync(ct);
            var visibility = Enum.TryParse<Visibility>(form["visibility"], true, out var v) ? v : Visibility.Crew;
            var offset = int.TryParse(form["cameraOffsetMinutes"], out var o) ? o : 0;
            var dayOverride = DateOnly.TryParse(form["day"], out var d) ? d : (DateOnly?)null;
            var results = new List<MediaDto>();
            var names = await ShipEndpoints.DisplayNames(db, [current.RequiredId], ct);
            foreach (var file in form.Files)
            {
                if (!Allowed.Contains(file.ContentType) || file.Length == 0 || file.Length > 50_000_000)
                    return Invalid("files", $"Dieser Dateityp wird nicht unterstützt: {file.FileName}");
                await using var stream = file.OpenReadStream();
                var processed = await storage.StorePhotoAsync(stream, ct);
                var media = new Media
                {
                    OwnerId = current.RequiredId, TripId = tripId, ShipId = trip.ShipId, Kind = MediaKind.Photo, StorageKey = processed.StorageKey,
                    OriginalFileName = Path.GetFileName(file.FileName), ContentType = "image/jpeg", SizeBytes = file.Length, Width = processed.Width, Height = processed.Height,
                    Visibility = visibility, ProcessingStatus = ProcessingStatus.Done, CreatedById = current.Id, Caption = form["caption"].FirstOrDefault(),
                };
                if (processed.TakenAt is { } taken)
                {
                    // EXIF time is local camera time; interpret it in the trip time zone, then apply the user's camera offset.
                    var tz = TimeZoneInfo.TryFindSystemTimeZoneById(trip.TimeZone, out var z) ? z : TimeZoneInfo.Utc;
                    var local = taken.DateTime.AddMinutes(offset);
                    media.TakenAt = new DateTimeOffset(local, tz.GetUtcOffset(local));
                }
                var exifDay = media.TakenAt is { } ta ? DayIn(ta, trip.TimeZone) : (DateOnly?)null;
                media.Day = dayOverride ?? (exifDay is { } ed && ed >= trip.StartDate && ed <= trip.EndDate ? ed : TodayIn(trip.TimeZone));
                if (processed.Location is not null) { media.Location = processed.Location; media.LocationSource = LocationSource.Exif; }
                else if (media.TakenAt is { } takenAt && await TrackLocationAsync(db, tripId, takenAt, ct) is { } onTrack) { media.Location = onTrack; media.LocationSource = LocationSource.Track; }
                else
                {
                    var entry = await db.LogbookEntries.AsNoTracking().Include(l => l.EndHarbour).Where(l => l.TripId == tripId).OrderByDescending(l => l.ArrivedAt).FirstOrDefaultAsync(ct);
                    if (entry is not null && DayIn(entry.ArrivedAt, trip.TimeZone) <= media.Day) { media.Location = entry.EndHarbour.Location; media.LocationSource = LocationSource.Harbour; }
                }
                db.Media.Add(media);
                results.Add(ToDto(media, names));
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(results);
        }).RequireAuthorization().WithTags("Media");

        api.MapPost("/harbours/{harbourId:guid}/photos", async (Guid harbourId, HttpRequest http, SmsDbContext db, CurrentUser current, MediaStorage storage, CancellationToken ct) =>
        {
            if (!await db.Harbours.AnyAsync(h => h.Id == harbourId, ct)) return Results.NotFound();
            if (!http.HasFormContentType) return Invalid("file", "multipart/form-data erwartet.");
            var form = await http.ReadFormAsync(ct);
            var ids = new List<Guid>();
            foreach (var file in form.Files)
            {
                if (!Allowed.Contains(file.ContentType)) return Invalid("files", "Dieser Dateityp wird nicht unterstützt.");
                await using var stream = file.OpenReadStream();
                var p = await storage.StorePhotoAsync(stream, ct);
                var media = new Media { OwnerId = current.RequiredId, HarbourId = harbourId, Kind = MediaKind.Photo, StorageKey = p.StorageKey, OriginalFileName = file.FileName, ContentType = "image/jpeg",
                    SizeBytes = file.Length, Width = p.Width, Height = p.Height, Visibility = Visibility.Public, ProcessingStatus = ProcessingStatus.Done, Day = DateOnly.FromDateTime(DateTime.UtcNow), CreatedById = current.Id };
                db.Media.Add(media); ids.Add(media.Id);
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(ids.Select(i => $"/api/media/{i}/thumb"));
        }).RequireAuthorization().WithTags("Media");

        api.MapPost("/me/avatar", async (HttpRequest http, SmsDbContext db, CurrentUser current, MediaStorage storage, CancellationToken ct) =>
        {
            if (!http.HasFormContentType) return Invalid("file", "multipart/form-data erwartet.");
            var form = await http.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || !Allowed.Contains(file.ContentType)) return Invalid("file", "Bild fehlt oder Dateityp nicht unterstützt.");
            await using var stream = file.OpenReadStream();
            var p = await storage.StorePhotoAsync(stream, ct);
            var media = new Media { OwnerId = current.RequiredId, Kind = MediaKind.Avatar, StorageKey = p.StorageKey, OriginalFileName = file.FileName, ContentType = "image/jpeg", SizeBytes = file.Length,
                Width = p.Width, Height = p.Height, Visibility = Visibility.Public, ProcessingStatus = ProcessingStatus.Done };
            db.Media.Add(media);
            await db.Profiles.Where(x => x.UserId == current.RequiredId).ExecuteUpdateAsync(s => s.SetProperty(x => x.AvatarMediaId, media.Id), ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { url = $"/api/media/{media.Id}/thumb" });
        }).RequireAuthorization().WithTags("Media");

        api.MapPost("/ships/{shipId:guid}/photo", async (Guid shipId, HttpRequest http, SmsDbContext db, CurrentUser current, Access access, MediaStorage storage, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(shipId, ct)) return Results.Forbid();
            if (!http.HasFormContentType) return Invalid("file", "multipart/form-data erwartet.");
            var form = await http.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || !Allowed.Contains(file.ContentType)) return Invalid("file", "Bild fehlt oder Dateityp nicht unterstützt.");
            await using var stream = file.OpenReadStream();
            var p = await storage.StorePhotoAsync(stream, ct);
            var media = new Media { OwnerId = current.RequiredId, ShipId = shipId, Kind = MediaKind.Photo, StorageKey = p.StorageKey, OriginalFileName = file.FileName, ContentType = "image/jpeg", SizeBytes = file.Length,
                Width = p.Width, Height = p.Height, Visibility = Visibility.Public, ProcessingStatus = ProcessingStatus.Done };
            db.Media.Add(media);
            await db.Ships.Where(s => s.Id == shipId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PhotoMediaId, media.Id), ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { url = $"/api/media/{media.Id}/preview" });
        }).RequireAuthorization().WithTags("Media");

        // Rich editor image picker: photos of the edited content's level (ship / trip / logbook entry / post) plus the caller's own uploads.
        g.MapGet("", async (Guid? shipId, Guid? tripId, Guid? logbookEntryId, Guid? postId, bool? mine, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            async Task<bool> CanUseTrip(Guid t) => viewer.CrewTripIds.Contains(t) || await db.Trips.AnyAsync(x => x.Id == t && viewer.MemberShipIds.Contains(x.ShipId), ct);
            if (!viewer.IsAdmin)
            {
                if (shipId is { } s && !viewer.MemberShipIds.Contains(s)) return Results.Forbid();
                if (tripId is { } t && !await CanUseTrip(t)) return Results.Forbid();
                if (logbookEntryId is { } l)
                {
                    var e = await db.LogbookEntries.AsNoTracking().Where(x => x.Id == l).Select(x => new { x.ShipId, x.TripId }).FirstOrDefaultAsync(ct);
                    if (e is null) return Results.NotFound();
                    if (!(e.TripId is { } et ? await CanUseTrip(et) : viewer.MemberShipIds.Contains(e.ShipId))) return Results.Forbid();
                }
                if (postId is { } p)
                {
                    var pt = await db.Posts.AsNoTracking().Where(x => x.Id == p).Select(x => (Guid?)x.TripId).FirstOrDefaultAsync(ct);
                    if (pt is null) return Results.NotFound();
                    if (!await CanUseTrip(pt.Value)) return Results.Forbid();
                }
            }
            var uid = current.RequiredId;
            var items = await db.Media.AsNoTracking()
                .Where(m => m.Kind == MediaKind.Photo && (m.OwnerId == uid || m.Visibility != Visibility.Private))
                .Where(m => (mine == true && m.OwnerId == uid) || (shipId != null && m.ShipId == shipId) || (tripId != null && m.TripId == tripId)
                            || (logbookEntryId != null && m.LogbookEntryId == logbookEntryId) || (postId != null && m.PostId == postId))
                .OrderByDescending(m => m.CreatedAt).Take(200).Select(m => new { m.Id, m.OriginalFileName, m.Caption }).ToListAsync(ct);
            return Results.Ok(items.Select(m => new MediaPick(m.Id, $"/api/media/{m.Id}/thumb", $"/api/media/{m.Id}/preview", m.OriginalFileName, m.Caption)));
        }).RequireAuthorization();

        // Rich editor upload outside a trip (ship pages/description: shipId; admin content pages: own upload only). Trips use /trips/{id}/media.
        g.MapPost("", async (HttpRequest http, SmsDbContext db, CurrentUser current, Access access, MediaStorage storage, CancellationToken ct) =>
        {
            if (!http.HasFormContentType) return Invalid("file", "multipart/form-data erwartet.");
            var form = await http.ReadFormAsync(ct);
            Guid? shipId = Guid.TryParse(form["shipId"], out var sid) ? sid : null;
            if (shipId is { } s && !await access.IsShipMemberAsync(s, null, ct) && !await current.IsAdminAsync(ct)) return Results.Forbid();
            var visibility = Enum.TryParse<Visibility>(form["visibility"], true, out var v) ? v : Visibility.Public;
            var names = await ShipEndpoints.DisplayNames(db, [current.RequiredId], ct);
            var results = new List<MediaDto>();
            foreach (var file in form.Files)
            {
                if (!Allowed.Contains(file.ContentType) || file.Length == 0 || file.Length > 50_000_000)
                    return Invalid("files", $"Dieser Dateityp wird nicht unterstützt: {file.FileName}");
                await using var stream = file.OpenReadStream();
                var p = await storage.StorePhotoAsync(stream, ct);
                var media = new Media { OwnerId = current.RequiredId, ShipId = shipId, Kind = MediaKind.Photo, StorageKey = p.StorageKey, OriginalFileName = Path.GetFileName(file.FileName), ContentType = "image/jpeg",
                    SizeBytes = file.Length, Width = p.Width, Height = p.Height, Visibility = visibility, ProcessingStatus = ProcessingStatus.Done, CreatedById = current.Id };
                db.Media.Add(media);
                results.Add(ToDto(media, names));
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(results);
        }).RequireAuthorization();

        // Authorised delivery (spec 11.2): visibility is checked per file.
        g.MapGet("/{id:guid}/{variant:regex(^(thumb|preview|original)$)}", async (Guid id, string variant, SmsDbContext db, CurrentUser current, Access access, MediaStorage storage, HttpResponse response, CancellationToken ct) =>
        {
            var m = await db.Media.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) return Results.NotFound();
            if (!await CanViewAsync(m, db, current, access, ct)) return current.IsAuthenticated ? Results.Forbid() : Results.Unauthorized();
            var path = storage.PathFor(m.StorageKey, variant);
            if (!File.Exists(path)) return Results.NotFound();
            response.Headers.CacheControl = m.Visibility == Visibility.Public ? "public,max-age=86400" : "private,max-age=3600";
            return Results.File(path, "image/jpeg");
        });

        g.MapPut("/{id:guid}", async (Guid id, MediaUpdate req, SmsDbContext db, CurrentUser current, CancellationToken ct) =>
        {
            var m = await db.Media.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) return Results.NotFound();
            if (m.OwnerId != current.Id && !await current.IsAdminAsync(ct)) return Results.Forbid();
            m.Caption = req.Caption; m.Visibility = req.Visibility; if (req.Day is { } d) m.Day = d; m.PostId = req.PostId;
            if (req.Lat is { } lat && req.Lng is { } lng) { m.Location = Geo.Point(lat, lng); m.LocationSource = LocationSource.Manual; }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapDelete("/{id:guid}", async (Guid id, SmsDbContext db, CurrentUser current, Access access, MediaStorage storage, CancellationToken ct) =>
        {
            var m = await db.Media.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) return Results.NotFound();
            var allowed = m.OwnerId == current.Id || await current.IsAdminAsync(ct) || (m.TripId is { } t && await access.IsTripSkipperAsync(t, ct));
            if (!allowed) return Results.Forbid();
            m.DeletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            storage.Delete(m.StorageKey);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static async Task<Point?> TrackLocationAsync(SmsDbContext db, Guid tripId, DateTimeOffset at, CancellationToken ct)
    {
        var from = at.AddMinutes(-30); var to = at.AddMinutes(30);
        var points = await db.TrackPoints.AsNoTracking().Where(p => p.TripId == tripId && !p.IsOutlier && !p.IsHidden && p.Time >= from && p.Time <= to).OrderBy(p => p.Time)
            .Select(p => new { p.Time, p.Location }).ToListAsync(ct);
        var fixes = points.Select(p => new TrackFix(p.Time, p.Location.Y, p.Location.X)).ToList();
        return TrackMath.Interpolate(fixes, at, TimeSpan.FromMinutes(30)) is { } r ? Geo.Point(r.lat, r.lng) : null;
    }

    private static async Task<bool> CanViewAsync(Media m, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct)
    {
        if (m.OwnerId == current.Id || await current.IsAdminAsync(ct)) return true;
        if (m.TripId is { } tripId)
        {
            var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).FirstAsync(t => t.Id == tripId, ct);
            return await access.CanViewAsync(m.Visibility, trip, trip.Ship, m.OwnerId, ct);
        }
        return m.Visibility == Visibility.Public || (m.ShipId is { } s && await access.IsShipMemberAsync(s, null, ct));
    }

    internal static MediaDto ToDto(Media m, Dictionary<Guid, string> names) => new(
        m.Id, m.OwnerId, names.GetValueOrDefault(m.OwnerId, "?"), m.TripId, m.PostId, m.Day, m.TakenAt, m.Location?.Y, m.Location?.X, m.LocationSource, m.Caption, m.Visibility,
        m.Width, m.Height, $"/api/media/{m.Id}/thumb", $"/api/media/{m.Id}/preview", $"/api/media/{m.Id}/original");
}
