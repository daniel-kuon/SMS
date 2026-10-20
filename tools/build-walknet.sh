#!/usr/bin/env bash
# Walking network around the harbours → PostGIS table walk_edge (migration WalkNetwork), used for the walking distances
# to the harbour services (Sms.Domain.WalkNetwork, /api/pois/near, /api/pois/walk, compute-services).
#
#   tools/build-walknet.sh [harbours.poly] [source.osm.pbf]
#
#   harbours.poly  GET /api/admin/map/harbour-areas.poly?radiusKm=5 (admin) > media/cache/walknet/harbours5.poly
#   source         default media/cache/osm/europe-latest.osm.pbf (harbours are Europe-wide; wide.osm.pbf covers only ~7.3 k of 13.9 k)
#
# Steps: osmium tags-filter (highway ways usable on foot) → osmium extract (5 km around the harbours, complete ways)
# → osmium add-locations-to-ways as OPL → tools/walknet.cs (split ways at shared nodes, drop foot=no/private/trunk without
# sidewalk, metres per edge) → COPY into walk_edge of the dev Postgres (docker container postgres-*, replaces the table).
# osmium runs in the Ubuntu WSL distro (OSMIUM_POOL_THREADS=8); intermediate files in media/cache/walknet.
set -euo pipefail
cd "$(dirname "$0")/.."
POLY=${1:-media/cache/walknet/harbours5.poly}
SRC=${2:-media/cache/osm/europe-latest.osm.pbf}
OUT=media/cache/walknet
TAG=$(basename "$SRC" .osm.pbf); HWF="$OUT/highways-$TAG.osm.pbf"; WALK="$OUT/walk-$TAG.osm.pbf"
mkdir -p "$OUT"
[ -f "$POLY" ] || { echo "missing $POLY (GET /api/admin/map/harbour-areas.poly?radiusKm=5)"; exit 1; }
osm() { MSYS_NO_PATHCONV=1 wsl -d Ubuntu --cd "$(pwd -W 2>/dev/null || pwd)" -- env OSMIUM_POOL_THREADS="${THREADS:-8}" osmium "$@"; }

HW=footway,path,pedestrian,steps,living_street,residential,service,unclassified,road,track,cycleway,bridleway,crossing,corridor,platform
HW=$HW,tertiary,tertiary_link,secondary,secondary_link,primary,primary_link,trunk,trunk_link
SECONDS=0
[ -s "$HWF" ] && [ "$HWF" -nt "$SRC" ] || osm tags-filter "$SRC" "w/highway=$HW" -o "$HWF" --overwrite
echo "tags-filter: ${SECONDS}s"
[ -s "$WALK" ] && [ "$WALK" -nt "$HWF" ] && [ "$WALK" -nt "$POLY" ] || osm extract -p "$POLY" "$HWF" -o "$WALK" --overwrite
echo "extract: ${SECONDS}s"

PG=$(docker ps -q -f name=postgres- | head -1)
psql() { docker exec -i "$PG" sh -c 'PGPASSWORD=$POSTGRES_PASSWORD psql -v ON_ERROR_STOP=1 -q -U postgres -d sms "$@"' -- "$@"; }
# same DDL as the migration WalkNetwork (the AppHost applies it on start; IF NOT EXISTS keeps both orders working)
psql -c 'CREATE TABLE IF NOT EXISTS walk_edge (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, source bigint NOT NULL, target bigint NOT NULL, length_m real NOT NULL, geom geometry(LineString, 4326) NOT NULL);
         CREATE INDEX IF NOT EXISTS ix_walk_edge_geom ON walk_edge USING gist (geom);'
dotnet build tools/walknet.cs -o "$OUT/bin" -v q -nologo >&2
# ~650 of 10 M ways reference nodes outside the source extract: walknet.cs drops them
osm add-locations-to-ways --ignore-missing-nodes "$WALK" -f opl -o - \
  | dotnet "$OUT/bin/walknet.dll" \
  | psql -1 -c 'DROP INDEX IF EXISTS ix_walk_edge_geom' -c 'TRUNCATE walk_edge RESTART IDENTITY' \
            -c 'COPY walk_edge (source, target, length_m, geom) FROM STDIN' -c 'CREATE INDEX ix_walk_edge_geom ON walk_edge USING gist (geom)'
psql -c 'ANALYZE walk_edge'
echo "load: ${SECONDS}s"
psql -c "SELECT count(*) AS edges, pg_size_pretty(pg_total_relation_size('walk_edge')) AS size FROM walk_edge"
