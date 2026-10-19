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
    public void Fuel_counts_only_for_boats_or_at_the_harbour()
    {
        HarbourService road = new("fuel", 898, "Shell", 83), quay = new("fuel", 140, "Hafentankstelle", 7), boat = new("fuel", 1200, "Bootstankstelle", 9);
        Assert.Empty(HarbourServices.Select(new Dictionary<string, bool>(), [road]));                       // road petrol station 900 m away
        Assert.Equal([quay], HarbourServices.Select(new Dictionary<string, bool>(), [road, quay]));          // on the harbour premises
        Assert.Equal([boat], HarbourServices.Select(new Dictionary<string, bool>(), [road, boat], new HashSet<long> { 9 })); // boat=yes / waterway=fuel
    }

    [Fact]
    public void Every_poi_service_has_a_display_slot() =>
        Assert.All(HarbourServices.PoiKinds, k => Assert.Contains(k.Service, HarbourServices.Order));

    [Fact]
    public void Public_transport_kinds_from_osm_tags()
    {
        string? K(params string[] kv) => HarbourServices.TransportKind(kv.Chunk(2).ToDictionary(p => p[0], p => p[1]));
        Assert.Equal("bus_stop", K("highway", "bus_stop"));
        Assert.Equal("bus_stop", K("public_transport", "platform", "bus", "yes"));
        Assert.Null(K("public_transport", "platform", "train", "yes"));          // railway platform: the station is the place
        Assert.Null(K("public_transport", "stop_position", "bus", "yes"));      // the bus's halt on the road
        Assert.Equal("bus_station", K("amenity", "bus_station"));
        Assert.Equal("station", K("railway", "station", "station", "subway"));   // metro counts as train
        Assert.Equal("station", K("railway", "halt"));
        Assert.Equal("station", K("public_transport", "station", "train", "yes"));
        Assert.Null(K("railway", "station", "station", "miniature"));
        Assert.Equal("tram_stop", K("railway", "tram_stop", "public_transport", "platform"));
        Assert.Equal("ferry_terminal", K("amenity", "ferry_terminal"));
        Assert.Null(K("amenity", "restaurant"));
    }

    [Fact]
    public void Bus_stops_nearest_only()
    {
        var r = HarbourServices.Select(new Dictionary<string, bool>(), [new("bus", 800, "Markt", 1), new("bus", 250, "Hafen", 2), new("train", 1400, "Bahnhof", 3)]);
        Assert.Equal([new HarbourService("bus", 250, "Hafen", 2), new HarbourService("train", 1400, "Bahnhof", 3)], r);
    }
}
