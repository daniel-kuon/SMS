using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(SmsDbContext))]
    [Migration("20261020120000_WalkNetwork")]
    public partial class WalkNetwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // walking network around the harbours (tools/build-walknet.sh): OSM ways split at shared nodes, source/target = OSM node ids
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS walk_edge (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, source bigint NOT NULL, target bigint NOT NULL, length_m real NOT NULL, geom geometry(LineString, 4326) NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_walk_edge_geom ON walk_edge USING gist (geom);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS walk_edge;");
        }
    }
}
