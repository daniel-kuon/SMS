using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeamarkLinesAndPois : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<LineString>(
                name: "Line",
                table: "Seamarks",
                type: "geometry(LineString,3857)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sectors",
                table: "Seamarks",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Pois",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OsmType = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    OsmId = table.Column<long>(type: "bigint", nullable: false),
                    Class = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TagsJson = table.Column<string>(type: "jsonb", maxLength: 4000, nullable: false),
                    Location = table.Column<Point>(type: "geometry(Point,3857)", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pois", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Seamarks_Line",
                table: "Seamarks",
                column: "Line")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_Pois_Location",
                table: "Pois",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_Pois_OsmType_OsmId",
                table: "Pois",
                columns: new[] { "OsmType", "OsmId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Pois");

            migrationBuilder.DropIndex(
                name: "IX_Seamarks_Line",
                table: "Seamarks");

            migrationBuilder.DropColumn(
                name: "Line",
                table: "Seamarks");

            migrationBuilder.DropColumn(
                name: "Sectors",
                table: "Seamarks");
        }
    }
}
