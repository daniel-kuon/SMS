using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RoutingIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssueDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CorrectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Geometry = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: false),
                    Title = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    BasisKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BasisRefs = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OutdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OutdatedReason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueDecisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoutingIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    GraphVersion = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<double>(type: "double precision", nullable: false),
                    Title = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Detail = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RefId = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Geometry = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingIssues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueDecisions_Key",
                table: "IssueDecisions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoutingIssues_Geometry",
                table: "RoutingIssues",
                column: "Geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingIssues_Key",
                table: "RoutingIssues",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_RoutingIssues_Type_Priority",
                table: "RoutingIssues",
                columns: new[] { "Type", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueDecisions");

            migrationBuilder.DropTable(
                name: "RoutingIssues");
        }
    }
}
