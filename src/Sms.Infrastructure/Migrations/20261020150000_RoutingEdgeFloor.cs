using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(SmsDbContext))]
    [Migration("20261020150000_RoutingEdgeFloor")]
    public partial class RoutingEdgeFloor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // derived channels ("Enge") the depth limit leaves open: the band floor they run through ("definitely deep enough" test, SeaGraph.Floor)
            migrationBuilder.Sql("ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS floor_m float8 NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE routing_edge DROP COLUMN IF EXISTS floor_m;");
        }
    }
}
