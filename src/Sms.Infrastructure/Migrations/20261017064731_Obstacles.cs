using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Obstacles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS clearance_m float8 NULL;");
            migrationBuilder.AddColumn<double>(
                name: "ClearanceM",
                table: "RoutingCorrections",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE routing_edge DROP COLUMN IF EXISTS clearance_m;");
            migrationBuilder.DropColumn(
                name: "ClearanceM",
                table: "RoutingCorrections");
        }
    }
}
