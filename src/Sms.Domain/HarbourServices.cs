namespace Sms.Domain;

/// <summary>A service near a harbour: the nearest matching OSM POI (straight-line metres) or an on-site facility flag (0 m, no POI).</summary>
public record HarbourService(string Kind, int DistanceM, string? Name, long? PoiId);

/// <summary>Which services count for sailors, which OSM POI kinds provide them, and how the nearest one per service is picked.</summary>
public static class HarbourServices
{
    public const int MaxDistanceM = 1500;
    /// <summary>A road petrol station this close is on the harbour premises and counts as fuel for boats; farther away only boat fuel counts.</summary>
    public const int FuelAtHarbourM = 150;

    /// <summary>Service → (Pois.Class, Pois.Kind). Several kinds may provide one service; the nearest wins.</summary>
    public static readonly (string Service, string Class, string Kind)[] PoiKinds =
    [
        ("fuel", "amenity", "fuel"),
        ("drinking_water", "amenity", "drinking_water"), ("drinking_water", "amenity", "water_point"),
        ("toilets", "amenity", "toilets"),
        ("shower", "amenity", "shower"),
        ("laundry", "shop", "laundry"), ("laundry", "amenity", "washing_machine"),
        ("sanitary_dump_station", "amenity", "sanitary_dump_station"),
        ("waste_disposal", "amenity", "waste_disposal"),
        ("slipway", "leisure", "slipway"),
        ("chandlery", "shop", "ship_chandler"), ("chandlery", "shop", "chandler"), ("chandlery", "shop", "boat"),
        ("boat_repair", "craft", "boatbuilder"), ("boat_repair", "shop", "boat_repair"),
        ("supermarket", "shop", "supermarket"),
        ("convenience", "shop", "convenience"),
        ("bakery", "shop", "bakery"), ("bakery", "craft", "bakery"),
        ("restaurant", "amenity", "restaurant"), ("restaurant", "amenity", "fast_food"),
        ("cafe", "amenity", "cafe"),
        ("pharmacy", "amenity", "pharmacy"), ("pharmacy", "healthcare", "pharmacy"),
        ("atm", "amenity", "atm"), ("atm", "amenity", "bank"),
        ("bicycle_rental", "amenity", "bicycle_rental"),
        ("bus_station", "amenity", "bus_station"),
    ];

    /// <summary>Display order; power_supply and internet_access come only from the harbour's own facility flags.</summary>
    public static readonly string[] Order =
    [
        "fuel", "power_supply", "drinking_water", "toilets", "shower", "laundry", "sanitary_dump_station", "waste_disposal", "slipway", "chandlery", "boat_repair",
        "supermarket", "convenience", "bakery", "restaurant", "cafe", "pharmacy", "atm", "internet_access", "bicycle_rental", "bus_station",
    ];

    /// <summary>
    /// Nearest candidate per service within <see cref="MaxDistanceM"/>; a facility flag of the harbour itself counts as on site (0 m).
    /// Fuel counts only for boats: a POI in <paramref name="boatFuel"/> (OSM boat=yes/only or waterway=fuel) or one within <see cref="FuelAtHarbourM"/>.
    /// </summary>
    public static List<HarbourService> Select(IReadOnlyDictionary<string, bool> facilities, IEnumerable<HarbourService> candidates, IReadOnlySet<long>? boatFuel = null)
    {
        var best = new Dictionary<string, HarbourService>();
        foreach (var c in candidates)
        {
            if (c.Kind == "fuel" && c.DistanceM > FuelAtHarbourM && !(c.PoiId is { } id && boatFuel?.Contains(id) == true)) continue;
            if (c.DistanceM <= MaxDistanceM && Order.Contains(c.Kind) && (!best.TryGetValue(c.Kind, out var b) || c.DistanceM < b.DistanceM)) best[c.Kind] = c;
        }
        foreach (var (key, on) in facilities)
            if (on && Order.Contains(key)) best[key] = new HarbourService(key, 0, null, null);
        return Order.Where(best.ContainsKey).Select(k => best[k]).ToList();
    }
}
