using System.Globalization;
using System.Text.Json;
using System.Xml;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

namespace Sms.Domain.Tracking;

public enum TrackSource { GpsLive, GpsFile, Ais, Calculated, Mixed }
public enum TrackingProtocol { OsmAnd, OwnTracks, Json }
public enum TrackFormat { Gpx, Kml, Nmea }

/// <summary>One position fix as delivered by a device, file or AIS provider.</summary>
public readonly record struct TrackFix(DateTimeOffset Time, double Lat, double Lng, double? SogKn = null, double? CogDeg = null, double? AccuracyM = null);

public class TrackingDevice : Entity
{
    public Guid ShipId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TripId { get; set; }
    public string Name { get; set; } = "";
    public TrackingProtocol Protocol { get; set; }
    public string TokenHash { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTimeOffset? LastSeenAt { get; set; }
}

public class TrackPoint
{
    public long Id { get; set; }
    public Guid ShipId { get; set; }
    public Guid? TripId { get; set; }
    public TrackSource Source { get; set; }
    public Guid? DeviceId { get; set; }
    public Guid? ImportId { get; set; }
    public DateTimeOffset Time { get; set; }
    public Point Location { get; set; } = null!;
    public double? SogKn { get; set; }
    public double? CogDeg { get; set; }
    public double? AccuracyM { get; set; }
    public bool IsOutlier { get; set; }
    public bool IsHidden { get; set; }
}

public class TrackImport : Entity
{
    public Guid ShipId { get; set; }
    public Guid? TripId { get; set; }
    public Guid UploadedById { get; set; }
    public string FileName { get; set; } = "";
    public TrackFormat Format { get; set; }
    public int PointCount { get; set; }
}

/// <summary>Materialised per-day track (spec 12 §12.2).</summary>
public class TrackSegment : Entity
{
    public Guid TripId { get; set; }
    public Guid? LogbookEntryId { get; set; }
    public DateOnly Day { get; set; }
    public TrackSource Source { get; set; }
    public TrackSource? SourceOverride { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public decimal DistanceNm { get; set; }
    public int DurationMinutes { get; set; }
    public double? MaxSogKn { get; set; }
    public double? AvgSogKn { get; set; }
    public LineString Geometry { get; set; } = null!;
    public LineString GeometryCoarse { get; set; } = null!;
    public int PointCount { get; set; }
}

public class PrivacyZone : Entity
{
    public Guid ShipId { get; set; }
    public string Name { get; set; } = "";
    public Point Center { get; set; } = null!;
    public double RadiusM { get; set; }
}

public static class TrackMath
{
    public const double Nm = 1852.0;

    public static double Haversine(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6_371_008.8;
        var dLat = (lat2 - lat1) * Math.PI / 180; var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>Marks duplicates, inaccurate fixes and implausible jumps (spec 12 §12.1 B).</summary>
    public static List<TrackFix> Clean(IEnumerable<TrackFix> fixes, double maxAccuracyM = 100, double maxSpeedKn = 30)
    {
        var ordered = fixes.OrderBy(f => f.Time).ToList();
        var result = new List<TrackFix>();
        TrackFix? prev = null;
        foreach (var f in ordered)
        {
            if (f.AccuracyM is > 0 && f.AccuracyM > maxAccuracyM) continue;
            if (prev is { } p)
            {
                if (f.Time == p.Time) continue;
                var seconds = (f.Time - p.Time).TotalSeconds;
                var metres = Haversine(p.Lat, p.Lng, f.Lat, f.Lng);
                if (seconds > 0 && metres / seconds * 3600 / Nm > maxSpeedKn) continue;
            }
            result.Add(f);
            prev = f;
        }
        return result;
    }

    /// <summary>Position on the track at <paramref name="at"/> (linear interpolation) if a fix exists within ±<paramref name="window"/>.</summary>
    public static (double lat, double lng)? Interpolate(IReadOnlyList<TrackFix> ordered, DateTimeOffset at, TimeSpan window)
    {
        if (ordered.Count == 0) return null;
        TrackFix? before = null, after = null;
        foreach (var f in ordered)
        {
            if (f.Time <= at) before = f;
            if (f.Time >= at) { after = f; break; }
        }
        if (before is { } b && after is { } a)
        {
            if (a.Time == b.Time) return (b.Lat, b.Lng);
            var t = (at - b.Time).TotalSeconds / (a.Time - b.Time).TotalSeconds;
            return (b.Lat + (a.Lat - b.Lat) * t, b.Lng + (a.Lng - b.Lng) * t);
        }
        var nearest = before ?? after;
        return nearest is { } n && (n.Time - at).Duration() <= window ? (n.Lat, n.Lng) : null;
    }

    public static double DistanceM(IReadOnlyList<TrackFix> fixes)
    {
        var d = 0.0;
        for (var i = 1; i < fixes.Count; i++) d += Haversine(fixes[i - 1].Lat, fixes[i - 1].Lng, fixes[i].Lat, fixes[i].Lng);
        return d;
    }

    public static LineString ToLine(GeometryFactory f, IReadOnlyList<TrackFix> fixes, double toleranceDeg)
    {
        var coords = fixes.Select(x => new Coordinate(x.Lng, x.Lat)).ToArray();
        if (coords.Length < 2) coords = [coords[0], coords[0]];
        var line = f.CreateLineString(coords);
        var simplified = DouglasPeuckerSimplifier.Simplify(line, toleranceDeg);
        return simplified is LineString ls && ls.NumPoints >= 2 ? ls : line;
    }
}

/// <summary>Parsers for GPX, KML/gx:Track, NMEA 0183 RMC, OsmAnd query strings and OwnTracks JSON (spec 12 §12.1).</summary>
public static class TrackParsers
{
    public static List<TrackFix> ParseGpx(Stream s)
    {
        var result = new List<TrackFix>();
        using var r = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Ignore });
        double? lat = null, lng = null; DateTimeOffset? time = null; double? speed = null, course = null; var inPoint = false;
        var moved = r.Read();
        while (moved)
        {
            var consumed = false; // ReadElementContentAsString already advances to the next node
            if (r.NodeType == XmlNodeType.Element && r.LocalName is "trkpt" or "rtept")
            {
                inPoint = true; time = null; speed = null; course = null;
                lat = D(r.GetAttribute("lat")); lng = D(r.GetAttribute("lon"));
                if (r.IsEmptyElement) inPoint = false;
            }
            else if (inPoint && r.NodeType == XmlNodeType.Element)
            {
                switch (r.LocalName)
                {
                    case "time": time = T(r.ReadElementContentAsString()); consumed = true; break;
                    case "speed": speed = D(r.ReadElementContentAsString()) is { } ms ? ms * 3600 / TrackMath.Nm : null; consumed = true; break;
                    case "course": course = D(r.ReadElementContentAsString()); consumed = true; break;
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName is "trkpt" or "rtept")
            {
                inPoint = false;
                if (lat is { } la && lng is { } ln && time is { } t) result.Add(new TrackFix(t, la, ln, speed, course));
            }
            moved = consumed || r.Read();
        }
        return result;
    }

    public static List<TrackFix> ParseKml(Stream s)
    {
        var result = new List<TrackFix>();
        var doc = new XmlDocument();
        doc.Load(s);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("k", "http://www.opengis.net/kml/2.2"); ns.AddNamespace("gx", "http://www.google.com/kml/ext/2.2");
        // gx:Track: parallel <when>/<gx:coord> lists
        foreach (XmlNode track in doc.SelectNodes("//gx:Track", ns)!)
        {
            var whens = track.SelectNodes("k:when", ns)!.Cast<XmlNode>().Select(n => T(n.InnerText)).ToList();
            var coords = track.SelectNodes("gx:coord", ns)!.Cast<XmlNode>().Select(n => n.InnerText.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
            for (var i = 0; i < Math.Min(whens.Count, coords.Count); i++)
                if (whens[i] is { } t && coords[i].Length >= 2 && D(coords[i][0]) is { } ln && D(coords[i][1]) is { } la) result.Add(new TrackFix(t, la, ln));
        }
        if (result.Count > 0) return result;
        // plain LineString without timestamps: spread evenly over the file's TimeSpan if given, else "now"
        var start = T(doc.SelectSingleNode("//k:TimeSpan/k:begin", ns)?.InnerText) ?? DateTimeOffset.UtcNow;
        var end = T(doc.SelectSingleNode("//k:TimeSpan/k:end", ns)?.InnerText) ?? start;
        var all = doc.SelectNodes("//k:LineString/k:coordinates", ns)!.Cast<XmlNode>()
            .SelectMany(n => n.InnerText.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)).Select(c => c.Split(',')).Where(c => c.Length >= 2).ToList();
        for (var i = 0; i < all.Count; i++)
            if (D(all[i][0]) is { } ln && D(all[i][1]) is { } la)
                result.Add(new TrackFix(all.Count > 1 ? start + (end - start) * i / (all.Count - 1) : start, la, ln));
        return result;
    }

    /// <summary>$GPRMC / $GNRMC sentences (time, status, lat, N/S, lon, E/W, speed kn, course, date).</summary>
    public static List<TrackFix> ParseNmea(Stream s)
    {
        var result = new List<TrackFix>();
        using var r = new StreamReader(s);
        while (r.ReadLine() is { } line)
        {
            if (!line.StartsWith("$G", StringComparison.Ordinal) || line.Length < 6 || line.Substring(3, 3) != "RMC") continue;
            var p = line.Split('*')[0].Split(',');
            if (p.Length < 10 || p[2] != "A") continue;
            if (!TryNmeaCoord(p[3], p[4], 2, out var lat) || !TryNmeaCoord(p[5], p[6], 3, out var lng)) continue;
            if (!DateTime.TryParseExact(p[9] + p[1].Split('.')[0], "ddMMyyHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)) continue;
            result.Add(new TrackFix(new DateTimeOffset(dt, TimeSpan.Zero), lat, lng, D(p[7]), D(p[8])));
        }
        return result;
    }

    /// <summary>OsmAnd / Traccar Client parameters: lat, lon, timestamp (unix seconds or ISO), speed (kn), bearing, accuracy.</summary>
    public static TrackFix? ParseOsmAnd(IReadOnlyDictionary<string, string?> q)
    {
        if (D(q.GetValueOrDefault("lat")) is not { } lat || D(q.GetValueOrDefault("lon")) is not { } lon) return null;
        var ts = q.GetValueOrDefault("timestamp");
        var time = long.TryParse(ts, out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix) : T(ts) ?? DateTimeOffset.UtcNow;
        return new TrackFix(time, lat, lon, D(q.GetValueOrDefault("speed")), D(q.GetValueOrDefault("bearing")), D(q.GetValueOrDefault("accuracy")));
    }

    /// <summary>OwnTracks HTTP payload: {"_type":"location","lat":..,"lon":..,"tst":unix,"vel":kmh,"cog":..,"acc":..}.</summary>
    public static TrackFix? ParseOwnTracks(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || (e.TryGetProperty("_type", out var t) && t.GetString() != "location")) return null;
        if (!e.TryGetProperty("lat", out var la) || !e.TryGetProperty("lon", out var ln)) return null;
        var time = e.TryGetProperty("tst", out var tst) && tst.TryGetInt64(out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix) : DateTimeOffset.UtcNow;
        double? vel = e.TryGetProperty("vel", out var v) && v.TryGetDouble(out var kmh) ? kmh / 1.852 : null;
        double? cog = e.TryGetProperty("cog", out var c) && c.TryGetDouble(out var cd) ? cd : null;
        double? acc = e.TryGetProperty("acc", out var a) && a.TryGetDouble(out var ad) ? ad : null;
        return new TrackFix(time, la.GetDouble(), ln.GetDouble(), vel, cog, acc);
    }

    private static bool TryNmeaCoord(string value, string hemisphere, int degDigits, out double result)
    {
        result = 0;
        if (value.Length <= degDigits || !double.TryParse(value[degDigits..], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)) return false;
        var degrees = int.Parse(value[..degDigits], CultureInfo.InvariantCulture);
        result = degrees + minutes / 60;
        if (hemisphere is "S" or "W") result = -result;
        return true;
    }

    private static double? D(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    private static DateTimeOffset? T(string? s) => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
