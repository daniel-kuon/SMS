using Microsoft.EntityFrameworkCore;
using Sms.Infrastructure;
using static Sms.Api.ApiHelpers;

namespace Sms.Api.Endpoints;

/// <summary>A clicked map point: the charted depth band there (routing_depth, the shallowest overlapping area; null = no depth data) and the
/// bridges within 300 m (Bridge/OpeningBridge corrections, nearest first; clearanceM null = unknown) and the EMODnet DTM 2024 cell there
/// (routing_dtm; null = land, no data or not imported; interpolated = the DTM has no soundings in the cell).</summary>
public static class PointInfoEndpoints
{
    public const double BridgeRadiusM = 300;
    public sealed record DepthRow(double MinDepthM, double MaxDepthM);
    public sealed record DtmCell(byte[] E, int I);
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
            var dtm = await DtmAsync(db, lat, lng, ct);
            return Results.Ok(new
            {
                lat, lng,
                depth = depth is null ? null : new { minM = depth.MinDepthM, maxM = depth.MaxDepthM },
                dtm = dtm is { } d ? new { depthM = Math.Round(d.depthM, 1), d.interpolated } : null,
                bridges = bridges.Select(b => new { kind = b.Kind, clearanceM = b.ClearanceM, distanceM = Math.Round(b.DistanceM), b.Lat, b.Lng }),
            });
        }).WithTags("Routing");

    /// <summary>The DTM cell under the point: the chunk's water mask (one bit per cell, row 0 = north) gives the cell's place among the stored
    /// water cells, then only its float32 elevation (little-endian) and interpolation flag are read.</summary>
    public static async Task<(double depthM, bool interpolated)?> DtmAsync(SmsDbContext db, double lat, double lng, CancellationToken ct)
    {
        const int n = Sms.Infrastructure.Routing.DtmGrid.Cells;
        long gx = (long)Math.Floor(lng * n), gy = (long)Math.Floor(lat * n);
        double kx = Math.Floor((double)gx / n), ky = Math.Floor((double)gy / n);
        var water = (await db.Database.SqlQueryRaw<byte[]>("""SELECT water AS "Value" FROM routing_dtm WHERE min_lng = {0} AND min_lat = {1} AND cells = {2}""", kx, ky, n).ToListAsync(ct)).FirstOrDefault();
        var i = (int)((n - 1 - (gy - (long)ky * n)) * n + gx - (long)kx * n);
        if (water is null || (water[i >> 3] & (1 << (i & 7))) == 0) return null;
        var k = System.Numerics.BitOperations.PopCount((uint)(water[i >> 3] & ((1 << (i & 7)) - 1)));
        for (var b = 0; b < i >> 3; b++) k += System.Numerics.BitOperations.PopCount(water[b]);
        var cell = (await db.Database.SqlQueryRaw<DtmCell>("""
            SELECT substring(elevation FROM {2} FOR 4) AS "E", get_byte(interpolated, {3}) AS "I" FROM routing_dtm WHERE min_lng = {0} AND min_lat = {1}
            """, kx, ky, k * 4 + 1, k).ToListAsync(ct)).FirstOrDefault();
        if (cell is null || cell.E.Length != 4) return null;
        var e = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(cell.E);
        return e < 0 ? (-e, cell.I != 0) : null;
    }
}
