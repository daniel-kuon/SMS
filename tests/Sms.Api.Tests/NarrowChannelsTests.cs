using NetTopologySuite.Geometries;
using Sms.Infrastructure.Routing;

namespace Sms.Api.Tests;

public class NarrowChannelsTests
{
    private static readonly GeometryFactory F = new(new PrecisionModel(), 4326);
    private static Polygon Box(double x1, double y1, double x2, double y2) => (Polygon)F.ToGeometry(new Envelope(x1, x2, y1, y2));

    [Fact]
    public void A_200_m_sound_between_two_islands_gets_a_centreline_reaching_the_open_water_on_both_sides()
    {
        // lat 59: 0.0035° lng ≈ 200 m; the islands are 2.2 km long, the sound opens into open water north and south
        var west = Box(18.000, 59.000, 18.010, 59.020);
        var east = Box(18.0135, 59.000, 18.0235, 59.020);
        var tile = new Envelope(17.95, 18.08, 58.97, 59.05);
        var lines = NarrowChannels.Derive([west, east], tile, new NarrowChannels.Options(), F);
        Assert.NotEmpty(lines);
        // the skeleton forks at the mouths (one spur per corner), so the channel comes as a few lines joined at junctions
        Assert.All(lines.SelectMany(l => l.Coordinates), c => Assert.InRange(c.X, 18.0105, 18.0130)); // inside the sound
        Assert.True(lines.Min(l => l.EnvelopeInternal.MinY) <= 59.0005, $"south end {lines.Min(l => l.EnvelopeInternal.MinY)}");
        Assert.True(lines.Max(l => l.EnvelopeInternal.MaxY) >= 59.0195, $"north end {lines.Max(l => l.EnvelopeInternal.MaxY)}");
        var longest = lines.OrderByDescending(l => l.EnvelopeInternal.Height).First();
        Assert.True(longest.EnvelopeInternal.Height >= 0.015, $"main axis {longest.EnvelopeInternal.Height}"); // ≥ 1.7 km of the 2.2 km sound in one line
        // a lone island on an open shore yields nothing: its fringe is within reach of the open water everywhere
        Assert.Empty(NarrowChannels.Derive([west], tile, new NarrowChannels.Options(), F));
    }
}
