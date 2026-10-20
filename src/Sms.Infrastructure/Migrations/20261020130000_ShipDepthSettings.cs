using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShipDepthSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MinDepthM",
                table: "Ships",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WarnDepthM",
                table: "Ships",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            // coastal water without depth data: floor of the band next to it, the router treats the edge as that band (DepthLimits)
            migrationBuilder.Sql("ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS adjacent_depth_m float8 NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE routing_edge DROP COLUMN IF EXISTS adjacent_depth_m;");

            migrationBuilder.DropColumn(
                name: "MinDepthM",
                table: "Ships");

            migrationBuilder.DropColumn(
                name: "WarnDepthM",
                table: "Ships");
        }
    }
}
