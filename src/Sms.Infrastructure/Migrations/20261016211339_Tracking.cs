using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Tracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PublicPositionDelayMinutes",
                table: "Trips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TrackVisibility",
                table: "Trips",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "PrivacyZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Center = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    RadiusM = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivacyZones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedById = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PointCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackingDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Protocol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackPoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShipId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImportId = table.Column<Guid>(type: "uuid", nullable: true),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    SogKn = table.Column<double>(type: "double precision", nullable: true),
                    CogDeg = table.Column<double>(type: "double precision", nullable: true),
                    AccuracyM = table.Column<double>(type: "double precision", nullable: true),
                    IsOutlier = table.Column<bool>(type: "boolean", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackPoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogbookEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceOverride = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DistanceNm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxSogKn = table.Column<double>(type: "double precision", nullable: true),
                    AvgSogKn = table.Column<double>(type: "double precision", nullable: true),
                    Geometry = table.Column<LineString>(type: "geography (linestring, 4326)", nullable: false),
                    GeometryCoarse = table.Column<LineString>(type: "geography (linestring, 4326)", nullable: false),
                    PointCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackSegments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivacyZones_ShipId",
                table: "PrivacyZones",
                column: "ShipId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingDevices_ShipId",
                table: "TrackingDevices",
                column: "ShipId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackingDevices_TokenHash",
                table: "TrackingDevices",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackPoints_DeviceId_Time",
                table: "TrackPoints",
                columns: new[] { "DeviceId", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackPoints_ShipId_Time",
                table: "TrackPoints",
                columns: new[] { "ShipId", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackPoints_TripId_Time",
                table: "TrackPoints",
                columns: new[] { "TripId", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackSegments_TripId_Day",
                table: "TrackSegments",
                columns: new[] { "TripId", "Day" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrivacyZones");

            migrationBuilder.DropTable(
                name: "TrackImports");

            migrationBuilder.DropTable(
                name: "TrackingDevices");

            migrationBuilder.DropTable(
                name: "TrackPoints");

            migrationBuilder.DropTable(
                name: "TrackSegments");

            migrationBuilder.DropColumn(
                name: "PublicPositionDelayMinutes",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "TrackVisibility",
                table: "Trips");
        }
    }
}
