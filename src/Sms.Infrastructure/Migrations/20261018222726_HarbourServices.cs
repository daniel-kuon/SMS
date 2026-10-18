using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HarbourServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Services",
                table: "Harbours",
                type: "jsonb",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Services",
                table: "Harbours");
        }
    }
}
