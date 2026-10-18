using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;
using Sms.Domain;
using Sms.Infrastructure;
using Sms.Infrastructure.Routing;

namespace Sms.Api;

public sealed record AuditFinding(string Check, string From, string To, double Lat, double Lng, string Detail);
public sealed record AuditReport(int Routes, int Failed, int Unroutable, IReadOnlyDictionary<string, int> Counts, IReadOnlyList<AuditFinding> Findings, double Seconds);

/// <summary>
/// Automatic check of the routing quality: routes between every sampled harbour and a few of its neighbours, then verifies
/// each route against the rules a skipper applies – no leg over land or through a no-go area, no leg across water charted
/// shallower than the draft (except on a marked passage or in the harbour approach), and where a buoy chain leads to the
/// harbour, the final approach runs inside it. The user's sample cases (Marstal, Svendborg, Wendtorf, Great Belt) are
/// instances of these rules; the audit finds the rest.
/// </summary>
public sealed class RoutingAudit(SeaRouter router, RoutingGraphBuilder builder, SmsDbContext db)
{
    /// <param name="afterRoute">called after every routed pair with (routes so far, expected total = picked × neighbours, counts per check);
    /// the background job reports progress and throttles there</param>
    public async Task<AuditReport> RunAsync(int sample, double maxNm, int neighbours, double? draftM, CancellationToken ct, Func<int, int, IReadOnlyDictionary<string, int>, Task>? afterRoute = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var o = builder.Options;
        var status = await router.StatusAsync(ct);
        if (status.ActiveVersion == 0) return new AuditReport(0, 0, 0, new Dictionary<string, int>(), [], 0);
        var region = status.Region?.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var harboursQ = db.Harbours.AsNoTracking().Where(h => h.IsActive && h.MergedIntoId == null);
        if (region is { Length: 4 }) { var box = Geo.Factory.ToGeometry(new Envelope(region[0], region[2], region[1], region[3])); box.SRID = 4326; harboursQ = harboursQ.Where(h => h.Location.Intersects(box)); }
        var harbours = await harboursQ.OrderBy(h => h.Id).Select(h => new { h.Id, h.Name, h.Location }).ToListAsync(ct);
        var step = Math.Max(1, harbours.Count / Math.Max(1, sample));
        var picked = harbours.Where((h, i) => i % step == 0).Take(sample).ToList();
        var names = harbours.ToDictionary(h => h.Id, h => h.Name);
        var draft = draftM ?? router.DefaultDraftM ?? 0;

        var land = Index(await db.Database.SqlQueryRaw<GeomRow>("SELECT geom AS \"Geom\" FROM routing_land WHERE graph_version = {0}", status.ActiveVersion).Select(r => r.Geom).ToListAsync(ct));
        var blocks = await db.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == CorrectionKind.Block).Select(c => new { c.Geometry, c.Note }).ToListAsync(ct);
        var blockIndex = Index(blocks.Select(b => b.Geometry).ToList());
        var passages = await db.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == CorrectionKind.Passage).Select(c => c.Geometry).ToListAsync(ct);
        var passageIndex = Index(passages.Select(p => (Geometry)p.Buffer(300 / WaterGraphTiler.MPerDegLat)).ToList());
        var fairways = await db.RoutingCorrections.AsNoTracking().Where(c => c.Active && c.Kind == CorrectionKind.Passage && c.Source == CorrectionSource.Derived && c.Note != null && c.Note.StartsWith("Fahrwasser (")).Select(c => c.Geometry).ToListAsync(ct);
        var fairwayLines = fairways.OfType<LineString>().ToList();
        var fairwayIndex = new STRtree<LineString>(); foreach (var f in fairwayLines) fairwayIndex.Insert(f.EnvelopeInternal, f); fairwayIndex.Build();
        var shallow = draft > 0 ? Index(await db.Database.SqlQueryRaw<GeomRow>("SELECT geom AS \"Geom\" FROM routing_depth WHERE min_depth_m < {0}", draft + o.DepthSafetyMarginM).Select(r => r.Geom).ToListAsync(ct)) : new STRtree<IPreparedGeometry>();
        var accessDeg = o.HarbourAccessM / WaterGraphTiler.MPerDegLat;
        // bridges with charted spans: the parts outside the spans are impassable for every ship
        var bridgePieces = Obstacles.WithSpans((await db.RoutingCorrections.AsNoTracking().Where(c => c.Active && (c.Kind == CorrectionKind.Bridge || c.Kind == CorrectionKind.OpeningBridge)).Select(c => new { c.Kind, c.Geometry, c.ClearanceM }).ToListAsync(ct))
            .Select(c => (c.Geometry is LineString ? c.Geometry : c.Geometry.Centroid, c.Kind, c.ClearanceM)).ToList());
        var closedSpans = Index(bridgePieces.Where(p => p.geom is not Point && p.clearanceM is <= 0).Select(p => p.geom).ToList());

        var findings = new List<AuditFinding>(); var counts = new Dictionary<string, int>(); var routes = 0; var failedRoutes = 0; var unroutable = 0;
        void Count(string k) => counts[k] = counts.GetValueOrDefault(k) + 1;
        var expected = picked.Count * neighbours;
        foreach (var h in picked)
        {
            ct.ThrowIfCancellationRequested();
            var near = await router.NearbyAsync(h.Id, null, maxNm, neighbours, ct);
            if (near is null) continue;
            foreach (var n in near)
            {
                var to = harbours.FirstOrDefault(x => x.Id == n.HarbourId); if (to is null) continue;
                routes++;
                var r = await router.RouteAsync(h.Id, n.HarbourId, ct, null, draft > 0 ? draft : null);
                if (r is null) { unroutable++; Count("unroutable"); findings.Add(new("unroutable", h.Name, to.Name, h.Location.Y, h.Location.X, $"no route for draft {draft} m")); if (afterRoute is not null) await afterRoute(routes, expected, counts); continue; }
                var failed = false;
                var g = r.Geometry;
                var a = h.Location.Coordinate; var b = to.Location.Coordinate;
                // 1. land and no-go areas: any part of the route beyond the harbour approach
                for (var i = 1; i < g.NumPoints; i++)
                {
                    var seg = Geo.Factory.CreateLineString([g.GetCoordinateN(i - 1), g.GetCoordinateN(i)]);
                    var inApproach = seg.Distance(Geo.Factory.CreatePoint(a)) < accessDeg || seg.Distance(Geo.Factory.CreatePoint(b)) < accessDeg;
                    // a leg on a passage (a river or canal is land in the coastline polygons) is fine; any other leg over land is not
                    if (!inApproach && land.Query(seg.EnvelopeInternal).Any(l => l.Crosses(seg) || l.Contains(seg)) && !passageIndex.Query(seg.EnvelopeInternal).Any(p => p.Covers(seg))) { failed = true; Count("land"); findings.Add(new("land", h.Name, to.Name, seg.Centroid.Y, seg.Centroid.X, "leg crosses land")); break; }
                    if (blockIndex.Query(seg.EnvelopeInternal).Any(bk => bk.Intersects(seg))) { failed = true; Count("no-go"); findings.Add(new("no-go", h.Name, to.Name, seg.Centroid.Y, seg.Centroid.X, "leg crosses a blocked area")); break; }
                    if (closedSpans.Query(seg.EnvelopeInternal).Any(bk => bk.Intersects(seg))) { failed = true; Count("bridge"); findings.Add(new("bridge", h.Name, to.Name, seg.Centroid.Y, seg.Centroid.X, "leg crosses a bridge outside its charted span")); break; }
                    if (draft > 0 && !inApproach && shallow.Query(seg.EnvelopeInternal).Any(s => s.Intersects(seg)) && !passageIndex.Query(seg.EnvelopeInternal).Any(p => p.Covers(seg)))
                    {
                        // shallow: only report when the segment really runs inside the shallow area (not merely touches its edge)
                        var inside = shallow.Query(seg.EnvelopeInternal).Select(s => s.Geometry.Intersection(seg).Length).Sum();
                        if (inside > seg.Length * 0.2) { failed = true; Count("shallow"); findings.Add(new("shallow", h.Name, to.Name, seg.Centroid.Y, seg.Centroid.X, $"leg runs through water charted below {draft + o.DepthSafetyMarginM:0.0} m off any passage")); break; }
                    }
                }
                // 2. buoy chain into the harbour: a derived fairway ending within 2 km of the destination must carry the approach
                var chain = fairwayIndex.Query(new Envelope(b.X - 0.03, b.X + 0.03, b.Y - 0.02, b.Y + 0.02)).Where(f => Math.Min(DistM(f.StartPoint.Coordinate, b), DistM(f.EndPoint.Coordinate, b)) <= 2000 && LengthM(f) >= 800).ToList();
                if (chain.Count > 0)
                {
                    var (inside, total) = ApproachInBand(g, b, 4000, chain, 300);
                    if (total > 1500 && inside / total < 0.5) { failed = true; Count("channel"); findings.Add(new("channel", h.Name, to.Name, b.Y, b.X, $"approach runs only {100 * inside / total:0}% inside the buoy chain")); }
                }
                if (failed) failedRoutes++;
                if (afterRoute is not null) await afterRoute(routes, expected, counts);
            }
        }
        return new AuditReport(routes, failedRoutes, unroutable, counts, findings.Take(400).ToList(), Math.Round(sw.Elapsed.TotalSeconds, 1));
    }

    /// <summary>Of the last lastM metres of the route before the destination, how much lies within bandM of one of the chains.</summary>
    public static (double inside, double total) ApproachInBand(LineString route, Coordinate dest, double lastM, IReadOnlyList<LineString> chains, double bandM)
    {
        var pts = route.Coordinates.Reverse().ToList();
        double inside = 0, total = 0;
        for (var i = 1; i < pts.Count && total < lastM; i++)
        {
            var a = pts[i - 1]; var b = pts[i]; var len = DistM(a, b); var n = Math.Max(1, (int)(len / 100));
            for (var k = 0; k < n && total < lastM; k++)
            {
                var p = new Coordinate(a.X + (b.X - a.X) * (k + 0.5) / n, a.Y + (b.Y - a.Y) * (k + 0.5) / n);
                var cos = Math.Cos(p.Y * Math.PI / 180);
                var d = chains.Min(c => NetTopologySuite.Operation.Distance.DistanceOp.NearestPoints(c, Geo.Factory.CreatePoint(p)) is var np ? Math.Sqrt(Math.Pow((np[0].X - np[1].X) * cos, 2) + Math.Pow(np[0].Y - np[1].Y, 2)) * WaterGraphTiler.MPerDegLat : double.MaxValue);
                total += len / n; if (d <= bandM) inside += len / n;
            }
        }
        return (inside, total);
    }

    private static double DistM(Coordinate a, Coordinate b) => RoutingGraphBuilder.Haversine(a.Y, a.X, b.Y, b.X);
    private static double LengthM(LineString l) { var m = 0.0; for (var i = 1; i < l.NumPoints; i++) m += DistM(l.GetCoordinateN(i - 1), l.GetCoordinateN(i)); return m; }
    private static STRtree<IPreparedGeometry> Index(IReadOnlyList<Geometry> gs) { var t = new STRtree<IPreparedGeometry>(); foreach (var g in gs) t.Insert(g.EnvelopeInternal, PreparedGeometryFactory.Prepare(g)); t.Build(); return t; }
    private sealed class GeomRow { public Geometry Geom { get; set; } = null!; }
}

public sealed record AuditParameters(int Sample, double MaxNm, int Neighbours, double? Draft);

/// <summary>
/// Runs <see cref="RoutingAudit"/> in the background (one at a time) with a progress list the admin page polls, like
/// <see cref="PbfImportJob"/>. Pauses 150 ms between routes so route requests of other users are not starved meanwhile.
/// </summary>
public sealed class RoutingAuditJob(IServiceScopeFactory scopes, ILogger<RoutingAuditJob> logger)
{
    private readonly List<PbfProgress> _progress = [];
    private int _running;
    private CancellationTokenSource? _cts;
    public bool IsRunning => _running == 1;
    public DateTimeOffset? StartedAt { get; private set; }
    public AuditParameters? Parameters { get; private set; }
    public AuditReport? Report { get; private set; }
    public AuditParameters? ReportParameters { get; private set; }
    public DateTimeOffset? ReportFinishedAt { get; private set; }
    public IReadOnlyList<PbfProgress> Progress { get { lock (_progress) return _progress.ToList(); } }

    public bool Start(AuditParameters p, Guid? userId)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        lock (_progress) _progress.Clear();
        var cts = _cts = new CancellationTokenSource();
        StartedAt = DateTimeOffset.UtcNow; Parameters = p;
        Add("started", $"sample {p.Sample}, max {p.MaxNm} nm, {p.Neighbours} neighbours, draft {(p.Draft is { } d ? $"{d} m" : "default")} (user {userId})");
        // ponytail: thread-pool task at normal priority; the 150 ms pause between routes is the throttle
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var audit = scope.ServiceProvider.GetRequiredService<RoutingAudit>();
                var report = await audit.RunAsync(p.Sample, p.MaxNm, p.Neighbours, p.Draft, cts.Token, async (n, total, counts) =>
                {
                    if (n % 10 == 0) Add("routing", $"{n}/~{total} routes, failed so far: {(counts.Count == 0 ? "none" : string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")))}");
                    await Task.Delay(150, cts.Token);
                });
                Report = report; ReportParameters = p; ReportFinishedAt = DateTimeOffset.UtcNow;
                Add("done", $"{report.Routes} routes, {report.Failed} failed, {report.Unroutable} unroutable, {report.Findings.Count} findings ({report.Seconds:0} s)");
            }
            catch (OperationCanceledException) { Add("cancelled", "Audit cancelled."); }
            catch (Exception ex) { Add("failed", $"{ex.GetType().Name}: {ex.Message}"); logger.LogError(ex, "Routing audit failed"); }
            finally { cts.Dispose(); _cts = null; Interlocked.Exchange(ref _running, 0); }
        });
        return true;
    }

    public bool Cancel() { try { _cts?.Cancel(); return IsRunning; } catch (ObjectDisposedException) { return false; } }

    private void Add(string phase, string message)
    {
        lock (_progress) { if (_progress.Count > 200) _progress.RemoveAt(0); _progress.Add(new PbfProgress(phase, message, DateTimeOffset.UtcNow)); }
        logger.LogInformation("Routing audit [{Phase}] {Message}", phase, message);
    }
}
