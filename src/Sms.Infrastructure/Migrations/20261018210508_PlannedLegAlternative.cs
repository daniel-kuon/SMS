using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlannedLegAlternative : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlannedLegs_TripId_Order",
                table: "PlannedLegs");

            migrationBuilder.AddColumn<int>(
                name: "Alternative",
                table: "PlannedLegs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PlannedLegs_TripId_Order_Alternative",
                table: "PlannedLegs",
                columns: new[] { "TripId", "Order", "Alternative" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlannedLegs_TripId_Order_Alternative",
                table: "PlannedLegs");

            migrationBuilder.DropColumn(
                name: "Alternative",
                table: "PlannedLegs");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedLegs_TripId_Order",
                table: "PlannedLegs",
                columns: new[] { "TripId", "Order" });
        }
    }
}
