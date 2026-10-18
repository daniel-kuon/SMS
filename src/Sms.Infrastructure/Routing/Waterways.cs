using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Linemerge;

namespace Sms.Infrastructure.Routing;

public sealed record Waterway(string? Name, LineString Line, int Ways);
/// <summary>Opening: a movable bridge (bridge:movable=*, seamark:bridge:category=opening/bascule/swing/lift…); ClearanceM is then the open clearance (null = unlimited).</summary>
/// <summary>Line: the bridge's way geometry (a 7 km bridge is crossed wherever a leg meets the line, not only near its centre).</summary>
public sealed record Obstacle(bool IsLock, double Lat, double Lng, double? ClearanceM, string? Name, bool Opening = false, LineString? Line = null);

/// <summary>
/// Spec 10 §10.5 step 1: OSM waterway centrelines (canals, rivers, fairways with navigability tags) become routing
/// passages. Input is Overpass JSON with way geometries (<c>out geom</c>); ways are filtered for navigability and
/// merged end-to-end so one canal becomes one line, which the graph builder turns into an edge chain.
/// </summary>
public static class Waterways
{
    public const string OverpassQuery = """[out:json][timeout:180];way["waterway"~"^(canal|river|fairway)$"]["motorboat"!~"no"]["boat"!~"no"]({0})->.w;(way.w["CEMT"];way.w["ship"~"yes|designated"];way.w["boat"~"yes|designated"];way.w["motorboat"~"yes|designated"];way.w["waterway"="fairway"];way.w["seamark:type"="fairway"];);out geom;(nwr["seamark:type"="bridge"]({0});nwr["bridge"]["seamark:bridge:clearance_height"]({0});nwr["bridge:movable"]({0});nwr["waterway"="lock_gate"]({0});nwr["lock"="yes"]({0});nwr["seamark:type"="gate"]({0}););out center tags;(way["seamark:type"="fairway"]({0});relation["seamark:type"="fairway"]({0});way["seamark:type"~"^(restricted_area|military_area)$"]({0});relation["seamark:type"~"^(restricted_area|military_area)$"]({0}););out geom;""";

    /// <summary>Navigable for yachts: an explicit boat/motorboat/ship permission, a CEMT class, or a fairway.</summary>
    public static bool IsNavigable(IReadOnlyDictionary<string, string> tags)
    {
        if (!tags.TryGetValue("waterway", out var kind) || kind is not ("canal" or "river" or "fairway")) return false;
        if (Is(tags, "boat", "no") || Is(tags, "motorboat", "no") || Is(tags, "ship", "no")) return false;
        return kind == "fairway" || tags.ContainsKey("CEMT") || Is(tags, "boat", "yes", "designated") || Is(tags, "motorboat", "yes", "designated") || Is(tags, "ship", "yes", "designated");
    }

    private static bool Is(IReadOnlyDictionary<string, string> tags, string key, params string[] values) => tags.TryGetValue(key, out var v) && values.Contains(v);

    /// <summary>Parses Overpass JSON (ways with geometry) and merges navigable ways into chains.</summary>
    public static List<Waterway> Parse(JsonDocument doc, GeometryFactory f)
    {
        var lines = new List<(LineString line, string? name)>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (el.GetProperty("type").GetString() != "way" || !el.TryGetProperty("geometry", out var geom)) continue;
            var tags = el.TryGetProperty("tags", out var t) ? t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "") : new Dictionary<string, string>();
            if (!IsNavigable(tags)) continue;
            var coords = geom.EnumerateArray().Select(c => new Coordinate(c.GetProperty("lon").GetDouble(), c.GetProperty("lat").GetDouble())).ToArray();
            if (coords.Length < 2) continue;
            lines.Add((f.CreateLineString(coords), tags.GetValueOrDefault("name")));
        }
        return Merge(lines, f);
    }

    private static readonly HashSet<string> NoEntry = ["no_entry", "entry_prohibited", "entry_restricted", "no_entry_except", "restricted_entry", "no_boating"];
    /// <summary>Areas a yacht must stay out of (Sperrgebiete): military areas and restricted areas whose restriction forbids entry.</summary>
    public static bool IsNoGoArea(IReadOnlyDictionary<string, string> tags)
    {
        var type = tags.GetValueOrDefault("seamark:type");
        if (type == "military_area") return true;
        if (type != "restricted_area") return false;
        var r = tags.GetValueOrDefault("seamark:restricted_area:restriction") ?? tags.GetValueOrDefault("seamark:restriction") ?? "";
        return r.Split(';').Any(x => NoEntry.Contains(x.Trim()));
    }

    /// <summary>No-go areas as polygons with a label ("Sperrgebiet Todendorf (entry_prohibited)").</summary>
    public static List<(Polygon area, string label)> ParseNoGoAreas(JsonDocument doc, GeometryFactory f)
    {
        var result = new List<(Polygon, string)>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!el.TryGetProperty("tags", out var t)) continue;
            var tags = t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            if (!IsNoGoArea(tags)) continue;
            var name = tags.GetValueOrDefault("seamark:name") ?? tags.GetValueOrDefault("name");
            var restriction = tags.GetValueOrDefault("seamark:restricted_area:restriction") ?? tags.GetValueOrDefault("seamark:restriction") ?? tags.GetValueOrDefault("seamark:type");
            var label = $"Sperrgebiet{(name is null ? "" : " " + name)} ({restriction})";
            var type = el.GetProperty("type").GetString();
            if (type == "way" && el.TryGetProperty("geometry", out var geom)) { if (Ring(geom, f) is { } ring) result.Add((ring, label)); }
            else if (type == "relation" && el.TryGetProperty("members", out var members))
                foreach (var m in members.EnumerateArray())
                    if (m.GetProperty("type").GetString() == "way" && m.TryGetProperty("role", out var role) && role.GetString() != "inner" && m.TryGetProperty("geometry", out var mg) && Ring(mg, f) is { } ring)
                        result.Add((ring, label));
        }
        return result;
    }

    /// <summary>Fairway areas (seamark:type=fairway): closed ways and the outer rings of multipolygon relations, as polygons.</summary>
    public static List<(Polygon area, string? name)> ParseFairwayAreas(JsonDocument doc, GeometryFactory f)
    {
        var result = new List<(Polygon, string?)>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!el.TryGetProperty("tags", out var t)) continue;
            var tags = t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            if (tags.GetValueOrDefault("seamark:type") != "fairway") continue;
            var name = tags.GetValueOrDefault("seamark:name") ?? tags.GetValueOrDefault("name");
            var type = el.GetProperty("type").GetString();
            if (type == "way" && el.TryGetProperty("geometry", out var geom)) { if (Ring(geom, f) is { } ring) result.Add((ring, name)); }
            else if (type == "relation" && el.TryGetProperty("members", out var members))
                foreach (var m in members.EnumerateArray())
                    if (m.GetProperty("type").GetString() == "way" && m.TryGetProperty("role", out var role) && role.GetString() != "inner" && m.TryGetProperty("geometry", out var mg) && Ring(mg, f) is { } ring)
                        result.Add((ring, name)); // ponytail: inner rings ignored; outer rings split over several ways are skipped
        }
        return result;
    }

    private static Polygon? Ring(JsonElement geometry, GeometryFactory f)
    {
        var coords = geometry.EnumerateArray().Select(c => new Coordinate(c.GetProperty("lon").GetDouble(), c.GetProperty("lat").GetDouble())).ToArray();
        if (coords.Length < 4 || !coords[0].Equals2D(coords[^1])) return null;
        var poly = f.CreatePolygon(coords);
        return poly.IsValid ? poly : (Polygon?)(poly.Buffer(0) as Polygon);
    }

    /// <summary>Bridges (with vertical clearance where tagged) and locks/gates on the waterways, as points.</summary>
    public static List<Obstacle> ParseObstacles(JsonDocument doc)
    {
        var result = new List<Obstacle>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!el.TryGetProperty("tags", out var t)) continue;
            var tags = t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            var opening = IsOpening(tags);
            var isBridge = tags.GetValueOrDefault("seamark:type") == "bridge" || (tags.ContainsKey("bridge") && (tags.ContainsKey("seamark:bridge:clearance_height") || opening));
            var isLock = tags.GetValueOrDefault("waterway") == "lock_gate" || tags.GetValueOrDefault("lock") == "yes" || tags.GetValueOrDefault("seamark:type") == "gate";
            if (!isBridge && !isLock) continue;
            double lat, lng;
            if (el.TryGetProperty("center", out var c)) { lat = c.GetProperty("lat").GetDouble(); lng = c.GetProperty("lon").GetDouble(); }
            else if (el.TryGetProperty("lat", out var la)) { lat = la.GetDouble(); lng = el.GetProperty("lon").GetDouble(); }
            else continue;
            LineString? line = null;
            if (isBridge && el.TryGetProperty("geometry", out var geom))
            {
                var coords = geom.EnumerateArray().Select(g => new Coordinate(g.GetProperty("lon").GetDouble(), g.GetProperty("lat").GetDouble())).ToArray();
                if (coords.Length >= 2 && !coords[0].Equals2D(coords[^1])) { line = new LineString(coords) { SRID = 4326 }; }
            }
            result.Add(new Obstacle(isLock && !isBridge, lat, lng, isBridge ? (opening ? OpenClearance(tags) : Clearance(tags)) : null, tags.GetValueOrDefault("name") ?? tags.GetValueOrDefault("seamark:name"), isBridge && opening, line));
        }
        return result;
    }

    private static readonly HashSet<string> OpeningCategories = ["opening", "bascule", "swing", "lift", "drawbridge", "folding", "retractable", "submersible", "transporter"];
    /// <summary>Movable bridge: bridge:movable=* (anything but "no") or an opening seamark category.</summary>
    public static bool IsOpening(IReadOnlyDictionary<string, string> tags) =>
        (tags.TryGetValue("bridge:movable", out var m) && m != "no") || (tags.TryGetValue("seamark:bridge:category", out var c) && OpeningCategories.Contains(c)) || tags.ContainsKey("seamark:bridge:clearance_height_open");

    /// <summary>Clearance of an opening bridge when open (seamark:bridge:clearance_height_open); null = unlimited.</summary>
    public static double? OpenClearance(IReadOnlyDictionary<string, string> tags) =>
        tags.TryGetValue("seamark:bridge:clearance_height_open", out var v) ? Clearance(new Dictionary<string, string> { ["maxheight"] = v }) : null;

    /// <summary>First number of seamark:bridge:clearance_height / maxheight ("42", "42.5 m").</summary>
    public static double? Clearance(IReadOnlyDictionary<string, string> tags)
    {
        foreach (var key in new[] { "seamark:bridge:clearance_height", "seamark:bridge:clearance_height_closed", "maxheight" })
        {
            if (!tags.TryGetValue(key, out var v)) continue;
            var m = System.Text.RegularExpressions.Regex.Match(v.Replace(',', '.'), @"\d+(\.\d+)?");
            if (m.Success && double.TryParse(m.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
        }
        return null;
    }

    /// <summary>Joins ways that share end points (degree-2 chains); junctions stay separate lines that share a coordinate.</summary>
    public static List<Waterway> Merge(IReadOnlyList<(LineString line, string? name)> lines, GeometryFactory f)
    {
        var merger = new LineMerger();
        foreach (var (line, _) in lines) merger.Add(line);
        var result = new List<Waterway>();
        foreach (var g in merger.GetMergedLineStrings())
        {
            if (g is not LineString merged || merged.NumPoints < 2) continue;
            // name = most frequent name among the ways this chain was built from
            var parts = lines.Where(l => merged.EnvelopeInternal.Covers(l.line.EnvelopeInternal) && merged.Covers(l.line)).ToList(); // the ways this chain is made of (touching branches are not)
            var name = parts.Where(p => p.name is { Length: > 0 }).GroupBy(p => p.name).OrderByDescending(gr => gr.Count()).Select(gr => gr.Key).FirstOrDefault();
            merged.SRID = 4326;
            result.Add(new Waterway(name, merged, parts.Count));
        }
        return result;
    }
}
