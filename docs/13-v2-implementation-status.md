# 13. v2 Implementation Status and Decisions

State of the rewrite on branch `v2` (2026-10-17). Phases refer to `11` §11.5.

## Done

| Phase | Status | Notes |
|-------|--------|-------|
| 0 Scaffolding | ✅ | Aspire AppHost (one-click), SigNoz stack, PostGIS/pgRouting image, Mailpit, Angular shell, i18n de/en, OpenAPI (`/api/docs`), domain tests |
| 1 Accounts | ✅ | Register + e-mail confirmation (Mailpit), login/logout (cookie), password reset, profile, settings, data export, account deletion with anonymisation, rate limiting |
| 2 Ships | ✅ | CRUD, owner/skipper members, invitations by user search or e-mail link, ship page with stats, photo |
| 3 Harbours | ✅ (OSM import for small areas) | PostGIS catalogue, bbox map with clustering + sea-mark overlay, detail panel, WiFi (passwords for registered users), reviews, revisions + revert, admin Overpass import + merge |
| 4 Trips | ✅ | CRUD, crew (users, guests, "Logbuch schreiben", from/to), invitations (user, e-mail, link), accept/decline, planner legs, iCal feeds per user/ship |
| 5 Logbook | ✅ | Paged/sorted ship logbook, entry dialog with legacy + new fields, defaults from previous entry and trip crew, entries without trip, visibility-filtered views |
| 6 Posts / photos / timeline | ✅ | Upload pipeline (ImageSharp thumb/preview/original, EXIF day + GPS, EXIF stripped), day timeline, entry detail with day content, posts with photos, central visibility filter (`Viewer`), authorised media delivery |
| 7 Costs | ✅ | Split modes, ECB rates of the payment date (daily job + history bootstrap), balances, greedy transfers, settlements report/confirm, lock, CSV and PDF export (expenses, balances, suggested transfers, settlements) |
| 8 Sea routing | ✅ (region-configurable) | `RoutingGraphBuilder`: OSM land polygons (downloaded once, ≈900 MB) → adaptive quadtree over water → pgRouting tables; `SeaRouter`: A* routes with string-pulling smoothing, driving-distance nearby ports, cache; admin rebuild/status/corrections. Default region western Baltic (build ≈75 s, 72k nodes); Europe = set `Routing:Region` and wait hours |
| 8a Trip overview | ✅ | Overview map with recorded (solid) or calculated/straight (dashed) legs, harbour stops, photo/post markers with clustering, stats, day list hover, last position, track playback (play/pause, scrubber, 1×/4×/16×; boat marker with heading, travelled trail, current leg highlighted) |
| 8b Tracking | ✅ (no AIS provider yet) | Devices with one-time tokens, OsmAnd/Traccar, OwnTracks and JSON ingest, GPX/KML/NMEA import, cleaning, per-day segments with source priority, media/post placement on the track, public delay, privacy zones, source override, stop detection → logbook suggestions |
| 12 PWA / offline | ✅ (basic) | Installable (service worker + manifest), cached reference data, offline upload queue (IndexedDB, retried when online), autosaved post and logbook drafts; logbook entries saved without connection go into a localStorage outbox and are sent when online (nav chip shows pending items, list reloads). No background sync API |
| 9 Social | ✅ | Comments/likes on posts, photos, logbook entries, trips, reviews (target visibility enforced), follows, personal feed, public explore, in-app notifications (+ e-mail for invitations/comments/settlements), reports, admin hide/dismiss, user blocks |
| 10 Tasks / pages | ✅ | Trip and ship task trees, admin-editable content pages in footer |
| 11 Reviews / stats | ✅ | Harbour ratings, ship statistics |
| 14 Canals / rivers / fairways | ✅ (steps 1–2) | Navigable OSM waterways (`waterway=canal/river/fairway` with CEMT/boat/motorboat/ship tags) imported per bounding box (Overpass mirrors or JSON upload) as *derived* passages; ways merged end-to-end, junctions share graph nodes; inland harbours get access via passage nodes. Fairway centrelines derived from the imported lateral marks (port/starboard pairs → midpoints → chains) as further derived passages; passage edges cost ×`Routing:PassageCostFactor` (0.8) so routes prefer canals and marked channels. Bridges (with `seamark:bridge:clearance_height`) and locks/gates come with the waterway import as point corrections; the builder stores the lowest clearance on the passage edges under a bridge (`routing_edge.clearance_m`) and adds `Routing:LockPenaltyM` (1 nm equivalent) per lock; routes for a ship (planner legs, logbook routes, `/api/routing/route?airDraft=`) exclude edges whose clearance is below the ship's air draft (uncached). Admin page shows all corrections on a map (blocks red, manual passages blue, derived orange, bridges/locks as points) with a list to deactivate/delete, and lets admins draw new passages/blocks by clicking. Not done: penalising unmarked shallow water, opening bridges/lock schedules, fairway areas from OSM polygons |
| Vector maps | ✅ | MapLibre GL everywhere (harbour map, trip overview); base style OpenFreeMap *Liberty* (vector, no API key); own sea-mark overlay from `Sms.Tiles` |
| Sea-mark tile server | ✅ | `Sms.Tiles` (separate Aspire resource): `GET /tiles/seamark/{z}/{x}/{y}.mvt` from the `Seamarks` table via `ST_AsMVT`, zoom ≥ 8, memory cache + ETag; admin import of OpenSeaMap data per bounding box (Overpass with mirror fallback) or Overpass-JSON upload |
| CMS | ✅ | Global content pages as a tree (nav + footer); per-ship websites with sub pages (owner-editable, published/draft, slug paths); domains assigned to ships → the site served on that host shows only the ship's pages and latest logbook (`/api/site`); dev preview via `X-Tenant-Host` |
| Tests | ✅ | 17 domain tests, 42 API/infrastructure tests (Testcontainers PostGIS, all endpoint groups + authorization matrix + CMS/tiles), 36 Angular vitest specs, 7 Playwright e2e tests (`npm run e2e`, needs the AppHost: web fixed on :5173, Mailpit on :8025) |

## Open (follow-ups)

- **AIS provider** (`12` §12.1 C): `IAisPositionProvider` + MarineTraffic/aisstream polling; the track pipeline already accepts `Ais` points.
- **Europe-wide routing build**: run with `Routing:Region` = −32…45 / 27…72; the build is tile-wise with bounded memory (see decisions), only time (hours) and PostGIS disk are needed. Untested at that scale so far.
- **13 legacy import** (needs the old SQL Server database).
- **14 canals, rest**: higher cost on unmarked shallow water near fairways, opening bridges (closed clearance vs. opening times), fairway areas (`seamark:type=fairway` polygons) as geometry source.
- Europe-wide OSM harbour **and sea-mark** import (osmium pipeline); the Overpass import works for bounding boxes (≈30 s for Kiel Bay, 5 000 marks).
- Self-host the base map (OpenFreeMap tiles are a free public service; `Map:StyleUrl` points to any MapLibre style, e.g. a Planetiler/Martin stack next to `Sms.Tiles`).
- (done: Playwright e2e smoke – `npm run e2e` in `src/sms-web` against the running AppHost: map + sea marks, register/confirm/login, ship + trip, logbook entry + overview, costs with CSV/PDF, ship website + domain tenant mode.)

## Decisions made during implementation

| Topic | Decision |
|-------|----------|
| Project layout | `Sms.Application` folded into `Sms.Api` (endpoint groups + `Access`/`Viewer` services); `Sms.Routing*`, `Sms.Worker` deferred until routing/tracking exist |
| Authorization | One scoped `Access` service + a `Viewer` record that yields EF-translatable visibility predicates instead of policy/resource handlers — same matrix, less plumbing |
| CSRF | Cookie auth + `SameSite=Lax` + mandatory `X-Requested-With: XMLHttpRequest` header on state-changing `/api` calls (SPA interceptor) instead of ASP.NET antiforgery tokens |
| Rich text | Plain textarea → paragraphs; server-side `HtmlSanitizer`, Angular `[innerHTML]` sanitising. A real editor (TinyMCE/Quill) can be dropped in later |
| Geography | Columns are `geography(Point,4326)`; coordinates are projected in memory because PostGIS has no `ST_X/ST_Y` for geography |
| Images | ImageSharp **3.1.12** (Six Labors Split License) — 4.x requires a paid license for Release builds. The open advisories against 3.1 concern TIFF/BigTIFF, HistogramEqualization and ICC CLUT parsing; decoding is restricted to JPEG/PNG/WebP and uploads are limited to signed-in crew, so only the ICC parsing one is reachable (memory DoS by a crafted profile). Revisit when buying a 4.x license |
| Mail | `System.Net.Mail.SmtpClient` to Mailpit; switch to MailKit for a real provider |
| SigNoz | Pinned `signoz/signoz:v0.116.1` + collector `v0.144.2` (last upstream docker-compose before Foundry); a `signoz-setup` container registers the admin org on first start because the collector replaces its pipelines with `nop` until an org exists |
| Angular | 21 (Node 22.14 on the dev machine is below Angular 22's minimum) |
| Calendar tokens | Data-protection-encrypted ids instead of stored secrets |
| Routing build | In-process background job (admin button) instead of a separate `Sms.Routing.Builder` CLI; same tables as spec 10 §10.2 so a CLI can be split out later |
| PDF | `SimplePdf` (≈100 lines, PDF 1.4, standard Helvetica, WinAnsi) writes the cost statement – no PDF library and no font files to ship; switch to PDFsharp/QuestPDF if images or non-Latin text are needed |
| Tile-wise graph build | Land polygons stream from the shapefile straight into `routing_land`; the region is then processed in tiles of `Routing:TileCells`² start cells (`WaterGraphTiler`): each tile loads only its clipped land from PostGIS, builds the quadtree, writes nodes/edges via COPY and stitches to already built neighbours through their retained border cells. Cells fully covered by land are pruned at the top instead of descending to the minimum size. One longitude scale per region (cells widen towards the north); the new version only becomes active at the end |
| Track segments | Rebuilt per trip after a 20 s debounce (in-process); no separate worker |
| Maps | Leaflet replaced by MapLibre GL 6. Base style from OpenFreeMap (vector, OpenMapTiles schema); the MapLibre worker is served as a static asset (`/maplibre-gl-worker.mjs`) because Vite's dependency pre-bundling breaks its relative worker URL |
| Sea marks | Stored as `geometry(Point,3857)` with the raw OSM tags (jsonb) and the few attributes the style needs; tiles cut in SQL (`ST_AsMVT`) by a separate minimal-API project so the tile server can scale/cache independently of the API |
| Tenancy | Resolved per request from `Host`/`X-Forwarded-Host` against `ShipDomains`; no separate Angular build — the SPA asks `/api/site` on start and switches routes (`canMatch`) |
| Overpass | Public instances are often overloaded (406/504); `Overpass:Endpoints` lists mirrors tried in order, without the standard resilience handler (its 10 s attempt timeout killed every query) |
