namespace Sms.Domain.Tests;

public class HarbourServicesTests
{
    [Fact]
    public void Nearest_per_service_within_range_in_display_order_and_flags_are_on_site()
    {
        var result = HarbourServices.Select(
            new Dictionary<string, bool> { ["fuel"] = true, ["power_supply"] = true, ["toilets"] = false, ["unknown"] = true },
            [
                new("restaurant", 400, "Klabautermann", 1), new("restaurant", 120, "Imbiss bei Moni", 2),
                new("fuel", 900, "Tankstelle", 3),          // on-site flag wins
                new("toilets", 200, "WC", 4),               // flag false: the POI stays
                new("pharmacy", 1501, "Apotheke", 5),       // too far
                new("nightclub", 10, "Disco", 6),           // not a sailor's service
            ]);

        Assert.Equal(["fuel", "power_supply", "toilets", "restaurant"], result.Select(s => s.Kind));
        Assert.Equal(new HarbourService("fuel", 0, null, null), result[0]);
        Assert.Equal(new HarbourService("restaurant", 120, "Imbiss bei Moni", 2), result[3]);
    }

    [Fact]
    public void Every_poi_service_has_a_display_slot() =>
        Assert.All(HarbourServices.PoiKinds, k => Assert.Contains(k.Service, HarbourServices.Order));
}
