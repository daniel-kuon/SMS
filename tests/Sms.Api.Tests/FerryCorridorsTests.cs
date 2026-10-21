using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class FerryCorridorsTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static LineString L(double x1, double y1, double x2, double y2) => F.CreateLineString([new Coordinate(x1, y1), new Coordinate(x2, y2)]);

    [Fact]
    public void Ferry_corridor_share_is_measured_in_metres()
    {
        // a track along 59.30 N: 40 m off is inside the 50 m corridor, 70 m off is not (a buffer in degrees would be 2x too narrow east-west)
        var ferries = new FerryCorridors([L(18.20, 59.30, 18.24, 59.30)], 50);
        double M(double m) => m / 111_320.0;
        Assert.True(ferries.Along(L(18.21, 59.30 + M(40), 18.23, 59.30 + M(40))));
        Assert.False(ferries.Along(L(18.21, 59.30 + M(70), 18.23, 59.30 + M(70))));
        // across the track: 1 km perpendicular, a tenth of it within the corridor
        var across = L(18.22, 59.30 - M(500), 18.22, 59.30 + M(500));
        Assert.Equal(0.1, ferries.Share(across), 2);
        Assert.False(ferries.Along(across));
        // a 60 m hop across the track lies wholly inside the corridor (accepted)
        Assert.True(ferries.Along(L(18.22, 59.30 - M(30), 18.22, 59.30 + M(30))));
        Assert.False(FerryCorridors.Empty.Along(L(18.21, 59.30, 18.23, 59.30)));
        Assert.False(new FerryCorridors([L(18.20, 59.30, 18.24, 59.30)], 0).Along(L(18.21, 59.30, 18.23, 59.30))); // option 0 = off
    }
}
