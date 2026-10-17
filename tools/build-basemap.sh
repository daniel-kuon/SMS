#!/usr/bin/env bash
# Builds the self-hosted vector base map (OpenMapTiles schema) with Planetiler into media/tiles/basemap.pmtiles.
# Martin (AppHost resource "basemap") serves it at http://localhost:5161/basemap; the API switches the map style to
# it automatically when the file exists (GET /api/map/style.json).
#
#   tools/build-basemap.sh schleswig-holstein      # ≈ 3 min, downloads the Geofabrik extract + Natural Earth/water polygons once
#   tools/build-basemap.sh europe                  # hours, ≈ 100 GB of temp space, needs ~ 30 GB RAM (see Planetiler docs)
#
# Requires Docker. Data: © OpenStreetMap contributors (ODbL), Natural Earth.
set -euo pipefail
cd "$(dirname "$0")/.."
AREA="${1:-schleswig-holstein}"
mkdir -p media/tiles
MSYS_NO_PATHCONV=1 docker run --rm -e JAVA_TOOL_OPTIONS="${JAVA_TOOL_OPTIONS:--Xmx4g}" -v "$(pwd -W 2>/dev/null || pwd)/media/tiles:/data" ghcr.io/onthegomap/planetiler:latest \
  --download --area="$AREA" --output=/data/basemap.pmtiles --force --nodemap-type=sparsearray
ls -la media/tiles/basemap.pmtiles
echo "done – restart the AppHost (or Martin) if it was running before the file existed"
