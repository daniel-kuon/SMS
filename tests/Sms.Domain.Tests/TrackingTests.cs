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

    [Fact]
    public void Streams_timezero_gpx_with_fractional_times_and_skips_waypoints_and_routes()
    {
        const string gpx = """
            <?xml version="1.0"?>
            <gpx xmlns:xsd="http://www.w3.org/2001/XMLSchema" version="1.1" xmlns="http://www.topografix.com/GPX/1/1">
              <wpt lat="39.5673897084446" lon="2.64186278525243"><time>2026-08-13T10:25:15Z</time><name>Event 0336</name>
                <extensions><MxTimeZeroSymbol>67</MxTimeZeroSymbol><T0Color>1</T0Color></extensions></wpt>
              <rte><name>Rte 2016-09-22</name><cmt /><extensions><T0Color>3</T0Color><Comment></Comment></extensions>
                <rtept lat="59.22" lon="18.39"><time>2016-09-22T10:00:00Z</time><extensions><T0StepSide>Über</T0StepSide></extensions></rtept></rte>
              <trk><name>Ownship</name><extensions><guid>d58ff347-5307-11e6-be7e-48a9d281a636</guid></extensions><trkseg>
                <trkpt lat="55.3210499999952" lon="15.18585"><time>2016-07-26T08:06:20.4106144Z</time></trkpt>
                <trkpt lat="55.3210999996205" lon="15.1858500000004"><time>2016-07-26T08:06:24.3030857Z</time><extensions><x>1</x></extensions></trkpt>
                <trkpt lat="55.3211" lon="15.18585" />
              </trkseg></trk>
              <trk><name>Ownship</name><extensions><guid>e0000000-5307-11e6-be7e-48a9d281a636</guid></extensions><trkseg>
                <trkpt lat="39.5" lon="2.6"><time>2026-08-13T10:00:00.5Z</time></trkpt>
              </trkseg></trk>
            </gpx>
            """;
        var stats = new GpxStats();
        var fixes = TrackParsers.ReadGpx(S(gpx), stats).ToList();
        Assert.Equal(3, fixes.Count); // the point without time is dropped, the route point never shows up
        Assert.Equal(new DateTimeOffset(2016, 7, 26, 8, 6, 20, TimeSpan.Zero).AddTicks(4106144), fixes[0].Time);
        Assert.Equal(55.3210999996205, fixes[1].Lat);
        Assert.Equal(39.5, fixes[2].Lat);
        Assert.Equal((2, 1, 1), (stats.Tracks, stats.Waypoints, stats.Routes));
    }

    [Fact]
    public void Despike_drops_a_single_jump_but_not_everything_after_it()
    {
        var t0 = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
        TrackFix F(int s, double lat) => new(t0.AddSeconds(s), lat, 10.0);
        var kept = TrackMath.Despike([F(0, 54.0000), F(10, 54.0002), F(20, 54.5), F(30, 54.0006), F(30, 54.0006), F(40, 54.0008)]);
        Assert.Equal([0, 10, 30, 40], kept.Select(f => (int)(f.Time - t0).TotalSeconds));
        // a bad first fix only costs itself
        Assert.Equal(3, TrackMath.Despike([F(0, 55.0), F(10, 54.0002), F(20, 54.0004), F(30, 54.0006)]).Count);
    }

    [Fact]
    public void Voyages_split_at_long_gaps_and_harbour_stays()
    {
        var t0 = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var fixes = new List<TrackFix>();
        void Sail(DateTimeOffset start, double lat, int minutes) { for (var i = 0; i <= minutes; i++) fixes.Add(new TrackFix(start.AddMinutes(i), lat + i * 0.0015, 10.0)); } // ~5 kn north
        void Stay(DateTimeOffset start, double lat, int minutes) { for (var i = 0; i <= minutes; i++) fixes.Add(new TrackFix(start.AddMinutes(i), lat, 10.0)); }
        Sail(t0, 54.0, 60);                        // voyage 1: 08:00–09:00
        Stay(t0.AddMinutes(61), 54.09, 240);       // 4 h in harbour
        Sail(t0.AddMinutes(302), 54.09, 60);       // voyage 2
        Sail(t0.AddDays(1), 55.0, 120);            // next day after a gap: voyage 3
        Stay(t0.AddDays(2), 56.0, 30);             // a short stay only: no voyage
        var v = TrackMath.Voyages(fixes);
        Assert.Equal(3, v.Count);
        Assert.Equal(t0, v[0].From);
        Assert.InRange(v[0].DistanceM / TrackMath.Nm, 4.5, 5.5);
        Assert.Equal(t0.AddDays(1).AddMinutes(120), v[2].To);
        Assert.True(v[1].From >= t0.AddMinutes(61 + 240) && v[1].From <= t0.AddMinutes(302));
    }

    [Fact]
    public void True_wind_from_apparent_wind_and_boat_motion()
    {
        // boat north at 5 kn, true wind 10 kn from east: apparent 11.18 kn at 63.4° to starboard
        var (tws, twd) = TrackMath.TrueWind(Math.Sqrt(125), Math.Atan2(10, 5) * 180 / Math.PI, 5, 0);
        Assert.Equal(10, tws, 3);
        Assert.Equal(90, twd, 3);
        // no true wind: head wind equals the boat speed
        Assert.Equal(0, TrackMath.TrueWind(5, 0, 5, 270).SpeedKn, 6);
    }
}
