using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Sms.Domain;
using Sms.Domain.Tracking;

namespace Sms.Infrastructure.Tracking;

/// <summary>
/// Spec 12 §12.2: stores incoming fixes, assigns them to trips, and (debounced) rebuilds the per-day
/// <see cref="TrackSegment"/>s of a trip with source priority GPS &gt; AIS &gt; calculated legs.
/// </summary>
public sealed class TrackProcessor(IServiceScopeFactory scopes, ILogger<TrackProcessor> logger)
{
    private static readonly ActivitySource Activity = new("Sms");
    private readonly Dictionary<Guid, CancellationTokenSource> _pending = [];
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(20);

    public static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Stores fixes for a device/import; returns how many were new. Points inside the trip date range of an active/completed trip are assigned to it.</summary>
    public async Task<int> IngestAsync(SmsDbContext db, Guid shipId, Guid? deviceId, Guid? importId, Guid? forcedTripId, TrackSource source, IEnumerable<TrackFix> fixes, CancellationToken ct)
    {
        using var span = Activity.StartActivity("tracking.ingest");
        var clean = TrackMath.Clean(fixes);
        if (clean.Count == 0) return 0;
        var trips = await db.Trips.AsNoTracking().Where(t => t.ShipId == shipId && (t.Status == TripStatus.Active || t.Status == TripStatus.Completed || t.Status == TripStatus.Planned))
            .Select(t => new { t.Id, t.StartDate, t.EndDate, t.TimeZone }).ToListAsync(ct);
        var from = clean[0].Time; var to = clean[^1].Time;
        var existing = (await db.TrackPoints.Where(p => p.ShipId == shipId && p.DeviceId == deviceId && p.Time >= from && p.Time <= to).Select(p => p.Time).ToListAsync(ct)).ToHashSet();
        var touched = new HashSet<Guid>();
        var added = 0;
        foreach (var f in clean)
        {
            if (deviceId is not null && existing.Contains(f.Time)) continue;
            Guid? tripId = forcedTripId;
            if (tripId is null)
                foreach (var t in trips)
                {
                    var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(f.Time, TimeZoneInfo.TryFindSystemTimeZoneById(t.TimeZone, out var z) ? z : TimeZoneInfo.Utc).DateTime);
                    if (day >= t.StartDate && day <= t.EndDate) { tripId = t.Id; break; }
                }
            db.TrackPoints.Add(new TrackPoint { ShipId = shipId, TripId = tripId, Source = source, DeviceId = deviceId, ImportId = importId, Time = f.Time, Location = Geo.Point(f.Lat, f.Lng), SogKn = f.SogKn, CogDeg = f.CogDeg, AccuracyM = f.AccuracyM });
            if (tripId is { } tid) touched.Add(tid);
            added++;
        }
        await db.SaveChangesAsync(ct);
        span?.SetTag("tracking.points", added);
        foreach (var t in touched) Schedule(t);
        return added;
    }

    /// <summary>Debounced rebuild of a trip's segments (ingest batches arrive every few seconds).</summary>
    public void Schedule(Guid tripId)
    {
        lock (_pending)
        {
            if (_pending.TryGetValue(tripId, out var old)) old.Cancel();
            var cts = new CancellationTokenSource();
            _pending[tripId] = cts;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(Debounce, cts.Token); await ProcessTripAsync(tripId, CancellationToken.None); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { logger.LogError(ex, "Track processing for trip {TripId} failed", tripId); }
            });
        }
    }

    public async Task ProcessTripAsync(Guid tripId, CancellationToken ct)
    {
        using var span = Activity.StartActivity("tracking.process");
        span?.SetTag("trip.id", tripId);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var trip = await db.Trips.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tripId, ct);
        if (trip is null) return;
        var tz = TimeZoneInfo.TryFindSystemTimeZoneById(trip.TimeZone, out var z) ? z : TimeZoneInfo.Utc;
        var points = await db.TrackPoints.AsNoTracking().Where(p => p.TripId == tripId && !p.IsOutlier && !p.IsHidden).OrderBy(p => p.Time).ToListAsync(ct);
        var entries = await db.LogbookEntries.AsNoTracking().Include(l => l.StartHarbour).Include(l => l.EndHarbour).Where(l => l.TripId == tripId).OrderBy(l => l.DepartedAt).ToListAsync(ct);
        var overrides = await db.TrackSegments.AsNoTracking().Where(s => s.TripId == tripId && s.SourceOverride != null).ToDictionaryAsync(s => s.Day, s => s.SourceOverride!.Value, ct);
        await db.TrackSegments.Where(s => s.TripId == tripId).ExecuteDeleteAsync(ct);

        var byDay = points.GroupBy(p => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(p.Time, tz).DateTime)).ToDictionary(g => g.Key, g => g.ToList());
        var days = byDay.Keys.Union(entries.Select(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.DepartedAt, tz).DateTime))).OrderBy(d => d);
        foreach (var day in days)
        {
            var dayEntries = entries.Where(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.DepartedAt, tz).DateTime) == day).ToList();
            var dayPoints = byDay.GetValueOrDefault(day) ?? [];
            var recorded = dayPoints.Count >= 2 ? Covered(dayPoints, dayEntries) : false;
            var wanted = overrides.GetValueOrDefault(day, recorded ? dayPoints[0].Source : TrackSource.Calculated);
            if (wanted != TrackSource.Calculated && dayPoints.Count >= 2)
            {
                var fixes = dayPoints.Select(p => new TrackFix(p.Time, p.Location.Y, p.Location.X, p.SogKn, p.CogDeg, p.AccuracyM)).ToList();
                var sources = dayPoints.Select(p => p.Source).Distinct().ToList();
                var sogs = fixes.Where(f => f.SogKn is > 0).Select(f => f.SogKn!.Value).ToList();
                db.TrackSegments.Add(new TrackSegment
                {
                    TripId = tripId, Day = day, LogbookEntryId = dayEntries.FirstOrDefault()?.Id,
                    Source = sources.Count == 1 ? sources[0] : TrackSource.Mixed, SourceOverride = overrides.GetValueOrDefault(day) is var o && overrides.ContainsKey(day) ? o : null,
                    StartTime = fixes[0].Time, EndTime = fixes[^1].Time, DistanceNm = Math.Round((decimal)(TrackMath.DistanceM(fixes) / TrackMath.Nm), 1),
                    DurationMinutes = (int)(fixes[^1].Time - fixes[0].Time).TotalMinutes, MaxSogKn = sogs.Count > 0 ? sogs.Max() : null, AvgSogKn = sogs.Count > 0 ? sogs.Average() : null,
                    Geometry = TrackMath.ToLine(Geo.Factory, fixes, 0.0001), GeometryCoarse = TrackMath.ToLine(Geo.Factory, fixes, 0.002), PointCount = fixes.Count,
                });
            }
            else
            {
                foreach (var e in dayEntries)
                {
                    var line = e.CalculatedRoute ?? Geo.Factory.CreateLineString([e.StartHarbour.Location.Coordinate, e.EndHarbour.Location.Coordinate]);
                    db.TrackSegments.Add(new TrackSegment
                    {
                        TripId = tripId, Day = day, LogbookEntryId = e.Id, Source = TrackSource.Calculated, SourceOverride = overrides.ContainsKey(day) ? overrides[day] : null,
                        StartTime = e.DepartedAt, EndTime = e.ArrivedAt, DistanceNm = e.CalculatedDistanceNm ?? e.SailedDistanceNm ?? 0, DurationMinutes = (int)e.Duration.TotalMinutes,
                        Geometry = line, GeometryCoarse = line, PointCount = line.NumPoints,
                    });
                }
            }
        }
        await db.SaveChangesAsync(ct);
        await PlaceMediaAsync(db, tripId, points, ct);
    }

    /// <summary>Spec 12 §12.2: a recorded source covers a day when it spans ≥ 80 % of the logged time without gaps &gt; 30 min (or there is no logbook entry).</summary>
    private static bool Covered(List<TrackPoint> dayPoints, List<LogbookEntry> dayEntries)
    {
        for (var i = 1; i < dayPoints.Count; i++) if ((dayPoints[i].Time - dayPoints[i - 1].Time) > TimeSpan.FromMinutes(30)) return false;
        if (dayEntries.Count == 0) return true;
        var logged = dayEntries.Sum(e => e.Duration.TotalMinutes);
        var covered = dayEntries.Sum(e => Math.Max(0, (Min(e.ArrivedAt, dayPoints[^1].Time) - Max(e.DepartedAt, dayPoints[0].Time)).TotalMinutes));
        return logged <= 0 || covered / logged >= 0.8;
    }

    /// <summary>Spec 12 §12.4 #4: media and posts without a fixed location are placed on the track at their capture time.</summary>
    private static async Task PlaceMediaAsync(SmsDbContext db, Guid tripId, List<TrackPoint> points, CancellationToken ct)
    {
        if (points.Count == 0) return;
        var fixes = points.Select(p => new TrackFix(p.Time, p.Location.Y, p.Location.X)).ToList();
        var media = await db.Media.Where(m => m.TripId == tripId && m.TakenAt != null && (m.LocationSource == null || m.LocationSource == LocationSource.Track || m.LocationSource == LocationSource.Harbour)).ToListAsync(ct);
        foreach (var m in media)
            if (TrackMath.Interpolate(fixes, m.TakenAt!.Value, TimeSpan.FromMinutes(30)) is { } p) { m.Location = Geo.Point(p.lat, p.lng); m.LocationSource = LocationSource.Track; }
        var posts = await db.Posts.Where(p => p.TripId == tripId && (p.LocationSource == null || p.LocationSource == LocationSource.Track || p.LocationSource == LocationSource.Harbour)).ToListAsync(ct);
        foreach (var post in posts)
            if (TrackMath.Interpolate(fixes, post.PublishedAt, TimeSpan.FromMinutes(30)) is { } p) { post.Location = Geo.Point(p.lat, p.lng); post.LocationSource = LocationSource.Track; }
        await db.SaveChangesAsync(ct);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
}
