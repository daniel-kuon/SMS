using Microsoft.EntityFrameworkCore;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

/// <summary>A clicked map point: the charted depth band there (routing_depth, the shallowest overlapping area; null = no depth data) and the
/// bridges within 300 m (Bridge/OpeningBridge corrections, nearest first; clearanceM null = unknown).</summary>
public static class PointInfoEndpoints
{
    public const double BridgeRadiusM = 300;
    public sealed record DepthRow(double MinDepthM, double MaxDepthM);
    public sealed record BridgeRow(string Kind, double? ClearanceM, double DistanceM, double Lat, double Lng);

    public static void MapPointInfo(this IEndpointRouteBuilder api) =>
        api.MapGet("/routing/point-info", async (double lat, double lng, SmsDbContext db, CancellationToken ct) =>
        {
            if (lat is < -90 or > 90 || lng is < -180 or > 180) return (IResult)Invalid("lat", "lat/lng außerhalb des gültigen Bereichs.");
            var depth = (await db.Database.SqlQueryRaw<DepthRow>("""
                SELECT min_depth_m AS "MinDepthM", max_depth_m AS "MaxDepthM" FROM routing_depth
                WHERE ST_Intersects(geom, ST_SetSRID(ST_MakePoint({0}, {1}), 4326)) ORDER BY min_depth_m LIMIT 1
                """, lng, lat).ToListAsync(ct)).FirstOrDefault();
            // the && box (0.02° ≥ 300 m up to 70° N) lets the gist index pick the candidates before the exact geography distance
            var bridges = await db.Database.SqlQueryRaw<BridgeRow>("""
                SELECT c."Kind" AS "Kind", c."ClearanceM" AS "ClearanceM", ST_Distance(c."Geometry"::geography, q.p::geography) AS "DistanceM",
                       ST_Y(ST_ClosestPoint(c."Geometry", q.p)) AS "Lat", ST_X(ST_ClosestPoint(c."Geometry", q.p)) AS "Lng"
                FROM "RoutingCorrections" c, (SELECT ST_SetSRID(ST_MakePoint({0}, {1}), 4326) AS p) q
                WHERE c."Active" AND c."Kind" IN ('Bridge', 'OpeningBridge') AND c."Geometry" && ST_Expand(q.p, 0.02)
                  AND ST_DWithin(c."Geometry"::geography, q.p::geography, {2})
                ORDER BY 3 LIMIT 5
                """, lng, lat, BridgeRadiusM).ToListAsync(ct);
            return Results.Ok(new
            {
                lat, lng,
                depth = depth is null ? null : new { minM = depth.MinDepthM, maxM = depth.MaxDepthM },
                bridges = bridges.Select(b => new { kind = b.Kind, clearanceM = b.ClearanceM, distanceM = Math.Round(b.DistanceM), b.Lat, b.Lng }),
            });
        }).WithTags("Routing");
}
