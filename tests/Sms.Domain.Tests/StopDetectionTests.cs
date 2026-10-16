using Sms.Domain.Tracking;

namespace Sms.Domain.Tests;

public class StopDetectionTests
{
    [Fact]
    public void Detects_harbour_stops_longer_than_30_minutes()
    {
        var t0 = new DateTimeOffset(2026, 7, 10, 7, 0, 0, TimeSpan.Zero);
        var fixes = new List<TrackFix>();
        for (var m = 0; m <= 60; m += 5) fixes.Add(new TrackFix(t0.AddMinutes(m), 54.3400 + (m % 10 == 0 ? 0.0003 : 0), 10.1600)); // 1 h in Kiel
        for (var m = 65; m < 180; m += 5) fixes.Add(new TrackFix(t0.AddMinutes(m), 54.3400 + (m - 60) * 0.0006, 10.1600 + (m - 60) * 0.0005)); // sailing
        for (var m = 180; m <= 240; m += 5) fixes.Add(new TrackFix(t0.AddMinutes(m), 54.4120, 10.2190)); // 1 h in Laboe
        var stops = TrackMath.DetectStops(fixes);
        Assert.Equal(2, stops.Count);
        Assert.Equal(t0, stops[0].From);
        Assert.Equal(t0.AddMinutes(60), stops[0].To);
        Assert.Equal(54.412, stops[1].Lat, 3);
        Assert.True(TrackMath.DistanceM(fixes.Where(f => f.Time >= stops[0].To && f.Time <= stops[1].From).ToList()) > 5000);
    }
}
