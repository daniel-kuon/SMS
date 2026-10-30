using System.Globalization;

namespace Sms.Companion.Nmea;

/// <summary>One checksum-valid NMEA 0183 sentence: talker ("GP", "GN", "II", "WI", ...), type ("RMC") and the fields after the address.</summary>
public sealed record NmeaSentence(string Talker, string Type, string[] Fields)
{
    public string F(int i) => i < Fields.Length ? Fields[i] : "";
    public double? D(int i) => double.TryParse(F(i), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}

public static class NmeaParser
{
    /// <summary>
    /// Parses one line of an NMEA stream or log. Returns null for anything that is not a usable sentence: no '$', a wrong checksum,
    /// proprietary ($P...) or encapsulated (!AIVDM) sentences. A missing checksum is accepted (allowed by NMEA 0183 for some talkers),
    /// a present one must match. Leading log timestamps and NMEA 4.10 tag blocks (\s:...*hh\) are skipped.
    /// </summary>
    public static NmeaSentence? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var s = line.Trim();
        if (s.StartsWith('\\') && s.IndexOf('\\', 1) is var end and > 0) s = s[(end + 1)..];
        var start = s.IndexOf('$');
        if (start < 0) return null;
        s = s[(start + 1)..];
        var star = s.LastIndexOf('*');
        if (star >= 0)
        {
            if (star + 3 > s.Length || !byte.TryParse(s.AsSpan(star + 1, 2), NumberStyles.HexNumber, null, out var expected)) return null;
            s = s[..star];
            if (Checksum(s) != expected) return null;
        }
        var fields = s.Split(',');
        var address = fields[0];
        if (address.Length != 5 || address[0] == 'P' || !address.All(char.IsAsciiLetterOrDigit)) return null;
        return new NmeaSentence(address[..2], address[2..], fields[1..]);
    }

    public static byte Checksum(string body) { byte sum = 0; foreach (var c in body) sum ^= (byte)c; return sum; }

    /// <summary>Builds "$body*HH" (used by the GPX replay and tests).</summary>
    public static string Sentence(string body) => $"${body}*{Checksum(body):X2}";

    /// <summary>ddmm.mmmm + hemisphere to decimal degrees.</summary>
    public static double? Coordinate(string value, string hemisphere)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) return null;
        var deg = Math.Floor(v / 100);
        var result = deg + (v - deg * 100) / 60;
        return hemisphere switch { "N" or "E" => result, "S" or "W" => -result, _ => null };
    }

    /// <summary>hhmmss(.ss) on the given UTC date.</summary>
    public static DateTime? Time(string hhmmss, DateOnly date)
    {
        if (hhmmss.Length < 6 || !int.TryParse(hhmmss[..2], out var h) || !int.TryParse(hhmmss[2..4], out var m)
            || !double.TryParse(hhmmss[4..], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec) || h > 23 || m > 59 || sec >= 61) return null;
        return date.ToDateTime(new TimeOnly(h, m), DateTimeKind.Utc).AddSeconds(sec);
    }

    /// <summary>ddmmyy (RMC) to a date; two-digit years are 2000-2079 / 1980-1999.</summary>
    public static DateOnly? Date(string ddmmyy)
    {
        if (ddmmyy.Length != 6 || !int.TryParse(ddmmyy[..2], out var d) || !int.TryParse(ddmmyy[2..4], out var mo) || !int.TryParse(ddmmyy[4..], out var y)) return null;
        try { return new DateOnly(y >= 80 ? 1900 + y : 2000 + y, mo, d); } catch (ArgumentOutOfRangeException) { return null; }
    }
}

public readonly record struct Reading(double Value, DateTime At);

public sealed record Position(DateTime Time, double Lat, double Lon, double? AccuracyM);

/// <summary>
/// Latest navigation and sensor values built from the sentence stream. Every value carries its receive time (wall clock)
/// so stale sensors drop out of the snapshot. Not thread-safe: the caller serialises <see cref="Apply"/>.
/// </summary>
public sealed class NavState
{
    public Func<DateTime> Clock { get; init; } = () => DateTime.UtcNow;

    public Position? Position { get; private set; }
    public Reading? Sog, Cog, Heading, DepthM, TwsKn, TwdDeg, AwsKn, AwaDeg, PressureHpa, AirTempC, WaterTempC;
    public long Sentences, Used;
    public string? LastTalker;
    private DateOnly? date;
    private double? variation;   // magnetic variation, east positive (RMC or HDG)
    private DateTime lastHdt;

    /// <summary>Applies one sentence; true when it brought a new position.</summary>
    public bool Apply(NmeaSentence s)
    {
        Sentences++;
        var now = Clock();
        var used = true;
        var newPosition = false;
        switch (s.Type)
        {
            case "RMC": // time, status, lat, NS, lon, EW, sog kn, cog T, date, magvar, EW, mode
                if (NmeaParser.Date(s.F(8)) is { } d) date = d;
                if (s.D(9) is { } mv) variation = s.F(10) == "W" ? -mv : mv;
                if (s.F(1) != "A" || s.F(11) == "N") break;
                Set(ref Sog, s.D(6), now);
                Set(ref Cog, s.D(7), now);
                newPosition = SetPosition(s.F(0), s.F(2), s.F(3), s.F(4), s.F(5), null, now);
                break;
            case "GGA": // time, lat, NS, lon, EW, quality, sats, hdop, alt, ...
                if (s.F(5) is "" or "0") break;
                // ponytail: accuracy = HDOP x 5 m (typical UERE); good enough for the portal's 100 m outlier filter
                newPosition = SetPosition(s.F(0), s.F(1), s.F(2), s.F(3), s.F(4), s.D(7) is { } hdop ? Math.Round(hdop * 5, 1) : null, now);
                break;
            case "GLL": // lat, NS, lon, EW, time, status, mode
                if (s.F(5) != "A" || s.F(6) == "N") break;
                newPosition = SetPosition(s.F(4), s.F(0), s.F(1), s.F(2), s.F(3), null, now);
                break;
            case "VTG": // cog T, T, cog M, M, sog kn, N, sog km/h, K, mode
                if (s.F(8) == "N") break;
                Set(ref Cog, s.D(0), now);
                Set(ref Sog, s.D(4) ?? s.D(6) / 1.852, now);
                break;
            case "HDT":
                if (s.D(0) is { } hdt) { Set(ref Heading, hdt, now); lastHdt = now; }
                break;
            case "HDG": // magnetic heading, deviation, EW, variation, EW
                if (s.D(3) is { } v) variation = s.F(4) == "W" ? -v : v;
                if (s.D(0) is { } mag && variation is { } var && now - lastHdt > TimeSpan.FromSeconds(10)) // a true heading (HDT) wins
                {
                    var dev = s.D(1) is { } dv ? (s.F(2) == "W" ? -dv : dv) : 0;
                    Set(ref Heading, Norm(mag + dev + var), now);
                }
                break;
            case "DPT": // depth below transducer m, offset (+ to waterline, - to keel), range
                if (s.D(0) is { } dpt) Set(ref DepthM, s.D(1) is > 0 and var off ? dpt + off : dpt, now);
                break;
            case "DBT": // feet, f, metres, M, fathoms, F
                Set(ref DepthM, s.D(2) ?? s.D(0) * 0.3048 ?? s.D(4) * 1.8288, now);
                break;
            case "MWV": // angle, R/T, speed, unit, status
                if (s.F(4) == "V" || s.D(0) is not { } angle || Knots(s.D(2), s.F(3)) is not { } speed) { used = false; break; }
                if (s.F(1) == "R") { Set(ref AwaDeg, angle > 180 ? angle - 360 : angle, now); Set(ref AwsKn, speed, now); } // portal: -180..180, + = starboard
                else if (s.F(1) == "T")
                {
                    Set(ref TwsKn, speed, now);
                    // true wind angle is relative to the bow: needs a heading (course over ground as fallback)
                    if ((Fresh(Heading, now) ?? Fresh(Cog, now)) is { } bow) Set(ref TwdDeg, Norm(bow + angle), now);
                }
                break;
            case "MWD": // dir T, T, dir M, M, kn, N, m/s, M
                Set(ref TwdDeg, s.D(0) ?? (s.D(2) is { } m && variation is { } vv ? Norm(m + vv) : null), now);
                Set(ref TwsKn, s.D(4) ?? s.D(6) * 1.943844, now);
                break;
            case "MDA": // baro inHg, I, baro bar, B, air C, C, water C, C, rel hum, abs hum, dew C, C, dir T, T, dir M, M, kn, N, m/s, M
                SetPressure(s.D(2) * 1000 ?? s.D(0) * 33.8639, now);
                Set(ref AirTempC, s.D(4), now);
                Set(ref WaterTempC, s.D(6), now);
                Set(ref TwdDeg, s.D(12), now);
                Set(ref TwsKn, s.D(16) ?? s.D(18) * 1.943844, now);
                break;
            case "MTW":
                Set(ref WaterTempC, s.D(0), now);
                break;
            case "XDR": // quadruples: type, value, unit, name
                used = false;
                for (var i = 0; i + 3 <= s.Fields.Length; i += 4)
                {
                    var name = s.F(i + 3).ToUpperInvariant();
                    if (s.D(i + 1) is not { } val) continue;
                    if (s.F(i) == "P" && !name.Contains("OIL") && !name.Contains("ENG")) // barometer: unit B (bar) or P (pascal)
                    { var hpa = s.F(i + 2) == "B" ? val * 1000 : s.F(i + 2) == "P" ? val / 100 : (double?)null; used |= SetPressure(hpa, now); }
                    else if (s.F(i) == "C" && s.F(i + 2) == "C")
                    {
                        if (name.Contains("WATER") || name.Contains("SEA")) { Set(ref WaterTempC, val, now); used = true; }
                        else if (name.Contains("AIR") || name.Contains("OUTSIDE") || name.Contains("ENV")) { Set(ref AirTempC, val, now); used = true; }
                    }
                }
                break;
            default:
                used = false;
                break;
        }
        if (used) { Used++; LastTalker = s.Talker; }
        return newPosition;
    }

    /// <summary>The latest fix with every sensor value received within <paramref name="maxAge"/>; null without a position.</summary>
    public PointDto? Snapshot(TimeSpan maxAge)
    {
        if (Position is not { } p) return null;
        var now = Clock();
        double? V(Reading? r) => r is { } x && now - x.At <= maxAge ? Math.Round(x.Value, 2) : null;
        return new PointDto(p.Time, Math.Round(p.Lat, 7), Math.Round(p.Lon, 7), V(Sog), V(Cog), p.AccuracyM, V(Heading), V(DepthM), V(TwsKn), V(TwdDeg),
            V(AwsKn), V(AwaDeg), V(PressureHpa), V(AirTempC), V(WaterTempC));
    }

    private bool SetPosition(string time, string lat, string ns, string lon, string ew, double? acc, DateTime now)
    {
        if (NmeaParser.Coordinate(lat, ns) is not { } la || NmeaParser.Coordinate(lon, ew) is not { } lo || la is < -90 or > 90 || lo is < -180 or > 180) return false;
        if (la == 0 && lo == 0) return false;
        // GGA/GLL carry no date: the last RMC date, else today (UTC)
        var t = NmeaParser.Time(time, date ?? DateOnly.FromDateTime(now)) ?? now;
        if (Position is { } prev && prev.Time == t && prev.Lat == la && prev.Lon == lo) // RMC + GGA of the same epoch: one fix
        {
            if (acc is not null) Position = prev with { AccuracyM = acc };
            return false;
        }
        Position = new Position(t, la, lo, acc ?? Position?.AccuracyM); // RMC comes before GGA in an epoch: keep the last HDOP-based accuracy
        return true;
    }

    private bool SetPressure(double? hpa, DateTime now)
    {
        if (hpa is not (>= 850 and <= 1100)) return false; // plausible sea-level pressure only (filters engine/oil pressure)
        PressureHpa = new Reading(hpa.Value, now);
        return true;
    }

    private static void Set(ref Reading? r, double? value, DateTime now) { if (value is { } v && double.IsFinite(v)) r = new Reading(v, now); }
    private static double? Fresh(Reading? r, DateTime now) => r is { } x && now - x.At < TimeSpan.FromSeconds(10) ? x.Value : null;
    private static double Norm(double deg) => ((deg % 360) + 360) % 360;
    private static double? Knots(double? v, string unit) => unit switch { "N" => v, "K" => v / 1.852, "M" => v * 1.943844, "S" => v * 0.868976, _ => null };
}
