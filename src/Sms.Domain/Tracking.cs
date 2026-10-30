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

public enum TrackImportStatus { Queued, Running, Done, Failed }

/// <summary>An uploaded track file (GPX/KML/NMEA, optionally zipped/gzipped/7z), imported by a background job with progress.</summary>
public class TrackImport : Entity
{
    public Guid ShipId { get; set; }
    public Guid? TripId { get; set; }
    public Guid UploadedById { get; set; }
    /// <summary>Set when a tracking device (companion app) uploaded the file.</summary>
    public Guid? DeviceId { get; set; }
    public string FileName { get; set; } = "";
    public TrackFormat Format { get; set; }
    /// <summary>Parsed fixes.</summary>
    public int PointCount { get; set; }
    public TrackImportStatus Status { get; set; } = TrackImportStatus.Queued;
    public int Added { get; set; }
    /// <summary>Fixes the ship already had within ±1 s.</summary>
    public int Duplicates { get; set; }
    /// <summary>Implausible jumps (isolated position spikes) and fixes with equal times.</summary>
    public int Rejected { get; set; }
    public int Tracks { get; set; }
    /// <summary>GPX waypoints (TimeZero events): counted, not imported.</summary>
    public int Waypoints { get; set; }
    /// <summary>GPX routes (TimeZero planned routes): counted, not imported.</summary>
    public int Routes { get; set; }
    public int ProgressPercent { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
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

    public sealed record Stop(DateTimeOffset From, DateTimeOffset To, double Lat, double Lng);

    /// <summary>Spec 12 §12.2 step 4: positions staying within <paramref name="radiusM"/> for at least <paramref name="minDuration"/>.</summary>
    public static List<Stop> DetectStops(IReadOnlyList<TrackFix> ordered, double radiusM = 200, TimeSpan? minDuration = null)
    {
        var min = minDuration ?? TimeSpan.FromMinutes(30);
        var stops = new List<Stop>();
        var i = 0;
        while (i < ordered.Count)
        {
            var anchor = ordered[i];
            var j = i + 1;
            while (j < ordered.Count && Haversine(anchor.Lat, anchor.Lng, ordered[j].Lat, ordered[j].Lng) <= radiusM) j++;
            var last = ordered[j - 1];
            if (last.Time - anchor.Time >= min)
            {
                var cluster = ordered.Skip(i).Take(j - i).ToList();
                stops.Add(new Stop(anchor.Time, last.Time, cluster.Average(f => f.Lat), cluster.Average(f => f.Lng)));
                i = j;
            }
            else i++;
        }
        return stops;
    }

    public static double DistanceM(IReadOnlyList<TrackFix> fixes)
    {
        var d = 0.0;
        for (var i = 1; i < fixes.Count; i++) d += Haversine(fixes[i - 1].Lat, fixes[i - 1].Lng, fixes[i].Lat, fixes[i].Lng);
        return d;
    }

    /// <summary>Drops isolated position spikes (implied speed above <paramref name="maxSpeedKn"/> to the previous kept fix and to the next fix)
    /// and fixes with the time of the previous one. Unlike <see cref="Clean"/>, one bad fix does not reject everything after it.</summary>
    public static List<TrackFix> Despike(IReadOnlyList<TrackFix> sorted, double maxSpeedKn = 30)
    {
        var result = new List<TrackFix>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var f = sorted[i];
            if (result.Count > 0 && f.Time == result[^1].Time) continue;
            // a bad first fix: too far from the next one while the next two agree
            if (result.Count == 0 && i + 2 < sorted.Count && TooFast(f, sorted[i + 1], maxSpeedKn) && !TooFast(sorted[i + 1], sorted[i + 2], maxSpeedKn)) continue;
            if (result.Count > 0 && TooFast(result[^1], f, maxSpeedKn) && (i + 1 == sorted.Count || TooFast(f, sorted[i + 1], maxSpeedKn))) continue;
            result.Add(f);
        }
        return result;
    }

    private static bool TooFast(TrackFix a, TrackFix b, double maxKn)
    {
        var seconds = Math.Abs((b.Time - a.Time).TotalSeconds);
        return seconds > 0 && Haversine(a.Lat, a.Lng, b.Lat, b.Lng) / seconds * 3600 / Nm > maxKn;
    }

    public sealed record Voyage(DateTimeOffset From, DateTimeOffset To, double DistanceM, TrackFix Start, TrackFix End);

    /// <summary>Splits an ordered track into voyages: a recording gap over <paramref name="maxGap"/> (default 6 h) or a stay within
    /// <paramref name="stayRadiusM"/> for at least <paramref name="minStay"/> (default 3 h: harbour or anchorage) ends one. Voyages shorter
    /// than <paramref name="minDistanceM"/> (manoeuvring in the harbour) are dropped.</summary>
    public static List<Voyage> Voyages(IReadOnlyList<TrackFix> ordered, TimeSpan? maxGap = null, TimeSpan? minStay = null, double stayRadiusM = 300, double minDistanceM = 926)
    {
        var gap = maxGap ?? TimeSpan.FromHours(6);
        var result = new List<Voyage>();
        void Add(List<TrackFix> part)
        {
            if (part.Count < 2) return;
            var d = DistanceM(part);
            if (d >= minDistanceM) result.Add(new Voyage(part[0].Time, part[^1].Time, d, part[0], part[^1]));
        }
        var start = 0;
        for (var i = 1; i <= ordered.Count; i++)
        {
            if (i < ordered.Count && ordered[i].Time - ordered[i - 1].Time <= gap) continue;
            var chunk = ordered.Skip(start).Take(i - start).ToList();
            start = i;
            var from = 0;
            foreach (var stay in DetectStops(chunk, stayRadiusM, minStay ?? TimeSpan.FromHours(3)))
            {
                var arrive = chunk.FindIndex(from, f => f.Time >= stay.From);
                var leave = chunk.FindLastIndex(f => f.Time <= stay.To);
                Add(chunk.GetRange(from, arrive - from + 1));
                from = Math.Max(leave, arrive);
            }
            Add(chunk.GetRange(from, chunk.Count - from));
        }
        return result;
    }

    /// <summary>True wind over ground from apparent wind (angle relative to the bow, positive to starboard) and the boat's motion.</summary>
    public static (double SpeedKn, double DirDeg) TrueWind(double awsKn, double awaDeg, double boatKn, double headingDeg)
    {
        var awd = (headingDeg + awaDeg) * Math.PI / 180;
        var hdg = headingDeg * Math.PI / 180;
        // the air relative to the boat blows away from where it comes from; adding the boat's velocity gives the air over ground
        var x = -awsKn * Math.Sin(awd) + boatKn * Math.Sin(hdg);
        var y = -awsKn * Math.Cos(awd) + boatKn * Math.Cos(hdg);
        var dir = (Math.Atan2(-x, -y) * 180 / Math.PI + 360) % 360;
        return (Math.Sqrt(x * x + y * y), dir);
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

public sealed class GpxStats { public int Tracks; public int Waypoints; public int Routes; }

/// <summary>Parsers for GPX, KML/gx:Track, NMEA 0183 RMC, OsmAnd query strings and OwnTracks JSON (spec 12 §12.1).</summary>
public static class TrackParsers
{
    public static List<TrackFix> ParseGpx(Stream s) => ReadGpx(s).ToList();

    /// <summary>Streams the track points (trkpt) of a GPX file with an XmlReader, so archives of millions of points never sit in memory as a DOM.
    /// Waypoints (wpt) and routes (rte) are skipped and only counted in <paramref name="stats"/>; extensions are ignored.</summary>
    public static IEnumerable<TrackFix> ReadGpx(Stream s, GpxStats? stats = null)
    {
        stats ??= new GpxStats();
        using var r = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true, DtdProcessing = DtdProcessing.Ignore });
        r.MoveToContent();
        while (!r.EOF)
        {
            if (r.NodeType != XmlNodeType.Element) { r.Read(); continue; }
            switch (r.LocalName)
            {
                case "wpt": stats.Waypoints++; r.Skip(); continue;
                case "rte": stats.Routes++; r.Skip(); continue;
                case "extensions": r.Skip(); continue;
                case "trk": stats.Tracks++; r.Read(); continue;
                case "trkpt":
                    var lat = D(r.GetAttribute("lat")); var lng = D(r.GetAttribute("lon"));
                    DateTimeOffset? time = null; double? speed = null, course = null;
                    if (!r.IsEmptyElement)
                    {
                        using var sub = r.ReadSubtree();
                        sub.Read(); sub.Read(); // onto the first child
                        while (!sub.EOF)
                        {
                            if (sub.NodeType != XmlNodeType.Element) { sub.Read(); continue; }
                            switch (sub.LocalName)
                            {
                                case "time": time = T(sub.ReadElementContentAsString()); break;
                                case "speed": speed = D(sub.ReadElementContentAsString()) is { } ms ? ms * 3600 / TrackMath.Nm : null; break;
                                case "course": course = D(sub.ReadElementContentAsString()); break;
                                default: sub.Skip(); break;
                            }
                        }
                    }
                    r.Read(); // past </trkpt> (or the empty element)
                    if (lat is { } la && lng is { } ln && time is { } t) yield return new TrackFix(t, la, ln, speed, course);
                    continue;
                default: r.Read(); continue;
            }
        }
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
