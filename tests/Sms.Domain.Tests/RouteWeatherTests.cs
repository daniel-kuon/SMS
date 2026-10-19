using static Sms.Domain.RouteWeather;

namespace Sms.Domain.Tests;

public class RouteWeatherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Samples_every_5_nm_plus_the_end_across_vertices()
    {
        // ~12 nm north in two segments (vertex at ~3 nm), the duplicate vertex is ignored
        var s = SampleLine([(54.0, 10.0), (54.05, 10.0), (54.05, 10.0), (54.2, 10.0)]);
        Assert.Equal(4, s.Count);
        Assert.Equal([0, 5, 10], s.Take(3).Select(x => x.DistanceNm));
        Assert.Equal(12.0, s[3].DistanceNm, 1);
        Assert.Equal(54.2, s[3].Lat, 6);
        Assert.All(s, x => Assert.Equal(0, x.HeadingDeg, 3));
        Assert.Empty(SampleLine([(54.0, 10.0)]));
    }

    [Fact]
    public void Upwind_stretches_are_slower_and_shift_later_etas()
    {
        var s = SampleLine([(54.0, 10.0), (54.2, 10.0)]); // heading 0
        var free = Chain(s, T0, 5, (_, _) => 180);         // wind from astern
        Assert.Equal(T0.AddHours(2), free[2].Time);
        Assert.DoesNotContain(free, e => e.Tacking);
        // head wind on the first stretch only: 1.4 h instead of 1 h, the rest at full speed
        var mixed = Chain(s, T0, 5, (i, _) => i == 0 ? 30 : null);
        Assert.True(mixed[0].Tacking); Assert.False(mixed[1].Tacking); Assert.False(mixed[^1].Tacking);
        Assert.Equal(2.4, (mixed[2].Time - T0).TotalHours, 6);
    }

    [Fact]
    public void Close_hauled_is_within_45_degrees_either_side()
    {
        Assert.True(IsUpwind(10, 350)); Assert.True(IsUpwind(350, 35)); Assert.True(IsUpwind(90, 135));
        Assert.False(IsUpwind(90, 136)); Assert.False(IsUpwind(0, 180));
        Assert.Equal(90, Bearing(0, 0, 0, 1), 3);
    }
}
