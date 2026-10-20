using Microsoft.Extensions.Caching.Memory;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.LinearReferencing;
using Npgsql;
using Sms.Domain;
using Sms.Infrastructure;

namespace Sms.Api;

/// <summary>
/// Walking distances over the network in walk_edge (tools/build-walknet.sh): the start and the places are snapped to the nearest
/// ways (several candidates each, the straight offset counts), one Dijkstra per start (Sms.Domain.WalkNetwork) over the edges in
/// the box the limit can reach. No network near the start (or an empty table) = no walking distances, the straight ones stand.
/// </summary>
public static class Walking
{
    /// <summary>How far the start (harbour point, often in the basin) and a place may be from the nearest way.</summary>
    public const double StartSnapM = 1000, PlaceSnapM = 500;
    /// <summary>The search the harbour page shares between /pois/near and /pois/walk: everything within 5 km.</summary>
    public const double PageLimitM = 5000;

    /// <summary>A new connection for parallel work; a clone keeps the password an opened connection string has lost.</summary>
    public static NpgsqlConnection Connection(System.Data.Common.DbConnection efConnection) => (NpgsqlConnection)((ICloneable)efConnection).Clone();

    /// <summary>Dijkstra from a point over the ways within <paramref name="maxM"/>; null when no way is within <see cref="StartSnapM"/>.</summary>
    public static async Task<WalkSearch?> SearchAsync(NpgsqlConnection conn, double lat, double lng, double maxM, CancellationToken ct)
    {
        var start = (await SnapAsync(conn, [(lat, lng)], StartSnapM, ct))[0];
        if (start.Count == 0) return null;
        var dLat = maxM / 111_320.0; var dLng = dLat / Math.Cos(lat * Math.PI / 180);
        var edges = new List<WalkEdge>();
        await using (var cmd = new NpgsqlCommand("SELECT id, source, target, length_m FROM walk_edge WHERE geom && ST_MakeEnvelope($1, $2, $3, $4, 4326)", conn))
        {
            cmd.Parameters.Add(new() { Value = lng - dLng }); cmd.Parameters.Add(new() { Value = lat - dLat });
            cmd.Parameters.Add(new() { Value = lng + dLng }); cmd.Parameters.Add(new() { Value = lat + dLat });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) edges.Add(new WalkEdge(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetFloat(3)));
        }
        return new WalkNetwork(edges).Search(start, maxM);
    }

    /// <summary>The harbour page's 5 km search, cached for a few minutes (hovering the list asks for one path after the other).</summary>
    public static Task<WalkSearch?> PageSearchAsync(IMemoryCache cache, NpgsqlConnection conn, double lat, double lng, CancellationToken ct) =>
        // ponytail: unbounded cache of 5 km networks (tens of MB in a city) with a short sliding expiry; a size limit if many harbours are browsed at once
        cache.GetOrCreateAsync(("walk", Math.Round(lat, 5), Math.Round(lng, 5)), e => { e.SlidingExpiration = TimeSpan.FromMinutes(5); return SearchAsync(conn, lat, lng, PageLimitM, ct); });

    /// <summary>Candidate edges per point (nearest 8 by index, then real metres) within <paramref name="radiusM"/>.</summary>
    public static async Task<List<WalkSnap>[]> SnapAsync(NpgsqlConnection conn, IReadOnlyList<(double Lat, double Lng)> points, double radiusM, CancellationToken ct)
    {
        var result = points.Select(_ => new List<WalkSnap>()).ToArray();
        if (points.Count == 0) return result;
        await using var cmd = new NpgsqlCommand("""
            SELECT p.i::int, e.id, ST_LineLocatePoint(e.geom, p.g), ST_DistanceSphere(ST_ClosestPoint(e.geom, p.g), p.g)
            FROM (SELECT i, ST_SetSRID(ST_MakePoint(x, y), 4326) AS g FROM unnest($1::float8[], $2::float8[]) WITH ORDINALITY AS u(x, y, i)) p
            CROSS JOIN LATERAL (SELECT id, geom FROM walk_edge ORDER BY geom <-> p.g LIMIT 8) e
            """, conn);
        cmd.Parameters.Add(new() { Value = points.Select(p => p.Lng).ToArray() });
        cmd.Parameters.Add(new() { Value = points.Select(p => p.Lat).ToArray() });
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            if (r.GetDouble(3) <= radiusM) result[r.GetInt32(0) - 1].Add(new WalkSnap(r.GetInt64(1), r.GetDouble(2), r.GetDouble(3)));
        return result;
    }

    /// <summary>
    /// Walking metres from the search's start to each place. A value: walked. Null: no way near the place, its straight distance stands.
    /// Left out: there are ways near the place but none is reached within the search's limit – too far on foot (other side of the fjord).
    /// </summary>
    public static async Task<Dictionary<long, int?>> DistancesAsync(NpgsqlConnection conn, WalkSearch search, IReadOnlyList<(long Id, double Lat, double Lng)> places, CancellationToken ct)
    {
        var snaps = await SnapAsync(conn, places.Select(p => (p.Lat, p.Lng)).ToList(), PlaceSnapM, ct);
        var result = new Dictionary<long, int?>();
        for (var i = 0; i < places.Count; i++)
            if (snaps[i].Count == 0) result[places[i].Id] = null;
            else if (search.DistanceTo(snaps[i]) is { } m) result[places[i].Id] = (int)Math.Round(m);
        return result;
    }

    /// <summary>The walked line from the start to the place: start point, the edge pieces, the place.</summary>
    public static async Task<LineString> PathGeometryAsync(NpgsqlConnection conn, WalkPath path, (double Lat, double Lng) from, (double Lat, double Lng) to, CancellationToken ct)
    {
        var lines = new Dictionary<long, LineString>();
        await using (var cmd = new NpgsqlCommand("SELECT id, ST_AsBinary(geom) FROM walk_edge WHERE id = ANY($1)", conn))
        {
            cmd.Parameters.Add(new() { Value = path.Legs.Select(l => l.Edge).Distinct().ToArray() });
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var wkb = new WKBReader();
            while (await r.ReadAsync(ct)) lines[r.GetInt64(0)] = (LineString)wkb.Read((byte[])r[1]);
        }
        var coords = new List<Coordinate> { new(from.Lng, from.Lat) };
        foreach (var leg in path.Legs)
        {
            if (!lines.TryGetValue(leg.Edge, out var line)) continue;   // reloaded network meanwhile: skip the piece
            // ST_LineLocatePoint fractions are of the line's own (degree) length, so is LengthIndexedLine; To < From comes out reversed
            var piece = (LineString)new LengthIndexedLine(line).ExtractLine(leg.From * line.Length, leg.To * line.Length);
            foreach (var c in piece.Coordinates) if (!coords[^1].Equals2D(c)) coords.Add(c);
        }
        coords.Add(new Coordinate(to.Lng, to.Lat));
        return Geo.Factory.CreateLineString([.. coords]);
    }
}
