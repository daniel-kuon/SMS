using Sms.Domain;
using Xunit;

namespace Sms.Domain.Tests;

public class DepthBandsTests
{
    private static readonly (double, double)[] Emodnet = [(0, 1.8288), (1.8288, 3.6576), (3.6576, 5.4864), (5.4864, 9.144), (9.144, 18.288)];

    [Fact]
    public void A_1_8_m_boat_passes_the_1_83_to_3_66_band()
        => Assert.Equal(1.8288, DepthBands.EffectiveFloor(1.8 + 0.5, Emodnet), 4);

    [Fact]
    public void A_deeper_boat_needs_the_next_band()
        => Assert.Equal(3.0, DepthBands.EffectiveFloor(2.5 + 0.5, Emodnet), 4); // the 1.83 band (middle 2.74) stays closed

    [Fact]
    public void Never_looser_than_the_limit_when_no_band_passes()
        => Assert.Equal(30, DepthBands.EffectiveFloor(30, Emodnet));
}
