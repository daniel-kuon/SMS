using System.Text.Json;

namespace Sms.Api;

/// <summary>
/// Runs an Overpass QL query against the configured mirrors in order (Overpass:Endpoints) and returns the first
/// successful JSON document, or null when every mirror fails. The public instances are frequently overloaded.
/// </summary>
public static class Overpass
{
    public static readonly string[] DefaultEndpoints =
    [
        "https://overpass-api.de/api/interpreter",
        "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
        "https://overpass.openstreetmap.fr/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
    ];

    public static async Task<JsonDocument?> QueryAsync(IHttpClientFactory http, IConfiguration cfg, string query, CancellationToken ct, ILogger? log = null)
    {
        var endpoints = cfg.GetSection("Overpass:Endpoints").Get<string[]>() is { Length: > 0 } e ? e : DefaultEndpoints;
        foreach (var url in endpoints)
        {
            try
            {
                var client = http.CreateClient("overpass");
                client.Timeout = TimeSpan.FromMinutes(6);
                using var resp = await client.PostAsync(url, new FormUrlEncodedContent([new("data", query)]), ct);
                if (!resp.IsSuccessStatusCode) { log?.LogWarning("Overpass {Url} answered {Status}", url, (int)resp.StatusCode); continue; }
                var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("elements", out _)) return doc;
                doc.Dispose();
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) // mirror down, timeout, resilience pipeline, bad JSON: try the next one
            {
                log?.LogWarning(ex, "Overpass {Url} failed", url);
            }
        }
        return null;
    }
}
