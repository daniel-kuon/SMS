# 15 – Self-hosted base map: options, measurements, recommendation

Status 2026-10-19: research below; what was then built (class-filtered lean set, 1.5 km harbour detail, thinned style) is in **§6**. Goal: our own tiles for the whole routing region
(4.5–19.5 E, 52–60 N) with **very little detail away from harbours** (coast/land/water, city names, Autobahn/Bundesstrasse
only) and **full detail within ~3 km of a harbour** (4 556 harbours). Today the app runs on OpenFreeMap
(`Map__RemoteBasemap=true` in `src/Sms.AppHost/AppHost.cs`) because the local `media/tiles/basemap.pmtiles` is only the
Schleswig-Holstein extract (bounds 7.46–11.80 E, 53.36–55.15 N, 130 MB, OpenMapTiles z0–14, Planetiler 0.10.3-SNAPSHOT).

## 1. What already exists (so most of the work is done)

* `tools/build-basemap.sh` has three modes: `full <area>`, `lean <area>` (z0–12, `--exclude_layers=building,housenumber,poi,aerodrome_label,park`)
  and `detail <area> harbours.poly` (z12–14 inside the polygon, `--polygon`). Planetiler runs in Docker
  (`ghcr.io/onthegomap/planetiler:latest`, help output shows 0.10.x, `--polygon` is supported), temp files in the Docker volume `sms-planetiler-tmp`.
* `GET /api/admin/map/harbour-areas.poly` (MapStyle.cs) delivers the union of 3 km circles around all harbours as a Planetiler `.poly`.
* `MapStyle.Rewrite` repoints the OpenFreeMap Liberty style to Martin, and, when `basemap-detail.pmtiles` exists, clones every
  Liberty layer onto a second source `openmaptiles_detail` with `minzoom >= 13` (replaced by the lean/detail split in §6). Martin serves every `.pmtiles` in `media/tiles`.
* The lean Europe build was never run ("on hold until the WSL memory cap is set", docs/13: the host crashed on 2026-10-17 when the
  35 GB Europe PBF import and an mmap Planetiler Europe build ran side by side).
* `media/cache/osm/` holds `europe-latest.osm.pbf` (35.2 GB, 2026-10-17) and `schleswig-holstein-latest.osm.pbf` (159 MB).

So the open problem is not the architecture but (1) a build that fits the machine and (2) that "lean" is not lean enough yet.

## 2. Environment (measured)

| | |
|---|---|
| Host | 127.7 GiB RAM, 24 logical cores (Windows 11), Docker 29.8, Java 11/17 (JDK 21 is not installed; Planetiler 0.10 needs 21+, so keep using the Docker image) |
| WSL2 | `.wslconfig`: `memory=108GB`, `swap=200GB`, `swapFile=D:\`, `sparseVhd=true`; Docker reports 106 GiB, 24 CPUs |
| Disk | C: 47.7 GB free (**the Docker VHDX `docker_data.vhdx` lives here**, 165 GB file, 120 GB used inside, virtual size 1 TB), D: 100 GB free |
| Docker | `docker system df`: 81 GB images (48 GB reclaimable), 37 GB build cache (20 GB reclaimable) |
| Tools missing | `pmtiles`, `tilemaker`, `osmium`, `tippecanoe` (the go-pmtiles CLI v1.31.2 was downloaded to the scratchpad only, for measuring) |

Consequence: a **Europe** Planetiler run (temp files 5–10x the PBF = 175–350 GB; script comment says 60 GB with mmap) cannot be done on this
disk setup, and it is exactly what crashed the host. A run on a **region-cut PBF** (4–7 GB, estimate) needs ~15–35 GB temp and ~8 GB heap and is safe.

## 3. Measurements

### 3.1 Protomaps public build (build.protomaps.com/20261018.pmtiles, 139 GB, z0–15, schema v4.15.2, Planetiler 0.10.2)

`pmtiles extract … --bbox=4.5,52,19.5,60 --dry-run` (directory only, costs nothing) gives the exact archive size for our routing region:

| maxzoom | tiles with data | archive |
|---|---|---|
| 6 | 26 | 1.9 MB |
| 8 | 183 | 18 MB |
| 10 | 2 247 | 143 MB |
| 11 | 8 413 | 320 MB |
| 12 | 30 537 | 680 MB |
| 13 | 110 946 | 1.3 GB |
| 14 | 407 889 | 2.5 GB |
| 15 | 1 526 283 | 5.1 GB |

Schleswig-Holstein bbox, z14: 190 MB (our own OpenMapTiles file: 130 MB). A 0.4 x 0.2 degree box around Kiel (z0–15) downloaded in 10 s: 14 MB.
Conclusion: the full-detail region is **2.5–5 GB, not the 25 GB assumed for Europe in docs/13**; the lean set is a few hundred MB.
(The "Europe lean = 3–4 GB" estimate in docs/13 is probably 5–10x too high for the region we actually need.)

### 3.2 What a tile contains (decoded with python mapbox-vector-tile, gzip sizes, features = vector features)

Our own OpenMapTiles tiles (`media/tiles/basemap.pmtiles`), "lean7" = layers water, waterway, water_name, place, transportation, transportation_name, boundary only;
"+major" additionally keeps only motorway/trunk/primary/ferry roads:

| Tile | full | lean7 | lean7 + major roads |
|---|---|---|---|
| Kiel z8 | 50 KB / 633 f | 17 KB / 122 f | 16 KB / 116 f |
| Kiel z10 | 76 KB / 1 101 f | 32 KB / 577 f | 27 KB / 547 f |
| Kiel z12 | 51 KB / 1 507 f | 36 KB / 1 357 f | 13 KB / 379 f |
| Kiel z14 | 126 KB / 3 723 f (poi 64 KB, building 21 KB) | 30 KB / 700 f | 3 KB / 39 f |
| Lübeck z14 | 176 KB / 4 124 f | 32 KB / 761 f | 3 KB / 48 f |
| Fehmarn (rural) z12 | 12 KB / 182 f | 7 KB / 129 f | 5 KB / 106 f |

Protomaps tiles (Kiel, Copenhagen, Swedish rural, open Baltic, Stockholm archipelago; z8/10/12) are heavier at low zoom because `landuse` alone carries
1 000–2 400 features per z8–z10 tile (60–140 KB of a 77–163 KB tile). Keeping only earth, water, localities and motorway/trunk/primary/ferry gives
**4–20 % of the bytes and roughly 5–10 % of the features on land** (e.g. Kiel z10: 124 KB / ~2 500 f → 12 KB / ~150 f); in the Stockholm archipelago
it is only 40–48 % because the coastline geometry dominates.

Take-aways: (1) dropping layers (`--only_layers`) alone saves only 2–3x at z8–12; the big win at z12+ needs class filtering of roads
(`transportation` 25 KB of 51 KB at Kiel z12 is mostly secondary/tertiary/minor), and at z10 of `place` (13 KB, mostly villages);
(2) the lean set stops at z12 (overzoomed above), the detail set carries z13+; (3) everything else (landcover, landuse, park, poi, building, housenumber, aeroway) is
the part that costs both bytes and client time.

### 3.3 Client rendering cost

MapLibre cost per tile = decode + per-feature layer matching + tessellation in the worker, then draw calls per style layer. Features per visible tile go from
~1 000–4 000 (full, z10–z14 in a town) to ~40–550 (lean), i.e. a factor 2 at z8–10 with layer dropping only and **5–10x with road/place class filtering and no landuse**.
Liberty has 111 style layers; the lean style needs about 15. A harbour-detail tile is as heavy as today's full tile, but only within 3 km of a harbour.
Serving cost is negligible either way (Martin reads byte ranges from a memory-mapped pmtiles file; OFM tiles come over the internet and add 100–300 ms per cold tile).
Note: the existing `Rewrite` clones every layer into a `-detail` twin (111 → ~222 layers, all `minzoom` 13, so free below z13). At z13+ the overzoomed lean source and the detail source both draw inside the harbour areas, i.e. roads/coast are drawn twice with slightly different geometry. Check visually; if it shows, give the lean twins `maxzoom: 13` for the layers the detail set also carries (outside the polygons then only the detail source's absence shows the lean layers - decide after looking at the result).

## 4. Options

### (a) Planetiler, OpenMapTiles schema, two tiers (what the repo does) — **recommended**

Work:
1. Cut the region from the existing Europe PBF (one pass, 15–25 min, no extra download):
   `osmium extract -b 4.5,52,19.5,60 --strategy=simple -o media/cache/osm/region-latest.osm.pbf media/cache/osm/europe-latest.osm.pbf`
   (osmium-tool via `apt install osmium-tool` in the Ubuntu WSL distro; expected output 4–7 GB).
2. Change `tools/build-basemap.sh` lean mode to `--only_layers=water,waterway,water_name,place,transportation,transportation_name,boundary --maxzoom=12`
   (keeps Liberty working unchanged, because the source-layer names are the same) and accept the area name `region`.
3. Detail: `GET /api/admin/map/harbour-areas.poly > media/cache/harbours.poly`, then `tools/build-basemap.sh detail region media/cache/harbours.poly` (z12–14).
4. Thin the style in `MapStyle.Rewrite` for the lean source: filter `transportation` to `class in (motorway, trunk, primary, ferry)` and `place` to `class in (city, town, island)` below z13 (client rendering cost goes away immediately; bytes only after step 5).
5. Optional second pass for bytes: build lean as `.mbtiles`, filter with `tile-join -l transportation -l place … -j '{"transportation":["in","class","motorway","trunk","primary","ferry"],"place":["in","class","city","town","village","island"]}'`
   (tippecanoe, built in WSL or a tiny Dockerfile) and `pmtiles convert lean.mbtiles basemap.pmtiles` (go-pmtiles converts only mbtiles → pmtiles).
   A Java/YAML custom profile would give the same without a second pass, but costs 2–3 days (Planetiler YAML schemas are "in active development"; the OpenMapTiles profile is Java only).

Hardware: heap 8–12 GB (`JAVA_XMX=12g`), page cache the rest, temp 15–35 GB inside the Docker VHDX (C: has 47 GB free: stop other builds, `docker builder prune`), 24 cores.
Build time (estimates scaled from "Schleswig-Holstein 159 MB = 3 min" and Planetiler's published scaling; not measured): lean z0–12 15–40 min, detail 10–25 min (the PBF must still be read), osmium cut 15–25 min; full z0–14 of the region would be 60–120 min and 25–50 GB temp (not needed).
Europe is not needed; if it ever is, `--storage=mmap` needs > 150 GB free on the Docker disk.
File size (estimates from 3.1/3.2): lean 60–150 MB without class filtering, 40–100 MB with; detail 0.15–0.5 GB (z12–14: 4 556 discs of 28 km², much of it sea, i.e. empty tiles); whole thing < 0.7 GB.
Update: monthly, re-run steps 1–3 after re-downloading the Europe PBF (35 GB) or the ~12 country extracts (Denmark, Sweden, Norway, Finland, Poland, Baltics, Netherlands, Germany north) and `osmium merge`; the harbour polygon must be regenerated whenever harbours change. Natural Earth and `water-polygons-split-3857.zip` (931 MB, already in `media/tiles/sources`) are downloaded by Planetiler, updated about monthly.
Licensing: OSM data ODbL ("© OpenStreetMap contributors"), OpenMapTiles schema CC-BY 4.0 ("© OpenMapTiles", already in the file metadata), Natural Earth public domain, OSM water polygons ODbL, Liberty style from OpenFreeMap (check its licence file before redistributing a modified copy), fonts OFL.
Risks: Planetiler on Windows/WSL memory (mitigated by the region PBF); lean+detail double draw at z13 (see 3.3).

### (b) Protomaps basemap build (pmtiles extract) + filtered style

Work: download the CLI (`go-pmtiles_*_Windows_x86_64.zip`), then
`pmtiles extract https://build.protomaps.com/YYYYMMDD.pmtiles lean.pmtiles --bbox=4.5,52,19.5,60 --maxzoom=12` (680 MB, 2 min on this connection)
and `pmtiles extract … detail.pmtiles --region=harbours.geojson --minzoom=12 --maxzoom=15` (`--region` needs a GeoJSON polygon; the API endpoint would need a `.geojson` variant, ~15 lines).
That already yields the **complete region in minutes with no PBF, no Java and no RAM concern**, but: (1) it cannot drop layers or classes — landuse alone is 60–80 % of a low-zoom tile, so a filter pass is still needed (tippecanoe `tile-join` after `pmtiles` → mbtiles is not possible with go-pmtiles; you would build the lean part with Planetiler + the Protomaps profile instead, which brings back (a)'s PBF work); (2) the **schema differs** from OpenMapTiles (layers `earth, water, landuse, roads, places, buildings, pois, boundaries`, attribute `kind`/`kind_detail`), so Liberty cannot be reused: a new style (generated with the `@protomaps/basemaps` npm package, light flavour, Noto Sans fonts incl. "Medium" which our glyph cache does not serve yet), a `MapStyle` rewrite, the POI-click code (`source-layer === 'poi'`, `class/subclass` in `core/map.ts`) and `MapStyleTests` must be redone: about 2 days plus visual tuning. No sprites needed for the lean style.
Size: z0–12 680 MB unfiltered (150–250 MB filtered, estimate from 3.2); detail z12–15 0.3–0.8 GB (estimate); full region z0–14 2.5 GB, z0–15 5.1 GB.
Update: re-run the two extract commands (daily builds exist, the page asks not to hotlink and to copy the file to your own storage; each run transfers ~1 GB).
Licensing: the basemap is an ODbL *Produced Work*; attribution "© OpenStreetMap" is required, no further terms are listed; the daily builds are free to download, but they are a best-effort service (URLs may change). Code and styles BSD-3.
Verdict: best option if we ever want to drop Planetiler maintenance; **not** the quickest way to the requested "very little detail", and the restyle is real work. Keep as the fallback if the region Planetiler build keeps failing.

### (c) tilemaker (Lua profile)

Work: Docker image `ghcr.io/systemed/tilemaker`, `config.json` + `process.lua` (layers, zoom ranges, per-class `MinZoom`) — the default profile is OpenMapTiles-like, so Liberty mostly works.
Gives the finest control (motorway/trunk/primary only below z12, place classes by population) in a single pass, output `.pmtiles`. Needs coastline (`water-polygons`, Natural Earth landcover, ~2 GB download via its get-coast scripts), keeps data in RAM by default (`--store <ssd dir>` for big inputs; region 5 GB PBF would need ~20–30 GB RAM or `--store`), no two-tier concept: lean and detail are two runs (`--bbox`, no polygon support, so the harbour circles would have to be cut afterwards with `pmtiles extract --region`).
Effort 2–3 days (learn Lua profile, replicate our pier rules, check Liberty attribute compatibility), no time or size advantage over (a) once step 5 is done. License FTWPL (tool), data as above, schema CC-BY (OpenMapTiles). Not recommended unless Planetiler is a dead end.

### (d) Keep OpenFreeMap (+ caching proxy / style thinning)

* Style thinning in `MapStyle.Rewrite` (hide landcover, landuse, park, poi, housenumber, aeroway, building and all but major road classes and city/town labels below z13) cuts client work roughly as in 3.3 but not the bytes: OFM serves full tiles (~100 KB at z10 around Kiel). 0.5 day; works for the whole of Europe today.
* A caching reverse proxy (like the existing fonts/sprites proxy) removes the OFM dependency for repeat visits only; the first view of every tile still comes from OFM. OFM is free without key and limits; its quick start allows offline downloads only up to 3 000 tiles / 50 MB per download (4 parallel, unique user agent) and asks for contact beyond that, so a bulk pre-seed of the region is not allowed without asking; a cache filled by real users is ordinary use. Attribution "OpenFreeMap © OpenMapTiles, data from OpenStreetMap"; they ask for sponsorship if it saves you money.
* No effect on the "drastically reduced detail" requirement for bytes, no offline use.

## 5. Recommendation and plan

**Step 0 (now, 0.5 day, independent):** do the style thinning from (d) in `MapStyle.Rewrite`, applied to *any* source (remote or local), so the map is light today in Sweden/Poland too.
**Steps 1–4 (1–1.5 days incl. waiting for builds): option (a).**

```bash
# in the Ubuntu WSL distro (once)
sudo apt install osmium-tool
cd "/mnt/d/Rider Projects 2/SMS"
osmium extract -b 4.5,52,19.5,60 --strategy=simple -o media/cache/osm/region-latest.osm.pbf media/cache/osm/europe-latest.osm.pbf   # 15–25 min

# build script change (lean mode): --only_layers=water,waterway,water_name,place,transportation,transportation_name,boundary --maxzoom=12 ; area "region" -> PBF region-latest.osm.pbf
JAVA_XMX=12g tools/build-basemap.sh lean region                                   # 15–40 min -> basemap-lean-region.pmtiles
curl -H "Authorization: Bearer <admin token>" http://localhost:<api>/api/admin/map/harbour-areas.poly > media/cache/harbours.poly
JAVA_XMX=12g tools/build-basemap.sh detail region media/cache/harbours.poly       # 10–25 min -> basemap-detail.pmtiles
cp media/tiles/basemap-lean-region.pmtiles media/tiles/basemap.pmtiles && docker restart $(docker ps -q -f name=basemap)
# then AppHost: Map__RemoteBasemap=false (or remove the line)
```

Preconditions: nothing else heavy running (the 2026-10-17 crash), `docker builder prune` + `docker image prune` for headroom on C:, `JAVA_TOOL_OPTIONS` below 16 GB.
Then: verify in the browser at Kiel, Grossenbrode, Swedish archipelago (coast joins between lean z12 and detail z13), decide the double-draw fix from 3.3.
**Step 5 (optional, +1 day):** tile-join class filter for another 2x in bytes; only worth it if the lean file is > 150 MB or low-end clients struggle. **Fallback:** option (b) if the region Planetiler build fails twice (+2 days).

Developer effort: 0.5 day (step 0) + 1–1.5 days (a) = **about 2 days**, of which ~3 h is machine time; +1 day for step 5; (b) instead of (a) would be 2–3 days, (c) 2–3 days.

## 6. Built 2026-10-19/20 (lean filter, 1.5 km detail, thinned style)

**Region cut:** `media/cache/osm/region.osm.pbf` (3.9 GB, osmium, `--strategy=simple`; "missing node" warnings in Planetiler are the ways cut at the bbox edge).

**Lean tiles = Planetiler `lean region` + class filter.** Mechanism: tippecanoe `tile-join -j` (felt/tippecanoe 2.79.0, built once into the
local Docker image `sms-tippecanoe:2.79.0` by `tools/build-basemap.sh`, ~1 min). Chosen over a Planetiler YAML schema (would re-implement the
whole OpenMapTiles profile, coast/water handling included) and over our own filter tool (code to maintain, and the pmtiles writer is not in the
repo): it is one scripted pass, does not touch geometry, and its filter knows the tile zoom (`$zoom`). It runs inside the Docker volume (on the
Windows bind mount it took 14 min instead of 45 s), keeps only the attributes the style reads (`-y`, the ~80 `name:xx` translations per label were
a big part of place/water_name), and the input's pmtiles header is copied back afterwards with go-pmtiles (`protomaps/go-pmtiles:v1.31.2 edit`),
because tile-join writes world bounds. `tools/build-basemap.sh lean region` runs it after Planetiler; `tools/build-basemap.sh filter <file>` runs it alone.

| layer | kept |
|---|---|
| transportation | motorway, trunk, primary, ferry, main rail (`class=rail`, no `service`); secondary from z10; no links (`ramp=1`), nothing else |
| transportation_name | motorway, trunk, primary; secondary from z10 (ref shields, road names) |
| place | continent, country, state, province, city, town, island; village and suburb from z11 (no hamlet, isolated_dwelling, neighbourhood, quarter, locality) |
| boundary | admin_level ≤ 4 |
| water, waterway, water_name | unchanged |

Result (`basemap.pmtiles.new-lean`): **116 MB** (unfiltered 216 MB; without the attribute cut 127 MB). Size shares measured with `tile-join -l`: water 65 MB
(coast, Swedish/Norwegian lakes; untouchable), place 28 → ~12 MB, transportation(_name) 17 MB, waterway 8.5 MB, water_name + boundary 11 MB; z12 is ~60 %
of the file. Main rail costs 2.1 MB (1.9 %), secondary at z10–11 2.2 MB, both kept. Filter run: 45–60 s.

| tile | before | after |
|---|---|---|
| Kiel z10 | 32.4 KB / 577 f | 15.6 KB / 214 f |
| Kiel z12 | 35.6 KB / 1 356 f | 14.4 KB / 677 f (540 of them secondary/trunk pieces) |
| Stockholm archipelago z10 | 31.4 KB / 250 f | 28.6 KB / 190 f (water 22 KB) |

**Detail tiles:** harbour polygon with 1.5 km radius (`GET /api/admin/map/harbour-areas.poly?radiusKm=1.5` → `media/cache/harbours-1500.poly`, 6 439 rings
for all harbours; the script keeps the 1 676 merged rings touching the region). **The first two detail builds (3 km and 1.5 km) were silently
full-region z12–14 (2.76 GB, 588 k tiles, a tile in inland Sweden):** Planetiler 0.10.3 ignores `--polygon` when `--bounds` is set too
(`Bounds.setShape` does not recompute the tile extents). The script now passes no `--bounds` in detail mode, the polygon supplies them
(log line `prepareShapeForZoom z14 … tiles=20,521`). Landcover was 106 MB of the 561 MB result (and wood/grass discs ended at the polygon edge),
housenumber 42 MB and mountain_peak are not drawn by Liberty: all three are excluded. Result (`basemap-detail.pmtiles.new`): **414 MB**, 32 179
tiles z12–14, Planetiler 6 min. Lübeck z14: 158 KB / 3 666 f (poi 2 599, building 241), Kiel z14 118 KB / 3 442 f.

**Style (`MapStyle.Rewrite`, only when a detail file exists):** decision for the double draw from 3.3: the lean source keeps every layer on
`water, waterway, place, transportation, transportation_name, boundary, water_name` at all zooms (overzoomed z12), so main roads, town labels and
shields stay visible at z13+ outside the harbour areas too. Layers on source-layers lean lacks (landuse, landcover, park, poi, building, aeroway,
aerodrome_label, …) move to the detail source with minzoom 13 (no copy left on lean; a layer whose maxzoom ≤ 13 is dropped). Lean layers get a
z13+ detail twin that only draws what lean filtered out (`!(class in LeanClasses && ramp != 1)`): side streets, links, paths, hamlets, but never a
second motorway or city label. water/waterway twins are plain (same colour; the finer z14 coast covers the overzoomed z12 one); boundary and
water_name get no twin. The pier rules read the detail source. Layers on source-layers the detail build excludes (`DetailExcluded`:
landcover, housenumber, mountain_peak) are dropped, because MapLibre logs an error for every layer naming a source-layer missing from the
TileJSON. 111 Liberty layers → 184 (86 lean, 96 detail; previously ~222).
`LeanClasses` in MapStyle.cs and `LEAN_FILTER` in the script must stay in sync.

**Browser check 2026-10-20** (screenshots `.playwright-mcp/bm-*.png`): overview z6–z11, Kiel z12–z15, inland Schleswig-Holstein z13, open sea,
Stockholm archipelago z10/z13 render as intended; no double roads or labels; the app's own layers (POI, seamarks, depth, harbours, route) stay above
every base-map layer. Tiles come from Martin in 10–18 ms, panning z9–z13 shows no long tasks. Known limits: (1) **outside the region
(east of 19.5 E, north of 60 N, south of 52 N) there are no tiles, so the sea is drawn in land colour**, although harbours exist there
(of 6 439 harbour rings only 1 670 lie in the region); (2) the detail area ends at z13/z14 tile edges (squares, not circles), and outside it a
town at z14 shows only main roads; (3) `highway-shield-*` filters warn about a missing `ref_length` (a Liberty quirk, harmless).

**Switch (done 2026-10-20 by the user):**

```bash
cd "/d/Rider Projects 2/SMS/media/tiles"
mv basemap.pmtiles basemap.pmtiles.bak                     # Schleswig-Holstein full build
mv basemap.pmtiles.new-lean basemap.pmtiles
mv basemap-detail.pmtiles.new basemap-detail.pmtiles        # there is no older basemap-detail.pmtiles
# AppHost.cs: Map__RemoteBasemap "false", then restart the AppHost (Martin only lists the files at start)
```

**Wide area (2026-10-20, replaces the region pair).** Limit (1) above: the base map now covers **3–31 E, 50.5–66 N** (whole Baltic incl.
Finland, Gulf of Bothnia up to Oulu/Luleå, Baltic states, St Petersburg, south Norway incl. Bergen, Benelux and the German North Sea coast;
2 579 of the harbour areas). `osmium extract -b 3,50.5,31,66 --strategy=simple` → `media/cache/osm/wide.osm.pbf` 10.8 GB in 12 min.
The script knows the area names `region` and `wide` (bbox → `--bounds`, PBF `<area>.osm.pbf`, polygon clipping in detail mode).
Outside the bbox, MapLibre would draw Liberty's land-coloured background where the sea is, so every lean build merges a **z0–5 world tier**
(Planetiler, water + boundary only, both from Natural Earth at these zooms; OSM input is any small file, 1.6 min, cached in the volume as
`world-z5.pmtiles`) into the file with the same tile-join pass; the header then keeps tile-join's world bounds (correct now; z6+ tiles
outside the bbox are a 204 from Martin). Zoomed in beyond z5 outside the bbox the background is still land-coloured.

| | lean `basemap.pmtiles.new-wide-lean` | detail `basemap-detail.pmtiles.new-wide` |
|---|---|---|
| size | **403 MB** (water 198 MB: Finnish/Swedish lakes; place 91 MB, of it islands 10 MB; roads 59 MB; waterway 41 MB; water_name + boundary 34 MB; z0–11 184 MB) | **737 MB**, 58 002 tiles z12–14 (z14 shape 36 706 tiles) |
| time | Planetiler 9 min 20 s + world tier 1 min 36 s + filter ≈ 2 min = 13.5 min | 13.5 min |
| tiles | Kiel z10 15.6 KB / 214 f, z12 14.4 KB / 677 f (unchanged); Helsinki z10 42 KB (374 place, mostly islands); Oulu z10 12.9 KB; Riga z10 14.5 KB; Rotterdam z10 14.1 KB; Atlantic z3 4 KB (world tier) | Lübeck z14 158 KB; Helsinki South Harbour z14 215 KB / 6 214 f (4 765 POI); Tallinn z14 190 KB; Den Helder z14 78 KB |

If 403 MB is too much: villages/suburbs from z12 instead of z11 and islands only from z11 would cut most of the 91 MB place layer.

## 7. Sources

Protomaps docs (downloads, layers, ODbL Produced Work), Planetiler README (hardware/benchmarks: heap ≈ 0.5x PBF, disk 5–10x PBF, v0.10.1 planet in 19 min on 192 cores), Planetiler custommap README (YAML schema),
tilemaker README (`--store`, Lua profile, pmtiles output, FTWPL), OpenFreeMap quick start (usage limits, attribution), `pmtiles show/extract --dry-run` against build.protomaps.com/20261018.pmtiles.
