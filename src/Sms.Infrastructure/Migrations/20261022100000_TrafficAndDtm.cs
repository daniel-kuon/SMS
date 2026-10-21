using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(SmsDbContext))]
    [Migration("20261022100000_TrafficAndDtm")]
    public partial class TrafficAndDtm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- vessel density cells (EMODnet Human Activities, CC BY 4.0): one row per 1 km cell with sailing/pleasure traffic, polygon in WGS84
                CREATE TABLE IF NOT EXISTS routing_traffic (
                  id bigserial PRIMARY KEY, geom geometry(Polygon,4326) NOT NULL, sailing real NOT NULL, pleasure real NOT NULL, years text NOT NULL DEFAULT '');
                CREATE INDEX IF NOT EXISTS ix_routing_traffic_geom ON routing_traffic USING gist (geom);
                -- EMODnet DTM 2024 (ERDDAP bathymetry_dtm_2024, CC BY 4.0) per 1 x 1 degree chunk of cells x cells cells (row 0 = north): water = bit mask of
                -- the cells with elevation < 0; the other columns hold those water cells only, packed in row order, little-endian: elevation float32
                -- (m, negative = depth below LAT), interpolated uint8 (1 = interpolated, no soundings), value_count int32, elevation_min/elevation_max/stdev
                -- float32, cdi_index int32 (source reference)
                CREATE TABLE IF NOT EXISTS routing_dtm (
                  id serial PRIMARY KEY, min_lng double precision NOT NULL, min_lat double precision NOT NULL, cells integer NOT NULL, step double precision NOT NULL,
                  water bytea NOT NULL, elevation bytea NOT NULL, interpolated bytea NOT NULL, value_count bytea NOT NULL, elevation_min bytea NOT NULL,
                  elevation_max bytea NOT NULL, stdev bytea NOT NULL, cdi_index bytea NOT NULL, imported_at timestamptz NOT NULL DEFAULT now(), UNIQUE (min_lng, min_lat));
                -- edges in dense sailing traffic (known-navigable) and the shallowest counted DTM depth along edges with a band floor
                ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS traffic boolean NOT NULL DEFAULT false;
                ALTER TABLE routing_edge ADD COLUMN IF NOT EXISTS dtm_min_m real NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE routing_edge DROP COLUMN IF EXISTS dtm_min_m;
                ALTER TABLE routing_edge DROP COLUMN IF EXISTS traffic;
                DROP TABLE IF EXISTS routing_dtm;
                DROP TABLE IF EXISTS routing_traffic;
                """);
        }
    }
}
