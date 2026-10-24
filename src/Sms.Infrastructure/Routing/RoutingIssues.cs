using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;
using Sms.Domain;

namespace Sms.Infrastructure.Routing;

/// <summary>
/// The admin's problem list (spec-admin-issues): where the active graph's data is uncertain - uncertain water, bridges without a
/// clearance, fairway gaps and dead ends, harbours off the water - and the basis fingerprints that mark a decision on one outdated
/// when the data under it changes. Detection is SQL over routing_edge and the corrections; it runs after every build.
/// </summary>
public static class RoutingIssues
{
    /// <summary>The "typical minimum depth" of the uncertain-water rule (SeaGraph.Uncertain at this depth).</summary>
    public const double MinDepthM = 2.3;
    /// <summary>Grid of the uncertain-water cells (degrees, ST_SquareGrid from 0,0: the keys stay stable): ≈ 3.3 × 1.9 km at 55° N.</summary>
    public const double CellDeg = 0.03;
    public const double MinCellKm = 1, HarbourAccessM = 300, FairwayJoinM = 100, KnownSearchDeg = 0.1, BridgeBasisM = 30, FairwayBasisM = 20, AreaBufferM = 100;
    /// <summary>Note of a Manual Fairway area a fix creates for water of known depth: buoyed water for the depth limits (DepthLimits.Apply: no
    /// depth, never uncertain) and, through <see cref="Fairways.WideNote"/>, no channel zone round it.</summary>
    public const string KnownDepthNote = "Bekannte Tiefe (Problemliste, " + Fairways.WideNote + ")";

    private static readonly string Md = MinDepthM.ToString(CultureInfo.InvariantCulture);
    /// <summary>SeaGraph.Uncertain(e, MinDepthM) in SQL (alias e); a NaN DTM depth is "none", as in the graph.</summary>
    public static readonly string UncertainSql = $"NOT e.ferry AND NOT e.traffic AND LEAST(COALESCE(e.min_depth_m, e.adjacent_depth_m), e.floor_m) < {Md} AND NOT COALESCE(e.dtm_min_m >= {Md} AND e.dtm_min_m <> 'NaN', false)";
    private const string FairwayLine = """c."Active" AND c."Kind" = 'Passage' AND c."Source" = 'Derived' AND c."Note" LIKE 'Fahrwasser%'""";

    public sealed record Result(int Version, int Issues, IReadOnlyDictionary<IssueType, int> ByType, int Checked, int Outdated, double Seconds);

    /// <summary>Detects on the active graph, replaces RoutingIssues, then checks the decisions.</summary>
    public static async Task<Result> RunAsync(SmsDbContext db, CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var conn = await OpenAsync(db, ct);
        var version = Convert.ToInt32(await new NpgsqlCommand("SELECT COALESCE((SELECT value FROM routing_meta WHERE key = 'active_version')::int, 0)", conn).ExecuteScalarAsync(ct));
        var issues = version > 0 ? await DetectAsync(db, version, ct) : [];
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.RoutingIssues.ExecuteDeleteAsync(ct);
            db.RoutingIssues.AddRange(issues);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        db.ChangeTracker.Clear();
        var (checkedCount, outdated) = await CheckAsync(db, ct);
        return new(version, issues.Count, issues.GroupBy(i => i.Type).ToDictionary(g => g.Key, g => g.Count()), checkedCount, outdated, Math.Round(clock.Elapsed.TotalSeconds, 1));
    }

    public static async Task<List<RoutingIssue>> DetectAsync(SmsDbContext db, int version, CancellationToken ct)
    {
        var conn = await OpenAsync(db, ct);
        var gridNodes = Convert.ToInt64(await new NpgsqlCommand("SELECT COALESCE(MAX(value::bigint), 0) FROM routing_meta WHERE key = 'grid_nodes'", conn).ExecuteScalarAsync(ct));
        var result = new List<RoutingIssue>();
        result.AddRange(await UncertainWaterAsync(conn, version, ct));
        result.AddRange(await BridgesAsync(conn, version, ct));
        result.AddRange(await FairwayGapsAsync(conn, version, ct));
        result.AddRange(await FairwayEndsAsync(conn, version, gridNodes, ct));
        result.AddRange(await HarboursAsync(conn, version, ct));
        foreach (var i in result) { i.GraphVersion = version; i.Geometry.SRID = 4326; }
        return result;
    }

    // ---------------------------------------------------------------- detection

    /// <summary>Uncertain edges clipped to the CellDeg grid; per cell the uncertain km, ranked by fairway, harbour and sailing traffic in it.</summary>
    private static async Task<List<RoutingIssue>> UncertainWaterAsync(NpgsqlConnection conn, int version, CancellationToken ct)
    {
        var sql = $"""
            WITH c AS (
              SELECT g.i, g.j, sum(ST_Length(ST_ClipByBox2D(e.geom, g.geom)::geography)) / 1000 AS km, count(*) AS n
              FROM routing_edge e, LATERAL ST_SquareGrid(@cell, e.geom) g
              WHERE e.graph_version = @v AND {UncertainSql} AND ST_Intersects(e.geom, g.geom)
              GROUP BY g.i, g.j),
            b AS (SELECT c.*, ST_MakeEnvelope(c.i * @cell, c.j * @cell, (c.i + 1) * @cell, (c.j + 1) * @cell, 4326) AS geom FROM c WHERE c.km >= @minKm)
            SELECT b.i, b.j, b.km, b.n, b.geom,
              EXISTS (SELECT 1 FROM "RoutingCorrections" c WHERE c."Active" AND (c."Kind" = 'Fairway' OR (c."Kind" = 'Passage' AND c."Source" = 'Derived' AND c."Note" LIKE 'Fahrwasser%'))
                      AND c."Geometry" && b.geom AND ST_Intersects(c."Geometry", b.geom)) AS fairway,
              EXISTS (SELECT 1 FROM "Harbours" h WHERE h."IsActive" AND h."MergedIntoId" IS NULL AND h."Location" && b.geom) AS harbour,
              COALESCE((SELECT sum(t.sailing) FROM routing_traffic t WHERE t.geom && b.geom AND ST_Intersects(t.geom, b.geom)), 0)::float8 AS traffic
            FROM b
            """;
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 3600 };
        cmd.Parameters.AddWithValue("v", version); cmd.Parameters.AddWithValue("cell", CellDeg); cmd.Parameters.AddWithValue("minKm", MinCellKm);
        var list = new List<RoutingIssue>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var (i, j, km, n) = (r.GetInt32(0), r.GetInt32(1), r.GetDouble(2), r.GetInt64(3));
            var (fairway, harbour, traffic) = (r.GetBoolean(5), r.GetBoolean(6), r.GetDouble(7));
            list.Add(new RoutingIssue
            {
                Key = $"water:{i}:{j}", Type = IssueType.UncertainWater, Geometry = r.GetFieldValue<Geometry>(4),
                Priority = WaterPriority(km, fairway, harbour, traffic),
                Title = fairway ? "Unsichere Tiefe am Fahrwasser" : harbour ? "Unsichere Tiefe am Hafen" : "Unsichere Tiefe",
                Detail = $"{km:0.0} Kanten-km unsicher ({n} Kanten), Bandboden unter {Md} m ohne Fahrwasser-, Fähr-, Verkehrs- oder DTM-Beleg; " +
                         $"Fahrwasser: {(fairway ? "ja" : "nein")}, Hafen: {(harbour ? "ja" : "nein")}, Segelverkehr: {traffic:0.#}",
            });
        }
        return list;
    }

    /// <summary>10·log10(1 + km) × (1 + 2·fairway + harbour + log10(1 + sailing traffic)): routes go where the fairways, harbours and boats
    /// are; the km only damped (the visibility graph's edges overlap: their km measure graph density more than water).</summary>
    public static double WaterPriority(double km, bool fairway, bool harbour, double traffic) =>
        Math.Round(10 * Math.Log10(1 + km) * (1 + (fairway ? 2 : 0) + (harbour ? 1 : 0) + Math.Log10(1 + Math.Max(0, traffic))), 2);

    /// <summary>Active derived fixed bridges without a clearance that an unknown_bridge edge of the graph passes under.</summary>
    private static async Task<List<RoutingIssue>> BridgesAsync(NpgsqlConnection conn, int version, CancellationToken ct)
    {
        const string sql = """
            SELECT b."Id", b."Geometry", b."Note",
              EXISTS (SELECT 1 FROM "RoutingCorrections" c WHERE c."Active" AND (c."Kind" = 'Fairway' OR (c."Kind" = 'Passage' AND c."Source" = 'Derived' AND c."Note" LIKE 'Fahrwasser%'))
                      AND c."Geometry" && ST_Expand(b."Geometry", 0.0003) AND ST_DWithin(c."Geometry", b."Geometry", 0.0003)) AS fairway,
              EXISTS (SELECT 1 FROM "RoutingCorrections" c WHERE c."Active" AND c."Kind" = 'Passage' AND c."Geometry" && ST_Expand(b."Geometry", 0.0003) AND ST_DWithin(c."Geometry", b."Geometry", 0.0003)) AS passage,
              COALESCE((SELECT sum(t.sailing) FROM routing_traffic t WHERE t.geom && b."Geometry" AND ST_Intersects(t.geom, b."Geometry")), 0)::float8 AS traffic,
              x.n
            FROM "RoutingCorrections" b,
              LATERAL (SELECT count(*) AS n FROM routing_edge e WHERE e.graph_version = @v AND e.unknown_bridge AND e.geom && ST_Expand(b."Geometry", 0.0003) AND ST_DWithin(e.geom, b."Geometry", 0.0003)) x
            WHERE b."Active" AND b."Kind" = 'Bridge' AND b."Source" = 'Derived' AND b."ClearanceM" IS NULL AND x.n > 0
            """;
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 3600 };
        cmd.Parameters.AddWithValue("v", version);
        var list = new List<RoutingIssue>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        while (await r.ReadAsync(ct))
        {
            var g = r.GetFieldValue<Geometry>(1);
            var (fairway, passage, traffic, n) = (r.GetBoolean(3), r.GetBoolean(4), r.GetDouble(5), r.GetInt64(6));
            list.Add(new RoutingIssue
            {
                Key = BridgeKey(g), Type = IssueType.BridgeUnknown, Geometry = g, RefId = r.GetGuid(0).ToString(),
                Priority = Math.Round(10 * (1 + (fairway ? 2 : 0) + (passage ? 1 : 0) + Math.Log10(1 + traffic)), 2),
                Title = r.IsDBNull(2) ? "Brücke ohne Höhenangabe" : r.GetString(2),
                Detail = $"Über {(fairway ? "ein Fahrwasser" : passage ? "eine Passage" : "befahrbares Wasser")}, {n} Graphkanten darunter, Segelverkehr: {traffic:0.#}",
            });
        }
        // one issue per key: the same way imported twice, or road and rail drawn on one line - the fix deactivates all of their rows
        return list.GroupBy(i => i.Key).Select(g => { var first = g.OrderByDescending(i => i.Priority).First(); first.RefId = string.Join(",", g.Select(i => i.RefId)); return first; }).ToList();
    }

    public static string BridgeKey(Geometry g) => $"bridge:{Coord(g.Coordinates[0])};{Coord(g.Coordinates[^1])}";
    private static string Coord(Coordinate c) => string.Create(CultureInfo.InvariantCulture, $"{c.X:0.00000},{c.Y:0.00000}");

    private static async Task<List<(Guid id, LineString line)>> FairwayLinesAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var lines = new List<(Guid, LineString)>();
        await using var cmd = new NpgsqlCommand($"""SELECT c."Id", c."Geometry" FROM "RoutingCorrections" c WHERE {FairwayLine} AND GeometryType(c."Geometry") = 'LINESTRING'""", conn) { CommandTimeout = 600 };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lines.Add((r.GetGuid(0), (LineString)r.GetFieldValue<Geometry>(1)));
        return lines;
    }

    /// <summary>The fairway's gaps (Fairways.GapChords) no active passage runs along yet, ranked by length and the uncertain share of the
    /// graph edges crossing the chord.</summary>
    private static async Task<List<RoutingIssue>> FairwayGapsAsync(NpgsqlConnection conn, int version, CancellationToken ct)
    {
        var lines = await FairwayLinesAsync(conn, ct);
        var chords = Fairways.GapChords(lines.Select(l => l.line).ToList());
        if (chords.Count == 0) return [];
        var sql = $"""
            SELECT b.i, (SELECT count(*) FROM routing_edge e WHERE e.graph_version = @v AND e.geom && x.g AND ST_Intersects(e.geom, x.g)) AS n,
              (SELECT count(*) FROM routing_edge e WHERE e.graph_version = @v AND e.geom && x.g AND ST_Intersects(e.geom, x.g) AND {UncertainSql}) AS u,
              ARRAY(SELECT ST_AsBinary(c."Geometry") FROM "RoutingCorrections" c WHERE c."Active" AND c."Kind" = 'Passage' AND NOT (c."Source" = 'Derived' AND c."Note" LIKE 'Fahrwasser%')
                    AND c."Geometry" && ST_Expand(x.g, 0.001) AND ST_DWithin(c."Geometry", x.g, 0.001)) AS passages
            FROM unnest(@w::bytea[]) WITH ORDINALITY AS b(w, i), LATERAL (SELECT ST_SetSRID(ST_GeomFromWKB(b.w), 4326) AS g) x
            """;
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 3600 };
        cmd.Parameters.AddWithValue("v", version); cmd.Parameters.AddWithValue("w", chords.Select(c => c.AsBinary()).ToArray());
        var list = new List<RoutingIssue>();
        var wkb = new NetTopologySuite.IO.WKBReader();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var chord = chords[(int)r.GetInt64(0) - 1];
            var (n, u) = (r.GetInt64(1), r.GetInt64(2));
            var passages = r.GetFieldValue<byte[][]>(3).Select(b => wkb.Read(b)).ToList();
            if (FeedbackRegression.Coverage(chord, passages, FeedbackRegression.MissingToleranceM) >= FeedbackRegression.MissingSatisfiedShare) continue; // bridged already
            var km = FeedbackRegression.LengthM(chord) / 1000;
            var share = n == 0 ? 0 : (double)u / n;
            list.Add(new RoutingIssue
            {
                Key = $"gap:{Coord(chord.StartPoint.Coordinate)};{Coord(chord.EndPoint.Coordinate)}", Type = IssueType.FairwayGap, Geometry = chord,
                RefId = string.Join(",", lines.Where(l => l.line.Coordinates[0].Equals2D(chord.StartPoint.Coordinate) || l.line.Coordinates[^1].Equals2D(chord.StartPoint.Coordinate)
                    || l.line.Coordinates[0].Equals2D(chord.EndPoint.Coordinate) || l.line.Coordinates[^1].Equals2D(chord.EndPoint.Coordinate)).Select(l => l.id)),
                Priority = GapPriority(km, share), Title = "Lücke im Fahrwasser",
                Detail = $"{km:0.00} km zwischen zwei Fahrwasserlinien ohne Passage; {u} von {n} kreuzenden Kanten unsicher",
            });
        }
        return list;
    }

    public static double GapPriority(double km, double uncertainShare) => Math.Round(5 + km + 20 * uncertainShare, 2);

    /// <summary>
    /// Fairway line ends with no other passage (fairway, OSM waterway, channel, manual) within FairwayJoinM whose way into the water is uncertain: no link from the end vertex
    /// into the water graph, or every water edge at the far ends of its links is uncertain. The issue runs from the end to the nearest
    /// certain water edge within KnownSearchDeg (its length: the uncertain stretch); both ends of a line make one isolated fairway.
    /// </summary>
    private static async Task<List<RoutingIssue>> FairwayEndsAsync(NpgsqlConnection conn, int version, long gridNodes, CancellationToken ct)
    {
        var sql = $"""
            WITH f AS (SELECT c."Id" AS id, c."Geometry" AS g FROM "RoutingCorrections" c WHERE {FairwayLine} AND GeometryType(c."Geometry") = 'LINESTRING'),
            ends AS (SELECT id, g, 0 AS side, ST_StartPoint(g) AS p FROM f UNION ALL SELECT id, g, 1, ST_EndPoint(g) FROM f),
            open AS (SELECT ends.* FROM ends WHERE NOT EXISTS (SELECT 1 FROM "RoutingCorrections" o WHERE o."Active" AND o."Kind" = 'Passage' AND o."Id" <> ends.id
                      AND o."Geometry" && ST_Expand(ends.p, 0.002) AND ST_DWithin(o."Geometry"::geography, ends.p::geography, @join))),
            v AS (SELECT open.*, (SELECT CASE WHEN ST_DWithin(ST_StartPoint(e.geom), open.p, 1e-6) THEN e.source ELSE e.target END FROM routing_edge e
                    WHERE e.graph_version = @v AND e.geom && ST_Expand(open.p, 1e-6) AND (ST_DWithin(ST_StartPoint(e.geom), open.p, 1e-6) OR ST_DWithin(ST_EndPoint(e.geom), open.p, 1e-6)) LIMIT 1) AS node FROM open),
            links AS (SELECT v.id, v.side, l.id AS lid, CASE WHEN l.source = v.node THEN l.target ELSE l.source END AS far
              FROM v JOIN routing_edge l ON l.graph_version = @v AND (l.source = v.node OR l.target = v.node) WHERE NOT ST_CoveredBy(l.geom, ST_Buffer(v.g, 0.00005))),
            hop AS (SELECT links.id, links.side, bool_or(links.far > @grid) AS passage, count(e.id) AS n, count(e.id) FILTER (WHERE {UncertainSql}) AS u
              FROM links LEFT JOIN routing_edge e ON links.far <= @grid AND e.graph_version = @v AND (e.source = links.far OR e.target = links.far) AND e.id <> links.lid
              GROUP BY 1, 2),
            dead AS (SELECT v.id, v.side, v.p, v.g, hop.n IS NULL AS unlinked, COALESCE(hop.n, 0) AS n FROM v LEFT JOIN hop ON hop.id = v.id AND hop.side = v.side
              WHERE hop.n IS NULL OR (NOT hop.passage AND hop.n > 0 AND hop.u = hop.n))
            SELECT dead.id, dead.side, dead.p, dead.g, dead.unlinked, dead.n, k.q
            FROM dead LEFT JOIN LATERAL (SELECT ST_ClosestPoint(e.geom, dead.p) AS q FROM routing_edge e
              WHERE e.graph_version = @v AND e.source <= @grid AND e.target <= @grid AND e.geom && ST_Expand(dead.p, @search) AND NOT ({UncertainSql})
              ORDER BY e.geom <-> dead.p LIMIT 1) k ON true
            """;
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 3600 };
        cmd.Parameters.AddWithValue("v", version); cmd.Parameters.AddWithValue("grid", gridNodes); cmd.Parameters.AddWithValue("join", FairwayJoinM); cmd.Parameters.AddWithValue("search", KnownSearchDeg);
        var ends = new List<(Guid id, int side, Point p, LineString line, bool unlinked, Point? q)>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                ends.Add((r.GetGuid(0), r.GetInt32(1), (Point)r.GetFieldValue<Geometry>(2), (LineString)r.GetFieldValue<Geometry>(3), r.GetBoolean(4), r.IsDBNull(6) ? null : (Point)r.GetFieldValue<Geometry>(6)));
        return FairwayEndIssues(ends.Select(e => new DeadEnd(e.id, e.side, e.p, e.line, e.unlinked, e.q)).ToList());
    }

    public sealed record DeadEnd(Guid LineId, int Side, Point End, LineString Line, bool Unlinked, Point? NearestKnown);

    /// <summary>Dead ends → issues: a line with both ends dead is one isolated fairway, otherwise one issue per end (end → nearest certain water).</summary>
    public static List<RoutingIssue> FairwayEndIssues(IReadOnlyList<DeadEnd> ends)
    {
        var list = new List<RoutingIssue>();
        foreach (var line in ends.GroupBy(e => e.LineId))
        {
            var items = line.ToList();
            string Why(DeadEnd e) => (e.Unlinked ? "nicht ans Wassernetz angebunden" : "führt in unsichere Tiefe") +
                (e.NearestKnown is { } q ? $", {StretchM(e):0} m bis sicherem Wasser" : $", kein sicheres Wasser im Umkreis von {KnownSearchDeg * WaterGraphTiler.MPerDegLat / 1000:0} km");
            if (items.Count == 2)
            {
                var l = items[0].Line;
                list.Add(new RoutingIssue
                {
                    Key = $"isolated:{Coord(l.Coordinates[0])};{Coord(l.Coordinates[^1])}", Type = IssueType.FairwayIsolated, Geometry = l, RefId = line.Key.ToString(),
                    Priority = Math.Round(2 * (20 + items.Sum(e => Math.Min(50, StretchM(e) / 100))), 2), Title = "Isoliertes Fahrwasser",
                    Detail = $"{FeedbackRegression.LengthM(l) / 1000:0.0} km; Anfang {Why(items.Single(e => e.Side == 0))}; Ende {Why(items.Single(e => e.Side == 1))}",
                });
                continue;
            }
            var d = items[0];
            Geometry g = d.NearestKnown is { } k && !k.Coordinate.Equals2D(d.End.Coordinate) ? new LineString([d.End.Coordinate.Copy(), k.Coordinate.Copy()]) : (Point)d.End.Copy();
            list.Add(new RoutingIssue
            {
                Key = $"deadend:{Coord(d.End.Coordinate)}", Type = IssueType.FairwayDeadEnd, Geometry = g, RefId = line.Key.ToString(),
                Priority = Math.Round(20 + Math.Min(50, StretchM(d) / 100) + (d.Unlinked ? 0 : 10), 2),
                Title = d.Unlinked ? "Fahrwasser-Ende ohne Anbindung" : "Fahrwasser endet in unsicherem Wasser", Detail = Why(d),
            });
        }
        return list;
    }

    /// <summary>The uncertain stretch from a dead end to certain water (metres; 10 km when there is none in reach).</summary>
    private static double StretchM(DeadEnd e) => e.NearestKnown is { } q ? RoutingGraphBuilder.Haversine(e.End.Y, e.End.X, q.Y, q.X) : KnownSearchDeg * WaterGraphTiler.MPerDegLat;

    /// <summary>Active harbours of the region without access to the graph or with the access node farther than HarbourAccessM.
    /// ponytail: pins on land with a short access (Bua) need a land test, minutes on the region's land polygons; add when it matters.</summary>
    private static async Task<List<RoutingIssue>> HarboursAsync(NpgsqlConnection conn, int version, CancellationToken ct)
    {
        const string sql = """
            SELECT h."Id", h."Name", h."Location", a.distance_m
            FROM "Harbours" h LEFT JOIN routing_harbour_access a ON a.harbour_id = h."Id" AND a.graph_version = @v,
              LATERAL (SELECT string_to_array(value, ',')::float8[] AS r FROM routing_meta WHERE key = 'region') m
            WHERE h."IsActive" AND h."MergedIntoId" IS NULL AND h."Location" && ST_MakeEnvelope(m.r[1], m.r[2], m.r[3], m.r[4], 4326)
              AND (a.harbour_id IS NULL OR a.distance_m > @max)
            """;
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 600 };
        cmd.Parameters.AddWithValue("v", version); cmd.Parameters.AddWithValue("max", HarbourAccessM);
        var list = new List<RoutingIssue>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var none = r.IsDBNull(3);
            list.Add(new RoutingIssue
            {
                Key = $"harbour:{r.GetGuid(0)}", Type = IssueType.HarbourOffWater, Geometry = r.GetFieldValue<Geometry>(2), RefId = r.GetGuid(0).ToString(),
                Priority = none ? 5 : Math.Round(2 + Math.Min(3, r.GetDouble(3) / 1000), 2), Title = r.GetString(1),
                Detail = none ? "Kein Zugang zum Routing-Graphen (keine Route möglich)" : $"Pin {r.GetDouble(3):0} m vom Routing-Graphen",
            });
        }
        return list;
    }

    // ---------------------------------------------------------------- basis and outdated

    public sealed record Basis(string Kind, string Fingerprint, string Refs, int Rows);

    /// <summary>The data kind a decision on this issue type rests on.</summary>
    public static string BasisKind(IssueType t) => t switch
    {
        IssueType.UncertainWater => "depth",
        IssueType.BridgeUnknown => "bridge",
        IssueType.HarbourOffWater => "harbour",
        _ => "fairway",
    };

    /// <summary>Where the basis is looked up: a dead end's end point, otherwise the issue geometry.</summary>
    public static Geometry BasisGeometry(IssueType t, Geometry g) => t == IssueType.FairwayDeadEnd && g is LineString l ? l.StartPoint : g;

    /// <summary>
    /// Fingerprint (md5 of the sorted row hashes) of the data under an issue: depth = the routing_depth rows intersecting it (bands and
    /// source); bridge = the charted bridge data within BridgeBasisM (derived Bridge rows with a clearance and opening bridges - the unknown
    /// row itself vanishes by design: import-bridges skips a way next to an active bridge); fairway = the active derived "Fahrwasser" lines
    /// within FairwayBasisM (a re-derivation with the same course hashes the same); harbour = name, position and state of the harbour.
    /// </summary>
    public static async Task<Basis> BasisAsync(SmsDbContext db, IssueType type, string key, Geometry geometry, CancellationToken ct)
    {
        var kind = BasisKind(type);
        var g = BasisGeometry(type, geometry); g.SRID = 4326;
        var rows = kind switch
        {
            "depth" => """
                SELECT d.id::text AS ref, d.source AS tag, md5(d.min_depth_m::text || '|' || d.max_depth_m::text || '|' || d.source || '|' || encode(ST_AsBinary(ST_SnapToGrid(d.geom, 1e-7)), 'hex')) AS h
                FROM routing_depth d WHERE d.geom && @g AND ST_Intersects(d.geom, @g)
                """,
            "bridge" => $"""
                SELECT c."Id"::text AS ref, c."Kind" || coalesce(' ' || c."ClearanceM"::text || ' m', '') AS tag,
                  md5(c."Kind" || '|' || coalesce(c."ClearanceM"::text, '') || '|' || coalesce(c."Note", '') || '|' || encode(ST_AsBinary(ST_SnapToGrid(c."Geometry", 1e-6)), 'hex')) AS h
                FROM "RoutingCorrections" c WHERE c."Source" = 'Derived' AND (c."Kind" = 'OpeningBridge' OR (c."Kind" = 'Bridge' AND c."ClearanceM" IS NOT NULL))
                  AND c."Geometry" && ST_Expand(@g, 0.001) AND ST_DWithin(c."Geometry"::geography, @g::geography, {BridgeBasisM.ToString(CultureInfo.InvariantCulture)})
                """,
            "fairway" => $"""
                SELECT c."Id"::text AS ref, 'Fahrwasser' AS tag, md5(encode(ST_AsBinary(ST_SnapToGrid(c."Geometry", 1e-6)), 'hex')) AS h
                FROM "RoutingCorrections" c WHERE {FairwayLine} AND c."Geometry" && ST_Expand(@g, 0.001) AND ST_DWithin(c."Geometry"::geography, @g::geography, {FairwayBasisM.ToString(CultureInfo.InvariantCulture)})
                """,
            _ => """
                SELECT h."Id"::text AS ref, h."Name" AS tag, md5(h."Name" || '|' || ST_AsText(ST_SnapToGrid(h."Location"::geometry, 1e-7)) || '|' || h."IsActive"::text || '|' || coalesce(h."MergedIntoId"::text, '')) AS h
                FROM "Harbours" h WHERE h."Id" = @id
                """,
        };
        var conn = await OpenAsync(db, ct);
        await using var cmd = new NpgsqlCommand($"""
            WITH x AS ({rows})
            SELECT count(*)::int, md5(coalesce(string_agg(h, '' ORDER BY h), '')),
              coalesce((SELECT string_agg(t || ': ' || n, ', ' ORDER BY t) FROM (SELECT tag AS t, count(*) AS n FROM x GROUP BY tag) s), ''),
              coalesce((SELECT string_agg(ref, ',') FROM (SELECT ref FROM x ORDER BY ref LIMIT 50) s), '')
            FROM x
            """, conn) { CommandTimeout = 600 };
        cmd.Parameters.AddWithValue("g", g);
        cmd.Parameters.AddWithValue("id", key.StartsWith("harbour:") && Guid.TryParse(key[8..], out var hid) ? hid : Guid.Empty);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        var count = r.GetInt32(0);
        var refs = count == 0 ? "keine" : $"{r.GetString(2)}; ids {r.GetString(3)}";
        return new(kind, r.GetString(1), refs.Length > 3900 ? refs[..3900] : refs, count);
    }

    /// <summary>
    /// Recomputes the basis of every decision that is not outdated yet: a changed fingerprint marks it outdated with a reason. The fix
    /// (its correction) is never changed or deleted; confirming the decision (re-baselining) is the admin's call.
    /// </summary>
    public static async Task<(int checkedCount, int outdated)> CheckAsync(SmsDbContext db, CancellationToken ct)
    {
        var decisions = await db.IssueDecisions.Where(d => d.OutdatedAt == null).ToListAsync(ct);
        var outdated = 0; var now = DateTimeOffset.UtcNow;
        foreach (var d in decisions)
        {
            var b = await BasisAsync(db, d.Type, d.Key, d.Geometry, ct);
            d.CheckedAt = now;
            if (b.Fingerprint == d.Fingerprint) continue;
            d.OutdatedAt = now; outdated++;
            d.OutdatedReason = OutdatedReason(b.Kind, d.BasisRefs, b);
        }
        await db.SaveChangesAsync(ct);
        return (decisions.Count, outdated);
    }

    public static string OutdatedReason(string kind, string? before, Basis now)
    {
        var what = kind switch { "depth" => "Tiefendaten", "bridge" => "Brückendaten", "fairway" => "Fahrwasser", _ => "Hafen" };
        var change = now.Rows == 0 ? kind switch { "fairway" => "Fahrwasser nicht mehr abgeleitet", "harbour" => "Hafen nicht mehr vorhanden", _ => $"{what} entfernt" }
            : before is null or "keine" ? $"Neue {what}" : kind == "fairway" ? "Fahrwasser neu abgeleitet, Verlauf geändert" : $"{what} geändert";
        var text = $"{change} – vorher: {before ?? "keine"}; jetzt: {now.Refs}";
        return text.Length > 3900 ? text[..3900] : text;
    }

    private static async Task<NpgsqlConnection> OpenAsync(SmsDbContext db, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await db.Database.OpenConnectionAsync(ct);
        return conn;
    }
}
