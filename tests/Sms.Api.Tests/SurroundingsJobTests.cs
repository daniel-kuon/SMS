using System.Text.Json;

namespace Sms.Api.Tests;

public class SurroundingsJobTests
{
    [Fact]
    public void The_same_harbour_is_queued_once()
    {
        var job = new SurroundingsJob(null!, null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<SurroundingsJob>.Instance);
        var id = Guid.NewGuid();
        Assert.True(job.Enqueue(id));
        Assert.False(job.Enqueue(id));
        Assert.True(job.Enqueue(Guid.NewGuid()));
    }

    [Fact]
    public void Query_covers_the_poi_classes_and_transport_and_the_filter_drops_the_rest()
    {
        var q = SurroundingsJob.Query(54.5, 10.25);
        Assert.Contains("around:3000,54.5,10.25", q);
        Assert.Contains("bus_stop", q); Assert.Contains("tram_stop", q); Assert.Contains("amenity|shop", q);
        using var doc = JsonDocument.Parse("""{"elements":[{"type":"node","id":1,"lat":1,"lon":1,"tags":{"amenity":"toilets"}},{"type":"node","id":2,"lat":1,"lon":1,"tags":{"amenity":"bench"}},{"type":"node","id":3,"lat":1,"lon":1,"tags":{"highway":"bus_stop"}},{"type":"node","id":4,"lat":1,"lon":1,"tags":{"shop":"bakery"}}]}""");
        using var kept = SurroundingsJob.KeepPois(doc);
        Assert.Equal([1L, 3L], kept.RootElement.GetProperty("elements").EnumerateArray().Select(e => e.GetProperty("id").GetInt64()));
    }
}
