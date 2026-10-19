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
    public const string OverpassQuery = """[out:json][timeout:180];way["waterway"~"^(canal|river|fairway)$"]["motorboat"!~"no"]["boat"!~"no"]({0})->.w;(way.w["CEMT"];way.w["ship"~"yes|designated"];way.w["boat"~"yes|designated"];way.w["motorboat"~"yes|designated"];way.w["waterway"="fairway"];way.w["seamark:type"="fairway"];);out geom;(nwr["seamark:type"="bridge"]({0});nwr["bridge"]["seamark:bridge:clearance_height"]({0});nwr["bridge:movable"]({0});nwr["waterway"="lock_gate"]({0});nwr["lock"="yes"]({0});nwr["seamark:type"="gate"]({0});way["bridge"](if:length()>300)({0}););out geom;(way["seamark:type"="fairway"]({0});relation["seamark:type"="fairway"]({0});way["seamark:type"~"^(restricted_area|military_area)$"]({0});relation["seamark:type"~"^(restricted_area|military_area)$"]({0}););out geom;""";

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

    /// <summary>Hard: never enter (Sperrgebiet). Soft: entry restricted at times (Warngebiet, firing range) – avoided, not blocked.</summary>
    private static readonly HashSet<string> NoEntry = ["no_entry", "entry_prohibited", "no_entry_except", "no_boating"];
    private static readonly HashSet<string> RestrictedEntry = ["entry_restricted", "restricted_entry", "to_be_avoided"];
    /// <summary>Categories whose "entry restricted" means ships keep out while active (firing, exercises); a nature reserve's
    /// restricted entry (Wadden Sea zone 2) concerns landing and anchoring, navigation is free.</summary>
    private static readonly HashSet<string> WarningCategories = ["military", "firing_danger_area", "warning", "exercise", "mine_laying", "submarine_exercise", "torpedo_exercise", "danger", "explosives_dumping"];
    private static bool IsWarningArea(IReadOnlyDictionary<string, string> tags)
    {
        if (tags.GetValueOrDefault("seamark:type") == "military_area") return true;
        var cat = tags.GetValueOrDefault("seamark:restricted_area:category") ?? "";
        if (cat.Split(';').Any(c => WarningCategories.Contains(c.Trim()))) return true;
        var name = (tags.GetValueOrDefault("seamark:name") ?? tags.GetValueOrDefault("name") ?? "").ToLowerInvariant();
        return name.Contains("warngebiet") || name.Contains("schießgebiet") || name.Contains("skjutfält") || name.Contains("skyde") || name.Contains("firing");
    }
    /// <summary>Areas a yacht must stay out of (Sperrgebiete): military areas and restricted areas whose restriction forbids entry.</summary>
    public static bool IsNoGoArea(IReadOnlyDictionary<string, string> tags)
    {
        var type = tags.GetValueOrDefault("seamark:type");
        if (type == "military_area") return true;
        if (type != "restricted_area") return false;
        var r = tags.GetValueOrDefault("seamark:restricted_area:restriction") ?? tags.GetValueOrDefault("seamark:restriction") ?? "";
        var parts = r.Split(';').Select(x => x.Trim()).ToList();
        return parts.Any(NoEntry.Contains) || (parts.Any(RestrictedEntry.Contains) && IsWarningArea(tags));
    }

    /// <summary>True when the area must never be entered (Block); false for a restricted/warning area (Avoid).</summary>
    public static bool IsHardNoGo(IReadOnlyDictionary<string, string> tags)
    {
        // a military practice area (Tofta, Todendorf) is closed during exercises only: avoided unless it says no entry
        var r = tags.GetValueOrDefault("seamark:restricted_area:restriction") ?? tags.GetValueOrDefault("seamark:restriction") ?? "";
        return r.Split(';').Any(x => NoEntry.Contains(x.Trim()));
    }

    /// <summary>No-go areas as polygons with a label ("Sperrgebiet Todendorf (entry_prohibited)").</summary>
    public static List<(Polygon area, string label, bool hard)> ParseNoGoAreas(JsonDocument doc, GeometryFactory f)
    {
        var result = new List<(Polygon, string, bool)>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!el.TryGetProperty("tags", out var t)) continue;
            var tags = t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            if (!IsNoGoArea(tags)) continue;
            var name = tags.GetValueOrDefault("seamark:name") ?? tags.GetValueOrDefault("name");
            var restriction = tags.GetValueOrDefault("seamark:restricted_area:restriction") ?? tags.GetValueOrDefault("seamark:restriction") ?? tags.GetValueOrDefault("seamark:type");
            var hard = IsHardNoGo(tags);
            var label = $"{(hard ? "Sperrgebiet" : "Warngebiet")}{(name is null ? "" : " " + name)} ({restriction})";
            var type = el.GetProperty("type").GetString();
            if (type == "way" && el.TryGetProperty("geometry", out var geom)) { if (Ring(geom, f) is { } ring) result.Add((ring, label, hard)); }
            else if (type == "relation" && el.TryGetProperty("members", out var members))
                foreach (var m in members.EnumerateArray())
                    if (m.GetProperty("type").GetString() == "way" && m.TryGetProperty("role", out var role) && role.GetString() != "inner" && m.TryGetProperty("geometry", out var mg) && Ring(mg, f) is { } ring)
                        result.Add((ring, label, hard));
        }
        return result;
    }

    /// <summary>Inland water bodies the coastline polygons count as land: lakes, reservoirs, lagoons, fjords (the Schlei), river and canal areas (Elbe above Glückstadt, Göta älv, Warnow) and harbour basins, ways with a perimeter above 3 km (ponds are noise) and multipolygon relations.</summary>
    public const string OverpassWaterQuery = """[out:json][timeout:180];(way["natural"="water"]["water"~"^(lake|reservoir|lagoon|fjord|river|canal|harbour|basin)$"](if:length()>3000)({0});relation["natural"="water"]["water"~"^(lake|reservoir|lagoon|fjord|river|canal|harbour|basin)$"]({0}););out geom;""";

    /// <summary>Piers, jetties, floating pontoons (man_made=pier + floating=yes), breakwaters and groynes: not part of the coastline, so
    /// the land polygons do not know them; the router steers round them after smoothing (PierAvoidance).</summary>
    public const string OverpassPierQuery = """[out:json][timeout:300];way["man_made"~"^(pier|breakwater|groyne)$"]({0});out geom;""";

    public static bool IsPier(IReadOnlyDictionary<string, string> tags) => tags.GetValueOrDefault("man_made") is "pier" or "breakwater" or "groyne";

    /// <summary>Pier ways as lines, closed ways as polygons (pier/breakwater areas).</summary>
    public static List<(Geometry geom, string label)> ParsePiers(JsonDocument doc, GeometryFactory f)
    {
        var result = new List<(Geometry, string)>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!el.TryGetProperty("tags", out var t) || el.GetProperty("type").GetString() != "way" || !el.TryGetProperty("geometry", out var geom)) continue;
            var tags = t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            if (!IsPier(tags)) continue;
            var coords = geom.EnumerateArray().Select(c => new Coordinate(c.GetProperty("lon").GetDouble(), c.GetProperty("lat").GetDouble())).ToArray();
            if (coords.Length < 2) continue;
            Geometry? g = coords.Length >= 4 && coords[0].Equals2D(coords[^1]) ? Ring(geom, f) : f.CreateLineString(coords);
            if (g is null) continue;
            g.SRID = 4326;
            var kind = tags["man_made"] switch { "breakwater" => "Mole", "groyne" => "Buhne", _ => tags.GetValueOrDefault("floating") == "yes" ? "Schwimmsteg" : "Steg" };
            result.Add((g, tags.GetValueOrDefault("name") is { } name ? $"{kind} {name}" : kind));
        }
        return result;
    }

    /// <summary>Lakes as polygons with holes (islands): closed ways directly, multipolygon relations by stitching their outer/inner member ways into rings. Only areas of at least minKm2.</summary>
    public static List<(Polygon area, string? name)> ParseWaterAreas(JsonDocument doc, GeometryFactory f, double minKm2 = 0.05)
    {
        var result = new List<(Polygon, string?)>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!el.TryGetProperty("tags", out var t)) continue;
            var tags = t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            if (tags.GetValueOrDefault("natural") != "water") continue;
            var name = tags.GetValueOrDefault("name");
            var type = el.GetProperty("type").GetString();
            if (type == "way" && el.TryGetProperty("geometry", out var geom)) { if (Ring(geom, f) is { } ring && AreaKm2(ring) >= minKm2) result.Add((ring, name)); }
            else if (type == "relation" && el.TryGetProperty("members", out var members))
            {
                var outers = new List<Coordinate[]>(); var inners = new List<Coordinate[]>();
                foreach (var m in members.EnumerateArray())
                {
                    if (m.GetProperty("type").GetString() != "way" || !m.TryGetProperty("geometry", out var mg)) continue;
                    var coords = mg.EnumerateArray().Select(c => new Coordinate(c.GetProperty("lon").GetDouble(), c.GetProperty("lat").GetDouble())).ToArray();
                    if (coords.Length < 2) continue;
                    (m.TryGetProperty("role", out var role) && role.GetString() == "inner" ? inners : outers).Add(coords);
                }
                var holes = Stitch(inners, f);
                foreach (var shell in Stitch(outers, f))
                {
                    Polygon poly;
                    try { poly = f.CreatePolygon(shell, holes.Where(h => shell.EnvelopeInternal.Contains(h.EnvelopeInternal) && f.CreatePolygon(shell).Contains(h.StartPoint)).ToArray()); if (!poly.IsValid) poly = poly.Buffer(0) as Polygon ?? poly; }
                    catch (Exception) { continue; }
                    if (AreaKm2(poly) >= minKm2) result.Add((poly, name));
                }
            }
        }
        return result;
    }

    /// <summary>Joins open ways end to end into closed rings (OSM multipolygon members come in any order and direction).</summary>
    internal static List<LinearRing> Stitch(List<Coordinate[]> ways, GeometryFactory f)
    {
        var rings = new List<LinearRing>();
        var open = ways.Select(w => w.ToList()).ToList();
        while (open.Count > 0)
        {
            var ring = open[0]; open.RemoveAt(0);
            var grew = true;
            while (!ring[0].Equals2D(ring[^1]) && grew)
            {
                grew = false;
                for (var i = 0; i < open.Count; i++)
                {
                    var w = open[i];
                    if (w[0].Equals2D(ring[^1])) ring.AddRange(w.Skip(1));
                    else if (w[^1].Equals2D(ring[^1])) { w.Reverse(); ring.AddRange(w.Skip(1)); }
                    else if (w[^1].Equals2D(ring[0])) ring.InsertRange(0, w.Take(w.Count - 1));
                    else if (w[0].Equals2D(ring[0])) { w.Reverse(); ring.InsertRange(0, w.Take(w.Count - 1)); }
                    else continue;
                    open.RemoveAt(i); grew = true; break;
                }
            }
            if (ring.Count >= 4 && ring[0].Equals2D(ring[^1])) rings.Add(f.CreateLinearRing(ring.ToArray()));
        }
        return rings;
    }

    private static double LengthM(LineString l)
    {
        var m = 0.0;
        for (var i = 1; i < l.NumPoints; i++) m += RoutingGraphBuilder.Haversine(l.GetCoordinateN(i - 1).Y, l.GetCoordinateN(i - 1).X, l.GetCoordinateN(i).Y, l.GetCoordinateN(i).X);
        return m;
    }

    private static double AreaKm2(Geometry g)
    {
        var cos = Math.Cos(g.Centroid.Y * Math.PI / 180);
        return g.Area * cos * WaterGraphTiler.MPerDegLat * WaterGraphTiler.MPerDegLat / 1e6;
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
            LineString? line = null;
            if (el.TryGetProperty("geometry", out var geom))
            {
                var coords = geom.EnumerateArray().Select(g => new Coordinate(g.GetProperty("lon").GetDouble(), g.GetProperty("lat").GetDouble())).ToArray();
                if (coords.Length >= 2 && !coords[0].Equals2D(coords[^1])) line = new LineString(coords) { SRID = 4326 };
            }
            // a long bridge way (Great Belt, Øresund: >800 m) counts even without a charted clearance – its charted span nodes carry it
            var longBridge = line is not null && tags.ContainsKey("bridge") && LengthM(line) > 300;
            var isBridge = tags.GetValueOrDefault("seamark:type") == "bridge" || (tags.ContainsKey("bridge") && (tags.ContainsKey("seamark:bridge:clearance_height") || opening)) || longBridge;
            var isLock = tags.GetValueOrDefault("waterway") == "lock_gate" || tags.GetValueOrDefault("lock") == "yes" || tags.GetValueOrDefault("seamark:type") == "gate";
            if (!isBridge && !isLock) continue;
            double lat, lng;
            if (el.TryGetProperty("center", out var c)) { lat = c.GetProperty("lat").GetDouble(); lng = c.GetProperty("lon").GetDouble(); }
            else if (el.TryGetProperty("lat", out var la)) { lat = la.GetDouble(); lng = el.GetProperty("lon").GetDouble(); }
            else if (line is not null) { var mid = line.Centroid; lat = mid.Y; lng = mid.X; }
            else if (el.TryGetProperty("geometry", out var cg) && cg.GetArrayLength() > 0) { var first = cg[0]; lat = first.GetProperty("lat").GetDouble(); lng = first.GetProperty("lon").GetDouble(); }
            else continue;
            if (!isBridge) line = null;
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
