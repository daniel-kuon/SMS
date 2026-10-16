# 10. Automatic Sea Routing from OpenStreetMap / OpenSeaMap Data

**Goal:** estimate the sea distance (nautical miles) and a plausible route
between harbours, or from any point to the nearest harbours, so sailors see
"how far away the next ports are". **Not for navigation** — depth, draft,
traffic separation schemes, restricted areas, currents and wind are not
considered (at least in v1). Every result shows the disclaimer
"Entfernungen sind Schätzwerte und nicht zur Navigation geeignet." and the
attribution "© OpenStreetMap-Mitwirkende, OpenSeaMap".

This replaces the legacy hand-drawn waypoint graph (`05` §5.1, §5.3). The
legacy distance display (one decimal, " sm") and route highlighting (`05`
§5.2) stay as UI behaviour.

## 10.0 Decisions (owner, 2026-10)

| Topic | Decision |
|-------|----------|
| Database | **PostgreSQL** with **PostGIS**; routing graph stored in PostgreSQL and queried with **pgRouting** (§10.2) |
| Coverage | **Europe**, **sea areas only** for the start (§10.1) |
| Canals / rivers / buoyed channels | later stage (v2, §10.5), derived from **OpenSeaMap seamarks** (lateral buoys/beacons that guide ships) plus OSM waterway centerlines |

## 10.1 Coverage and data sources

### Region (v1)
European seas: North-East Atlantic coast incl. Azores, Madeira, Canaries,
British Isles, Norwegian Sea up to the North Cape, North Sea, Baltic Sea,
Mediterranean, Black Sea. Configure as a **region polygon** (GeoJSON in
config), not just a bbox; a sensible starting bbox is lon −32…45,
lat 27…72. Exclude disconnected water bodies (e.g. the Caspian Sea) and
inland lakes — the land-polygon data already treats lakes, rivers and canals
as land, which matches "sea areas only".

Because Europe is large, every query is **bounded** (see §10.2): nearby-ports
searches use a radius (default 50 nm, max 150 nm) and harbour→harbour routes
have a maximum length (default 500 nm). Longer routes return the straight-line
distance with a hint.

### Sources
| Data | Source | Licence | Use |
|------|--------|---------|-----|
| Land polygons (from OSM `natural=coastline`) | osmdata.openstreetmap.de → "Land polygons", WGS84 split version (`land-polygons-split-4326`), clipped to the region polygon | ODbL | everything outside land = sea |
| Harbours / marinas | Geofabrik `europe-latest.osm.pbf` filtered with `osmium tags-filter` (Overpass is too slow for all of Europe; fine for small test areas) | ODbL | harbour catalogue seed (§10.3) |
| Seamarks (OpenSeaMap = OSM `seamark:*` tags) | same Europe extract, filtered for `seamark:type=*` | ODbL | v2: buoyed fairways and canals (§10.5); optional blocks |
| Inland waterways | OSM `waterway=canal|river` (+ `CEMT`, `ship`, `boat`, `motorboat` tags) from the same extract | ODbL | v2 (§10.5) |
| Map tiles | OSM (or other base layer) + `https://tiles.openseamap.org/seamark/{z}/{x}/{y}.png` | ODbL / CC-BY-SA | display only |
| Bathymetry (future) | e.g. EMODnet Bathymetry (Europe) | open | v3: avoid water shallower than the ship's draft |

Verify current download URLs, file names and licence terms when implementing.

## 10.2 Algorithm

### Build step (offline: `Sms.Routing.Builder` CLI / background job)
1. **Prepare obstacles**: load land polygons, clip to the region polygon,
   import into PostGIS table `routing_land` (subdivided with `ST_Subdivide`
   for fast intersection tests, GiST index) together with active `Block`
   corrections.
2. **Adaptive quadtree over water**: start with ~20 km cells; split cells that
   intersect land, down to a minimum size of ~100 m near coasts (config; use
   e.g. 200 m in regions where the node count explodes, such as the Norwegian
   and Croatian coasts). Fully-land cells are dropped; open-sea cells stay
   coarse. Process **per tile** (e.g. 1°×1°) in parallel; tiles share border
   nodes so the graph is seamless. (An H3 hexagon grid with adaptive
   resolution is an equivalent alternative.)
3. **Nodes** = water-cell centres (or the nearest water point if the centre is
   on land). **Edges** between neighbouring cells (incl. different sizes) when
   the straight segment does not intersect land (`ST_Intersects` against
   `routing_land`, or NetTopologySuite with an STR-tree in the builder).
   Edge cost = geodesic length in metres (`ST_Length(geography)`); optional
   penalty within 50 m of land so routes do not hug the coast.
4. **Harbour access edges**: connect every active harbour to the nearest
   reachable water node(s) within 3 km (allowed to cross the near-shore land
   strip — marinas lie in basins). Harbours without access are flagged "nicht
   erreichbar" for admins.
5. **Passages** (`RoutingCorrection.Kind = Passage`): manual lines added as
   edge chains (also the v2 canal mechanism, §10.5).
6. **Write to PostgreSQL** (Npgsql binary `COPY`):
   - `routing_node(id bigint, geom geography(Point), tile_id int, graph_version int)`
   - `routing_edge(id bigint, source bigint, target bigint, cost float8, reverse_cost float8, geom geometry(LineString,4326), tile_id int, graph_version int)` — pgRouting layout, GiST index on `geom`
   - `routing_harbour_access(harbour_id uuid, node_id bigint, distance_m float8)`
   Build into a new `graph_version`, then switch the active version in one
   transaction; delete old versions afterwards.
7. **Rebuild** monthly (new OSM data) and on demand after corrections change
   (only affected tiles if possible); invalidate `RouteCache` of old versions.

Expected size: millions of nodes for Europe — that is why the graph lives in
PostgreSQL and every query works on a bounded subgraph instead of loading
everything into memory.

### Query step (API → PostgreSQL/pgRouting)
- **Snap**: harbour → its access node; map point → nearest water node within
  2 km (`ORDER BY geom <-> point LIMIT 1`), else error "Punkt liegt an Land".
- **Nearby ports** ("Nächste Häfen"): `pgr_drivingDistance` from the start
  node over the edges inside a bbox of the search radius (edges SQL with
  `geom && ST_Expand(...)`), join reachable nodes with
  `routing_harbour_access`, return the `limit` nearest harbours with sea
  distance, straight-line distance and ETA = distance / ship cruising speed.
- **Harbour → harbour / point → point**: `pgr_aStar` (or `pgr_bdAstar`) on the
  edges inside the bbox of both endpoints expanded by 30 % (retry with a larger
  box if no path is found, up to the max route length).
- **Smoothing** (in .NET): string-pulling over the node path — connect each
  point to the farthest later point whose segment does not intersect land
  (PostGIS check or cached land geometries of the bbox). Distance = geodesic
  length of the smoothed line, rounded to 0.1 nm (1 nm = 1852 m).
- **Caching**: harbour↔harbour results in `RouteCache`; nearby-ports results
  per harbour + radius + graph version in memory/Redis.
- **Trip planning**: each `PlannedLeg` gets distance + geometry from the
  harbour → harbour query (cached on the leg).

If performance targets are not met, add an in-memory .NET routing service that
loads tiles from the same tables on demand (LRU cache) — the table layout above
supports both.

## 10.3 Harbour catalogue import from OSM

Europe-wide:
```
osmium tags-filter europe-latest.osm.pbf \
  nwr/leisure=marina nwr/seamark:type=harbour nwr/harbour=yes \
  -o harbours.osm.pbf
```
then read with a .NET OSM PBF reader (e.g. OsmSharp) and compute centres
for ways/relations. For small test areas an Overpass query works:
```
[out:json][timeout:600];
(
  nwr["leisure"="marina"](54.2,9.8,54.7,10.9);
  nwr["seamark:type"="harbour"](54.2,9.8,54.7,10.9);
  nwr["harbour"="yes"](54.2,9.8,54.7,10.9);
);
out center tags;
```

Only harbours on the sea (within 3 km of a routing water node) are imported in
v1; inland harbours follow with v2 (§10.5).

Mapping: `name` → Name; centre → Location; `website`/`contact:website` →
Website; `seamark:harbour:category` / `leisure` → Category; `phone`,
`vhf`/`seamark:communication:channel` → Phone/VHF; facility tags (`fuel`,
`drinking_water`, `power_supply`, `toilets`, `shower`, …) → Facilities;
`addr:country`/reverse lookup → Country.

Deduplication: candidates within 300 m with similar names (normalised
Levenshtein / `pg_trgm` similarity) are merged; user/legacy harbours keep their
text, OSM fills empty fields. Re-imports update only OSM-sourced fields that
users have not edited (compare with the last imported snapshot). Store
`OsmType`/`OsmId` for traceability.

## 10.4 API (suggested)

| Method & route | Response |
|----------------|----------|
| `GET /api/routing/route?fromHarbour={id}&toHarbour={id}` (or `from=lat,lng&to=lat,lng`) | `{ distanceNm, straightLineNm, geometry: GeoJSON LineString, graphVersion, viaPassages: [] }` |
| `GET /api/routing/nearby-ports?harbour={id}` or `?lat=&lng=` `&maxNm=50&limit=20&speedKn=5` | `[{ harbourId, name, distanceNm, straightLineNm, etaHours }]` |
| `GET /api/routing/status` | active graph version, build date, region, node/edge count |
| `POST /api/admin/routing/rebuild` (admin) | starts rebuild job (all or selected tiles) |
| `GET/POST/PUT/DELETE /api/admin/routing/corrections` (admin) | manage blocks/passages |

UI:
- Harbour side panel: "Entfernungen zu anderen Häfen" from `nearby-ports`
  (legacy table `04` §4.5 with "Route zeigen"); hover shows the route line
  with "`<n> sm`" label (legacy behaviour).
- Map: right-click / long-press "Nächste Häfen von hier"; button "Von meiner
  Position" (browser geolocation).
- Trip planner legs show distance and ETA.
- Admin: map editor for block polygons and passage lines, list of unreachable
  harbours, rebuild button and status.

## 10.5 Stage 2: canals, rivers and buoyed channels via OpenSeaMap

Owner's idea: canals and channels are usually marked with **buoys/beacons
("Tonnen")** that guide ships; OpenSeaMap has these as OSM seamark objects.
Use them to extend routing beyond pure sea areas — and to improve routes in
shallow sea areas (Wadden Sea, Bodden, estuaries like Elbe/Weser/Schlei) where
"water" in the land-polygon sense is not necessarily navigable.

Relevant tags:
| Object | Tags |
|--------|------|
| Lateral buoys / beacons | `seamark:type=buoy_lateral` / `beacon_lateral`, `seamark:buoy_lateral:category` / `seamark:beacon_lateral:category` = `port` / `starboard` (also `preferred_channel_port/starboard`), `seamark:buoy_lateral:system` (IALA A in Europe) |
| Fairway / channel areas | `seamark:type=fairway`, `seamark:type=recommended_track`, `seamark:type=navigation_line` |
| Safe water / cardinal marks | `seamark:type=buoy_safe_water`, `buoy_cardinal` (side relative to danger) |
| Canal / river centrelines | `waterway=canal|river|fairway`, `CEMT=*`, `ship=yes`, `boat=yes`, `motorboat=yes`, `name` |
| Locks / bridges (later) | `lock=yes`, `waterway=lock_gate`, `seamark:type=bridge` (+ clearance → compare with ship air draft) |

Approach:
1. **Waterway centrelines** (`waterway=canal|river|fairway` with navigability
   tags) give connected lines for canals such as the Nord-Ostsee-Kanal and
   rivers to inland marinas. Add them as passage edges, connected to the sea
   graph where they meet it.
2. **Buoy-derived fairways**: cluster lateral marks along a channel, pair each
   port mark with the nearest starboard mark on the opposite side (respecting
   IALA A colours/sides and order), take the midpoints and chain them into a
   fairway centreline; use `fairway`/`recommended_track` geometries where
   they exist. Add as low-cost edges so routes prefer marked channels, and
   connect them to the sea graph at both ends.
3. In marked shallow areas (where fairway data exists) optionally **raise the
   cost of unmarked water** so routes follow the channels.
4. Admin review: show derived fairways on the admin map; let admins accept,
   fix or block them (they become `RoutingCorrection`s with `Source = Derived`).
5. Inland harbours become importable once they are reachable via passages.

Pairing buoys is heuristic; start with waterway centrelines (simple, robust)
and use buoy chains where no centreline exists or for sea approaches.

## 10.6 Tests and validation
- Unit: A*/Dijkstra on synthetic graphs; smoothing never crosses land;
  snapping rejects land points; region polygon clipping.
- Integration (Testcontainers `postgis` + pgRouting image): build the graph
  for a small extract (e.g. Kieler Förde + Fehmarn), run nearby-ports and
  route queries.
- Golden tests: distances for a list of harbour pairs within ±10 % of
  reference values. The **legacy database** is a good reference: its
  hand-drawn waypoint graph gives realistic distances between the old
  harbours (`05` §5.1). Add reference pairs for other seas (e.g. Solent,
  Mallorca, Split, Stockholm archipelago).
- Performance: harbour→harbour (≤ 100 nm) < 300 ms, nearby-ports (50 nm)
  < 300 ms, measured on the Europe graph.
