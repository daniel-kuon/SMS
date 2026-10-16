using System.Text;
using System.Text.Json;
using Sms.Domain.Tracking;

namespace Sms.Domain.Tests;

public class TrackingTests
{
    private static Stream S(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Parses_gpx_track_points()
    {
        const string gpx = """
            <?xml version="1.0"?><gpx xmlns="http://www.topografix.com/GPX/1/1" version="1.1"><trk><name>t</name><trkseg>
            <trkpt lat="54.40" lon="10.20"><ele>1</ele><time>2026-07-10T08:00:00Z</time></trkpt>
            <trkpt lat="54.41" lon="10.21"><time>2026-07-10T08:10:00Z</time><speed>2.5</speed></trkpt>
            </trkseg></trk></gpx>
            """;
        var fixes = TrackParsers.ParseGpx(S(gpx));
        Assert.Equal(2, fixes.Count);
        Assert.Equal(54.41, fixes[1].Lat);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 8, 10, 0, TimeSpan.Zero), fixes[1].Time);
        Assert.NotNull(fixes[1].SogKn);
    }

    [Fact]
    public void Parses_kml_gx_track_and_plain_linestring()
    {
        const string gx = """
            <kml xmlns="http://www.opengis.net/kml/2.2" xmlns:gx="http://www.google.com/kml/ext/2.2"><Document><Placemark><gx:Track>
            <when>2026-07-10T08:00:00Z</when><when>2026-07-10T09:00:00Z</when>
            <gx:coord>10.20 54.40 0</gx:coord><gx:coord>10.30 54.45 0</gx:coord>
            </gx:Track></Placemark></Document></kml>
            """;
        var fixes = TrackParsers.ParseKml(S(gx));
        Assert.Equal(2, fixes.Count);
        Assert.Equal(54.45, fixes[1].Lat);
        const string plain = """<kml xmlns="http://www.opengis.net/kml/2.2"><Placemark><LineString><coordinates>10.2,54.4,0 10.3,54.5,0 10.4,54.6,0</coordinates></LineString></Placemark></kml>""";
        Assert.Equal(3, TrackParsers.ParseKml(S(plain)).Count);
    }

    [Fact]
    public void Parses_nmea_rmc()
    {
        const string nmea = "$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,*47\n$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A\n$GPRMC,123520,V,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A\n";
        var fixes = TrackParsers.ParseNmea(S(nmea));
        var f = Assert.Single(fixes);
        Assert.Equal(48.1173, f.Lat, 4);
        Assert.Equal(11.5167, f.Lng, 4);
        Assert.Equal(22.4, f.SogKn);
        Assert.Equal(new DateTimeOffset(1994, 3, 23, 12, 35, 19, TimeSpan.Zero), f.Time);
    }

    [Fact]
    public void Parses_osmand_and_owntracks()
    {
        var o = TrackParsers.ParseOsmAnd(new Dictionary<string, string?> { ["lat"] = "54.4", ["lon"] = "10.2", ["timestamp"] = "1783324800", ["speed"] = "5.1", ["bearing"] = "90", ["accuracy"] = "8" });
        Assert.NotNull(o);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1783324800), o!.Value.Time);
        Assert.Equal(5.1, o.Value.SogKn);
        var ot = TrackParsers.ParseOwnTracks(JsonDocument.Parse("""{"_type":"location","lat":54.4,"lon":10.2,"tst":1783324800,"vel":10,"cog":45,"acc":5}""").RootElement);
        Assert.NotNull(ot);
        Assert.Equal(10 / 1.852, ot!.Value.SogKn!.Value, 3);
        Assert.Null(TrackParsers.ParseOwnTracks(JsonDocument.Parse("""{"_type":"lwt"}""").RootElement));
    }

    [Fact]
    public void Clean_drops_outliers_and_interpolates()
    {
        var t0 = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        var fixes = new List<TrackFix>
        {
            new(t0, 54.40, 10.20), new(t0, 54.40, 10.20),                       // duplicate time
            new(t0.AddMinutes(10), 54.41, 10.21),
            new(t0.AddMinutes(11), 55.50, 11.50),                               // 80 nm jump in 1 min -> outlier
            new(t0.AddMinutes(20), 54.42, 10.22, AccuracyM: 500),               // inaccurate
            new(t0.AddMinutes(30), 54.43, 10.23),
        };
        var clean = TrackMath.Clean(fixes);
        Assert.Equal(3, clean.Count);
        var p = TrackMath.Interpolate(clean, t0.AddMinutes(20), TimeSpan.FromMinutes(30));
        Assert.NotNull(p);
        Assert.Equal(54.42, p!.Value.lat, 6);
        Assert.Null(TrackMath.Interpolate(clean, t0.AddHours(5), TimeSpan.FromMinutes(30)));
        Assert.True(TrackMath.DistanceM(clean) > 3000);
    }
}
