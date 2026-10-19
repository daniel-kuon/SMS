using Sms.Domain.Tracking;

namespace Sms.Domain;

/// <summary>
/// Weather along a planned route (pure part): sample points every <see cref="StepNm"/> along the line plus its end, chain the ETAs at a
/// boat speed; a stretch whose start sees the wind from within <see cref="CloseHauledDeg"/> of the heading is sailed at speed / <see cref="TackingFactor"/>.
/// </summary>
public static class RouteWeather
{
    public const double StepNm = 5, TackingFactor = 1.4, CloseHauledDeg = 45;

    /// <summary>A sample point: distance from the start and the heading of the line onwards (the last segment's at the end point).</summary>
    public sealed record Sample(double Lat, double Lng, double DistanceNm, double HeadingDeg);
    public sealed record Eta(Sample Point, DateTimeOffset Time, bool Tacking);

    /// <summary>Points every <paramref name="stepNm"/> along the line (lat/lng), the start included, plus the end point.</summary>
    public static List<Sample> SampleLine(IReadOnlyList<(double Lat, double Lng)> line, double stepNm = StepNm)
    {
        var segs = new List<(double La1, double Lo1, double La2, double Lo2, double Nm, double Brg)>();
        for (var i = 0; i + 1 < line.Count; i++)
        {
            var nm = TrackMath.Haversine(line[i].Lat, line[i].Lng, line[i + 1].Lat, line[i + 1].Lng) / TrackMath.Nm;
            if (nm > 1e-9) segs.Add((line[i].Lat, line[i].Lng, line[i + 1].Lat, line[i + 1].Lng, nm, Bearing(line[i].Lat, line[i].Lng, line[i + 1].Lat, line[i + 1].Lng)));
        }
        var res = new List<Sample>();
        if (segs.Count == 0) return res;
        double start = 0, next = 0;
        foreach (var s in segs)
        {
            // linear interpolation inside a segment: routed segments are short, the error is far below the 5 nm spacing
            for (; next < start + s.Nm - 1e-6; next += stepNm)
            {
                var f = (next - start) / s.Nm;
                res.Add(new(s.La1 + (s.La2 - s.La1) * f, s.Lo1 + (s.Lo2 - s.Lo1) * f, next, s.Brg));
            }
            start += s.Nm;
        }
        var last = segs[^1];
        res.Add(new(last.La2, last.Lo2, start, last.Brg));
        return res;
    }

    /// <summary>ETA of every sample. <paramref name="windFromDeg"/> (sample index, time) gives the forecast wind direction there, null without forecast (no tacking assumed).</summary>
    public static List<Eta> Chain(IReadOnlyList<Sample> samples, DateTimeOffset departure, double speedKn, Func<int, DateTimeOffset, double?> windFromDeg)
    {
        var res = new List<Eta>(samples.Count); var t = departure;
        for (var i = 0; i < samples.Count; i++)
        {
            var last = i == samples.Count - 1;
            var tack = !last && windFromDeg(i, t) is { } w && IsUpwind(samples[i].HeadingDeg, w);
            res.Add(new(samples[i], t, tack));
            if (!last) t += TimeSpan.FromHours((samples[i + 1].DistanceNm - samples[i].DistanceNm) / (tack ? speedKn / TackingFactor : speedKn));
        }
        return res;
    }

    /// <summary>Wind from within 45° of the heading: no direct course, the boat tacks.</summary>
    public static bool IsUpwind(double headingDeg, double windFromDeg) => Math.Abs((((windFromDeg - headingDeg) % 360) + 540) % 360 - 180) <= CloseHauledDeg;

    /// <summary>Initial great-circle bearing, 0..360.</summary>
    public static double Bearing(double lat1, double lng1, double lat2, double lng2)
    {
        double r = Math.PI / 180, p1 = lat1 * r, p2 = lat2 * r, dl = (lng2 - lng1) * r;
        var b = Math.Atan2(Math.Sin(dl) * Math.Cos(p2), Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl)) / r;
        return (b + 360) % 360;
    }
}
