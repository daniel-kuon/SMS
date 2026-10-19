using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(SmsDbContext))]
    [Migration("20261019210000_RoutingCorrectionsGeometryIndex")]
    public partial class RoutingCorrectionsGeometryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // the depth tiles (Sms.Tiles) look up the Block corrections by bounding box (~=) and the Water corrections per tile (&&)
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_RoutingCorrections_Geometry\" ON \"RoutingCorrections\" USING gist (\"Geometry\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_RoutingCorrections_Geometry\";");
        }
    }
}
