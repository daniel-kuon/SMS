using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Sms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RoutingGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:pgrouting", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "RouteCaches",
                columns: table => new
                {
                    FromHarbourId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToHarbourId = table.Column<Guid>(type: "uuid", nullable: false),
                    GraphVersion = table.Column<int>(type: "integer", nullable: false),
                    DistanceNm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Geometry = table.Column<LineString>(type: "geometry(LineString,4326)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteCaches", x => new { x.FromHarbourId, x.ToHarbourId, x.GraphVersion });
                });

            migrationBuilder.CreateTable(
                name: "RoutingCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Geometry = table.Column<Geometry>(type: "geometry(Geometry,4326)", nullable: false),
                    Note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoutingCorrections", x => x.Id);
                });

            // pgRouting graph tables (spec 10 §10.2); filled by RoutingGraphBuilder, queried by SeaRouter.
            migrationBuilder.Sql("""
                CREATE TABLE routing_land (id bigserial PRIMARY KEY, graph_version int NOT NULL, geom geometry(Geometry,4326) NOT NULL);
                CREATE INDEX ix_routing_land_geom ON routing_land USING gist (geom);
                CREATE TABLE routing_node (graph_version int NOT NULL, id bigint NOT NULL, geom geometry(Point,4326) NOT NULL, PRIMARY KEY (graph_version, id));
                CREATE INDEX ix_routing_node_geom ON routing_node USING gist (geom);
                CREATE TABLE routing_edge (id bigserial PRIMARY KEY, graph_version int NOT NULL, source bigint NOT NULL, target bigint NOT NULL,
                    cost float8 NOT NULL, reverse_cost float8 NOT NULL, x1 float8 NOT NULL, y1 float8 NOT NULL, x2 float8 NOT NULL, y2 float8 NOT NULL, geom geometry(LineString,4326) NOT NULL);
                CREATE INDEX ix_routing_edge_geom ON routing_edge USING gist (geom);
                CREATE INDEX ix_routing_edge_version ON routing_edge (graph_version);
                CREATE TABLE routing_harbour_access (graph_version int NOT NULL, harbour_id uuid NOT NULL, node_id bigint NOT NULL, distance_m float8 NOT NULL, PRIMARY KEY (graph_version, harbour_id));
                CREATE INDEX ix_routing_harbour_access_node ON routing_harbour_access (graph_version, node_id);
                CREATE TABLE routing_meta (key text PRIMARY KEY, value text NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS routing_meta, routing_harbour_access, routing_edge, routing_node, routing_land;");
            migrationBuilder.DropTable(
                name: "RouteCaches");

            migrationBuilder.DropTable(
                name: "RoutingCorrections");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pgrouting", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:postgis", ",,");
        }
    }
}
