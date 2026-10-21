using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using Npgsql;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public sealed record WaterwayImportResult(int Created, int Removed, int Bridges, int Locks, IReadOnlyList<object> Lines, int FairwayAreas = 0, int NoGoAreas = 0);
public record CorrectionUpsert(CorrectionKind Kind, Geometry Geometry, string? Note, bool Active = true, double? ClearanceM = null);

/// <summary>Spec 10 §10.4. Falls back to straight lines (method "straight-line") while no graph is built; with a graph, no route is a 422.</summary>
public static class RoutingEndpoints
{
    public const string Disclaimer = "Entfernungen sind Schätzwerte und nicht zur Navigation geeignet.";
    public const string Attribution = "© OpenStreetMap-Mitwirkende, OpenSeaMap, EMODnet Human Activities Vessel Density (Cogea Srl) CC BY 4.0, EMODnet Bathymetry DTM 2024 CC BY 4.0";
    /// <summary>derive-fairways reads marks this far (degrees of latitude) beyond its bbox: more than Fairways' 5 km wide hop.</summary>
    public const double DeriveMarginDeg = 0.15;

    public static void MapRouting(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/routing").WithTags("Routing");

        g.MapGet("/nearby-ports", async (Guid? harbour, double? lat, double? lng, decimal? maxNm, int? limit, decimal? speedKn, SmsDbContext db, SeaRouter router, CancellationToken ct) =>
        {
            Point origin;
            if (harbour is { } hid)
            {
                var h = await db.Harbours.AsNoTracking().FirstOrDefaultAsync(x => x.Id == hid, ct);
                if (h is null) return Results.NotFound();
                origin = h.Location;
            }
            else if (lat is { } la && lng is { } ln) origin = Geo.Point(la, ln);
            else return Invalid("harbour", "harbour oder lat/lng angeben.");

            var max = Math.Clamp(maxNm ?? 50, 1, 150);
            var speed = speedKn is > 0 ? speedKn.Value : 5;
            var n = Math.Clamp(limit ?? 20, 1, 100);
            var method = "sea-graph";
            var graph = await router.NearbyAsync(harbour, harbour is null ? origin : null, (double)max, n, ct);
            List<NearbyPort> ports;
            if (graph is not null)
            {
                var ids = graph.Select(r => r.HarbourId).ToList();
                var names = await db.Harbours.AsNoTracking().Where(h => ids.Contains(h.Id)).Select(h => new { h.Id, h.Name, h.Location }).ToDictionaryAsync(h => h.Id, ct);
                ports = graph.Select(r => new NearbyPort(r.HarbourId, names[r.HarbourId].Name, names[r.HarbourId].Location.Y, names[r.HarbourId].Location.X, r.DistanceNm, r.StraightLineNm, Math.Round(r.DistanceNm / speed, 1), method)).ToList();
            }
            else
            {
                method = "straight-line";
                var maxMeters = (double)max * 1852;
                var hits = await db.Harbours.AsNoTracking()
                    .Where(h => h.IsActive && h.MergedIntoId == null && h.Id != harbour && h.Location.IsWithinDistance(origin, maxMeters))
                    .OrderBy(h => h.Location.Distance(origin)).Take(n)
                    .Select(h => new { h.Id, h.Name, h.Location, Meters = h.Location.Distance(origin) }).ToListAsync(ct);
                ports = hits.Select(h => { var nm = Math.Round((decimal)h.Meters / 1852m, 1); return new NearbyPort(h.Id, h.Name, h.Location.Y, h.Location.X, nm, nm, Math.Round(nm / speed, 1), method); }).ToList();
            }
            return Results.Ok(new { method, disclaimer = Disclaimer, attribution = Attribution, ports });
        });

        // minDepth: routes avoid bands whose middle lies below it (as given); warnDepth: "checkDepth" warnings where a band's floor lies below it;
        // draft (old clients, ships without the two settings): minDepth = draft + margin, no warnings
        // unknownBridges=pass (default: passed, "bridgeUnknown" warning) | avoid (closed unless no other route): fixed bridges without a charted clearance
        g.MapGet("/route", async (Guid? fromHarbour, Guid? toHarbour, string? from, string? to, double? airDraft, double? draft, double? minDepth, double? warnDepth, string? unknownBridges, bool? debug, SmsDbContext db, SeaRouter router, CancellationToken ct) =>
        {
            if (unknownBridges is not (null or "pass" or "avoid")) return Invalid("unknownBridges", "unknownBridges=pass oder avoid.");
            var avoid = unknownBridges == "avoid";
            RouteResult? r = null;
            Point? a = null, b = null;
            var min = router.MinDepth(draft ?? (minDepth is > 0 ? null : router.DefaultDraftM), minDepth); // no ship given: route for a typical yacht, not for a dinghy
            var warn = warnDepth is > 0 ? warnDepth : null;
            if (fromHarbour is { } f && toHarbour is { } t)
            {
                a = await db.Harbours.AsNoTracking().Where(h => h.Id == f).Select(h => h.Location).FirstOrDefaultAsync(ct);
                b = await db.Harbours.AsNoTracking().Where(h => h.Id == t).Select(h => h.Location).FirstOrDefaultAsync(ct);
                if (a is null || b is null) return Results.NotFound();
                r = await router.RouteAsync(f, t, ct, airDraft, min, warn, avoid);
            }
            else if (TryParsePoint(from, out var pa) && TryParsePoint(to, out var pb))
            {
                a = pa; b = pb;
                r = await router.RouteAsync(pa, pb, ct, airDraft, min, warn, avoid);
            }
            else return Invalid("from", "fromHarbour/toHarbour oder from=lat,lng&to=lat,lng angeben.");

            if (r is null)
            {
                // with a graph, "no route" is an answer of its own: a straight line would cross land without a word (an inland logbook
                // harbour 2.5 km from the Strelasund drew 5.4 nm across Rügen's mainland side as "straight-line", graph version 0)
                if (await router.ActiveVersionAsync(ct) > 0)
                    return Results.Problem(statusCode: 422, title: $"Keine Seeroute: Start oder Ziel liegt mehr als {SeaRouter.WaterSearchM / 1000:0} km vom befahrbaren Wasser entfernt, die Punkte sind nicht durch Wasser verbunden oder mehr als {router.MaxRouteNm:0} sm entfernt.");
                var nm = Math.Round((decimal)(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / 1852), 1);
                var line = Geo.Factory.CreateLineString([a.Coordinate, b.Coordinate]);
                return Results.Ok(new { distanceNm = nm, straightLineNm = nm, geometry = line, graphVersion = 0, method = "straight-line", disclaimer = Disclaimer, attribution = Attribution });
            }
            return Results.Ok(new { distanceNm = r.DistanceNm, straightLineNm = r.StraightLineNm, geometry = r.Geometry, graphVersion = r.GraphVersion, method = r.Method, warnings = r.Warnings ?? [], disclaimer = Disclaimer, attribution = Attribution, timings = debug == true ? r.Timings : null });
        });

        g.MapGet("/status", async (SeaRouter router, RoutingGraphBuilder builder, SmsDbContext db, CancellationToken ct) =>
        {
            var s = await router.StatusAsync(ct);
            var waterways = await db.RoutingCorrections.CountAsync(c => c.Kind == CorrectionKind.Passage && c.Source == CorrectionSource.Derived && c.Active, ct);
            var e = builder.Estimate();
            return Results.Ok(new { method = s.ActiveVersion > 0 ? "sea-graph" : "straight-line", graphVersion = s.ActiveVersion, s.BuiltAt, s.Region, s.Nodes, s.Edges, waterways, building = builder.IsRunning,
                percent = e?.Percent, phase = e?.Phase, phaseDone = e?.PhaseDone, phaseTotal = e?.PhaseTotal, elapsedSeconds = e?.ElapsedSeconds, remainingSeconds = e?.RemainingSeconds,
                progress = builder.Progress, options = builder.Options });
        });

        var admin = api.MapGroup("/admin/routing").WithTags("Admin").RequireAuthorization();
        admin.MapPost("/rebuild", async (CurrentUser current, RoutingGraphBuilder builder, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            return builder.TryStart() ? Results.Accepted("/api/routing/status") : Results.Problem(statusCode: 409, title: "Ein Build läuft bereits.");
        });
        // the derived corrections run to hundreds of thousands (channel skeletons, fairways, lakes): the editor asks for its viewport,
        // a call without bbox gets the manual corrections only
        admin.MapGet("/corrections", async (string? bbox, int? limit, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var q = db.RoutingCorrections.AsNoTracking();
            if (bbox is not null)
            {
                var b = bbox.Split(',');
                if (b.Length != 4) return Invalid("bbox", "bbox=south,west,north,east");
                var v = b.Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var box = Geo.Factory.ToGeometry(new Envelope(v[1], v[3], v[0], v[2])); box.SRID = 4326;
                q = q.Where(c => c.Geometry.Intersects(box));
            }
            else q = q.Where(c => c.Source == CorrectionSource.Manual);
            var max = Math.Clamp(limit ?? 3000, 1, 20000);
            var items = await q.OrderBy(c => c.Source).ThenByDescending(c => c.CreatedAt).Take(max).ToListAsync(ct);
            return Results.Ok(items);
        });
        admin.MapPost("/corrections", async (CorrectionUpsert req, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (req.Kind == CorrectionKind.Block && req.Geometry is not Polygon) return Invalid("geometry", "Block braucht ein Polygon.");
            if (req.Kind is CorrectionKind.Passage or CorrectionKind.Ferry && req.Geometry is not LineString) return Invalid("geometry", "Passage/Fährlinie braucht eine LineString.");
            if (req.Kind is CorrectionKind.Bridge or CorrectionKind.Lock && req.Geometry is not Point) return Invalid("geometry", "Brücke/Schleuse braucht einen Punkt.");
            req.Geometry.SRID = 4326;
            var c = new RoutingCorrection { Kind = req.Kind, Geometry = req.Geometry, Note = req.Note, Active = req.Active, ClearanceM = req.ClearanceM, CreatedById = current.Id };
            db.RoutingCorrections.Add(c);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/routing/corrections/{c.Id}", new { c.Id });
        });
        admin.MapPut("/corrections/{id:guid}", async (Guid id, CorrectionUpsert req, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var c = await db.RoutingCorrections.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            req.Geometry.SRID = 4326;
            c.Kind = req.Kind; c.Geometry = req.Geometry; c.Note = req.Note; c.Active = req.Active; c.ClearanceM = req.ClearanceM; c.UpdatedById = current.Id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        admin.MapDelete("/corrections/{id:guid}", async (Guid id, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
            await current.IsAdminAsync(ct) ? (await db.RoutingCorrections.Where(x => x.Id == id).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : Results.NotFound()) : Results.Forbid());
        // Spec 10 §10.5 step 1: OSM waterway centrelines for a bounding box become derived passages (reviewable in the corrections list).
        // depth areas for the region (or a bbox south,west,north,east) from the depth provider; the next rebuild uses them
        // automatic quality check of the active graph (sample harbours × nearest neighbours); findings carry a position to look at
        // runs as a background job (one at a time); the admin page polls /audit/status
        admin.MapPost("/audit", async (int? sample, double? maxNm, int? neighbours, double? draft, CurrentUser current, RoutingAuditJob job, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var p = new AuditParameters(Math.Clamp(sample ?? 120, 1, 2000), Math.Clamp(maxNm ?? 40, 1, 200), Math.Clamp(neighbours ?? 3, 1, 10), draft is > 0 ? draft : null);
            return job.Start(p, current.Id) ? Results.Accepted("/api/admin/routing/audit/status", p) : Results.Problem(statusCode: 409, title: "Ein Audit läuft bereits.");
        });
        admin.MapGet("/audit/status", async (CurrentUser current, RoutingAuditJob job, CancellationToken ct) =>
            await current.IsAdminAsync(ct)
                ? Results.Ok(new { running = job.IsRunning, startedAt = job.StartedAt, parameters = job.Parameters, progress = job.Progress,
                    report = job.Report is { } r ? new { r.Routes, r.Failed, r.Unroutable, r.Counts, r.Findings, r.Seconds, parameters = job.ReportParameters, finishedAt = job.ReportFinishedAt } : null })
                : Results.Forbid());
        admin.MapPost("/audit/cancel", async (CurrentUser current, RoutingAuditJob job, CancellationToken ct) =>
            await current.IsAdminAsync(ct) ? (job.Cancel() ? Results.Accepted() : Results.NoContent()) : Results.Forbid());
        admin.MapPost("/import-depths", async (string? bbox, CurrentUser current, SmsDbContext db, IDepthProvider depths, RoutingGraphBuilder builder, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            double minLng, minLat, maxLng, maxLat;
            if (bbox is { Length: > 0 })
            {
                var p = bbox.Split(',').Select(v => double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
                if (p.Length != 4 || p.Any(double.IsNaN)) return Invalid("bbox", "bbox=south,west,north,east");
                (minLat, minLng, maxLat, maxLng) = (p[0], p[1], p[2], p[3]);
            }
            else { var r = builder.Options.Region; (minLng, minLat, maxLng, maxLat) = (r.MinLng, r.MinLat, r.MaxLng, r.MaxLat); }
            if ((maxLng - minLng) * (maxLat - minLat) > 400) return Invalid("bbox", "Höchstens 400 Quadratgrad pro Import.");
            var (areas, removed) = await DepthImport.RunAsync(depths, db, minLng, minLat, maxLng, maxLat, ct);
            return Results.Ok(new { areas, removed, provider = depths.Name });
        });
        // EMODnet Bathymetry DTM 2024 (routing_dtm) per 1° chunk from ERDDAP, at most 4° × 4° per call; the next graph build reads it
        admin.MapPost("/import-dtm", async (string? bbox, CurrentUser current, SmsDbContext db, IHttpClientFactory http, RoutingGraphBuilder builder, ILogger<SeaRouter> log, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var p = (bbox ?? "").Split(',').Select(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
            if (p.Length != 4 || p.Any(double.IsNaN) || p[2] <= p[0] || p[3] <= p[1]) return Invalid("bbox", "bbox=south,west,north,east");
            int s = (int)Math.Floor(p[0]), w = (int)Math.Floor(p[1]), n = (int)Math.Ceiling(p[2]), e = (int)Math.Ceiling(p[3]);
            if (n - s > 4 || e - w > 4) return Invalid("bbox", "Höchstens 4° × 4° (16 Kacheln) pro Aufruf.");
            var client = http.CreateClient("dtm");
            var (chunks, cells, measured, failed) = await DtmImport.RunAsync(client, builder.Options.DtmUrl, (NpgsqlConnection)db.Database.GetDbConnection(), s, w, n, e, log, ct);
            return Results.Ok(new { chunks, cells, measured, failed });
        });
        // EMODnet vessel density GeoTIFFs (vesseldensity_04_<year>.tif sailing, _05_ pleasure craft, as in the EMODnet zips) → routing_traffic,
        // replaced as a whole: upload every year wanted in one request (the files of a kind are averaged)
        admin.MapPost("/import-traffic-file", async (HttpRequest http, CurrentUser current, SmsDbContext db, RoutingGraphBuilder builder, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (!http.HasFormContentType) return Invalid("file", "multipart/form-data erwartet.");
            var form = await http.ReadFormAsync(new Microsoft.AspNetCore.Http.Features.FormOptions { MultipartBodyLengthLimit = 1L << 30 }, ct);
            if (form.Files.Count == 0) return Invalid("file", "GeoTIFF-Dateien vesseldensity_04_<Jahr>.tif / vesseldensity_05_<Jahr>.tif fehlen.");
            var files = new List<(string kind, GeoTiff tif)>(); var years = new SortedSet<int>();
            foreach (var f in form.Files)
            {
                if (TrafficImport.ParseName(f.FileName) is not { } name) return Invalid("file", $"{f.FileName}: Name vesseldensity_04_<Jahr>.tif oder vesseldensity_05_<Jahr>.tif erwartet.");
                await using var stream = f.OpenReadStream();
                try { files.Add((name.kind, GeoTiff.Open(stream))); }
                catch (NotSupportedException ex) { return Invalid("file", $"{f.FileName}: kein lesbares GeoTIFF ({ex.Message})."); }
                years.Add(name.year);
            }
            var r = builder.Options.Region;
            List<TrafficCell> cells;
            try { cells = TrafficImport.Cells(files, new Envelope(r.MinLng, r.MaxLng, r.MinLat, r.MaxLat)); }
            catch (InvalidDataException ex) { return Invalid("file", ex.Message); }
            var yearText = string.Join(",", years);
            await TrafficImport.WriteAsync((NpgsqlConnection)db.Database.GetDbConnection(), cells, yearText, ct);
            return Results.Ok(new { cells = cells.Count, files = files.Count, years = yearText });
        }).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(1L << 30)); // 6 files × 15 MB > Kestrel's 30 MB default
        admin.MapPost("/import-waterways", async (string bbox, CurrentUser current, SmsDbContext db, IHttpClientFactory http, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (bbox.Split(',').Length != 4) return Invalid("bbox", "bbox=south,west,north,east");
            using var doc = await Overpass.QueryAsync(http, cfg, string.Format(Waterways.OverpassQuery, bbox), ct);
            if (doc is null) return Results.Problem(statusCode: 502, title: "Overpass ist zurzeit nicht erreichbar – später erneut versuchen oder eine Overpass-JSON-Datei hochladen.");
            var b = bbox.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            return Results.Ok(await ImportWaterwaysAsync(doc, db, current.Id, ct, bbox: new Envelope(b[1], b[3], b[0], b[2])));
        });
        // diagnostics: why is there no visibility leg between two points? Lists the obstacle polygons of the area build that the segment meets
        admin.MapGet("/visible", async (string a, string b, CurrentUser current, SmsDbContext db, RoutingGraphBuilder builder, SeaRouter router, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (!TryParsePoint(a, out var pa) || !TryParsePoint(b, out var pb)) return Invalid("a", "a=lat,lng&b=lat,lng");
            try {
            var version = await router.ActiveVersionAsync(ct);
            var env = new Envelope(pa.Coordinate, pb.Coordinate);
            var conn = (NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync(ct);
            var polygons = await RoutingGraphBuilder.ObstaclePolygonsAsync(conn, version, env, builder.Options, ct);
            var seg = Geo.Factory.CreateLineString([pa.Coordinate, pb.Coordinate]);
            var hits = polygons.Where(p => p.EnvelopeInternal.Intersects(seg.EnvelopeInternal)).Select(p => new
            {
                intersects = p.Intersects(seg), prepared = PreparedGeometryFactory.Prepare(p).Intersects(seg), valid = p.IsValid, points = p.NumPoints, holes = p.NumInteriorRings,
                km2 = Math.Round(p.Area * Math.Cos(p.Centroid.Y * Math.PI / 180) * WaterGraphTiler.MPerDegLat * WaterGraphTiler.MPerDegLat / 1e6, 1), bbox = p.EnvelopeInternal.ToString(),
            }).ToList();
            // the raw land of the safety net (routing_land: coast tiles, lakes cut out, no-go blocks) as the build sees it in NTS
            var land = await db.Database.SqlQueryRaw<LandRow>("SELECT geom AS \"Geom\" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)", version, env.MinX, env.MinY, env.MaxX, env.MaxY).Select(r => r.Geom).ToListAsync(ct);
            var landHits = land.SelectMany(g => g is Polygon lp ? [lp] : g is MultiPolygon lmp ? lmp.Geometries.OfType<Polygon>() : []).Select(p => new
            {
                intersects = p.Intersects(seg), prepared = PreparedGeometryFactory.Prepare(p).Intersects(seg), valid = p.IsValid, points = p.NumPoints,
                km2 = Math.Round(p.Area * Math.Cos(p.Centroid.Y * Math.PI / 180) * WaterGraphTiler.MPerDegLat * WaterGraphTiler.MPerDegLat / 1e6, 1), bbox = p.EnvelopeInternal.ToString(),
            }).Where(h => h.intersects || h.prepared).ToList();
            // the bridge/lock step: which obstacle pieces (after chains and charted spans) would touch this leg, and with what clearance
            var raw = (await db.RoutingCorrections.AsNoTracking().Where(c => c.Active && (c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.Lock || c.Kind == CorrectionKind.OpeningBridge)).Select(c => new { c.Kind, c.Geometry, c.ClearanceM }).ToListAsync(ct))
                .Select(c => (c.Geometry is LineString ? c.Geometry : c.Geometry.Centroid, c.Kind, c.ClearanceM)).ToList();
            List<(Geometry geom, CorrectionKind kind, double? clearanceM)> pieces;
            try { pieces = Obstacles.WithSpans(raw); }
            catch (Exception ex) { return Results.Ok(new { polygons = polygons.Count, landPolygons = land.Count, landHits, withSpansError = ex.ToString()[..Math.Min(1500, ex.ToString().Length)] }); }
            var tolDeg = builder.Options.BridgeMatchAreaM / WaterGraphTiler.MPerDegLat;
            var lat = seg.Centroid.Y;
            var bridgeHits = pieces.Where(pc => seg.EnvelopeInternal.Distance(pc.geom.EnvelopeInternal) <= tolDeg)
                .Where(pc => pc.geom is Point pt ? seg.Distance(pt) * WaterGraphTiler.MPerDegLat * (Math.Abs(pt.X - seg.Centroid.X) > Math.Abs(pt.Y - lat) ? Math.Cos(lat * Math.PI / 180) : 1) <= builder.Options.BridgeMatchAreaM : pc.geom.Intersects(seg))
                .Select(pc => new { type = pc.geom.GeometryType, pc.kind, pc.clearanceM, points = pc.geom.NumPoints, bbox = pc.geom.EnvelopeInternal.ToString() }).ToList();
            return Results.Ok(new { polygons = polygons.Count, candidates = hits.Count, hits = hits.Where(h => h.intersects || h.prepared).ToList(), envelopeOnly = hits.Count(h => !h.intersects && !h.prepared), landPolygons = land.Count, landHits, obstaclePieces = pieces.Count, bridgeHits });
            } catch (Exception ex) { return Results.Ok(new { error = ex.ToString()[..Math.Min(2000, ex.ToString().Length)] }); }
        });
        // diagnostics: replay the visibility neighbour search of one node of the active graph
        admin.MapGet("/visible-from", async (long node, CurrentUser current, SmsDbContext db, RoutingGraphBuilder builder, SeaRouter router, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var version = await router.ActiveVersionAsync(ct);
            var conn = (NpgsqlConnection)db.Database.GetDbConnection(); await conn.OpenAsync(ct);
            var o = builder.Options;
            var maxDeg = o.MaxVisibilityM / WaterGraphTiler.MPerDegLat;
            var n = (await db.Database.SqlQueryRaw<NodeRow>("SELECT id AS \"Id\", ST_X(geom) AS \"X\", ST_Y(geom) AS \"Y\" FROM routing_node WHERE graph_version = {0} AND id = {1}", version, node).ToListAsync(ct)).FirstOrDefault();
            if (n is null) return Results.NotFound();
            var cos = Math.Cos(n.Y * Math.PI / 180);
            var env = new Envelope(n.X - maxDeg / cos, n.X + maxDeg / cos, n.Y - maxDeg, n.Y + maxDeg);
            var others = await db.Database.SqlQueryRaw<NodeRow>("SELECT id AS \"Id\", ST_X(geom) AS \"X\", ST_Y(geom) AS \"Y\" FROM routing_node WHERE graph_version = {0} AND id <> {1} AND geom && ST_MakeEnvelope({2},{3},{4},{5},4326)", version, node, env.MinX, env.MinY, env.MaxX, env.MaxY).ToListAsync(ct);
            var polygons = await RoutingGraphBuilder.ObstaclePolygonsAsync(conn, version, env, o, ct);
            var index = VisibilityGraph.Index(polygons);
            var me = new GraphNode(n.Id, n.X, n.Y, false);
            var rows = others.Select(m => (m, d: RoutingGraphBuilder.Haversine(n.Y, n.X, m.Y, m.X))).Where(x => x.d <= o.MaxVisibilityM).OrderBy(x => x.d).Take(60)
                .Select(x => new { x.m.Id, x.m.X, x.m.Y, km = Math.Round(x.d / 1000, 1), sector = (int)Math.Floor((Math.Atan2(x.m.Y - n.Y, (x.m.X - n.X) * cos) + Math.PI) / (2 * Math.PI) * 8) & 7, visible = VisibilityGraph.Visible(me, new GraphNode(x.m.Id, x.m.X, x.m.Y, false), index) }).ToList();
            return Results.Ok(new { node = n, candidates = others.Count, polygons = polygons.Count, rows });
        });
        // narrow natural channels (archipelago sounds, gaps between islands) closed by the coast buffer: medial axes as derived passages
        admin.MapPost("/derive-channels", async (string bbox, CurrentUser current, SmsDbContext db, RoutingGraphBuilder builder, SeaRouter router, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (bbox.Split(',').Length != 4) return Invalid("bbox", "bbox=south,west,north,east");
            var b = bbox.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            var tile = new Envelope(b[1], b[3], b[0], b[2]);
            var version = await router.ActiveVersionAsync(ct);
            if (version <= 0) return Invalid("graph", "Kein aktiver Routing-Graph – zuerst bauen.");
            var margin = new Envelope(tile); margin.ExpandBy(0.02);
            var land = await db.Database.SqlQueryRaw<LandRow>("SELECT ST_CollectionExtract(ST_Intersection(geom, ST_MakeEnvelope({1},{2},{3},{4},4326)), 3) AS \"Geom\" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)", version, margin.MinX, margin.MinY, margin.MaxX, margin.MaxY).Select(r => r.Geom).ToListAsync(ct);
            land = land.Where(g => g is not null && !g.IsEmpty).ToList();
            var o = builder.Options;
            var lines = NarrowChannels.Derive(land, tile, new NarrowChannels.Options(CoastBufferM: o.CoastBufferM, FillHoleKm2: o.FillHoleKm2), Geo.Factory);
            var extent = Geo.Factory.ToGeometry(tile); extent.SRID = 4326;
            var old = await db.RoutingCorrections.Where(c => c.Kind == CorrectionKind.Passage && c.Source == CorrectionSource.Derived && c.Note != null && c.Note.StartsWith("Enge") && c.Geometry.Intersects(extent)).ToListAsync(ct);
            db.RoutingCorrections.RemoveRange(old);
            foreach (var l in lines) db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Passage, Source = CorrectionSource.Derived, Geometry = Srid(l), Active = true, CreatedById = current.Id, Note = $"Enge (abgeleitet, {LengthNm(l):0.0} sm)" });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { landPieces = land.Count, created = lines.Count, removed = old.Count, totalNm = Math.Round(lines.Sum(LengthNm), 1) });
        });
        // fixed road/railway bridges without a charted clearance over water the graph uses (Waterways.ImportUnknownBridgesAsync): replaces
        // those of the bbox; the next rebuild marks the edges under them, routes pass them with a "bridgeUnknown" warning or avoid them
        admin.MapPost("/import-bridges", async (string bbox, CurrentUser current, SmsDbContext db, IHttpClientFactory http, IConfiguration cfg, ILogger<SeaRouter> log, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var b = bbox.Split(',').Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
            if (b.Length != 4 || b.Any(double.IsNaN)) return Invalid("bbox", "bbox=south,west,north,east");
            using var doc = await Overpass.QueryAsync(http, cfg, string.Format(Waterways.OverpassBridgeQuery, bbox), ct, log);
            if (doc is null) return Results.Problem(statusCode: 502, title: "Overpass ist zurzeit nicht erreichbar – später erneut versuchen.");
            var (candidates, created, removed) = await Waterways.ImportUnknownBridgesAsync(doc, db, current.Id, new Envelope(b[1], b[3], b[0], b[2]), ct);
            return Results.Ok(new { candidates, created, removed });
        });
        // assumed passages: where a ferry line or recommended track crosses the routing land (the coastline closes a strait boats pass,
        // Baggensstäket) or runs through coast-zone water the graph cannot connect, and buoy gaps (lateral marks on both sides of a
        // short land strip the graph cannot connect). Replaces the "Annahme" passages of the bbox; the next rebuild uses them
        admin.MapPost("/derive-assumed-passages", async (string bbox, CurrentUser current, SmsDbContext db, IHttpClientFactory http, IConfiguration cfg, RoutingGraphBuilder builder, SeaRouter router, ILogger<SeaRouter> log, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var b = bbox.Split(',').Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN).ToArray();
            if (b.Length != 4 || b.Any(double.IsNaN)) return Invalid("bbox", "bbox=south,west,north,east");
            var tile = new Envelope(b[1], b[3], b[0], b[2]);
            var version = await router.ActiveVersionAsync(ct);
            if (version <= 0) return Invalid("graph", "Kein aktiver Routing-Graph – zuerst bauen.");
            using var doc = await Overpass.QueryAsync(http, cfg, string.Format(Waterways.OverpassGuideQuery, bbox), ct, log);
            if (doc is null) return Results.Problem(statusCode: 502, title: "Overpass ist zurzeit nicht erreichbar – später erneut versuchen.");
            var guides = Waterways.ParseGuideLines(doc, Geo.Factory);
            var ferries = Waterways.ParseFerryLines(doc, Geo.Factory);
            var o = builder.Options;
            // the land of the active graph around the bbox (stretches and buoy pairs reach over its edge), indexed for point tests; beyond
            // the margin a guide line counts as open water (a stretch there would not belong to this chunk anyway)
            var margin = new Envelope(tile); margin.ExpandBy(0.05);
            var land = await db.Database.SqlQueryRaw<LandRow>("SELECT ST_CollectionExtract(ST_Intersection(geom, ST_MakeEnvelope({1},{2},{3},{4},4326)), 3) AS \"Geom\" FROM routing_land WHERE graph_version = {0} AND geom && ST_MakeEnvelope({1},{2},{3},{4},4326)", version, margin.MinX, margin.MinY, margin.MaxX, margin.MaxY).Select(r => r.Geom).ToListAsync(ct);
            var landIndex = new NetTopologySuite.Index.Strtree.STRtree<(IPreparedGeometry prepared, NetTopologySuite.Operation.Distance.IndexedFacetDistance distance)>();
            foreach (var poly in land.Where(g => g is not null && !g.IsEmpty).SelectMany(g => g is Polygon lp ? [lp] : g is MultiPolygon lmp ? lmp.Geometries.OfType<Polygon>() : []))
                landIndex.Insert(poly.EnvelopeInternal, (PreparedGeometryFactory.Prepare(poly), new NetTopologySuite.Operation.Distance.IndexedFacetDistance(poly)));
            landIndex.Build();
            bool OnLand(Coordinate c) { var pt = Geo.Factory.CreatePoint(c); return landIndex.Query(new Envelope(c)).Any(l => l.prepared.Intersects(pt)); }
            // the coast zone of the area build: water closer to land than the coast buffer has no visibility legs
            bool NearLand(Coordinate c)
            {
                var deg = o.CoastBufferM / WaterGraphTiler.MPerDegLat / Math.Max(0.2, Math.Cos(c.Y * Math.PI / 180)); // ponytail: planar degrees, generous north-south; the router test filters
                var env = new Envelope(c); env.ExpandBy(deg);
                var pt = Geo.Factory.CreatePoint(c);
                return landIndex.Query(env).Any(l => l.distance.Distance(pt) <= deg);
            }
            var extent = Geo.Factory.ToGeometry(tile); extent.SRID = 4326;
            // a passage belongs to the chunk its centroid lies in: guide lines and buoy pairs crossing a chunk edge are seen by both chunks
            var old = (await db.RoutingCorrections.Where(c => c.Kind == CorrectionKind.Passage && c.Source == CorrectionSource.Derived && c.Note != null && c.Note.StartsWith("Annahme") && c.Geometry.Intersects(extent)).ToListAsync(ct))
                .Where(c => c.Geometry.Centroid.Within(extent)).ToList();
            db.RoutingCorrections.RemoveRange(old);
            // the ferry tracks themselves (known-navigable water for the strict depth rule, FerryCorridors), one per OSM way and geometry
            var oldFerries = (await db.RoutingCorrections.Where(c => c.Kind == CorrectionKind.Ferry && c.Source == CorrectionSource.Derived && c.Geometry.Intersects(extent)).ToListAsync(ct))
                .Where(c => c.Geometry.Centroid.Within(extent)).ToList();
            db.RoutingCorrections.RemoveRange(oldFerries);
            var ferryGeoms = new HashSet<string>(); var ferryCount = 0;
            foreach (var (line, name) in ferries)
            {
                if (!line.Centroid.Within(extent) || !ferryGeoms.Add(line.AsText())) continue;
                db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Ferry, Source = CorrectionSource.Derived, Geometry = line, Note = name is null ? "Fährlinie" : $"Fährlinie {name}", Active = true, CreatedById = current.Id });
                ferryCount++;
            }
            var created = new List<(LineString line, string note, double? graphM)>();
            bool Near(Coordinate c, double m) => created.Any(x => x.line.IsWithinDistance(Geo.Factory.CreatePoint(c), m / WaterGraphTiler.MPerDegLat));
            // 1. guide-line stretches: across land always (unless the graph already connects them nearly as short – a canal or channel
            //    there), through coast-zone water only where the graph has no route or one more than 3× as long
            var stretches = 0;
            foreach (var gl in guides)
            {
                var coords = gl.Line.Coordinates;
                foreach (var st in AssumedPassages.Stretches(coords, OnLand, NearLand))
                {
                    var sub = AssumedPassages.SubLine(coords, st.From, st.To);
                    if (sub.Length < 2) continue;
                    var line = Geo.Factory.CreateLineString(sub); line.SRID = 4326;
                    if (!line.Centroid.Within(extent) || Near(AssumedPassages.Midpoint(sub), 100)) continue;
                    stretches++;
                    var lengthM = st.To - st.From;
                    var graphM = await router.GraphDistanceAsync(sub[0], sub[^1], ct);
                    if (graphM is { } gm && gm <= (st.Land ? 1.5 : 3) * lengthM) continue;
                    created.Add((line, $"Annahme: {gl.Name ?? "Fährlinie"}", graphM));
                }
            }
            // 2. buoy gaps: lateral marks on both sides of a short land strip; the passage follows a guide line when one passes both marks
            var sw = SeamarkEndpoints.ToMercator(margin.MinY, margin.MinX); var ne = SeamarkEndpoints.ToMercator(margin.MaxY, margin.MaxX);
            var markBox = Geo.Factory.ToGeometry(new Envelope(sw.X, ne.X, sw.Y, ne.Y)); markBox.SRID = 3857;
            var marks = (await db.Seamarks.AsNoTracking().Where(m => (m.Type == "buoy_lateral" || m.Type == "beacon_lateral") && m.Location.Within(markBox)).Select(m => m.Location).ToListAsync(ct))
                .Select(m => Fairways.FromMercator(m.X, m.Y)).Select(m => new Coordinate(m.lng, m.lat)).ToList();
            bool HitsLand(Coordinate p, Coordinate q) { var seg = Geo.Factory.CreateLineString([p, q]); return landIndex.Query(seg.EnvelopeInternal).Any(l => l.prepared.Intersects(seg)); }
            var gaps = AssumedPassages.BuoyGaps(marks, OnLand, hitsLand: HitsLand).Where(g => extent.Contains(Geo.Factory.CreatePoint(g.LandMid))).ToList();
            foreach (var gap in gaps)
            {
                if (Near(gap.LandMid, 200)) continue; // a guide-line stretch already crosses this strip
                Coordinate a = marks[gap.A], c = marks[gap.B];
                var graphM = await router.GraphDistanceAsync(a, c, ct);
                // where the graph connects the marks at all, an island over 100 m wide crossed between two channels side by side is no gap
                // (Rhinplate between the Elbe and the Glückstädter Nebenelbe: 3.2 km round its tip, the assumed 0.5 nm across it was taken)
                if (graphM is { } gm && (gm <= 3 * gap.DistanceM || (gap.LandM > 100 && !gap.ContinuesChain))) continue;
                Coordinate[] coords = [a, c]; var note = "Annahme: Tonnenlücke";
                var pa = Geo.Factory.CreatePoint(a); var pc = Geo.Factory.CreatePoint(c); var tolDeg = 100 / WaterGraphTiler.MPerDegLat;
                if (guides.FirstOrDefault(gl => gl.Line.IsWithinDistance(pa, tolDeg) && gl.Line.IsWithinDistance(pc, tolDeg)) is { } guide)
                {
                    var lil = new NetTopologySuite.LinearReferencing.LocationIndexedLine(guide.Line);
                    var la = lil.Project(a); var lc = lil.Project(c);
                    var along = lil.ExtractLine(la.CompareTo(lc) <= 0 ? la : lc, la.CompareTo(lc) <= 0 ? lc : la).Coordinates;
                    if (la.CompareTo(lc) > 0) Array.Reverse(along);
                    Coordinate[] guided = [a, .. along, c];
                    if (StringPull.Length(guided) <= 3 * gap.DistanceM) { coords = guided.Where((x, i) => i == 0 || !x.Equals2D(guided[i - 1])).ToArray(); note += $" entlang {guide.Name ?? "Fährlinie"}"; }
                }
                var line = Geo.Factory.CreateLineString(coords); line.SRID = 4326;
                created.Add((line, note, graphM));
            }
            foreach (var (line, note, _) in created)
                db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Passage, Source = CorrectionSource.Derived, Geometry = Srid(Geo.Factory.CreateLineString(line.Coordinates.Select(c => new Coordinate(c.X, c.Y)).ToArray())), Active = true, CreatedById = current.Id, Note = $"{note} ({LengthNm(line):0.0} sm)" }); // 2D: interpolated points carried Z (NaN), the column is XY
            await db.SaveChangesAsync(ct);
            return Results.Ok(new
            {
                guideLines = guides.Count, guideStretches = stretches, marks = marks.Count, buoyGaps = gaps.Count, created = created.Count, removed = old.Count, ferries = ferryCount, ferriesRemoved = oldFerries.Count,
                passages = created.Select(x => new { x.note, lat = Math.Round(x.line.Centroid.Y, 5), lng = Math.Round(x.line.Centroid.X, 5), lengthNm = Math.Round(LengthNm(x.line), 2), graphNm = x.graphM is { } g ? Math.Round(g / 1852, 1) : (double?)null }),
            });
        });
        // inland water bodies (lakes, IJsselmeer, Randmeren) – the coastline-based land polygons count them as land
        admin.MapPost("/import-water", async (string bbox, CurrentUser current, SmsDbContext db, IHttpClientFactory http, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (bbox.Split(',').Length != 4) return Invalid("bbox", "bbox=south,west,north,east");
            using var doc = await Overpass.QueryAsync(http, cfg, string.Format(Waterways.OverpassWaterQuery, bbox), ct);
            if (doc is null) return Results.Problem(statusCode: 502, title: "Overpass ist zurzeit nicht erreichbar – später erneut versuchen.");
            var b = bbox.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            var extent = Geo.Factory.ToGeometry(new Envelope(b[1], b[3], b[0], b[2])); extent.SRID = 4326;
            var areas = Waterways.ParseWaterAreas(doc, Geo.Factory);
            var removed = await db.RoutingCorrections.Where(c => c.Kind == CorrectionKind.Water && c.Source == CorrectionSource.Derived && c.Geometry.Intersects(extent)).ExecuteDeleteAsync(ct);
            foreach (var a in areas) db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Water, Source = CorrectionSource.Derived, Geometry = Srid(a.area), Active = true, CreatedById = current.Id, Note = $"Binnengewässer{(a.name is null ? "" : " " + a.name)}" });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { created = areas.Count, removed, lakes = areas.Select(a => new { a.name, km2 = Math.Round(a.area.Area * Math.Cos(a.area.Centroid.Y * Math.PI / 180) * WaterGraphTiler.MPerDegLat * WaterGraphTiler.MPerDegLat / 1e6, 1) }).OrderByDescending(x => x.km2).Take(30) });
        });
        // piers, jetties, pontoons, breakwaters, groynes (not in the coastline land): the router steers round them after smoothing
        admin.MapPost("/import-piers", async (string bbox, CurrentUser current, SmsDbContext db, IHttpClientFactory http, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (bbox.Split(',').Length != 4) return Invalid("bbox", "bbox=south,west,north,east");
            using var doc = await Overpass.QueryAsync(http, cfg, string.Format(Waterways.OverpassPierQuery, bbox), ct);
            if (doc is null) return Results.Problem(statusCode: 502, title: "Overpass ist zurzeit nicht erreichbar – später erneut versuchen.");
            var b = bbox.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            var extent = Geo.Factory.ToGeometry(new Envelope(b[1], b[3], b[0], b[2])); extent.SRID = 4326;
            var removed = await db.RoutingCorrections.Where(c => c.Kind == CorrectionKind.Pier && c.Source == CorrectionSource.Derived && c.Geometry.Intersects(extent)).ExecuteDeleteAsync(ct);
            var created = await AddPiersAsync(doc, db, current.Id, ct);
            return Results.Ok(new { created, removed });
        });
        admin.MapPost("/import-waterways-file", async (HttpRequest http, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (!http.HasFormContentType) return Invalid("file", "multipart/form-data erwartet.");
            var form = await http.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null) return Invalid("file", "Overpass-JSON-Datei fehlt.");
            await using var stream = file.OpenReadStream();
            JsonDocument doc;
            try { doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct); }
            catch (JsonException) { return Invalid("file", "Kein Overpass-JSON (Datei ist kein JSON)."); }
            using var owned = doc;
            if (!doc.RootElement.TryGetProperty("elements", out _)) return Invalid("file", "Kein Overpass-JSON (elements fehlt).");
            return Results.Ok(await ImportWaterwaysAsync(doc, db, current.Id, ct));
        });

        // Spec 10 §10.5 step 2: fairway centrelines from the lateral marks already imported into the Seamarks table.
        admin.MapPost("/derive-fairways", async (string bbox, CurrentUser current, SmsDbContext db, RoutingGraphBuilder builder, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var p = bbox.Split(',');
            if (p.Length != 4 || !double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) || !double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
                || !double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) || !double.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var e))
                return Invalid("bbox", "bbox=south,west,north,east");
            // marks from the bbox plus a margin, lines kept (and old ones replaced) by their centroid: a chain across a chunk border is
            // derived whole and belongs to one chunk (2° chunks cut the Mönsterås fairway at 16.5 E into two chains, the one-sided
            // stretch between them was never bridged, and lines crossing a border were never replaced)
            var marginLat = DeriveMarginDeg; var marginLng = DeriveMarginDeg / Math.Max(0.2, Math.Cos((s + n) / 2 * Math.PI / 180));
            await db.Database.OpenConnectionAsync(ct);
            var (marks, water) = await Fairways.InputsAsync((Npgsql.NpgsqlConnection)db.Database.GetDbConnection(), new Envelope(w - marginLng, e + marginLng, s - marginLat, n + marginLat), ct);
            var extent = Geo.Factory.ToGeometry(new Envelope(w, e, s, n)); extent.SRID = 4326;
            var fairways = Fairways.Derive(marks, Geo.Factory, water).Where(f => extent.Intersects(f.Line.Centroid)).ToList();
            // the lines, and the water between the marks of the paired lines as Fairway areas (the graph build cuts them out of the
            // channel zone and leaves them open under the strict depth rule: buoyed water, Fairways.AreaNote)
            var old = await db.RoutingCorrections.Where(c => c.Source == CorrectionSource.Derived && c.Note != null && ((c.Kind == CorrectionKind.Passage && c.Note.StartsWith("Fahrwasser")) || (c.Kind == CorrectionKind.Fairway && c.Note.StartsWith(Fairways.AreaNote))) && c.Geometry.Centroid.Intersects(extent)).ToListAsync(ct);
            db.RoutingCorrections.RemoveRange(old);
            foreach (var f in fairways)
            {
                db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Passage, Source = CorrectionSource.Derived, Geometry = f.Line, Note = Fairways.Note(f), Active = true, CreatedById = current.Id });
                if (f.Area is { } area) db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Fairway, Source = CorrectionSource.Derived, Geometry = area, Note = $"{Fairways.AreaNote}, {f.Pairs} {Fairways.PairedNote}{(f.Wide ? ", " + Fairways.WideNote : "")})", Active = true, CreatedById = current.Id });
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { marks = marks.Count, created = fairways.Count, areas = fairways.Count(f => f.Area is not null), removed = old.Count, lines = fairways.Select(f => new { f.Pairs, points = f.Line.NumPoints, lengthNm = Math.Round(LengthNm(f.Line), 1) }) });
        });

        // Spec 10 §10.1: Europe-wide import from a Geofabrik PBF extract (background job with progress).
        admin.MapPost("/import-pbf", async (PbfImportRequest req, CurrentUser current, PbfImportJob job, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (job.Validate(req.Url) is { } error) return Invalid("url", error);
            if (!req.Seamarks && !req.Harbours && !req.Waterways) return Invalid("seamarks", "Mindestens eine Datenart wählen.");
            return job.TryStart(req, current.Id) ? Results.Accepted("/api/admin/routing/import-pbf/status") : Results.Problem(statusCode: 409, title: "Ein Import läuft bereits.");
        });
        admin.MapGet("/import-pbf/status", async (CurrentUser current, PbfImportJob job, CancellationToken ct) =>
            await current.IsAdminAsync(ct) ? Results.Ok(new { running = job.IsRunning, progress = job.Progress }) : Results.Forbid());

        admin.MapGet("/unreachable-harbours", async (CurrentUser current, SmsDbContext db, SeaRouter router, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            var version = await router.ActiveVersionAsync(ct);
            var reachable = await db.Database.SqlQueryRaw<Guid>("SELECT harbour_id AS \"Value\" FROM routing_harbour_access WHERE graph_version = {0}", version).ToListAsync(ct);
            var region = (await router.StatusAsync(ct)).Region?.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var q = db.Harbours.AsNoTracking().Where(h => h.IsActive && h.MergedIntoId == null && !reachable.Contains(h.Id));
            if (region is { Length: 4 }) { var env = Geo.Factory.ToGeometry(new Envelope(region[0], region[2], region[1], region[3])); q = q.Where(h => h.Location.Intersects(env)); }
            return Results.Ok(await q.Select(h => new { h.Id, h.Name }).ToListAsync(ct));
        });
    }

    /// <summary>Replaces the derived corrections that touch the imported extent (the queried bbox, else the envelope of the parsed ways),
    /// keeps manual ones and derived ones elsewhere. The bbox matters for chunked imports: a river fetched by one chunk stretches the
    /// envelope of its ways over the neighbouring chunks, whose corrections would otherwise be deleted again.</summary>
    public static async Task<WaterwayImportResult> ImportWaterwaysAsync(JsonDocument doc, SmsDbContext db, Guid? userId, CancellationToken ct, bool replaceExisting = true, Envelope? bbox = null)
    {
        var ways = Waterways.Parse(doc, Geo.Factory);
        var obstacles = Waterways.ParseObstacles(doc);
        var areas = Waterways.ParseFairwayAreas(doc, Geo.Factory);
        var noGo = Waterways.ParseNoGoAreas(doc, Geo.Factory);
        if (ways.Count == 0 && obstacles.Count == 0 && areas.Count == 0 && noGo.Count == 0) return new WaterwayImportResult(0, 0, 0, 0, []);
        var env = bbox ?? (ways.Count == 0 ? null : ways.Select(w => w.Line.EnvelopeInternal).Aggregate(new Envelope(), (e, x) => { e.ExpandToInclude(x); return e; }));
        var old = new List<RoutingCorrection>();
        if (replaceExisting && env is not null)
        {
            var extent = Geo.Factory.ToGeometry(env); extent.SRID = 4326;
            old = await db.RoutingCorrections.Where(c => ((c.Kind == CorrectionKind.Passage && !(c.Note != null && (c.Note.StartsWith("Fahrwasser") || c.Note.StartsWith("Enge") || c.Note.StartsWith("Annahme")))) || (c.Kind == CorrectionKind.Bridge && !(c.Note != null && c.Note.StartsWith(Waterways.UnknownBridgeNote))) || c.Kind == CorrectionKind.Lock || c.Kind == CorrectionKind.OpeningBridge || (c.Kind == CorrectionKind.Fairway && !(c.Note != null && c.Note.StartsWith(Fairways.AreaNote))) || c.Kind == CorrectionKind.Avoid || (c.Kind == CorrectionKind.Block && c.Note != null && c.Note.StartsWith("Sperrgebiet"))) && c.Source == CorrectionSource.Derived && c.Geometry.Intersects(extent)).ToListAsync(ct);
        }
        // a waterway chain that crosses the bbox edge was merged from more ways by the neighbouring chunk's import: it is kept
        // (deleting it and re-adding only this chunk's part cut the Stockholm fairway off from Danvikskanalen and the Hammarby lock),
        // and a new chain lying along such a kept chain is not added twice
        var keptPassages = env is null ? [] : old.Where(c => c.Kind == CorrectionKind.Passage && !env.Contains(c.Geometry.EnvelopeInternal)).ToList();
        old = old.Except(keptPassages).ToList();
        var keptIndex = new NetTopologySuite.Index.Strtree.STRtree<NetTopologySuite.Geometries.Prepared.IPreparedGeometry>();
        foreach (var k in keptPassages) keptIndex.Insert(k.Geometry.EnvelopeInternal, NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(k.Geometry.Buffer(10 / 111_320.0)));
        bool AlongKept(Geometry line) => keptIndex.Query(line.EnvelopeInternal).Any(k => k.Covers(line));
        db.RoutingCorrections.RemoveRange(old);
        foreach (var w in ways.Where(w => !AlongKept(w.Line)))
            db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Passage, Source = CorrectionSource.Derived, Geometry = w.Line, Note = w.Name is null ? $"Wasserstraße ({w.Ways} OSM-Wege)" : $"{w.Name} ({w.Ways} OSM-Wege)", Active = true, CreatedById = userId });
        foreach (var ob in obstacles) db.RoutingCorrections.Add(ToCorrection(ob, userId));
        foreach (var a in areas) db.RoutingCorrections.Add(ToCorrection(a, userId));
        foreach (var a in noGo) db.RoutingCorrections.Add(ToBlock(a, userId));
        await db.SaveChangesAsync(ct);
        return new WaterwayImportResult(ways.Count, old.Count, obstacles.Count(x => !x.IsLock), obstacles.Count(x => x.IsLock), ways.Select(w => (object)new { w.Name, w.Ways, points = w.Line.NumPoints, lengthNm = Math.Round(LengthNm(w.Line), 1) }).ToList(), areas.Count, noGo.Count);
    }

    /// <summary>Adds the pier ways of an Overpass-style document as derived Pier corrections; drops the route cache (cached routes were
    /// computed without them) and the router's pier index.</summary>
    public static async Task<int> AddPiersAsync(JsonDocument doc, SmsDbContext db, Guid? userId, CancellationToken ct)
    {
        var piers = Waterways.ParsePiers(doc, Geo.Factory);
        if (piers.Count == 0) return 0;
        foreach (var (g, label) in piers) db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Pier, Source = CorrectionSource.Derived, Geometry = g, Active = true, CreatedById = userId, Note = label });
        await db.SaveChangesAsync(ct);
        await db.RouteCaches.ExecuteDeleteAsync(ct);
        SeaRouter.InvalidatePiers();
        return piers.Count;
    }

    /// <summary>A no-go area becomes a Block (the graph build treats it as land, the audit reports routes through it); a warning area
    /// becomes an Avoid (edges inside cost ×AvoidPenaltyFactor).</summary>
    private static RoutingCorrection ToBlock((Polygon area, string label, bool hard) a, Guid? userId) => new()
    {
        Kind = a.hard ? CorrectionKind.Block : CorrectionKind.Avoid, Source = CorrectionSource.Derived, Geometry = Srid(a.area), Active = true, CreatedById = userId, Note = a.label,
    };

    private static RoutingCorrection ToCorrection((Polygon area, string? name) a, Guid? userId) => new()
    {
        Kind = CorrectionKind.Fairway, Source = CorrectionSource.Derived, Geometry = Srid(a.area), Active = true, CreatedById = userId, Note = $"Fahrwasserfläche{(a.name is null ? "" : " " + a.name)}",
    };
    private static Geometry Srid(Geometry g) { g.SRID = 4326; return g; }

    private static RoutingCorrection ToCorrection(Obstacle ob, Guid? userId) => new()
    {
        Kind = ob.IsLock ? CorrectionKind.Lock : ob.Opening ? CorrectionKind.OpeningBridge : CorrectionKind.Bridge, Source = CorrectionSource.Derived, Geometry = (Geometry?)ob.Line ?? Geo.Point(ob.Lat, ob.Lng), ClearanceM = ob.ClearanceM, Active = true, CreatedById = userId,
        Note = ob.IsLock ? $"Schleuse{(ob.Name is null ? "" : " " + ob.Name)}" : $"{(ob.Opening ? "Klappbrücke" : "Brücke")}{(ob.Name is null ? "" : " " + ob.Name)}{(ob.ClearanceM is { } c ? $" – Durchfahrtshöhe {c.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} m{(ob.Opening ? " (geöffnet)" : "")}" : "")}",
    };

    private sealed class LandRow { public Geometry Geom { get; set; } = null!; }
    private sealed class NodeRow { public long Id { get; set; } public double X { get; set; } public double Y { get; set; } }

    private static double LengthNm(LineString l)
    {
        double m = 0;
        for (var i = 1; i < l.NumPoints; i++) m += RoutingGraphBuilder.Haversine(l.GetCoordinateN(i - 1).Y, l.GetCoordinateN(i - 1).X, l.GetCoordinateN(i).Y, l.GetCoordinateN(i).X);
        return m / 1852;
    }

    private static bool TryParsePoint(string? s, out Point p)
    {
        p = null!;
        var parts = s?.Split(',');
        if (parts is not { Length: 2 } || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lng)) return false;
        p = Geo.Point(lat, lng);
        return true;
    }
}
