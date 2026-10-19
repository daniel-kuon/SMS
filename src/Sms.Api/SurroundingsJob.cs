using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Sms.Api.Endpoints;
using Sms.Infrastructure;

namespace Sms.Api;

/// <summary>
/// Background refresh of the surroundings of a harbour: POIs within <see cref="RadiusM"/> from Overpass (the classes the PBF import keeps, plus public transport),
/// upserted like the bulk import, then the harbour's services recomputed. Created/moved harbours would otherwise have no POIs and no services.
/// One harbour at a time (Overpass etiquette); the same harbour queued twice runs once; a failure is retried once after <see cref="RetryDelay"/>.
/// </summary>
public sealed class SurroundingsJob(IServiceScopeFactory scopes, IHttpClientFactory http, IConfiguration cfg, ILogger<SurroundingsJob> log) : BackgroundService
{
    public const int RadiusM = 3000;
    public static TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(10);

    private readonly Channel<(Guid Id, int Attempt)> _queue = Channel.CreateUnbounded<(Guid, int)>();
    private readonly HashSet<Guid> _pending = [];

    /// <summary>Queues the harbour; false when it is already waiting.</summary>
    public bool Enqueue(Guid harbourId) => Enqueue(harbourId, 0);
    private bool Enqueue(Guid harbourId, int attempt)
    {
        lock (_pending) if (!_pending.Add(harbourId)) return false;
        return _queue.Writer.TryWrite((harbourId, attempt));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var (id, attempt) in _queue.Reader.ReadAllAsync(ct))
        {
            lock (_pending) _pending.Remove(id);   // before the run: a harbour moved again meanwhile is queued again
            try { await RefreshAsync(id, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Surroundings of harbour {Id} failed (attempt {Attempt})", id, attempt + 1);
                if (attempt == 0) _ = Task.Delay(RetryDelay, ct).ContinueWith(t => { if (!t.IsCanceled) Enqueue(id, 1); }, TaskScheduler.Default);
            }
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { return; }   // be polite to the public Overpass mirrors
        }
    }

    public async Task RefreshAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmsDbContext>();
        var h = await db.Harbours.AsNoTracking().Where(x => x.Id == id && x.IsActive).Select(x => x.Location).FirstOrDefaultAsync(ct);
        if (h is null) return;
        var (lat, lng) = (h.Y, h.X);
        using var doc = await Overpass.QueryAsync(http, cfg, Query(lat, lng), ct, log) ?? throw new InvalidOperationException("Overpass not reachable");
        var near = new PbfImportJob.NearIndex(RadiusM);
        near.Add(lat, lng);
        using var pois = KeepPois(doc);
        var n = await PoiEndpoints.ImportAsync(pois, near, db, ct);
        await HarbourEndpoints.ComputeServicesAsync(db, [id], ct);
        log.LogInformation("Surroundings of harbour {Id}: {Count} POIs", id, n);
    }

    /// <summary>All POI keys of the importer plus the public transport tags of <c>HarbourServices.TransportKind</c>.</summary>
    public static string Query(double lat, double lng)
    {
        var a = $"(around:{RadiusM},{lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)})";
        return $"""[out:json][timeout:120];(nwr{a}[~"^(amenity|shop|tourism|leisure|craft|office|healthcare)$"~"."];nwr{a}["highway"="bus_stop"];nwr{a}["public_transport"="platform"]["bus"="yes"];nwr{a}["railway"~"^(station|halt|tram_stop)$"];nwr{a}["public_transport"="station"];);out center tags;""";
    }

    /// <summary>The same selection as the PBF import (<see cref="PbfImportJob.IsPoi"/>): the query is broader than it.</summary>
    public static JsonDocument KeepPois(JsonDocument doc)
    {
        var kept = doc.RootElement.GetProperty("elements").EnumerateArray()
            .Where(el => el.TryGetProperty("tags", out var t) && PbfImportJob.IsPoi(t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? ""))).ToList();
        return JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(new { elements = kept }));
    }
}
