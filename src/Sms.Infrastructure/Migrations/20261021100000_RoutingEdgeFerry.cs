using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(SmsDbContext))]
    [Migration("20261021100000_RoutingEdgeFerry")]
    public partial class RoutingEdgeFerry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // edges along a scheduled ferry track (FerryCorridors): known-navigable water, never uncertain in the strict depth search
            migrationBuilder.Sql("ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS ferry boolean NOT NULL DEFAULT false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE routing_edge DROP COLUMN IF EXISTS ferry;");
        }
    }
}
