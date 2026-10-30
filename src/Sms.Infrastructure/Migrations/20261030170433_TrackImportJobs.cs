using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrackImportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DepthM",
                table: "WeatherObservations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WaterTempC",
                table: "WeatherObservations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Added",
                table: "TrackImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                table: "TrackImports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Duplicates",
                table: "TrackImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Error",
                table: "TrackImports",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FinishedAt",
                table: "TrackImports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgressPercent",
                table: "TrackImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Rejected",
                table: "TrackImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Routes",
                table: "TrackImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "TrackImports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Done"); // imports before this migration ran synchronously and are complete

            migrationBuilder.AddColumn<int>(
                name: "Tracks",
                table: "TrackImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Waypoints",
                table: "TrackImports",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepthM",
                table: "WeatherObservations");

            migrationBuilder.DropColumn(
                name: "WaterTempC",
                table: "WeatherObservations");

            migrationBuilder.DropColumn(
                name: "Added",
                table: "TrackImports");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "TrackImports");

            migrationBuilder.DropColumn(
                name: "Duplicates",
                table: "TrackImports");

            migrationBuilder.DropColumn(
                name: "Error",
                table: "TrackImports");

            migrationBuilder.DropColumn(
                name: "FinishedAt",
                table: "TrackImports");

            migrationBuilder.DropColumn(
                name: "ProgressPercent",
                table: "TrackImports");

            migrationBuilder.DropColumn(
                name: "Rejected",
                table: "TrackImports");

            migrationBuilder.DropColumn(
                name: "Routes",
                table: "TrackImports");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "TrackImports");

            migrationBuilder.DropColumn(
                name: "Tracks",
                table: "TrackImports");

            migrationBuilder.DropColumn(
                name: "Waypoints",
                table: "TrackImports");
        }
    }
}
