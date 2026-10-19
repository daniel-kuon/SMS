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

        // tides: nearest station within Tides:MaxStationKm (Baltic harbours often have none that matters, then 204)
        api.MapGet("/harbours/{id:guid}/tides", async (Guid id, SmsDbContext db, ITideProvider tides, IConfiguration cfg, CancellationToken ct) =>
        {
            var h = await db.Harbours.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Location }).FirstOrDefaultAsync(ct);
            if (h is null) return Results.NotFound();
            var fc = await tides.ForecastAsync(h.Location.Y, h.Location.X, cfg.GetValue("Tides:MaxStationKm", 60.0), ct);
            return fc is null ? Results.NoContent() : Results.Ok(fc);
        }).WithTags("Weather");
        g.MapGet("/tides", async (double lat, double lng, ITideProvider tides, IConfiguration cfg, CancellationToken ct) =>
        {
            if (lat is < -90 or > 90 || lng is < -180 or > 180) return Invalid("lat", "Ungültige Position.");
            var fc = await tides.ForecastAsync(lat, lng, cfg.GetValue("Tides:MaxStationKm", 60.0), ct);
            return fc is null ? Results.NoContent() : Results.Ok(fc);
        }).RequireRateLimiting("ingest");

        // weather along a planned route: points every 5 nm with ETAs (tacking when close-hauled) and the forecast at the time the boat gets there
        g.MapPost("/route", async (RouteWeatherRequest r, IWeatherProvider weather, CancellationToken ct) =>
        {
            if (r.Coordinates is not { Length: >= 2 and <= 20_000 } || r.Coordinates.Any(c => c is not { Length: >= 2 } || c[1] is < -90 or > 90 || c[0] is < -180 or > 180)) return Invalid("coordinates", "Ungültige Route.");
            if (r.SpeedKn is < 0.5 or > 40) return Invalid("speedKn", "Geschwindigkeit zwischen 0,5 und 40 kn.");
            var samples = RouteWeather.SampleLine(r.Coordinates.Select(c => (c[1], c[0])).ToList());
            if (samples.Count is 0 or > 101) return Invalid("coordinates", "Route zu kurz oder länger als 500 sm.");
            // one forecast per sample (the provider caches per ~1 km cell), then the ETAs pick the hour
            var forecasts = new WeatherForecast?[samples.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, samples.Count), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                async (i, c) => forecasts[i] = await weather.ForecastAsync(samples[i].Lat, samples[i].Lng, c));
            // nearest hourly value; outside the forecast horizon there is none
            WeatherPoint? At(int i, DateTimeOffset t) => forecasts[i]?.Hourly.MinBy(h => Math.Abs((h.Time - t).Ticks)) is { } h && Math.Abs((h.Time - t).TotalMinutes) <= 31 ? h : null;
            var etas = RouteWeather.Chain(samples, r.Departure, r.SpeedKn, (i, t) => At(i, t)?.WindDirDeg);
            var points = etas.Select((e, i) => { var w = At(i, e.Time); return new RouteWeatherPoint(i + 1, e.Point.Lat, e.Point.Lng, Math.Round(e.Point.DistanceNm, 1), Math.Round(e.Point.HeadingDeg), e.Time, e.Tacking, w is not null, w?.WindKn, w?.WindDirDeg, w?.GustKn, w?.WaveHeightM, w?.WaveDirDeg); }).ToList();
            var arrival = etas[^1].Time;
            return Results.Ok(new { departure = r.Departure, arrival, durationHours = Math.Round((arrival - r.Departure).TotalHours, 2), distanceNm = points[^1].DistanceNm, provider = weather.Name, points });
        }).RequireRateLimiting("ingest");

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

/// <summary>POST /api/weather/route: the route as GeoJSON positions [lng, lat], departure time and boat speed.</summary>
public sealed record RouteWeatherRequest(double[][] Coordinates, DateTimeOffset Departure, double SpeedKn);
/// <summary>A sample point with its ETA; Forecast false when the ETA lies beyond the forecast horizon (or the provider failed).</summary>
public sealed record RouteWeatherPoint(int No, double Lat, double Lng, double DistanceNm, double HeadingDeg, DateTimeOffset Eta, bool Tacking, bool Forecast, double? WindKn, double? WindDirDeg, double? GustKn, double? WaveHeightM, double? WaveDirDeg);
