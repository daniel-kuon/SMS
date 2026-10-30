using Sms.Companion.Nmea;

namespace Sms.Companion.Tests;

public class NmeaTests
{
    private static DateTime now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private static NavState Nav() => new() { Clock = () => now };
    private static string S(string body) => NmeaParser.Sentence(body);

    private static NavState Feed(params string[] lines)
    {
        var nav = Nav();
        foreach (var l in lines) nav.Apply(NmeaParser.Parse(l) ?? throw new Exception($"not parsed: {l}"));
        return nav;
    }

    [Fact]
    public void Checksum_is_validated()
    {
        const string ok = "$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A";
        Assert.NotNull(NmeaParser.Parse(ok));
        Assert.Null(NmeaParser.Parse(ok.Replace("*6A", "*6B")));
        Assert.Null(NmeaParser.Parse(ok.Replace("022.4", "022.5")));
        Assert.Null(NmeaParser.Parse("$GPRMC,123519,A*Z1"));
        Assert.NotNull(NmeaParser.Parse("$IIHDT,123.4,T")); // no checksum: accepted
    }

    [Fact]
    public void Talkers_tag_blocks_and_log_prefixes()
    {
        Assert.Equal("GN", NmeaParser.Parse(S("GNRMC,120000,A,5430.000,N,01015.000,E,5.0,90.0,301026,,,A"))!.Talker);
        Assert.Equal("WI", NmeaParser.Parse(S("WIMWV,45.0,R,12.0,N,A"))!.Talker);
        Assert.Equal("HDT", NmeaParser.Parse(@"\s:GPS1,c:1730000000*1F\" + S("IIHDT,10.0,T"))!.Type);
        Assert.Equal("RMC", NmeaParser.Parse("2026-10-30 12:00:00.123 " + S("GPRMC,120000,A,5430.000,N,01015.000,E,5.0,90.0,301026,,,A"))!.Type);
        Assert.Null(NmeaParser.Parse(S("PGRME,15.0,M,45.0,M,25.0,M"))); // proprietary
        Assert.Null(NmeaParser.Parse("!AIVDM,1,1,,A,13aEOK?P00PD2wVMdLDRhgvL289?,0*26"));
        Assert.Null(NmeaParser.Parse("garbage"));
    }

    [Fact]
    public void Rmc_gives_position_speed_course_and_date()
    {
        var nav = Feed(S("GNRMC,101530.50,A,5430.1234,N,01015.5000,W,6.2,123.4,301026,2.0,E,A"));
        var p = nav.Snapshot(TimeSpan.FromMinutes(2))!;
        Assert.Equal(new DateTime(2026, 10, 30, 10, 15, 30, 500, DateTimeKind.Utc), p.T);
        Assert.Equal(54 + 30.1234 / 60, p.Lat, 6);
        Assert.Equal(-(10 + 15.5 / 60), p.Lon, 6);
        Assert.Equal(6.2, p.Sog);
        Assert.Equal(123.4, p.Cog);
    }

    [Fact]
    public void Invalid_fixes_are_ignored()
    {
        Assert.Null(Feed(S("GPRMC,101530,V,5430.1234,N,01015.5,E,0,0,301026,,,N")).Position);
        Assert.Null(Feed(S("GPGGA,101530,5430.1234,N,01015.5,E,0,00,99.9,,M,,M,,")).Position);
        Assert.Null(Feed(S("GPGLL,5430.1234,N,01015.5,E,101530,V,N")).Position);
    }

    [Fact]
    public void Gga_after_rmc_of_the_same_epoch_adds_accuracy_not_a_new_fix()
    {
        var nav = Nav();
        Assert.True(nav.Apply(NmeaParser.Parse(S("GPRMC,101530,A,5430.000,N,01015.000,E,5.0,90.0,301026,,,A"))!));
        Assert.False(nav.Apply(NmeaParser.Parse(S("GPGGA,101530,5430.000,N,01015.000,E,1,08,1.2,10.0,M,40.0,M,,"))!));
        Assert.Equal(6.0, nav.Position!.AccuracyM);
        Assert.Equal(new DateTime(2026, 10, 30, 10, 15, 30, DateTimeKind.Utc), nav.Position.Time);
    }

    [Fact]
    public void Gll_and_vtg()
    {
        var nav = Feed(S("GPGLL,5430.000,S,01015.000,E,101530,A,A"), S("GPVTG,210.0,T,208.0,M,7.5,N,13.9,K,A"));
        Assert.Equal(-54.5, nav.Position!.Lat, 6);
        Assert.Equal(210.0, nav.Cog!.Value.Value);
        Assert.Equal(7.5, nav.Sog!.Value.Value);
        Assert.Equal(13.9 / 1.852, Feed(S("GPVTG,210.0,T,,M,,N,13.9,K,A")).Sog!.Value.Value, 6);
    }

    [Fact]
    public void Heading_true_and_magnetic()
    {
        Assert.Equal(123.4, Feed(S("IIHDT,123.4,T")).Heading!.Value.Value);
        Assert.Equal(5.0, Feed(S("HCHDG,10.0,1.0,W,4.0,W")).Heading!.Value.Value, 6); // 10 - 1 (deviation W) - 4 (variation W)
        Assert.Equal(358.0, Feed(S("HCHDG,2.0,0.0,E,4.0,W")).Heading!.Value.Value, 6);
    }

    [Fact]
    public void Hdg_uses_rmc_variation_and_yields_to_hdt()
    {
        Assert.Equal(13.0, Feed(S("GPRMC,101530,A,5430.000,N,01015.000,E,5.0,90.0,301026,3.0,E,A"), S("HCHDG,10.0,,,,")).Heading!.Value.Value, 6);
        Assert.Null(Feed(S("HCHDG,10.0,,,,")).Heading); // variation unknown: not a true heading
        Assert.Equal(200.0, Feed(S("IIHDT,200.0,T"), S("HCHDG,10.0,0,E,0,E")).Heading!.Value.Value);
    }

    [Fact]
    public void Depth_dpt_and_dbt()
    {
        Assert.Equal(12.8, Feed(S("SDDPT,12.3,0.5,100")).DepthM!.Value.Value, 6);  // + offset: to the waterline
        Assert.Equal(12.3, Feed(S("SDDPT,12.3,-1.2,100")).DepthM!.Value.Value, 6); // - offset (to the keel): below transducer
        Assert.Equal(5.5, Feed(S("SDDBT,18.0,f,5.5,M,3.0,F")).DepthM!.Value.Value);
        Assert.Equal(18.0 * 0.3048, Feed(S("SDDBT,18.0,f,,M,,F")).DepthM!.Value.Value, 6);
    }

    [Fact]
    public void Wind_apparent_true_and_mwd()
    {
        var nav = Feed(S("WIMWV,045.0,R,10.0,M,A"));
        Assert.Equal(45.0, nav.AwaDeg!.Value.Value);
        Assert.Equal(10 * 1.943844, nav.AwsKn!.Value.Value, 4);
        nav = Feed(S("IIHDT,350.0,T"), S("WIMWV,030.0,T,18.0,N,A"));
        Assert.Equal(18.0, nav.TwsKn!.Value.Value);
        Assert.Equal(20.0, nav.TwdDeg!.Value.Value, 6); // heading 350 + 30 on the bow
        Assert.Null(Feed(S("WIMWV,030.0,R,18.0,N,V")).AwsKn); // status V
        Assert.Equal(-90.0, Feed(S("WIMWV,270.0,R,8.0,K,A")).AwaDeg!.Value.Value); // port side is negative for the portal
        nav = Feed(S("WIMWD,270.0,T,268.0,M,22.0,N,11.3,M"));
        Assert.Equal(270.0, nav.TwdDeg!.Value.Value);
        Assert.Equal(22.0, nav.TwsKn!.Value.Value);
    }

    [Fact]
    public void Mda_xdr_mtw()
    {
        var nav = Feed(S("WIMDA,29.9180,I,1.0132,B,14.5,C,12.0,C,80.0,,9.0,C,250.0,T,248.0,M,15.0,N,7.7,M"));
        Assert.Equal(1013.2, nav.PressureHpa!.Value.Value, 6);
        Assert.Equal(14.5, nav.AirTempC!.Value.Value);
        Assert.Equal(12.0, nav.WaterTempC!.Value.Value);
        Assert.Equal(250.0, nav.TwdDeg!.Value.Value);
        Assert.Equal(15.0, nav.TwsKn!.Value.Value);

        nav = Feed(S("IIXDR,P,1.0089,B,Barometer,C,17.5,C,AirTemp,C,11.0,C,WaterTemp,C,85.0,C,ENGINE#0"));
        Assert.Equal(1008.9, nav.PressureHpa!.Value.Value, 6);
        Assert.Equal(17.5, nav.AirTempC!.Value.Value);
        Assert.Equal(11.0, nav.WaterTempC!.Value.Value);
        Assert.Equal(1012.5, Feed(S("WIXDR,P,101250,P,BARO")).PressureHpa!.Value.Value, 6);
        Assert.Null(Feed(S("IIXDR,P,3.5,B,OIL")).PressureHpa);
        Assert.Equal(9.5, Feed(S("YXMTW,9.5,C")).WaterTempC!.Value.Value);
    }

    [Fact]
    public void Snapshot_drops_stale_sensor_values()
    {
        var t = now;
        var nav = new NavState { Clock = () => t };
        nav.Apply(NmeaParser.Parse(S("SDDPT,4.0,0.0"))!);
        t = t.AddMinutes(3);
        nav.Apply(NmeaParser.Parse(S("GPRMC,120300,A,5430.000,N,01015.000,E,5.0,90.0,301026,,,A"))!);
        var p = nav.Snapshot(TimeSpan.FromMinutes(2))!;
        Assert.Null(p.DepthM);
        Assert.Equal(5.0, p.Sog);
    }
}
