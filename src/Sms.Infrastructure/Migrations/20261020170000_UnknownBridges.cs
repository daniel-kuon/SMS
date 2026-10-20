using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(SmsDbContext))]
    [Migration("20261020170000_UnknownBridges")]
    public partial class UnknownBridges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnknownBridges",
                table: "Ships",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pass");

            // edges under a fixed bridge without a charted clearance (Obstacles.Apply): passed with a warning or avoided, per ship
            migrationBuilder.Sql("ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS unknown_bridge boolean NOT NULL DEFAULT false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE routing_edge DROP COLUMN IF EXISTS unknown_bridge;");

            migrationBuilder.DropColumn(
                name: "UnknownBridges",
                table: "Ships");
        }
    }
}
