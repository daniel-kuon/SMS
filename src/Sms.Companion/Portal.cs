using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sms.Companion;

/// <summary>One position of POST /api/ingest/points. T is UTC (serialised as ISO-8601 with "Z"); sog in knots, angles in degrees true.</summary>
public sealed record PointDto(DateTime T, double Lat, double Lon, double? Sog = null, double? Cog = null, double? Acc = null, double? Hdg = null, double? DepthM = null,
    double? TwsKn = null, double? TwdDeg = null, double? AwsKn = null, double? AwaDeg = null, double? PressureHpa = null, double? AirTempC = null, double? WaterTempC = null);

public sealed record PointsBatch(string DeviceToken, IReadOnlyList<PointDto> Points);

public static class Wire
{
    /// <summary>camelCase, nulls omitted: the portal's PointsBatch/PointInput binding.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}

public sealed class PortalException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>HTTP calls to the portal. No cookies, no user identity: the device token is the only credential.</summary>
public sealed class PortalClient : IDisposable
{
    private readonly HttpClient http;
    private string pointsPath = "/api/ingest/points";

    public PortalClient(string serverUrl)
    {
        var baseUri = new Uri(serverUrl.TrimEnd('/') + "/");
        var handler = new HttpClientHandler { UseCookies = false };
        // ponytail: the ASP.NET dev certificate is often untrusted for other processes; only relaxed for loopback, never for a real server
        if (baseUri.IsLoopback) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        http = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(100) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SmsCompanion/0.1");
    }

    /// <summary>Posts one batch. Falls back to the older /api/tracking/points route (same body) while the portal does not have /api/ingest yet.</summary>
    public async Task<int?> PostPointsAsync(string token, IReadOnlyList<PointDto> points, CancellationToken ct)
    {
        var res = await http.PostAsJsonAsync(pointsPath.TrimStart('/'), new PointsBatch(token, points), Wire.Json, ct);
        if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed && pointsPath == "/api/ingest/points")
        {
            pointsPath = "/api/tracking/points";
            res = await http.PostAsJsonAsync(pointsPath.TrimStart('/'), new PointsBatch(token, points), Wire.Json, ct);
        }
        await EnsureAsync(res, ct);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct) is { Length: > 0 } b ? b : "{}");
        return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("added", out var a) && a.TryGetInt32(out var n) ? n : null;
    }

    /// <summary>POST /api/ingest/files (multipart, X-Device-Token); returns the import id.</summary>
    public async Task<string> UploadFileAsync(string token, string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var content = new MultipartFormDataContent();
        var file = new StreamContent(fs);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", Path.GetFileName(path));
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/ingest/files") { Content = content };
        req.Headers.Add("X-Device-Token", token);
        var res = await http.SendAsync(req, ct);
        await EnsureAsync(res, ct);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("importId", out var id) ? id.ToString() : throw new PortalException("Upload answer has no importId.");
    }

    /// <summary>GET /api/ingest/files/{id}: the import status as returned by the portal (raw JSON).</summary>
    public async Task<JsonElement> ImportStatusAsync(string token, string importId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"api/ingest/files/{Uri.EscapeDataString(importId)}");
        req.Headers.Add("X-Device-Token", token);
        var res = await http.SendAsync(req, ct);
        await EnsureAsync(res, ct);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement.Clone();
    }

    /// <summary>GET /api/weather?lat&amp;lng: the portal's model forecast (current + hourly) for a position.</summary>
    public async Task<JsonElement> WeatherAsync(double lat, double lon, CancellationToken ct)
    {
        var res = await http.GetAsync(FormattableString.Invariant($"api/weather?lat={lat:0.####}&lng={lon:0.####}"), ct);
        await EnsureAsync(res, ct);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)).RootElement.Clone();
    }

    private static async Task EnsureAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var body = await res.Content.ReadAsStringAsync(ct);
        var hint = res.StatusCode == HttpStatusCode.Unauthorized ? " (device token unknown or revoked)" : "";
        throw new PortalException($"{(int)res.StatusCode} {res.ReasonPhrase}{hint}: {body[..Math.Min(body.Length, 300)]}", res.StatusCode);
    }

    public void Dispose() => http.Dispose();
}

/// <summary>Points waiting to be sent, one JSON line each, oldest first. Survives restarts and offline periods.</summary>
public sealed class OfflineQueue
{
    private readonly string path;
    private readonly Lock gate = new();
    private List<string> lines;

    public OfflineQueue(string path)
    {
        this.path = path;
        lines = File.Exists(path) ? File.ReadAllLines(path).Where(l => l.Length > 0).ToList() : [];
    }

    public int Count { get { lock (gate) return lines.Count; } }

    public void Enqueue(PointDto p)
    {
        var line = JsonSerializer.Serialize(p, Wire.Json);
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line + Environment.NewLine);
            lines.Add(line);
        }
    }

    public List<PointDto> Peek(int max)
    {
        lock (gate) return lines.Take(max).Select(l => JsonSerializer.Deserialize<PointDto>(l, Wire.Json)!).ToList();
    }

    /// <summary>Drops the first <paramref name="n"/> points after they were delivered (or moved to rejected.jsonl).</summary>
    public void Remove(int n)
    {
        lock (gate)
        {
            lines.RemoveRange(0, Math.Min(n, lines.Count));
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, path, overwrite: true);
        }
    }
}

public sealed record UploadEntry(string Path, long Size, DateTime WriteTimeUtc, string Hash, string? ImportId, DateTime UploadedAt)
{
    public string? Status { get; set; }
}

/// <summary>Files already uploaded (path, size, write time, SHA-256), so a file or an identical copy is uploaded once.</summary>
public sealed class UploadStore
{
    private readonly string path;
    private readonly Lock gate = new();
    private readonly List<UploadEntry> entries;

    public UploadStore(string path)
    {
        this.path = path;
        entries = File.Exists(path) ? JsonSerializer.Deserialize<List<UploadEntry>>(File.ReadAllText(path), Wire.Json) ?? [] : [];
    }

    public bool IsUnchanged(string file, long size, DateTime writeUtc)
    {
        lock (gate) return entries.Any(e => e.Size == size && e.WriteTimeUtc == writeUtc && string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasHash(string hash) { lock (gate) return entries.Any(e => e.Hash == hash); }

    public void Add(UploadEntry e) { lock (gate) { entries.Add(e); Save(); } }

    public void Save()
    {
        lock (gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, Wire.Json));
        }
    }

    public List<UploadEntry> Recent(int n) { lock (gate) return entries.Where(e => e.ImportId is not null).TakeLast(n).Reverse().ToList(); }

    public static string Sha256(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(fs));
    }
}
