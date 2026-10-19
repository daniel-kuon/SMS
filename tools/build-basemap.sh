#!/usr/bin/env bash
# Builds the self-hosted vector base map (OpenMapTiles schema) with Planetiler into media/tiles/*.pmtiles.
# Martin (AppHost resource "basemap") serves every .pmtiles in media/tiles; the API switches the map style to the
# self-hosted files automatically when basemap.pmtiles (and optionally basemap-detail.pmtiles) exist.
#
#   tools/build-basemap.sh schleswig-holstein            # full detail (z0–14) for one Geofabrik area, ≈ 3 min → basemap.pmtiles
#   tools/build-basemap.sh europe                        # full detail for Europe: hours, ≈ 25 GB, ≈ 60 GB temp, 32+ GB RAM → basemap-europe.pmtiles
#   tools/build-basemap.sh lean wide                     # z0–12, only water/waterway/water_name/place/transportation(_name)/boundary, then the
#                                                        #   class filter below, plus a z0–5 world water tier → basemap-lean-wide.pmtiles
#   tools/build-basemap.sh filter media/tiles/x.pmtiles  # only the class filter, on an already built lean file → basemap-lean-filtered.pmtiles
#   tools/build-basemap.sh detail europe harbours.poly   # z12–14 full detail only inside the polygon (3 km around every harbour,
#                                                        #   GET /api/admin/map/harbour-areas.poly) → basemap-detail.pmtiles
#
# Areas "region" (routing region, 4.5–19.5 E, 52–60 N) and "wide" (whole Baltic incl. Finland, Gulf of Bothnia, Baltic states, south Norway,
# Benelux/German North Sea coast: 3–31 E, 50.5–66 N) are cut out of the Europe PBF (docs/15), in the Ubuntu WSL distro:
#   osmium extract -b 3,50.5,31,66 --strategy=simple -o media/cache/osm/wide.osm.pbf media/cache/osm/europe-latest.osm.pbf
# Lean builds also get a z0–5 world tier (water + country borders from Natural Earth) merged in, so the sea outside the cut is not
# drawn in Liberty's land colour when zoomed out.
# Env: OUT_NAME=<file> overrides the output name (e.g. basemap-detail.pmtiles.new, which Martin does not serve),
#      JAVA_XMX (heap), DOCKER_MEM (container cap, default 40g), THREADS (Planetiler threads, default 12),
#      LEAN_FILTER (tile-join -j feature filter, default below).
#
# Lean class filter: Planetiler's OpenMapTiles profile can drop layers but not classes, so the lean build is post-filtered with
# tippecanoe's tile-join (felt/tippecanoe, built once into the local image sms-tippecanoe, ~1 min). tile-join re-encodes each tile
# without touching geometry; "$zoom" is the tile's zoom. The style (src/Sms.Api/Endpoints/MapStyle.cs, LeanClasses) assumes exactly
# these classes at z12, keep both in sync.
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
MODE="full"; case "${1:-}" in lean|detail|filter) MODE="$1"; shift;; esac
AREA="${1:-schleswig-holstein}"; POLY="${2:-}"
mkdir -p media/tiles media/cache/osm media/cache/planetiler-tmp
HOST="$(pwd -W 2>/dev/null || pwd)"
case "$AREA" in region) BBOX=4.5,52,19.5,60;; wide) BBOX=3,50.5,31,66;; *) BBOX="";; esac
EXTRA=()
case "$MODE" in
  full)   if [ "$AREA" = "schleswig-holstein" ]; then OUT="basemap.pmtiles"; else OUT="basemap-$AREA.pmtiles"; fi;;
  lean)   OUT="basemap-lean-$AREA.pmtiles"; EXTRA+=(--maxzoom=12 --only_layers=water,waterway,water_name,place,transportation,transportation_name,boundary);;
  filter) OUT="basemap-lean-filtered.pmtiles"; [ -f "$AREA" ] || { echo "filter mode needs a .pmtiles file"; exit 1; };;
  detail) OUT="basemap-detail.pmtiles"
          [ -f "$POLY" ] || { echo "detail mode needs a .poly file (GET /api/admin/map/harbour-areas.poly > media/cache/harbours.poly)"; exit 1; }
          # landcover (19 % of the bytes; wood/grass discs would end at the polygon edge), housenumber and mountain_peak (Liberty draws neither) go;
          # MapStyle.cs DetailExcluded drops their style layers (keep in sync)
          cp "$POLY" media/cache/planetiler-tmp/area.poly; EXTRA+=(--polygon=/poly/area.poly --minzoom=12 --exclude_layers=landcover,housenumber,mountain_peak)
          # the endpoint lists every harbour; for a cut keep only rings touching it, so the bounds taken from the polygon stay the cut's
          if [ -n "$BBOX" ]; then
            IFS=, read -r W S E N <<< "$BBOX"
            awk -v W="$W" -v S="$S" -v E="$E" -v N="$N" 'NR==1{print;next} !ring && /^END/{print;next} !ring{ring=$0; buf=""; keep=0; next}
              /^END/{if(keep) printf "%s\n%sEND\n", ring, buf; ring=""; next} {buf=buf $0 "\n"; if($1>=W&&$1<=E&&$2>=S&&$2<=N) keep=1}' "$POLY" > media/cache/planetiler-tmp/area.poly
          fi;;
esac
OUT="${OUT_NAME:-$OUT}"
TIPPECANOE=sms-tippecanoe:2.79.0
LEAN_FILTER="${LEAN_FILTER:-$(cat <<'JSON'
{"transportation":["all",["!=","ramp",1],["any",["in","class","motorway","trunk","primary","ferry"],["all",["==","class","rail"],["!has","service"]],["all",["==","class","secondary"],[">=","$zoom",10]]]],
 "transportation_name":["any",["in","class","motorway","trunk","primary"],["all",["==","class","secondary"],[">=","$zoom",10]]],
 "place":["any",["in","class","continent","country","state","province","city","town","island"],["all",["in","class","village","suburb"],[">=","$zoom",11]]],
 "boundary":["<=","admin_level",4]}
JSON
)}"
# attributes kept (-y; everything else goes): what the Liberty style reads plus the German/English names. The ~80 name:xx translations
# per label are ~half of the place/water_name bytes and nothing renders them.
LEAN_KEEP="$(printf -- '-y %s ' class subclass brunnel ramp oneway intermittent admin_level maritime disputed claimed_by capital rank network ref ref_length   name name_en name_de name_int name:latin name:nonlatin)"
# z0–5 of the whole world, water and boundary only: at these zooms the OpenMapTiles profile takes both from Natural Earth, so any small
# OSM file will do as input. Built once (~2 min) and kept in the volume.
world_tier() {
  MSYS_NO_PATHCONV=1 docker run --rm -v sms-planetiler-tmp:/tmp-data alpine test -f /tmp-data/world-z5.pmtiles && return
  local osm=(--area=monaco --download); [ -f media/cache/osm/schleswig-holstein-latest.osm.pbf ] && osm=(--osm_path=/osm/schleswig-holstein-latest.osm.pbf)
  MSYS_NO_PATHCONV=1 docker run --rm --memory="${DOCKER_MEM:-40g}" --cpus="${THREADS:-12}" -e JAVA_TOOL_OPTIONS="-Xmx4g" \
    -v "$HOST/media/tiles:/data" -v "$HOST/media/cache/osm:/osm" -v sms-planetiler-tmp:/tmp-data ghcr.io/onthegomap/planetiler:latest \
    "${osm[@]}" --download --output=/tmp-data/world-z5.pmtiles --force --tmpdir=/tmp-data/tmp-world --threads="${THREADS:-12}" \
    --bounds=world --maxzoom=5 --only_layers=water,boundary
  MSYS_NO_PATHCONV=1 docker run --rm -v sms-planetiler-tmp:/tmp-data alpine rm -rf /tmp-data/tmp-world
}
# runs the class filter on /tmp-data/build.pmtiles in the volume (on a Windows bind mount tile-join's SQLite temp is ~10x slower)
lean_filter() {
  world_tier
  docker image inspect "$TIPPECANOE" >/dev/null 2>&1 || docker build -t "$TIPPECANOE" - <<'DOCKERFILE'
FROM debian:bookworm-slim AS build
RUN apt-get update && apt-get install -y --no-install-recommends build-essential libsqlite3-dev zlib1g-dev git ca-certificates
RUN git clone --depth 1 --branch 2.79.0 https://github.com/felt/tippecanoe.git /src && make -C /src -j8 && make -C /src install
FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends libsqlite3-0 zlib1g && rm -rf /var/lib/apt/lists/*
COPY --from=build /usr/local/bin/tile-join /usr/local/bin/tippecanoe /usr/local/bin/
DOCKERFILE
  # tile-join writes world bounds into the header, which is right with the world tier merged in (z6+ outside the cut: Martin answers 204)
  MSYS_NO_PATHCONV=1 docker run --rm --memory="${DOCKER_MEM:-40g}" --cpus="${THREADS:-12}" -v sms-planetiler-tmp:/tmp-data "$TIPPECANOE" \
    sh -c "tile-join -q -f -pk $LEAN_KEEP -j \"\$1\" -o /tmp-data/filtered.pmtiles /tmp-data/build.pmtiles /tmp-data/world-z5.pmtiles && mv /tmp-data/filtered.pmtiles /tmp-data/build.pmtiles" _ "$LEAN_FILTER"
}
if [ "$MODE" = filter ]; then
  MSYS_NO_PATHCONV=1 docker run --rm -v sms-planetiler-tmp:/tmp-data -v "$HOST/$(dirname "$AREA"):/in:ro" alpine cp "/in/$(basename "$AREA")" /tmp-data/build.pmtiles
  lean_filter
  MSYS_NO_PATHCONV=1 docker run --rm -v sms-planetiler-tmp:/tmp-data -v "$HOST/media/tiles:/data" alpine sh -c "mv /tmp-data/build.pmtiles /data/$OUT"
  ls -la "media/tiles/$OUT"; exit 0
fi
PBFNAME="${AREA}-latest.osm.pbf"; [ -n "$BBOX" ] && PBFNAME="$AREA.osm.pbf"
if [ -f "media/cache/osm/$PBFNAME" ]; then SRC=(--osm_path="/osm/$PBFNAME" --download); else SRC=(--download); fi
# RAM: Europe wants ≥ 32 GB heap plus page cache for the memory-mapped temp files; small areas are fine with 4 GB
XMX="${JAVA_XMX:-4g}"; STORAGE=()
# the osmium cut has no bbox in its header, and without bounds Planetiler would fill the whole world with ocean tiles.
# Not with --polygon: Planetiler 0.10.3 silently ignores the polygon when --bounds is given too (Bounds.setShape keeps the tile
# extents computed from --bounds), which made the first two detail builds full-region z12–14 (2.76 GB); the polygon sets the bounds.
if [ -n "$BBOX" ]; then XMX="${JAVA_XMX:-16g}"; [ "$MODE" = detail ] || EXTRA+=(--bounds="$BBOX"); fi
# Europe: the in-memory node map (array/ram) needs > 100 GB; the memory-mapped sparse map runs in ~ 30 GB heap, slower but safe
if [ "$AREA" = "europe" ]; then XMX="${JAVA_XMX:-32g}"; STORAGE=(--storage=mmap --nodemap_type=sparsearray --nodemap_storage=mmap); fi
# Planetiler (format is taken from the file extension, hence build.pmtiles) writes into the volume; the file reaches media/tiles only when complete (a stub left by a killed run makes Martin refuse to start)
MSYS_NO_PATHCONV=1 docker run --rm --memory="${DOCKER_MEM:-40g}" --cpus="${THREADS:-12}" -e JAVA_TOOL_OPTIONS="-Xmx$XMX" \
  -v "$HOST/media/tiles:/data" -v "$HOST/media/cache/osm:/osm" -v "$HOST/media/cache/planetiler-tmp:/poly" -v sms-planetiler-tmp:/tmp-data ghcr.io/onthegomap/planetiler:latest \
  "${SRC[@]}" --area="$AREA" --output="/tmp-data/build.pmtiles" --force --tmpdir=/tmp-data/tmp --threads="${THREADS:-12}" "${STORAGE[@]}" "${EXTRA[@]}"
if [ "$MODE" = lean ]; then lean_filter; fi
MSYS_NO_PATHCONV=1 docker run --rm -v sms-planetiler-tmp:/tmp-data -v "$HOST/media/tiles:/data" alpine sh -c "mv /tmp-data/build.pmtiles /data/$OUT"
rm -f media/cache/planetiler-tmp/area.poly
docker run --rm -v sms-planetiler-tmp:/tmp-data alpine sh -c "rm -rf /tmp-data/tmp" >/dev/null 2>&1 || true
ls -la "media/tiles/$OUT"
case "$OUT" in basemap.pmtiles|basemap-detail.pmtiles) echo "done – restart the AppHost (or Martin) if it was running before the file existed";;
  *) T=basemap.pmtiles; [ "$MODE" = detail ] && T=basemap-detail.pmtiles
     echo "done – activate with: cp media/tiles/$OUT media/tiles/$T && docker restart \$(docker ps -q -f name=basemap)";; esac
