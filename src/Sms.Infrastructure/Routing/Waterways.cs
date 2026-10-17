using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Linemerge;

namespace Sms.Infrastructure.Routing;

public sealed record Waterway(string? Name, LineString Line, int Ways);

/// <summary>
/// Spec 10 §10.5 step 1: OSM waterway centrelines (canals, rivers, fairways with navigability tags) become routing
/// passages. Input is Overpass JSON with way geometries (<c>out geom</c>); ways are filtered for navigability and
/// merged end-to-end so one canal becomes one line, which the graph builder turns into an edge chain.
/// </summary>
public static class Waterways
{
    public const string OverpassQuery = "[out:json][timeout:180];way[\"waterway\"~\"^(canal|river|fairway)$\"][\"motorboat\"!~\"no\"][\"boat\"!~\"no\"]({0})->.w;(way.w[\"CEMT\"];way.w[\"ship\"~\"yes|designated\"];way.w[\"boat\"~\"yes|designated\"];way.w[\"motorboat\"~\"yes|designated\"];way.w[\"waterway\"=\"fairway\"];way.w[\"seamark:type\"=\"fairway\"];);out geom;";

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
