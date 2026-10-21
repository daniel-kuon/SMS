using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Linemerge;
using Sms.Domain;

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
    public const string OverpassQuery = """[out:json][timeout:180];way["waterway"~"^(canal|river|fairway)$"]["motorboat"!~"no"]["boat"!~"no"]({0})->.w;(way.w["CEMT"];way.w["ship"~"yes|designated"];way.w["boat"~"yes|designated"];way.w["motorboat"~"yes|designated"];way.w["waterway"="fairway"];way.w["seamark:type"="fairway"];);out geom;(nwr["seamark:type"="bridge"]({0});nwr["bridge"]["seamark:bridge:clearance_height"]({0});nwr["bridge:movable"]({0});nwr["bridge"="movable"]({0});nwr["waterway"="lock_gate"]({0});nwr["lock"="yes"]({0});nwr["seamark:type"="gate"]({0});way["bridge"](if:length()>300)({0}););out geom;(way["seamark:type"="fairway"]({0});relation["seamark:type"="fairway"]({0});way["seamark:type"~"^(restricted_area|military_area)$"]({0});relation["seamark:type"~"^(restricted_area|military_area)$"]({0}););out geom;""";

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

    /// <summary>Inland water bodies the coastline polygons count as land: lakes, reservoirs, lagoons, fjords (the Schlei), river and canal areas (Elbe above Glückstadt, Göta älv, Warnow) and harbour basins, ways with a perimeter above 3 km (ponds are noise) and multipolygon relations. Harbour basins come at any
    /// size (from HarbourMinKm2): the Glückstadt Außenhafen (0.04 km², 1.2 km round) stayed land, the harbour's start snapped 330 m out
    /// and its entrance from the Nebenelbe was missing from the graph.</summary>
    public const string OverpassWaterQuery = """[out:json][timeout:180];(way["natural"="water"]["water"~"^(lake|reservoir|lagoon|fjord|river|canal)$"](if:length()>3000)({0});way["natural"="water"]["water"~"^(harbour|basin)$"]({0});relation["natural"="water"]["water"~"^(lake|reservoir|lagoon|fjord|river|canal|harbour|basin)$"]({0}););out geom;""";

    /// <summary>Piers, jetties, floating pontoons (man_made=pier + floating=yes), breakwaters and groynes: not part of the coastline, so
    /// the land polygons do not know them; the router steers round them after smoothing (PierAvoidance).</summary>
    public const string OverpassPierQuery = """[out:json][timeout:300];way["man_made"~"^(pier|breakwater|groyne)$"]({0});out geom;""";

    /// <summary>Guide lines for assumed passages: ferry ways (route=ferry, also the member ways of ferry relations, which carry the
    /// route name) and recommended tracks. The relations come last, without geometry, for the names.</summary>
    public const string OverpassGuideQuery = """[out:json][timeout:180];relation["route"="ferry"]({0})->.r;(way["route"="ferry"]({0});way(r.r)({0});way["seamark:type"="recommended_track"]({0}););out geom;.r out body;""";

    /// <summary>Ferry lines and recommended tracks, merged end to end per kind, labelled "Fährlinie Strömma kanalturen till Sandhamn" /
    /// "Empfohlener Kurs …" (the way's name, else its ferry relation's).</summary>
    public static List<Waterway> ParseGuideLines(JsonDocument doc, GeometryFactory f)
    {
        var relationName = RelationNames(doc);
        var ferries = new List<(LineString, string?)>(); var tracks = new List<(LineString, string?)>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (el.GetProperty("type").GetString() != "way" || !el.TryGetProperty("geometry", out var geom)) continue;
            var tags = el.TryGetProperty("tags", out var t) ? t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "") : new Dictionary<string, string>();
            var coords = geom.EnumerateArray().Select(c => new Coordinate(c.GetProperty("lon").GetDouble(), c.GetProperty("lat").GetDouble())).ToArray();
            if (coords.Length < 2) continue;
            var name = tags.GetValueOrDefault("name") ?? relationName.GetValueOrDefault(el.GetProperty("id").GetInt64()) ?? tags.GetValueOrDefault("seamark:name");
            var track = tags.GetValueOrDefault("seamark:type") == "recommended_track" && tags.GetValueOrDefault("route") != "ferry";
            (track ? tracks : ferries).Add((f.CreateLineString(coords), $"{(track ? "Empfohlener Kurs" : "Fährlinie")}{(string.IsNullOrEmpty(name) ? "" : " " + name)}"));
        }
        return [.. Merge(ferries, f), .. Merge(tracks, f)];
    }

    /// <summary>Scheduled ferry tracks (correction kind Ferry): every route=ferry way of the guide query, one line per way (no merging),
    /// cable ferries (ferry:cable=yes, ferry=cable: a few metres of a river crossing, nothing about the sound) skipped. Name: the way's,
    /// else its ferry relation's.</summary>
    public static List<(LineString line, string? name)> ParseFerryLines(JsonDocument doc, GeometryFactory f)
    {
        var relationName = RelationNames(doc);
        var result = new List<(LineString, string?)>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (el.GetProperty("type").GetString() != "way" || !el.TryGetProperty("geometry", out var geom) || !el.TryGetProperty("tags", out var t)) continue;
            var tags = t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            if (tags.GetValueOrDefault("route") != "ferry" || tags.GetValueOrDefault("ferry:cable") == "yes" || tags.GetValueOrDefault("ferry") == "cable") continue;
            var coords = geom.EnumerateArray().Select(c => new Coordinate(c.GetProperty("lon").GetDouble(), c.GetProperty("lat").GetDouble())).ToArray();
            if (coords.Length < 2) continue;
            var line = f.CreateLineString(coords); line.SRID = 4326;
            var name = tags.GetValueOrDefault("name") ?? relationName.GetValueOrDefault(el.GetProperty("id").GetInt64());
            result.Add((line, string.IsNullOrEmpty(name) ? null : name));
        }
        return result;
    }

    /// <summary>Member way id → name of its (first) named relation.</summary>
    private static Dictionary<long, string> RelationNames(JsonDocument doc)
    {
        var relationName = new Dictionary<long, string>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (el.GetProperty("type").GetString() != "relation" || !el.TryGetProperty("tags", out var t) || !t.TryGetProperty("name", out var n) || !el.TryGetProperty("members", out var members)) continue;
            foreach (var m in members.EnumerateArray())
                if (m.GetProperty("type").GetString() == "way") relationName.TryAdd(m.GetProperty("ref").GetInt64(), n.GetString() ?? "");
        }
        return relationName;
    }

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
    /// <summary>Smallest harbour basin taken (a marina box of ~70 × 70 m).</summary>
    public const double HarbourMinKm2 = 0.005;

    public static List<(Polygon area, string? name)> ParseWaterAreas(JsonDocument doc, GeometryFactory f, double minKm2 = 0.05)
    {
        var result = new List<(Polygon, string?)>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!el.TryGetProperty("tags", out var t)) continue;
            var tags = t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            if (tags.GetValueOrDefault("natural") != "water") continue;
            var name = tags.GetValueOrDefault("name");
            var min = tags.GetValueOrDefault("water") is "harbour" or "basin" ? Math.Min(minKm2, HarbourMinKm2) : minKm2;
            var type = el.GetProperty("type").GetString();
            if (type == "way" && el.TryGetProperty("geometry", out var geom)) { if (Ring(geom, f) is { } ring && AreaKm2(ring) >= min) result.Add((ring, name)); }
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
                    if (AreaKm2(poly) >= min) result.Add((poly, name));
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

    /// <summary>Road and railway bridges: the candidates for fixed bridges without a charted clearance (Öknöström, H 635).</summary>
    public const string OverpassBridgeQuery = """[out:json][timeout:300];(way["bridge"]["bridge"!="no"]["highway"]({0});way["bridge"]["bridge"!="no"]["railway"]({0}););out geom;""";
    /// <summary>Note prefix of the derived Bridge corrections without a clearance (ImportUnknownBridgesAsync); the waterway import keeps them.</summary>
    public const string UnknownBridgeNote = "Brücke ohne Höhenangabe";

    /// <summary>A road or railway bridge the waterway import does not take as an obstacle (no seamark bridge, no charted clearance, not movable).</summary>
    public static bool IsPlainBridge(IReadOnlyDictionary<string, string> tags) =>
        tags.TryGetValue("bridge", out var b) && b != "no" && (tags.ContainsKey("highway") || tags.ContainsKey("railway")) && tags.GetValueOrDefault("seamark:type") != "bridge"
        && !tags.ContainsKey("seamark:bridge:clearance_height") && !tags.ContainsKey("seamark:bridge:clearance_height_closed") && !IsOpening(tags);

    /// <summary>
    /// Fixed bridges of unknown clearance: the plain road/railway bridge ways of an Overpass-style document (<see cref="IsPlainBridge"/>) that
    /// cross water the routing graph uses - a passage or inland water area they intersect, or open water (not the active graph's land, inside
    /// its region) at their midpoint; a bridge over a creek or a river the graph does not know stays out. They become derived Bridge corrections
    /// without a clearance (note <see cref="UnknownBridgeNote"/>), replacing those inside <paramref name="replace"/> (null: none replaced).
    /// The graph picks them up at the next build.
    /// </summary>
    public static async Task<(int candidates, int created, int removed)> ImportUnknownBridgesAsync(JsonDocument doc, SmsDbContext db, Guid? userId, Envelope? replace, CancellationToken ct)
    {
        var bridges = new List<(LineString line, string? name)>();
        foreach (var el in doc.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (el.GetProperty("type").GetString() != "way" || !el.TryGetProperty("geometry", out var geom) || !el.TryGetProperty("tags", out var t)) continue;
            var tags = t.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
            if (!IsPlainBridge(tags)) continue;
            var coords = geom.EnumerateArray().Select(c => new Coordinate(c.GetProperty("lon").GetDouble(), c.GetProperty("lat").GetDouble())).ToArray();
            if (coords.Length < 2 || coords[0].Equals2D(coords[^1])) continue;
            bridges.Add((new LineString(coords) { SRID = 4326 }, tags.GetValueOrDefault("bridge:name") ?? tags.GetValueOrDefault("name") ?? tags.GetValueOrDefault("ref")));
        }
        var removed = 0;
        if (replace is not null)
        {
            var extent = Geo.Factory.ToGeometry(replace); extent.SRID = 4326;
            removed = await db.RoutingCorrections.Where(c => c.Kind == CorrectionKind.Bridge && c.Source == CorrectionSource.Derived && c.Note != null && c.Note.StartsWith(UnknownBridgeNote) && c.Geometry.Intersects(extent)).ExecuteDeleteAsync(ct);
        }
        if (bridges.Count == 0) return (0, 0, removed);
        var meta = (await db.Database.SqlQueryRaw<string>("SELECT string_agg(value, ';' ORDER BY key) AS \"Value\" FROM routing_meta WHERE key IN ('active_version', 'region')").ToListAsync(ct)).FirstOrDefault() ?? "";
        var parts = meta.Split(';');
        var version = parts.Length == 2 && int.TryParse(parts[0], out var v) ? v : 0;
        var region = version > 0 ? parts[1].Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray() : [0, 0, 0, 0];
        var wkb = bridges.Select(b => b.line.AsBinary()).ToArray();
        var onWater = (await db.Database.SqlQueryRaw<long>("""
            SELECT b.i AS "Value" FROM unnest({0}::bytea[]) WITH ORDINALITY AS b(w, i), LATERAL (SELECT ST_SetSRID(ST_GeomFromWKB(b.w), 4326) AS g) x, LATERAL (SELECT ST_LineInterpolatePoint(x.g, 0.5) AS m) p
            WHERE EXISTS (SELECT 1 FROM "RoutingCorrections" c WHERE c."Active" AND c."Kind" IN ('Passage', 'Water') AND c."Geometry" && x.g AND ST_Intersects(c."Geometry", x.g))
               OR ({1} > 0 AND p.m && ST_MakeEnvelope({2}, {3}, {4}, {5}, 4326)
                   AND NOT EXISTS (SELECT 1 FROM routing_land l WHERE l.graph_version = {1} AND l.geom && p.m AND ST_Intersects(l.geom, p.m)))
            """, wkb, version, region[0], region[1], region[2], region[3]).ToListAsync(ct)).Select(i => (int)i - 1).ToList();
        foreach (var i in onWater)
            db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Bridge, Source = CorrectionSource.Derived, Geometry = bridges[i].line, ClearanceM = null, Active = true, CreatedById = userId,
                Note = $"{UnknownBridgeNote}{(bridges[i].name is { Length: > 0 } n ? " – " + n : "")}" });
        await db.SaveChangesAsync(ct);
        return (bridges.Count, onWater.Count, removed);
    }

    private static readonly HashSet<string> OpeningCategories = ["opening", "bascule", "swing", "lift", "drawbridge", "folding", "retractable", "submersible", "transporter"];
    /// <summary>Movable bridge: bridge=movable, bridge:movable=* (anything but "no") or an opening seamark category.</summary>
    public static bool IsOpening(IReadOnlyDictionary<string, string> tags) =>
        tags.GetValueOrDefault("bridge") == "movable" || (tags.TryGetValue("bridge:movable", out var m) && m != "no") || (tags.TryGetValue("seamark:bridge:category", out var c) && OpeningCategories.Contains(c)) || tags.ContainsKey("seamark:bridge:clearance_height_open");

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
