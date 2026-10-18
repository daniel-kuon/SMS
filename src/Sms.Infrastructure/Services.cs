using System.Diagnostics;
using System.Net.Mail;
using Ganss.Xss;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Point = NetTopologySuite.Geometries.Point;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Sms.Infrastructure;

public static class Geo
{
    public static readonly NetTopologySuite.Geometries.GeometryFactory Factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
    public static Point Point(double lat, double lng) => Factory.CreatePoint(new NetTopologySuite.Geometries.Coordinate(lng, lat));
}

/// <summary>Sanitises rich text from the editor (spec 06 #16 / 11.2).</summary>
public sealed class HtmlSanitizerService
{
    private readonly HtmlSanitizer _sanitizer = Create();

    /// <summary>Ganss defaults plus what the TipTap editor emits (figures, link target/rel, image size); explicit so a library default change cannot strip them.</summary>
    private static HtmlSanitizer Create()
    {
        var s = new HtmlSanitizer();
        foreach (var tag in new[] { "p", "br", "h1", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s", "a", "img", "figure", "figcaption", "ul", "ol", "li", "blockquote", "pre", "code", "hr",
                     "table", "thead", "tbody", "tr", "th", "td" })
            s.AllowedTags.Add(tag);
        foreach (var attr in new[] { "href", "target", "rel", "src", "alt", "title", "width", "height", "colspan", "rowspan" })
            s.AllowedAttributes.Add(attr);
        return s;
    }
    public string Sanitize(string? html) => string.IsNullOrWhiteSpace(html) ? "" : _sanitizer.Sanitize(html);
}

/// <summary>Local file system media storage with ImageSharp variants (thumb 400 px, preview 1600 px), EXIF read + strip.</summary>
public sealed class MediaStorage(IConfiguration config, ILogger<MediaStorage> logger)
{
    private static readonly ActivitySource Activity = new("Sms");
    public static readonly string[] Variants = ["original", "preview", "thumb"];

    private string Root => Path.GetFullPath(config["Media:RootPath"] ?? "media");

    /// <summary>Decoders limited to the photo formats we accept (no GIF/TIFF/BMP code paths).</summary>
    private static readonly Configuration PhotoFormats = new(
        new SixLabors.ImageSharp.Formats.Jpeg.JpegConfigurationModule(),
        new SixLabors.ImageSharp.Formats.Png.PngConfigurationModule(),
        new SixLabors.ImageSharp.Formats.Webp.WebpConfigurationModule());

    public sealed record ProcessResult(string StorageKey, int Width, int Height, DateTimeOffset? TakenAt, Point? Location);

    public string PathFor(string storageKey, string variant) => Path.Combine(Root, variant, storageKey);

    /// <summary>Stores a photo: reads EXIF capture time + GPS, strips metadata, writes original/preview/thumb as JPEG.</summary>
    public async Task<ProcessResult> StorePhotoAsync(Stream input, CancellationToken ct)
    {
        using var span = Activity.StartActivity("media.process");
        var key = $"{DateTime.UtcNow:yyyy/MM}/{Guid.CreateVersion7():N}.jpg";
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        DateTimeOffset? takenAt = null;
        Point? location = null;
        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(buffer);
            var exif = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exif is not null && exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                takenAt = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeSpan.Zero); // EXIF has no zone; caller converts with the trip time zone
            // GPS is used for placement on the map only; EXIF is always stripped from the delivered files (spec 07 A3).
            var gps = dirs.OfType<GpsDirectory>().FirstOrDefault()?.GetGeoLocation();
            if (gps is { IsZero: false } g) location = Geo.Point(g.Latitude, g.Longitude);
        }
        catch (Exception ex) { logger.LogDebug(ex, "No EXIF metadata"); }

        buffer.Position = 0;
        using var image = await Image.LoadAsync(new DecoderOptions { Configuration = PhotoFormats, MaxFrames = 1 }, buffer, ct);
        image.Mutate(x => x.AutoOrient());
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;
        var (w, h) = (image.Width, image.Height);

        var encoder = new JpegEncoder { Quality = 88 };
        await SaveAsync(image, PathFor(key, "original"), encoder, ct);
        using (var preview = image.Clone(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(1600, 1600) })))
            await SaveAsync(preview, PathFor(key, "preview"), encoder, ct);
        using (var thumb = image.Clone(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(400, 400) })))
            await SaveAsync(thumb, PathFor(key, "thumb"), new JpegEncoder { Quality = 80 }, ct);

        span?.SetTag("media.width", w);
        span?.SetTag("media.height", h);
        return new ProcessResult(key, w, h, takenAt, location);
    }

    public async Task StoreFileAsync(Stream input, string storageKey, CancellationToken ct)
    {
        var path = PathFor(storageKey, "original");
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await input.CopyToAsync(file, ct);
    }

    public void Delete(string storageKey)
    {
        foreach (var v in Variants)
        {
            var p = PathFor(storageKey, v);
            if (File.Exists(p)) File.Delete(p);
        }
    }

    private static async Task SaveAsync(Image image, string path, JpegEncoder encoder, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await image.SaveAsync(path, encoder, ct);
    }
}

/// <summary>Sends mail through the SMTP endpoint provided by the AppHost (Mailpit in development).</summary>
public sealed class MailSender(IConfiguration config, ILogger<MailSender> logger)
{
    // ponytail: System.Net.Mail.SmtpClient is enough for Mailpit; swap for MailKit when a real provider needs auth/TLS.
    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var endpoint = config["SMTP_ENDPOINT"];
        var from = config["Mail:From"] ?? "portal@localhost";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            logger.LogWarning("SMTP_ENDPOINT not configured; mail to {To} with subject {Subject} not sent. Body:\n{Body}", to, subject, htmlBody);
            return;
        }
#pragma warning disable CS0618
        using var client = new SmtpClient(uri.Host, uri.Port);
#pragma warning restore CS0618
        using var msg = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };
        await client.SendMailAsync(msg, ct);
        logger.LogInformation("Mail sent to {To}: {Subject}", to, subject);
    }
}
