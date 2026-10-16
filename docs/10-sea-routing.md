# 10. Automatic Sea Routing from OpenStreetMap / OpenSeaMap Data

**Goal:** estimate the sea distance (nautical miles) and a plausible route
between harbours, or from any point to the nearest harbours, so sailors see
"how far away the next ports are". **Not for navigation** — no depth, draft,
traffic separation schemes, restricted areas, currents or wind are considered
(at least in v1). Every result shows the disclaimer
"Entfernungen sind Schätzwerte und nicht zur Navigation geeignet." and the
attribution "© OpenStreetMap-Mitwirkende, OpenSeaMap".

This replaces the legacy hand-drawn waypoint graph (`05` §5.1, §5.3). The
legacy distance display (one decimal, " sm") and route highlighting (`05`
§5.2) stay as UI behaviour.

## 10.1 Data sources

| Data | Source | Licence | Use |
|------|--------|---------|-----|
| Land polygons (derived from OSM `natural=coastline`) | osmdata.openstreetmap.de → "Land polygons", WGS84 split version (`land-polygons-split-4326`) | ODbL | everything outside land = sea |
| Harbours / marinas | OSM via Overpass API or a Geofabrik `.osm.pbf` extract filtered with osmium | ODbL | harbour catalogue seed (`10` §10.3) |
| Seamarks (OpenSeaMap = OSM `seamark:*` tags) | same | ODbL | v2: optional blocks (`seamark:type=restricted_area`, `separation_zone`), fairway preference |
| Inland waterways (canals, rivers to marinas) | OSM `waterway=canal|river` + `natural=water` polygons | ODbL | v1: manual "passage" corrections; v2: import navigable ones |
| Map tiles | OSM (or other base layer) + `https://tiles.openseamap.org/seamark/{z}/{x}/{y}.png` | ODbL / CC-BY-SA | display only |
| Bathymetry (future) | e.g. EMODnet Bathymetry (Europe) | open | v3: avoid water shallower than ship draft |

Verify current download URLs, file names and licence terms when implementing.

## 10.2 Algorithm (recommended)

### Build step (offline, background job / CLI `Sms.Routing.Builder`)
1. **Region**: configurable bounding boxes (launch: Baltic Sea + North Sea,
   roughly lon −5…31, lat 50…66).
2. **Obstacles** = land polygons clipped to the region ∪ active `Block`
   corrections; load into an STR-tree spatial index (NetTopologySuite `STRtree`).
   Optionally buffer land by a safety margin (config, default 50 m) — but keep
   narrow channels open (see step 5).
3. **Adaptive grid (quadtree) over water**: start with coarse cells (~10 km);
   recursively split cells that intersect land until a minimum size (~100 m)
   is reached. Cells completely on land are dropped; cells completely at sea
   stay coarse. This keeps the graph small offshore and detailed near coasts,
   islands and channels. (An H3 hexagon grid with adaptive resolution is an
   equivalent alternative.)
4. **Graph**: one node per water cell (cell centre, or nearest water point if
   the centre is on land); edges between neighbouring cells (8-neighbourhood
   across cell sizes) if the straight segment between the nodes does not
   intersect an obstacle. Edge weight = geodesic distance (haversine, metres).
5. **Narrow passages**: where the buffered water disconnects a channel that
   exists in unbuffered water, keep edges from the unbuffered check (do the
   intersection test against unbuffered land, use the buffer only as a cost
   penalty near land).
6. **Passages** (`RoutingCorrection.Kind = Passage`, e.g. Nord-Ostsee-Kanal,
   river to an inland marina): add their LineString as a chain of nodes/edges,
   connected at both ends to the nearest sea nodes.
7. **Harbour access**: connect every active harbour to the nearest reachable
   water node(s) within 3 km by an "access edge" (allowed to cross the land
   buffer — marinas lie in basins). Harbours without access within 3 km are
   flagged "nicht erreichbar" for admins.
8. **Serialize** the graph (nodes as lat/lng arrays, CSR adjacency, harbour →
   node map, `GraphVersion`) to a binary file / blob. The API loads it into
   memory at start-up and swaps it atomically on rebuild. Measure size; a few
   million nodes are expected for the launch region.
9. Rebuild monthly (new OSM data) and on demand after corrections change;
   invalidate `RouteCache` entries of older graph versions.

### Query step (API, in-memory)
- **Harbour → harbour / point → point**: snap endpoints (harbour access node, or
  nearest water node within 2 km for a map point; error "Punkt liegt an Land"
  otherwise), run **A*** (heuristic = haversine to target), then **smooth** the
  path (string pulling: repeatedly connect a node to the farthest later node
  with a land-free line of sight). Distance = geodesic length of the smoothed
  path; round to 0.1 nm (1 nm = 1852 m). Cache harbour↔harbour results in
  `RouteCache`.
- **Nearest ports ("Nächste Häfen")**: one **bounded Dijkstra** from the start
  node until either `maxNm` (default 50 nm) is exceeded or `limit` harbours
  (default 20) are reached; return harbours sorted by sea distance with
  straight-line distance and ETA = distance / ship cruising speed. Geometry
  (smoothed) is computed lazily when the user hovers/selects a result.
- **Trip planning**: each `PlannedLeg` gets distance + geometry from the
  harbour → harbour query (cached on the leg).

### Why not other approaches
- *Visibility graph on coastline vertices*: exact shortest paths but the Baltic
  coastline has millions of vertices → too expensive; fine for a small region
  if ever needed.
- *Global shipping-lane networks* (e.g. Eurostat "searoute"/MARNET): too coarse
  for coastal sailing between marinas.
- *pgRouting in PostGIS*: viable alternative if PostgreSQL is chosen (graph in
  tables, `pgr_aStar`/`pgr_drivingDistance`); the in-memory .NET approach
  avoids the database dependency and is faster for one-to-many queries.

## 10.3 Harbour catalogue import from OSM

Example Overpass query (bbox = south, west, north, east):
```
[out:json][timeout:600];
(
  nwr["leisure"="marina"](53.3,7.5,58.0,15.0);
  nwr["seamark:type"="harbour"](53.3,7.5,58.0,15.0);
  nwr["harbour"="yes"](53.3,7.5,58.0,15.0);
);
out center tags;
```
For whole regions prefer a Geofabrik extract +
`osmium tags-filter … leisure=marina seamark:type=harbour harbour=yes`.

Mapping: `name` → Name; centre → Location; `website`/`contact:website` →
Website; `seamark:harbour:category` / `leisure` → Category; `phone`,
`vhf`/`seamark:communication:channel` → Phone/VHF; facility tags (`fuel`,
`drinking_water`, `power_supply`, `toilets`, `shower`, …) → Facilities.

Deduplication: candidates within 300 m with similar names (normalised
Levenshtein) are merged; user/legacy harbours keep their text, OSM fills empty
fields. Re-imports update only OSM-sourced fields that users have not edited
(track per-field origin or compare with last imported snapshot). Store
`OsmType`/`OsmId` for traceability.

## 10.4 API (suggested)

| Method & route | Response |
|----------------|----------|
| `GET /api/routing/route?fromHarbour={id}&toHarbour={id}` (or `from=lat,lng&to=lat,lng`) | `{ distanceNm, straightLineNm, geometry: GeoJSON LineString, graphVersion }` |
| `GET /api/routing/nearby-ports?harbour={id}` or `?lat=&lng=` `&maxNm=50&limit=20&speedKn=5` | `[{ harbourId, name, distanceNm, straightLineNm, etaHours }]` |
| `GET /api/routing/status` | graph version, build date, region, node count |
| `POST /api/admin/routing/rebuild` (admin) | starts rebuild job |
| `GET/POST/PUT/DELETE /api/admin/routing/corrections` (admin) | manage blocks/passages |

## 10.5 UI
- Harbour side panel: "Entfernungen zu anderen Häfen" now comes from
  `nearby-ports` (legacy table `04` §4.5 with "Route zeigen"); hover shows the
  route line with "`<n> sm`" label (legacy behaviour).
- Map: right-click / long-press "Nächste Häfen von hier"; button "Von meiner
  Position" (browser geolocation).
- Trip planner legs show distance and ETA.
- Admin: map editor to draw block polygons and passage lines, list of
  unreachable harbours, rebuild button and status.

## 10.6 Tests and validation
- Unit: A*/Dijkstra on synthetic graphs; smoothing never crosses obstacles;
  snapping rejects land points.
- Golden tests on a small real extract (e.g. Kieler Förde + Fehmarn):
  distances for a list of harbour pairs within ±10 % of reference values. The
  **legacy database** is a good reference: its hand-drawn waypoint graph gives
  realistic distances between the old harbours (`05` §5.1 algorithm).
- Performance: harbour→harbour < 200 ms, nearby-ports (50 nm) < 300 ms on the
  launch region.
