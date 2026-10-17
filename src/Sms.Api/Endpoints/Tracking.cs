using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Sms.Domain;
using Sms.Domain.Tracking;
using Sms.Infrastructure;
using Sms.Infrastructure.Tracking;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record DeviceCreate(string Name, TrackingProtocol Protocol, Guid? TripId);
public record DeviceDto(Guid Id, string Name, TrackingProtocol Protocol, Guid? TripId, bool Active, DateTimeOffset? LastSeenAt, string Owner);
public record DeviceCreated(Guid Id, string Token, string OsmAndUrl, string OwnTracksUrl, string JsonUrl, string Hint);
public record PrivacyZoneUpsert(string Name, double Lat, double Lng, double RadiusM);
public record PointsBatch(string DeviceToken, List<PointInput> Points);
public record PointInput(DateTimeOffset T, double Lat, double Lon, double? Sog, double? Cog, double? Acc);

/// <summary>Spec 12 §12.7: tracking devices, ingest endpoints (token auth, no cookies), file import, track/position delivery, privacy zones.</summary>
public static class TrackingEndpoints
{
    public static void MapTracking(this IEndpointRouteBuilder api)
    {
        var ships = api.MapGroup("/ships/{shipId:guid}").WithTags("Tracking").RequireAuthorization();

        ships.MapGet("/tracking-devices", async (Guid shipId, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            if (!await access.IsShipMemberAsync(shipId, null, ct)) return Results.Forbid();
            var devices = await db.TrackingDevices.AsNoTracking().Where(d => d.ShipId == shipId).OrderBy(d => d.CreatedAt).ToListAsync(ct);
            var names = await ShipEndpoints.DisplayNames(db, devices.Select(d => d.UserId), ct);
            return Results.Ok(devices.Select(d => new DeviceDto(d.Id, d.Name, d.Protocol, d.TripId, d.Active, d.LastSeenAt, names.GetValueOrDefault(d.UserId, "?"))));
        });

        // Ship members create ship-wide devices; accepted crew may create trip-scoped devices (their phone for one trip).
        ships.MapPost("/tracking-devices", async (Guid shipId, DeviceCreate req, SmsDbContext db, Access access, CurrentUser current, HttpRequest http, CancellationToken ct) =>
        {
            if (req.TripId is { } t && !await db.Trips.AnyAsync(x => x.Id == t && x.ShipId == shipId, ct)) return Invalid("tripId", "Törn gehört nicht zum Schiff.");
            var member = await access.IsShipMemberAsync(shipId, null, ct);
            var crewOfTrip = req.TripId is { } tt && await access.IsCrewAsync(tt, ct);
            if (!member && !crewOfTrip) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Name)) return Invalid("name", "Name fehlt.");
            var token = NewToken();
            var d = new TrackingDevice { ShipId = shipId, UserId = current.RequiredId, TripId = req.TripId, Name = req.Name.Trim(), Protocol = req.Protocol, TokenHash = TrackProcessor.HashToken(token), CreatedById = current.Id };
            db.TrackingDevices.Add(d);
            await db.SaveChangesAsync(ct);
            var baseUrl = $"{http.Scheme}://{http.Host}/api/tracking";
            return Results.Ok(new DeviceCreated(d.Id, token, $"{baseUrl}/osmand?id={token}", $"{baseUrl}/owntracks?token={token}", $"{baseUrl}/points",
                "Traccar Client / OsmAnd / GPSLogger: Server-URL = OsmAnd-URL, Geräte-ID = Token. OwnTracks: HTTP-Modus mit der OwnTracks-URL. Token wird nur einmal angezeigt."));
        });

        ships.MapDelete("/tracking-devices/{id:guid}", async (Guid shipId, Guid id, SmsDbContext db, Access access, CurrentUser current, CancellationToken ct) =>
        {
            var d = await db.TrackingDevices.FirstOrDefaultAsync(x => x.Id == id && x.ShipId == shipId, ct);
            if (d is null) return Results.NotFound();
            if (d.UserId != current.Id && !await access.IsShipOwnerAsync(shipId, ct)) return Results.Forbid();
            d.Active = false; d.TokenHash = "revoked";
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // --- ingest (token auth, rate limited; no cookies involved)
        var ingest = api.MapGroup("/tracking").WithTags("Tracking").RequireRateLimiting("ingest");
        ingest.MapMethods("/osmand", ["GET", "POST"], async (HttpRequest http, SmsDbContext db, TrackProcessor processor, CancellationToken ct) =>
        {
            var q = http.Query.ToDictionary(k => k.Key, k => (string?)k.Value.ToString());
            if (http.HasFormContentType) foreach (var f in await http.ReadFormAsync(ct)) q[f.Key] = f.Value.ToString();
            var token = q.GetValueOrDefault("id") ?? q.GetValueOrDefault("deviceid");
            var device = await DeviceAsync(db, token, ct);
            if (device is null) return Results.Unauthorized();
            if (TrackParsers.ParseOsmAnd(q) is not { } fix) return Invalid("lat", "lat/lon fehlen.");
            await Store(db, processor, device, [fix], ct);
            return Results.Ok();
        });
        ingest.MapPost("/owntracks", async (HttpRequest http, SmsDbContext db, TrackProcessor processor, CancellationToken ct) =>
        {
            var token = http.Query["token"].FirstOrDefault() ?? http.Headers["X-Device-Token"].FirstOrDefault() ?? BasicPassword(http);
            var device = await DeviceAsync(db, token, ct);
            if (device is null) return Results.Unauthorized();
            using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct);
            if (TrackParsers.ParseOwnTracks(doc.RootElement) is { } fix) await Store(db, processor, device, [fix], ct);
            return Results.Json(Array.Empty<object>()); // OwnTracks expects a JSON array response
        });
        ingest.MapPost("/points", async (PointsBatch req, SmsDbContext db, TrackProcessor processor, CancellationToken ct) =>
        {
            var device = await DeviceAsync(db, req.DeviceToken, ct);
            if (device is null) return Results.Unauthorized();
            var added = await Store(db, processor, device, req.Points.Select(p => new TrackFix(p.T, p.Lat, p.Lon, p.Sog, p.Cog, p.Acc)), ct);
            return Results.Ok(new { added });
        });

        // --- file import (GPX / KML / NMEA)
        ships.MapPost("/track-imports", async (Guid shipId, HttpRequest http, SmsDbContext db, Access access, CurrentUser current, TrackProcessor processor, CancellationToken ct) =>
        {
            if (!http.HasFormContentType) return Invalid("file", "multipart/form-data erwartet.");
            var form = await http.ReadFormAsync(ct);
            Guid? tripId = Guid.TryParse(form["tripId"], out var t) ? t : null;
            if (tripId is { } tid0 && !await db.Trips.AnyAsync(x => x.Id == tid0 && x.ShipId == shipId, ct)) return Invalid("tripId", "Törn gehört nicht zum Schiff.");
            var allowed = await access.IsShipMemberAsync(shipId, null, ct) || (tripId is { } tid && await access.IsCrewAsync(tid, ct));
            if (!allowed) return Results.Forbid();
            var results = new List<object>();
            foreach (var file in form.Files)
            {
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                await using var stream = file.OpenReadStream();
                List<TrackFix> fixes; TrackFormat format;
                try
                {
                    (fixes, format) = ext switch
                    {
                        ".gpx" => (TrackParsers.ParseGpx(stream), TrackFormat.Gpx),
                        ".kml" => (TrackParsers.ParseKml(stream), TrackFormat.Kml),
                        ".nmea" or ".txt" or ".log" => (TrackParsers.ParseNmea(stream), TrackFormat.Nmea),
                        _ => throw new NotSupportedException("Unterstützt: .gpx, .kml, .nmea"),
                    };
                }
                catch (Exception ex) { return Invalid("files", $"{file.FileName}: {ex.Message}"); }
                var import = new TrackImport { ShipId = shipId, TripId = tripId, UploadedById = current.RequiredId, FileName = Path.GetFileName(file.FileName), Format = format, PointCount = fixes.Count, CreatedById = current.Id };
                db.TrackImports.Add(import);
                await db.SaveChangesAsync(ct);
                var added = await processor.IngestAsync(db, shipId, null, import.Id, tripId, TrackSource.GpsFile, fixes, ct);
                results.Add(new { import.Id, import.FileName, parsed = fixes.Count, added });
            }
            return Results.Ok(results);
        });

        // --- delivery
        var trips = api.MapGroup("/trips/{tripId:guid}").WithTags("Tracking");
        trips.MapGet("/track", async (Guid tripId, string? detail, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).Where(viewer.Trips()).FirstOrDefaultAsync(t => t.Id == tripId, ct);
            if (trip is null) return Results.NotFound();
            var isCrew = viewer.CrewTripIds.Contains(tripId) || viewer.IsAdmin;
            if (!isCrew && !await access.CanViewAsync(trip.TrackVisibility, trip, trip.Ship, null, ct)) return Results.Ok(new { type = "FeatureCollection", features = Array.Empty<object>() });
            var cutoff = isCrew ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(-trip.PublicPositionDelayMinutes);
            // public viewers only get segments that ended before the delay cutoff (spec 12 §12.5)
            var segments = await db.TrackSegments.AsNoTracking().Where(s => s.TripId == tripId && s.EndTime <= cutoff).OrderBy(s => s.Day).ToListAsync(ct);
            var zones = isCrew ? [] : await db.PrivacyZones.AsNoTracking().Where(z => z.ShipId == trip.ShipId).ToListAsync(ct);
            var legs = await db.PlannedLegs.AsNoTracking().Where(l => l.TripId == tripId && l.RouteGeometry != null).OrderBy(l => l.Order).ToListAsync(ct);
            var features = new List<object>();
            var dayIndex = 0;
            foreach (var s in segments)
            {
                var geom = detail == "coarse" ? s.GeometryCoarse : s.Geometry;
                geom = TrackProcessor.StripZones(geom, zones);
                if (geom is null) continue;
                features.Add(new { type = "Feature", geometry = geom, properties = new { kind = "track", day = s.Day, source = (s.SourceOverride ?? s.Source).ToString(), distanceNm = s.DistanceNm, durationMinutes = s.DurationMinutes, maxSogKn = s.MaxSogKn, avgSogKn = s.AvgSogKn, colourIndex = dayIndex++, logbookEntryId = s.LogbookEntryId, segmentId = s.Id } });
            }
            foreach (var l in legs) features.Add(new { type = "Feature", geometry = l.RouteGeometry, properties = new { kind = "planned", order = l.Order, distanceNm = l.DistanceNm } });
            return Results.Ok(new { type = "FeatureCollection", features });
        });

        trips.MapGet("/position", async (Guid tripId, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).Where(viewer.Trips()).FirstOrDefaultAsync(t => t.Id == tripId, ct);
            if (trip is null) return Results.NotFound();
            var isCrew = viewer.CrewTripIds.Contains(tripId) || viewer.IsAdmin;
            if (!isCrew && !await access.CanViewAsync(trip.TrackVisibility, trip, trip.Ship, null, ct)) return Results.NoContent();
            var cutoff = isCrew ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(-trip.PublicPositionDelayMinutes);
            var p = await db.TrackPoints.AsNoTracking().Where(x => x.TripId == tripId && !x.IsOutlier && !x.IsHidden && x.Time <= cutoff).OrderByDescending(x => x.Time).FirstOrDefaultAsync(ct);
            if (p is null) return Results.NoContent();
            if (!isCrew && await db.PrivacyZones.AsNoTracking().Where(z => z.ShipId == trip.ShipId).AnyAsync(z => z.Center.IsWithinDistance(p.Location, z.RadiusM), ct)) return Results.NoContent();
            return Results.Ok(new { p.Time, lat = p.Location.Y, lng = p.Location.X, p.SogKn, p.CogDeg, source = p.Source.ToString(), delayed = !isCrew && trip.PublicPositionDelayMinutes > 0 });
        });

        api.MapPut("/track-segments/{id:guid}/source", async (Guid id, SourceOverrideRequest req, SmsDbContext db, Access access, TrackProcessor processor, CancellationToken ct) =>
        {
            var s = await db.TrackSegments.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            if (!await access.CanWriteLogbookAsync(s.TripId, ct)) return Results.Forbid();
            s.SourceOverride = req.Source;
            await db.SaveChangesAsync(ct);
            await processor.ProcessTripAsync(s.TripId, ct);
            return Results.NoContent();
        }).RequireAuthorization().WithTags("Tracking");

        // Spec 12 §12.2 step 4: suggested logbook entries from detected harbour stops (crew with "Logbuch schreiben").
        trips.MapGet("/track/suggestions", async (Guid tripId, SmsDbContext db, Access access, CancellationToken ct) =>
        {
            if (!await access.CanWriteLogbookAsync(tripId, ct)) return Results.Forbid();
            var trip = await db.Trips.AsNoTracking().FirstAsync(t => t.Id == tripId, ct);
            var points = await db.TrackPoints.AsNoTracking().Where(p => p.TripId == tripId && !p.IsOutlier).OrderBy(p => p.Time).Select(p => new { p.Time, p.Location }).ToListAsync(ct);
            var fixes = points.Select(p => new TrackFix(p.Time, p.Location.Y, p.Location.X)).ToList();
            var stops = TrackMath.DetectStops(fixes);
            if (stops.Count < 2) return Results.Ok(Array.Empty<object>());
            var entries = await db.LogbookEntries.AsNoTracking().Where(l => l.TripId == tripId).Select(l => new { l.DepartedAt, l.ArrivedAt }).ToListAsync(ct);
            var suggestions = new List<object>();
            for (var i = 1; i < stops.Count; i++)
            {
                var from = stops[i - 1]; var to = stops[i];
                var departed = from.To; var arrived = to.From;
                if (entries.Any(e => e.DepartedAt < arrived && e.ArrivedAt > departed)) continue; // already logged
                var a = Geo.Point(from.Lat, from.Lng); var b = Geo.Point(to.Lat, to.Lng);
                var startHarbour = await db.Harbours.AsNoTracking().Where(h => h.IsActive && h.Location.IsWithinDistance(a, 1500)).OrderBy(h => h.Location.Distance(a)).Select(h => new { h.Id, h.Name }).FirstOrDefaultAsync(ct);
                var endHarbour = await db.Harbours.AsNoTracking().Where(h => h.IsActive && h.Location.IsWithinDistance(b, 1500)).OrderBy(h => h.Location.Distance(b)).Select(h => new { h.Id, h.Name }).FirstOrDefaultAsync(ct);
                var leg = fixes.Where(f => f.Time >= departed && f.Time <= arrived).ToList();
                suggestions.Add(new
                {
                    departedAt = departed, arrivedAt = arrived, startHarbourId = startHarbour?.Id, startHarbourName = startHarbour?.Name, endHarbourId = endHarbour?.Id, endHarbourName = endHarbour?.Name,
                    sailedDistanceNm = Math.Round((decimal)(TrackMath.DistanceM(leg) / TrackMath.Nm), 1), day = DayIn(departed, trip.TimeZone),
                });
            }
            return Results.Ok(suggestions);
        }).RequireAuthorization();

        trips.MapPost("/track/reprocess", async (Guid tripId, Access access, TrackProcessor processor, CancellationToken ct) =>
        {
            if (!await access.CanWriteLogbookAsync(tripId, ct)) return Results.Forbid();
            await processor.ProcessTripAsync(tripId, ct);
            return Results.NoContent();
        }).RequireAuthorization();

        // --- privacy zones (owners)
        ships.MapGet("/privacy-zones", async (Guid shipId, SmsDbContext db, Access access, CancellationToken ct) =>
            await access.IsShipMemberAsync(shipId, null, ct)
                ? Results.Ok((await db.PrivacyZones.AsNoTracking().Where(z => z.ShipId == shipId).Select(z => new { z.Id, z.Name, z.Center, z.RadiusM }).ToListAsync(ct))
                    .Select(z => new { z.Id, z.Name, lat = z.Center.Y, lng = z.Center.X, z.RadiusM })) // geography has no ST_X/ST_Y: read coordinates in memory
                : Results.Forbid());
        ships.MapPost("/privacy-zones", async (Guid shipId, PrivacyZoneUpsert req, SmsDbContext db, Access access, CurrentUser current, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(shipId, ct)) return Results.Forbid();
            var z = new PrivacyZone { ShipId = shipId, Name = req.Name, Center = Geo.Point(req.Lat, req.Lng), RadiusM = Math.Clamp(req.RadiusM, 50, 20_000), CreatedById = current.Id };
            db.PrivacyZones.Add(z);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/ships/{shipId}/privacy-zones/{z.Id}", new { z.Id });
        });
        ships.MapDelete("/privacy-zones/{id:guid}", async (Guid shipId, Guid id, SmsDbContext db, Access access, CancellationToken ct) =>
            await access.IsShipOwnerAsync(shipId, ct) ? (await db.PrivacyZones.Where(z => z.Id == id && z.ShipId == shipId).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : Results.NotFound()) : Results.Forbid());
    }

    public record SourceOverrideRequest(TrackSource? Source);

    private static async Task<TrackingDevice?> DeviceAsync(SmsDbContext db, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = TrackProcessor.HashToken(token.Trim());
        return await db.TrackingDevices.FirstOrDefaultAsync(d => d.TokenHash == hash && d.Active, ct);
    }

    private static async Task<int> Store(SmsDbContext db, TrackProcessor processor, TrackingDevice device, IEnumerable<TrackFix> fixes, CancellationToken ct)
    {
        device.LastSeenAt = DateTimeOffset.UtcNow;
        return await processor.IngestAsync(db, device.ShipId, device.Id, null, device.TripId, TrackSource.GpsLive, fixes, ct);
    }

    private static string? BasicPassword(HttpRequest http)
    {
        var auth = http.Headers.Authorization.FirstOrDefault();
        if (auth is null || !auth.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return null;
        try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..])).Split(':', 2).ElementAtOrDefault(1); } catch { return null; }
    }

}
