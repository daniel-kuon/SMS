using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Seamarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Seamarks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OsmType = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    OsmId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Category = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Name = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Colour = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Shape = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Light = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TagsJson = table.Column<string>(type: "jsonb", maxLength: 4000, nullable: false),
                    Location = table.Column<Point>(type: "geometry(Point,3857)", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seamarks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Seamarks_Location",
                table: "Seamarks",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_Seamarks_OsmType_OsmId",
                table: "Seamarks",
                columns: new[] { "OsmType", "OsmId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Seamarks_Type",
                table: "Seamarks",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Seamarks");
        }
    }
}
