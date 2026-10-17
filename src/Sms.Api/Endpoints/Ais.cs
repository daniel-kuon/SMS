using Microsoft.EntityFrameworkCore;
using Sms.Domain;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public record AisConfigUpsert(string Provider, string? ApiKey, string? Mmsi, int PollIntervalMinutes = 30, bool Enabled = true);
public record AisConfigDto(string Provider, bool Configured, string? Mmsi, int PollIntervalMinutes, bool Enabled, DateTimeOffset? LastPolledAt, int CallsThisMonth, string? LastError, IEnumerable<string> Providers);

/// <summary>Spec 12 §12.1 C: per-ship AIS configuration (owners), API key never returned.</summary>
public static class AisEndpoints
{
    public static void MapAis(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/ships/{shipId:guid}/ais-config").WithTags("Tracking").RequireAuthorization();

        g.MapGet("", async (Guid shipId, SmsDbContext db, Access access, AisPoller poller, CancellationToken ct) =>
        {
            if (!await access.IsShipMemberAsync(shipId, null, ct)) return Results.Forbid();
            var c = await db.ShipAisConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.ShipId == shipId, ct);
            var mmsi = c?.Mmsi ?? await db.Ships.Where(s => s.Id == shipId).Select(s => s.Mmsi).FirstOrDefaultAsync(ct);
            return Results.Ok(ToDto(c, mmsi, poller));
        });

        g.MapPut("", async (Guid shipId, AisConfigUpsert req, SmsDbContext db, Access access, AisPoller poller, AisKeys keys, CurrentUser current, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(shipId, ct)) return Results.Forbid();
            var provider = poller.Provider(req.Provider);
            if (provider is null) return Invalid("provider", $"Unbekannter Anbieter. Verfügbar: {string.Join(", ", poller.ProviderNames)}");
            if (req.PollIntervalMinutes is < 1 or > 1440) return Invalid("pollIntervalMinutes", "1 bis 1440 Minuten.");
            var c = await db.ShipAisConfigs.FirstOrDefaultAsync(x => x.ShipId == shipId, ct) ?? db.ShipAisConfigs.Add(new ShipAisConfig { ShipId = shipId }).Entity;
            c.Provider = provider.Name; c.Mmsi = string.IsNullOrWhiteSpace(req.Mmsi) ? null : req.Mmsi.Trim(); c.PollIntervalMinutes = req.PollIntervalMinutes; c.Enabled = req.Enabled; c.UpdatedAt = DateTimeOffset.UtcNow; c.UpdatedById = current.Id;
            if (!string.IsNullOrWhiteSpace(req.ApiKey)) c.ApiKeyEncrypted = keys.Protect(req.ApiKey.Trim()); // empty = keep the stored key
            if (c.Enabled && c.ApiKeyEncrypted is null) return Invalid("apiKey", "API-Schlüssel fehlt.");
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(c, c.Mmsi ?? await db.Ships.Where(s => s.Id == shipId).Select(s => s.Mmsi).FirstOrDefaultAsync(ct), poller));
        });

        g.MapDelete("", async (Guid shipId, SmsDbContext db, Access access, CancellationToken ct) =>
            await access.IsShipOwnerAsync(shipId, ct) ? (await db.ShipAisConfigs.Where(x => x.ShipId == shipId).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : Results.NotFound()) : Results.Forbid());

        // one live request without storing anything: does the key work, does the vessel transmit?
        g.MapPost("/test", async (Guid shipId, SmsDbContext db, Access access, AisPoller poller, AisKeys keys, CancellationToken ct) =>
        {
            if (!await access.IsShipOwnerAsync(shipId, ct)) return Results.Forbid();
            var c = await db.ShipAisConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.ShipId == shipId, ct);
            if (c?.ApiKeyEncrypted is null) return Invalid("apiKey", "Erst Anbieter und API-Schlüssel speichern.");
            var mmsi = c.Mmsi ?? await db.Ships.Where(s => s.Id == shipId).Select(s => s.Mmsi).FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(mmsi)) return Invalid("mmsi", "MMSI fehlt (Schiff oder AIS-Konfiguration).");
            var provider = poller.Provider(c.Provider);
            if (provider is null) return Invalid("provider", "Anbieter nicht verfügbar.");
            try
            {
                var fixes = await provider.PositionsAsync(keys.Unprotect(c.ApiKeyEncrypted), mmsi.Trim(), DateTimeOffset.UtcNow.AddHours(-24), ct);
                var latest = fixes.Count == 0 ? (Sms.Domain.Tracking.TrackFix?)null : fixes.MaxBy(f => f.Time);
                return Results.Ok(new { count = fixes.Count, latest = latest is { } l ? new { l.Time, l.Lat, l.Lng, l.SogKn, l.CogDeg } : null });
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Results.Problem(statusCode: 502, title: $"AIS-Abfrage fehlgeschlagen: {ex.Message}"); }
        });

        g.MapPost("/poll", async (Guid shipId, Access access, AisPoller poller, CancellationToken ct) =>
            await access.IsShipOwnerAsync(shipId, ct) ? Results.Ok(new { added = await poller.PollOnceAsync(force: true, ct, shipId) }) : Results.Forbid());
    }

    private static AisConfigDto ToDto(ShipAisConfig? c, string? mmsi, AisPoller poller) =>
        new(c?.Provider ?? poller.ProviderNames.FirstOrDefault() ?? "", c?.ApiKeyEncrypted is not null, mmsi, c?.PollIntervalMinutes ?? 30, c?.Enabled ?? false, c?.LastPolledAt, c?.CallsThisMonth ?? 0, c?.LastError, poller.ProviderNames);
}
