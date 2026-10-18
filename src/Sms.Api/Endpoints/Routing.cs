using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using NetTopologySuite.Geometries;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

public sealed record WaterwayImportResult(int Created, int Removed, int Bridges, int Locks, IReadOnlyList<object> Lines, int FairwayAreas = 0, int NoGoAreas = 0);
public record CorrectionUpsert(CorrectionKind Kind, Geometry Geometry, string? Note, bool Active = true, double? ClearanceM = null);

/// <summary>Spec 10 §10.4. Falls back to straight lines (method "straight-line") while no graph is built.</summary>
public static class RoutingEndpoints
{
    public const string Disclaimer = "Entfernungen sind Schätzwerte und nicht zur Navigation geeignet.";
    public const string Attribution = "© OpenStreetMap-Mitwirkende, OpenSeaMap";

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

        g.MapGet("/route", async (Guid? fromHarbour, Guid? toHarbour, string? from, string? to, double? airDraft, double? draft, SmsDbContext db, SeaRouter router, CancellationToken ct) =>
        {
            RouteResult? r = null;
            Point? a = null, b = null;
            draft ??= router.DefaultDraftM; // no ship given: route for a typical yacht, not for a dinghy
            if (fromHarbour is { } f && toHarbour is { } t)
            {
                a = await db.Harbours.AsNoTracking().Where(h => h.Id == f).Select(h => h.Location).FirstOrDefaultAsync(ct);
                b = await db.Harbours.AsNoTracking().Where(h => h.Id == t).Select(h => h.Location).FirstOrDefaultAsync(ct);
                if (a is null || b is null) return Results.NotFound();
                r = await router.RouteAsync(f, t, ct, airDraft, draft);
            }
            else if (TryParsePoint(from, out var pa) && TryParsePoint(to, out var pb))
            {
                a = pa; b = pb;
                r = await router.RouteAsync(pa, pb, ct, airDraft, draft);
            }
            else return Invalid("from", "fromHarbour/toHarbour oder from=lat,lng&to=lat,lng angeben.");

            if (r is null)
            {
                var nm = Math.Round((decimal)(RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X) / 1852), 1);
                var line = Geo.Factory.CreateLineString([a.Coordinate, b.Coordinate]);
                return Results.Ok(new { distanceNm = nm, straightLineNm = nm, geometry = line, graphVersion = 0, method = "straight-line", disclaimer = Disclaimer, attribution = Attribution });
            }
            return Results.Ok(new { distanceNm = r.DistanceNm, straightLineNm = r.StraightLineNm, geometry = r.Geometry, graphVersion = r.GraphVersion, method = r.Method, disclaimer = Disclaimer, attribution = Attribution });
        });

        g.MapGet("/status", async (SeaRouter router, RoutingGraphBuilder builder, SmsDbContext db, CancellationToken ct) =>
        {
            var s = await router.StatusAsync(ct);
            var waterways = await db.RoutingCorrections.CountAsync(c => c.Kind == CorrectionKind.Passage && c.Source == CorrectionSource.Derived && c.Active, ct);
            return Results.Ok(new { method = s.ActiveVersion > 0 ? "sea-graph" : "straight-line", graphVersion = s.ActiveVersion, s.BuiltAt, s.Region, s.Nodes, s.Edges, waterways, building = builder.IsRunning, progress = builder.Progress, options = builder.Options });
        });

        var admin = api.MapGroup("/admin/routing").WithTags("Admin").RequireAuthorization();
        admin.MapPost("/rebuild", async (CurrentUser current, RoutingGraphBuilder builder, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            return builder.TryStart() ? Results.Accepted("/api/routing/status") : Results.Problem(statusCode: 409, title: "Ein Build läuft bereits.");
        });
        admin.MapGet("/corrections", async (CurrentUser current, SmsDbContext db, CancellationToken ct) =>
            await current.IsAdminAsync(ct) ? Results.Ok(await db.RoutingCorrections.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(ct)) : Results.Forbid());
        admin.MapPost("/corrections", async (CorrectionUpsert req, CurrentUser current, SmsDbContext db, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (req.Kind == CorrectionKind.Block && req.Geometry is not Polygon) return Invalid("geometry", "Block braucht ein Polygon.");
            if (req.Kind == CorrectionKind.Passage && req.Geometry is not LineString) return Invalid("geometry", "Passage braucht eine LineString.");
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
        admin.MapPost("/audit", async (int? sample, double? maxNm, int? neighbours, double? draft, CurrentUser current, RoutingAudit audit, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            return Results.Ok(await audit.RunAsync(Math.Clamp(sample ?? 150, 1, 2000), Math.Clamp(maxNm ?? 40, 1, 200), Math.Clamp(neighbours ?? 3, 1, 10), draft, ct));
        });
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
        admin.MapPost("/import-waterways", async (string bbox, CurrentUser current, SmsDbContext db, IHttpClientFactory http, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!await current.IsAdminAsync(ct)) return Results.Forbid();
            if (bbox.Split(',').Length != 4) return Invalid("bbox", "bbox=south,west,north,east");
            using var doc = await Overpass.QueryAsync(http, cfg, string.Format(Waterways.OverpassQuery, bbox), ct);
            if (doc is null) return Results.Problem(statusCode: 502, title: "Overpass ist zurzeit nicht erreichbar – später erneut versuchen oder eine Overpass-JSON-Datei hochladen.");
            var b = bbox.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            return Results.Ok(await ImportWaterwaysAsync(doc, db, current.Id, ct, bbox: new Envelope(b[1], b[3], b[0], b[2])));
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
            var sw = SeamarkEndpoints.ToMercator(s, w); var ne = SeamarkEndpoints.ToMercator(n, e);
            var box = Geo.Factory.ToGeometry(new Envelope(sw.X, ne.X, sw.Y, ne.Y)); box.SRID = 3857;
            var rows = await db.Seamarks.AsNoTracking().Where(m => (m.Type == "buoy_lateral" || m.Type == "beacon_lateral") && m.Location.Within(box)).Select(m => new { m.Type, m.Category, m.Location }).ToListAsync(ct);
            var marks = rows.Select(m => (side: Fairways.SideOf(m.Type, m.Category), pos: Fairways.FromMercator(m.Location.X, m.Location.Y))).Where(m => m.side != null)
                .Select(m => new LateralMark(m.pos.lat, m.pos.lng, m.side!.Value)).ToList();
            var fairways = Fairways.Derive(marks, Geo.Factory);
            var extent = Geo.Factory.ToGeometry(new Envelope(w, e, s, n)); extent.SRID = 4326;
            var old = await db.RoutingCorrections.Where(c => c.Kind == CorrectionKind.Passage && c.Source == CorrectionSource.Derived && c.Note != null && c.Note.StartsWith("Fahrwasser") && c.Geometry.Within(extent)).ToListAsync(ct);
            db.RoutingCorrections.RemoveRange(old);
            foreach (var f in fairways)
                db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Passage, Source = CorrectionSource.Derived, Geometry = f.Line, Note = $"Fahrwasser ({f.Pairs} Tonnenpaare)", Active = true, CreatedById = current.Id });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { marks = marks.Count, created = fairways.Count, removed = old.Count, lines = fairways.Select(f => new { f.Pairs, points = f.Line.NumPoints, lengthNm = Math.Round(LengthNm(f.Line), 1) }) });
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
            old = await db.RoutingCorrections.Where(c => ((c.Kind == CorrectionKind.Passage && !(c.Note != null && c.Note.StartsWith("Fahrwasser"))) || c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.Lock || c.Kind == CorrectionKind.OpeningBridge || c.Kind == CorrectionKind.Fairway || c.Kind == CorrectionKind.Avoid || (c.Kind == CorrectionKind.Block && c.Note != null && c.Note.StartsWith("Sperrgebiet"))) && c.Source == CorrectionSource.Derived && c.Geometry.Intersects(extent)).ToListAsync(ct);
        }
        db.RoutingCorrections.RemoveRange(old);
        foreach (var w in ways)
            db.RoutingCorrections.Add(new RoutingCorrection { Kind = CorrectionKind.Passage, Source = CorrectionSource.Derived, Geometry = w.Line, Note = w.Name is null ? $"Wasserstraße ({w.Ways} OSM-Wege)" : $"{w.Name} ({w.Ways} OSM-Wege)", Active = true, CreatedById = userId });
        foreach (var ob in obstacles) db.RoutingCorrections.Add(ToCorrection(ob, userId));
        foreach (var a in areas) db.RoutingCorrections.Add(ToCorrection(a, userId));
        foreach (var a in noGo) db.RoutingCorrections.Add(ToBlock(a, userId));
        await db.SaveChangesAsync(ct);
        return new WaterwayImportResult(ways.Count, old.Count, obstacles.Count(x => !x.IsLock), obstacles.Count(x => x.IsLock), ways.Select(w => (object)new { w.Name, w.Ways, points = w.Line.NumPoints, lengthNm = Math.Round(LengthNm(w.Line), 1) }).ToList(), areas.Count, noGo.Count);
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
