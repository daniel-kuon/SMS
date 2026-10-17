using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

/// <summary>Weather: forecasts for positions and harbours, observations recorded along live tracks, raster overlay tiles.</summary>
public static class WeatherEndpoints
{
    public static void MapWeather(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/weather").WithTags("Weather");

        g.MapGet("", async (double lat, double lng, IWeatherProvider weather, CancellationToken ct) =>
        {
            if (lat is < -90 or > 90 || lng is < -180 or > 180) return Invalid("lat", "Ungültige Position.");
            var fc = await weather.ForecastAsync(lat, lng, ct);
            return fc is null ? Results.Problem(statusCode: 502, title: "Wetterdienst nicht erreichbar.") : Results.Ok(fc);
        }).RequireRateLimiting("ingest");

        api.MapGet("/harbours/{id:guid}/weather", async (Guid id, SmsDbContext db, IWeatherProvider weather, CancellationToken ct) =>
        {
            var h = await db.Harbours.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Location }).FirstOrDefaultAsync(ct);
            if (h is null) return Results.NotFound();
            var fc = await weather.ForecastAsync(h.Location.Y, h.Location.X, ct);
            return fc is null ? Results.Problem(statusCode: 502, title: "Wetterdienst nicht erreichbar.") : Results.Ok(fc);
        }).WithTags("Weather");

        // observations recorded at live positions (same visibility as the track)
        api.MapGet("/trips/{tripId:guid}/weather", async (Guid tripId, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            var trip = await db.Trips.AsNoTracking().Include(t => t.Ship).Where(viewer.Trips()).FirstOrDefaultAsync(t => t.Id == tripId, ct);
            if (trip is null) return Results.NotFound();
            var isCrew = viewer.CrewTripIds.Contains(tripId) || viewer.IsAdmin;
            if (!isCrew && !await access.CanViewAsync(trip.TrackVisibility, trip, trip.Ship, null, ct)) return Results.Ok(Array.Empty<object>());
            var cutoff = isCrew ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(-trip.PublicPositionDelayMinutes);
            var rows = await db.WeatherObservations.AsNoTracking().Where(o => o.TripId == tripId && o.Time <= cutoff).OrderBy(o => o.Time)
                .Select(o => new { o.Id, o.Time, o.Location, o.TemperatureC, o.WindKn, o.WindDirDeg, o.GustKn, o.PressureHpa, o.PrecipitationMm, o.WeatherCode, o.CloudCover, o.WaveHeightM, o.WaveDirDeg, o.WavePeriodS }).ToListAsync(ct);
            // like the track (spec 12 §12.5): positions inside the ship's privacy zones are not shown to non-crew viewers
            if (!isCrew)
            {
                var zones = await db.PrivacyZones.AsNoTracking().Where(z => z.ShipId == trip.ShipId).Select(z => new { z.Center, z.RadiusM }).ToListAsync(ct);
                rows = rows.Where(o => !zones.Any(z => Sms.Domain.Tracking.TrackMath.Haversine(o.Location.Y, o.Location.X, z.Center.Y, z.Center.X) <= z.RadiusM)).ToList();
            }
            return Results.Ok(rows.Select(o => new { o.Id, o.Time, lat = o.Location.Y, lng = o.Location.X, o.TemperatureC, o.WindKn, o.WindDirDeg, o.GustKn, o.PressureHpa, o.PrecipitationMm, o.WeatherCode, o.CloudCover, o.WaveHeightM, o.WaveDirDeg, o.WavePeriodS }));
        }).WithTags("Weather");

        // crew only: condensed observations of a time window, to prefill a logbook entry (wind, weather, sea state)
        api.MapGet("/trips/{tripId:guid}/weather/summary", async (Guid tripId, DateTimeOffset from, DateTimeOffset to, SmsDbContext db, CurrentUser current, Access access, CancellationToken ct) =>
        {
            var viewer = await Viewer.ForAsync(current, access, ct);
            if (!viewer.CrewTripIds.Contains(tripId) && !viewer.IsAdmin) return Results.Forbid();
            if (to < from) return Invalid("to", "Zeitraum endet vor seinem Anfang.");
            var obs = await db.WeatherObservations.AsNoTracking().Where(o => o.TripId == tripId && o.Time >= from && o.Time <= to).OrderBy(o => o.Time).ToListAsync(ct);
            return WeatherSummary.From(obs) is { } summary ? Results.Ok(summary) : Results.NoContent();
        }).WithTags("Weather");

        g.MapGet("/overlay/layers", (IWeatherOverlayProvider overlay) => Results.Ok(new { provider = overlay.Name, configured = overlay.Configured, layers = overlay.Layers, mapsGl = overlay.MapsGl }));

        // tiles are proxied so the provider key stays on the server; short browser cache, provider-side 10-minute buckets
        g.MapGet("/overlay/{layer}/{z:int}/{x:int}/{y:int}.png", async (string layer, int z, int x, int y, IWeatherOverlayProvider overlay, HttpResponse response, CancellationToken ct) =>
        {
            if (!overlay.Configured) return Results.NotFound();
            var bytes = await overlay.TileAsync(layer, z, x, y, ct);
            if (bytes is null) return Results.NotFound();
            response.Headers.CacheControl = "public,max-age=300";
            return Results.Bytes(bytes, "image/png");
        });
    }
}
