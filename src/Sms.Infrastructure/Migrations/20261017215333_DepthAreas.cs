using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DepthAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS min_depth_m float8 NULL;");
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS routing_depth (id bigserial PRIMARY KEY, geom geometry(Polygon,4326) NOT NULL, min_depth_m float8 NOT NULL, max_depth_m float8 NOT NULL, source text NOT NULL DEFAULT '');
                CREATE INDEX IF NOT EXISTS ix_routing_depth_geom ON routing_depth USING gist (geom);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE routing_edge DROP COLUMN IF EXISTS min_depth_m; DROP TABLE IF EXISTS routing_depth;");
        }
    }
}
