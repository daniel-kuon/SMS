using System.Data;
using System.Diagnostics;
using System.IO.Compression;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using NpgsqlTypes;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;
using Sms.Domain.Tracking;
using Sms.Infrastructure;
using Sms.Infrastructure.Tracking;

namespace Sms.Api;

public record TrackImportDto(Guid Id, string FileName, TrackImportStatus Status, int ProgressPercent, int Parsed, int Added, int Duplicates, int Rejected,
    int Tracks, int Waypoints, int Routes, string? Error, Guid? TripId, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt)
{
    public static TrackImportDto From(TrackImport i) => new(i.Id, i.FileName, i.Status, i.ProgressPercent, i.PointCount, i.Added, i.Duplicates, i.Rejected,
        i.Tracks, i.Waypoints, i.Routes, i.Error, i.TripId, i.CreatedAt, i.FinishedAt);
}

/// <summary>
/// Imports uploaded track files in the background (one at a time): GPX is streamed with an XmlReader, fixes go to the database in
/// batches through binary COPY. Fixes the ship already has within ±1 s are skipped, so importing the same archive twice adds nothing.
/// Fixes inside a trip's date range go to that trip (same rule as live ingest), the others stay on the ship (TripId null) and show up
/// as voyages without a trip. GPX waypoints (TimeZero events) and routes are counted, not imported.
/// </summary>
public sealed class TrackImportJob(IServiceScopeFactory scopes, TrackProcessor processor, IMemoryCache cache, IConfiguration cfg, ILogger<TrackImportJob> logger) : BackgroundService
{
    public static readonly string[] Extensions = [".gpx", ".kml", ".nmea", ".txt", ".log", ".zip", ".gz", ".7z"];
    public const long MaxUploadBytes = 1L << 30;
    private const int BatchSize = 20_000;
    /// <summary>A batch covers at most this time span, so the duplicate lookup stays a narrow index range even for unsorted archives.</summary>
    private static readonly TimeSpan BatchSpan = TimeSpan.FromDays(7);
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();

    private string Root => cfg["Tracking:ImportPath"] ?? Path.Combine(Path.GetTempPath(), "sms-track-imports");
    private string FileOf(TrackImport i) => Path.Combine(Root, i.Id + Path.GetExtension(i.FileName).ToLowerInvariant());
    public static string VoyagesKey(Guid shipId) => $"voyages:{shipId}";
    public static bool Accepts(string fileName) => Extensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <summary>Stores the upload for the job and queues it; the caller answers with the import id at once.</summary>
    public async Task<TrackImport> EnqueueAsync(SmsDbContext db, IFormFile file, Guid shipId, Guid? tripId, Guid userId, Guid? deviceId, CancellationToken ct)
    {
        var name = Path.GetFileName(file.FileName);
        var import = new TrackImport { ShipId = shipId, TripId = tripId, UploadedById = userId, DeviceId = deviceId, FileName = name, Format = FormatOf(name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name) ?? TrackFormat.Gpx, CreatedById = userId };
        Directory.CreateDirectory(Root);
        await using (var target = File.Create(FileOf(import))) await file.CopyToAsync(target, ct);
        db.TrackImports.Add(import);
        await db.SaveChangesAsync(ct);
        await _queue.Writer.WriteAsync(import.Id, ct);
        return import;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RequeueAsync(stoppingToken);
        await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try { await RunAsync(id, stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Track import {Id} failed", id); }
        }
    }

    /// <summary>After a restart: imports that were queued or interrupted run again (idempotent thanks to the duplicate check).</summary>
    private async Task RequeueAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        foreach (var i in await db.TrackImports.Where(i => i.Status == TrackImportStatus.Queued || i.Status == TrackImportStatus.Running).OrderBy(i => i.CreatedAt).ToListAsync(ct))
        {
            if (i.Status == TrackImportStatus.Queued && File.Exists(FileOf(i))) await _queue.Writer.WriteAsync(i.Id, ct);
            else
            {
                // an import that was running when the process stopped is not retried automatically (a file that crashes the import would loop forever)
                i.Error = i.Status == TrackImportStatus.Running ? "Import durch einen Neustart unterbrochen – bitte erneut hochladen." : "Datei nach einem Neustart nicht mehr vorhanden – bitte erneut hochladen.";
                i.Status = TrackImportStatus.Failed; i.FinishedAt = DateTimeOffset.UtcNow;
                try { File.Delete(FileOf(i)); } catch (IOException) { /* best effort */ }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RunAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var import = await db.TrackImports.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (import is null || import.Status is TrackImportStatus.Done or TrackImportStatus.Failed) return;
        var path = FileOf(import);
        import.Status = TrackImportStatus.Running; import.Error = null;
        await db.SaveChangesAsync(ct);
        var total = Stopwatch.StartNew(); var dbTime = new Stopwatch();
        var touched = new HashSet<Guid>();
        try
        {
            var trips = import.TripId is null ? await TrackProcessor.TripWindowsAsync(db, import.ShipId, ct) : [];
            var stats = new GpxStats();
            await using var file = File.OpenRead(path);
            var batch = new List<TrackFix>(BatchSize);
            DateTimeOffset min = default, max = default;
            async Task Flush()
            {
                if (batch.Count == 0) return;
                dbTime.Start();
                await FlushAsync(db, import, batch, trips, touched, ct);
                dbTime.Stop();
                batch.Clear();
                (import.Tracks, import.Waypoints, import.Routes) = (stats.Tracks, stats.Waypoints, stats.Routes);
                import.ProgressPercent = (int)Math.Min(99, file.Position * 100 / Math.Max(1, file.Length));
                await db.SaveChangesAsync(ct);
            }
            var budget = new DecompressionBudget();
            var entries = 0;
            foreach (var (name, raw) in Entries(file, import.FileName))
            {
                if (++entries > MaxEntries) throw new ImportLimitException($"Archiv mit mehr als {MaxEntries} Dateien wird nicht importiert.");
                var stream = new LimitedStream(raw, budget, ownsInner: !ReferenceEquals(raw, file)); // the parsers close their stream; the upload file itself must stay open for the progress
                try
                {
                    if (FormatOf(name) is not { } format) continue; // other files inside an archive
                    import.Format = format;
                    var fixes = format switch { TrackFormat.Gpx => TrackParsers.ReadGpx(stream, stats), TrackFormat.Kml => TrackParsers.ParseKml(stream), _ => TrackParsers.ParseNmea(stream) };
                    foreach (var f in fixes)
                    {
                        if (import.PointCount + batch.Count >= MaxFixes) throw new ImportLimitException($"Mehr als {MaxFixes:N0} Positionen in einer Datei – bitte aufteilen.");
                        if (batch.Count >= BatchSize || (batch.Count > 0 && (f.Time > min + BatchSpan || f.Time < max - BatchSpan))) await Flush();
                        if (batch.Count == 0) min = max = f.Time;
                        else if (f.Time < min) min = f.Time;
                        else if (f.Time > max) max = f.Time;
                        batch.Add(f);
                    }
                }
                finally { await stream.DisposeAsync(); /* disposes an archive entry, leaves the upload file open */ }
            }
            await Flush();
            (import.Tracks, import.Waypoints, import.Routes) = (stats.Tracks, stats.Waypoints, stats.Routes);
            foreach (var t in touched) await processor.ProcessTripAsync(t, ct);
            import.Status = TrackImportStatus.Done; import.ProgressPercent = 100;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Track import {Id} ({File}) failed", import.Id, import.FileName);
            import.Status = TrackImportStatus.Failed;
            var message = ex is ImportLimitException or System.Xml.XmlException or FormatException or InvalidDataException or NotSupportedException
                ? ex.Message : "Import fehlgeschlagen – Details im Server-Log.";
            import.Error = message.Length > 500 ? message[..500] : message;
        }
        import.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        try { File.Delete(path); } catch (IOException) { /* best effort */ }
        cache.Remove(VoyagesKey(import.ShipId));
        logger.LogInformation("Track import {Id} {Status}: {Parsed} parsed, {Added} added, {Duplicates} duplicates, {Rejected} rejected in {Total:0.0} s ({Db:0.0} s database)",
            import.Id, import.Status, import.PointCount, import.Added, import.Duplicates, import.Rejected, total.Elapsed.TotalSeconds, dbTime.Elapsed.TotalSeconds);
    }

    private static async Task FlushAsync(SmsDbContext db, TrackImport import, List<TrackFix> batch, List<TrackProcessor.TripWindow> trips, HashSet<Guid> touched, CancellationToken ct)
    {
        import.PointCount += batch.Count;
        var kept = TrackMath.Despike(batch.OrderBy(f => f.Time).ToList());
        import.Rejected += batch.Count - kept.Count;
        // duplicates: anything the ship already has within ±1 s (earlier imports, live devices, AIS, earlier batches of this file)
        var from = kept[0].Time.AddSeconds(-1); var to = kept[^1].Time.AddSeconds(1);
        var existing = (await db.TrackPoints.Where(p => p.ShipId == import.ShipId && p.Time >= from && p.Time <= to).Select(p => p.Time).ToListAsync(ct))
            .Select(t => t.UtcTicks).Order().ToArray();
        var rows = new List<(TrackFix Fix, Guid? TripId)>(kept.Count);
        foreach (var f in kept)
        {
            if (HasNear(existing, f.Time.UtcTicks, TimeSpan.TicksPerSecond)) { import.Duplicates++; continue; }
            var trip = import.TripId ?? TrackProcessor.TripFor(trips, f.Time);
            if (trip is { } t) touched.Add(t);
            rows.Add((f, trip));
        }
        if (rows.Count == 0) return;
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using (var w = await conn.BeginBinaryImportAsync("""COPY "TrackPoints" ("ShipId", "TripId", "Source", "DeviceId", "ImportId", "Time", "Location", "SogKn", "CogDeg", "IsOutlier", "IsHidden") FROM STDIN (FORMAT BINARY)""", ct))
        {
            foreach (var (f, trip) in rows)
            {
                await w.StartRowAsync(ct);
                await w.WriteAsync(import.ShipId, NpgsqlDbType.Uuid, ct);
                await Nullable(w, trip, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(nameof(TrackSource.GpsFile), NpgsqlDbType.Varchar, ct);
                await Nullable(w, import.DeviceId, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(import.Id, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(f.Time.ToUniversalTime(), NpgsqlDbType.TimestampTz, ct);
                await w.WriteAsync(Geo.Point(f.Lat, f.Lng), NpgsqlDbType.Geography, ct);
                await Nullable(w, f.SogKn, NpgsqlDbType.Double, ct);
                await Nullable(w, f.CogDeg, NpgsqlDbType.Double, ct);
                await w.WriteAsync(false, NpgsqlDbType.Boolean, ct);
                await w.WriteAsync(false, NpgsqlDbType.Boolean, ct);
            }
            await w.CompleteAsync(ct);
        }
        import.Added += rows.Count;
    }

    private static async Task Nullable<T>(NpgsqlBinaryImporter w, T? value, NpgsqlDbType type, CancellationToken ct) where T : struct
    {
        if (value is { } v) await w.WriteAsync(v, type, ct); else await w.WriteNullAsync(ct);
    }

    /// <summary>Whether the sorted <paramref name="ticks"/> contain a value within <paramref name="tolerance"/> of <paramref name="t"/>.</summary>
    public static bool HasNear(long[] ticks, long t, long tolerance)
    {
        var i = Array.BinarySearch(ticks, t);
        if (i >= 0) return true;
        i = ~i;
        return (i < ticks.Length && ticks[i] - t <= tolerance) || (i > 0 && t - ticks[i - 1] <= tolerance);
    }

    private const int MaxEntries = 500;
    private const long MaxFixes = 20_000_000;
    /// <summary>Total decompressed bytes per import (the 545 MB TimeZero archive is far below; protects against decompression bombs).</summary>
    public const long MaxDecompressedBytes = 4L * 1024 * 1024 * 1024;

    public sealed class ImportLimitException(string message) : Exception(message);
    private sealed class DecompressionBudget { public long Used; }

    /// <summary>Read-only pass-through that throws once the import's decompressed bytes exceed <see cref="MaxDecompressedBytes"/>.</summary>
    private sealed class LimitedStream(Stream inner, DecompressionBudget budget, bool ownsInner) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => Count(await inner.ReadAsync(buffer, ct));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        private int Count(int n)
        {
            budget.Used += n;
            if (budget.Used > MaxDecompressedBytes) throw new ImportLimitException($"Entpackte Daten größer als {MaxDecompressedBytes / (1024 * 1024 * 1024)} GB – Import abgebrochen.");
            return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing && ownsInner) inner.Dispose(); base.Dispose(disposing); }
    }

    private static TrackFormat? FormatOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".gpx" => TrackFormat.Gpx, ".kml" => TrackFormat.Kml, ".nmea" or ".txt" or ".log" => TrackFormat.Nmea, _ => null,
    };

    /// <summary>The track files inside the upload: the file itself, the content of a .gz, or the entries of a .zip / .7z.</summary>
    private static IEnumerable<(string Name, Stream Stream)> Entries(FileStream file, string fileName)
    {
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".zip":
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true))
                    foreach (var e in zip.Entries.Where(e => e.Length > 0)) yield return (e.FullName, e.Open());
                break;
            case ".gz":
                yield return (fileName[..^3], new GZipStream(file, CompressionMode.Decompress, leaveOpen: true));
                break;
            case ".7z":
                using (var archive = SevenZipArchive.OpenArchive(file, new ReaderOptions { LeaveStreamOpen = true }))
                    foreach (var e in archive.Entries.Where(e => !e.IsDirectory)) yield return (e.Key ?? "", e.OpenEntryStream());
                break;
            default:
                yield return (fileName, file);
                break;
        }
    }

    /// <summary>Ship-level fixes (no trip) split into voyages; read thinned to one fix per minute and cached until the next import or adoption.</summary>
    public async Task<List<TrackMath.Voyage>> VoyagesAsync(SmsDbContext db, Guid shipId, CancellationToken ct) =>
        (await cache.GetOrCreateAsync(VoyagesKey(shipId), async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
                SELECT "Time", ST_Y("Location"::geometry), ST_X("Location"::geometry) FROM "TrackPoints"
                WHERE "ShipId" = @ship AND "TripId" IS NULL AND NOT "IsOutlier" AND NOT "IsHidden" ORDER BY "Time"
                """, conn);
            cmd.Parameters.AddWithValue("ship", shipId);
            var fixes = new List<TrackFix>();
            var last = DateTimeOffset.MinValue;
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                {
                    var t = r.GetFieldValue<DateTimeOffset>(0);
                    if (t - last < TimeSpan.FromMinutes(1)) continue;
                    last = t;
                    fixes.Add(new TrackFix(t, r.GetDouble(1), r.GetDouble(2)));
                }
            return TrackMath.Voyages(fixes);
        }))!;
}
