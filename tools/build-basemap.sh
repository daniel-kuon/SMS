#!/usr/bin/env bash
# Builds the self-hosted vector base map (OpenMapTiles schema) with Planetiler into media/tiles/*.pmtiles.
# Martin (AppHost resource "basemap") serves every .pmtiles in media/tiles; the API switches the map style to the
# self-hosted files automatically when basemap.pmtiles (and optionally basemap-detail.pmtiles) exist.
#
#   tools/build-basemap.sh schleswig-holstein            # full detail (z0–14) for one Geofabrik area, ≈ 3 min → basemap.pmtiles
#   tools/build-basemap.sh europe                        # full detail for Europe: hours, ≈ 25 GB, ≈ 60 GB temp, 32+ GB RAM → basemap-europe.pmtiles
#   tools/build-basemap.sh lean region                   # z0–12, only water/waterway/water_name/place/transportation(_name)/boundary → basemap-lean-region.pmtiles
#   tools/build-basemap.sh detail europe harbours.poly   # z12–14 full detail only inside the polygon (3 km around every harbour,
#                                                        #   GET /api/admin/map/harbour-areas.poly) → basemap-detail.pmtiles
#
# Area "region" is the routing region cut out of the Europe PBF (4.5–19.5 E, 52–60 N, docs/15):
#   osmium extract -b 4.5,52,19.5,60 --strategy=simple -o media/cache/osm/region.osm.pbf media/cache/osm/europe-latest.osm.pbf
# Env: OUT_NAME=<file> overrides the output name (e.g. basemap-detail.pmtiles.new, which Martin does not serve),
#      JAVA_XMX (heap), DOCKER_MEM (container cap, default 40g), THREADS (Planetiler threads, default 12).
#
# Only the single-area full build writes basemap.pmtiles itself. Every other build writes its own file, because Martin
# serves media/tiles live and a --force build would pull the file from under the running server; activate a result with
#   cp media/tiles/basemap-lean-europe.pmtiles media/tiles/basemap.pmtiles && docker restart $(docker ps -q -f name=basemap)
# Temp files go to the Docker volume sms-planetiler-tmp: a Windows bind mount is far too slow for them, and they must never
# land in the served directory. A Geofabrik .osm.pbf already in
# media/cache/osm is reused. The lean + detail pair is what the spec asks for: coast, water, places and main roads
# everywhere, streets/shops/piers only near harbours. Requires Docker. Data: © OpenStreetMap contributors (ODbL), Natural Earth.
set -euo pipefail
cd "$(dirname "$0")/.."
MODE="full"; case "${1:-}" in lean|detail) MODE="$1"; shift;; esac
AREA="${1:-schleswig-holstein}"; POLY="${2:-}"
mkdir -p media/tiles media/cache/osm media/cache/planetiler-tmp
HOST="$(pwd -W 2>/dev/null || pwd)"
EXTRA=()
case "$MODE" in
  full)   if [ "$AREA" = "schleswig-holstein" ]; then OUT="basemap.pmtiles"; else OUT="basemap-$AREA.pmtiles"; fi;;
  lean)   OUT="basemap-lean-$AREA.pmtiles"; EXTRA+=(--maxzoom=12 --only_layers=water,waterway,water_name,place,transportation,transportation_name,boundary);;
  detail) OUT="basemap-detail.pmtiles"
          [ -f "$POLY" ] || { echo "detail mode needs a .poly file (GET /api/admin/map/harbour-areas.poly > media/cache/harbours.poly)"; exit 1; }
          cp "$POLY" media/cache/planetiler-tmp/area.poly; EXTRA+=(--polygon=/poly/area.poly --minzoom=12);;
esac
OUT="${OUT_NAME:-$OUT}"
PBFNAME="${AREA}-latest.osm.pbf"; [ "$AREA" = "region" ] && PBFNAME="region.osm.pbf"
if [ -f "media/cache/osm/$PBFNAME" ]; then SRC=(--osm_path="/osm/$PBFNAME" --download); else SRC=(--download); fi
# RAM: Europe wants ≥ 32 GB heap plus page cache for the memory-mapped temp files; small areas are fine with 4 GB
XMX="${JAVA_XMX:-4g}"; STORAGE=()
# the osmium cut has no bbox in its header, and without bounds Planetiler would fill the whole world with ocean tiles
if [ "$AREA" = "region" ]; then XMX="${JAVA_XMX:-12g}"; EXTRA+=(--bounds=4.5,52,19.5,60); fi
# Europe: the in-memory node map (array/ram) needs > 100 GB; the memory-mapped sparse map runs in ~ 30 GB heap, slower but safe
if [ "$AREA" = "europe" ]; then XMX="${JAVA_XMX:-32g}"; STORAGE=(--storage=mmap --nodemap_type=sparsearray --nodemap_storage=mmap); fi
# Planetiler (format is taken from the file extension, hence build.pmtiles) writes into the volume; the file reaches media/tiles only when complete (a stub left by a killed run makes Martin refuse to start)
MSYS_NO_PATHCONV=1 docker run --rm --memory="${DOCKER_MEM:-40g}" --cpus="${THREADS:-12}" -e JAVA_TOOL_OPTIONS="-Xmx$XMX" \
  -v "$HOST/media/tiles:/data" -v "$HOST/media/cache/osm:/osm" -v "$HOST/media/cache/planetiler-tmp:/poly" -v sms-planetiler-tmp:/tmp-data ghcr.io/onthegomap/planetiler:latest \
  "${SRC[@]}" --area="$AREA" --output="/tmp-data/build.pmtiles" --force --tmpdir=/tmp-data/tmp --threads="${THREADS:-12}" "${STORAGE[@]}" "${EXTRA[@]}"
MSYS_NO_PATHCONV=1 docker run --rm -v sms-planetiler-tmp:/tmp-data -v "$HOST/media/tiles:/data" alpine sh -c "mv /tmp-data/build.pmtiles /data/$OUT"
rm -f media/cache/planetiler-tmp/area.poly
docker run --rm -v sms-planetiler-tmp:/tmp-data alpine sh -c "rm -rf /tmp-data/tmp" >/dev/null 2>&1 || true
ls -la "media/tiles/$OUT"
case "$OUT" in basemap.pmtiles|basemap-detail.pmtiles) echo "done – restart the AppHost (or Martin) if it was running before the file existed";;
  *) echo "done – activate with: cp media/tiles/$OUT media/tiles/basemap.pmtiles && docker restart \$(docker ps -q -f name=basemap)";; esac
