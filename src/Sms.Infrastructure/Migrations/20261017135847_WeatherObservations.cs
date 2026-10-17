using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WeatherObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WeatherObservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShipId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    Time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TemperatureC = table.Column<double>(type: "double precision", nullable: true),
                    WindKn = table.Column<double>(type: "double precision", nullable: true),
                    WindDirDeg = table.Column<double>(type: "double precision", nullable: true),
                    GustKn = table.Column<double>(type: "double precision", nullable: true),
                    PressureHpa = table.Column<double>(type: "double precision", nullable: true),
                    PrecipitationMm = table.Column<double>(type: "double precision", nullable: true),
                    WeatherCode = table.Column<int>(type: "integer", nullable: true),
                    CloudCover = table.Column<int>(type: "integer", nullable: true),
                    WaveHeightM = table.Column<double>(type: "double precision", nullable: true),
                    WaveDirDeg = table.Column<double>(type: "double precision", nullable: true),
                    WavePeriodS = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeatherObservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherObservations_ShipId_Time",
                table: "WeatherObservations",
                columns: new[] { "ShipId", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_WeatherObservations_TripId_Time",
                table: "WeatherObservations",
                columns: new[] { "TripId", "Time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WeatherObservations");
        }
    }
}
