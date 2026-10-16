# 13. v2 Implementation Status and Decisions

State of the rewrite on branch `v2` (2026-10-16). Phases refer to `11` §11.5.

## Done

| Phase | Status | Notes |
|-------|--------|-------|
| 0 Scaffolding | ✅ | Aspire AppHost (one-click), SigNoz stack, PostGIS/pgRouting image, Mailpit, Angular shell, i18n de/en, OpenAPI (`/api/docs`), domain tests |
| 1 Accounts | ✅ | Register + e-mail confirmation (Mailpit), login/logout (cookie), password reset, profile, settings, data export, account deletion with anonymisation, rate limiting |
| 2 Ships | ✅ | CRUD, owner/skipper members, invitations by user search or e-mail link, ship page with stats, photo |
| 3 Harbours | ✅ (OSM import for small areas) | PostGIS catalogue, bbox map with clustering + OpenSeaMap, detail panel, WiFi (passwords for registered users), reviews, revisions + revert, admin Overpass import + merge |
| 4 Trips | ✅ | CRUD, crew (users, guests, "Logbuch schreiben", from/to), invitations (user, e-mail, link), accept/decline, planner legs, iCal feeds per user/ship |
| 5 Logbook | ✅ | Paged/sorted ship logbook, entry dialog with legacy + new fields, defaults from previous entry and trip crew, entries without trip, visibility-filtered views |
| 6 Posts / photos / timeline | ✅ | Upload pipeline (ImageSharp thumb/preview/original, EXIF day + GPS, EXIF stripped), day timeline, entry detail with day content, posts with photos, central visibility filter (`Viewer`), authorised media delivery |
| 7 Costs | ✅ | Split modes, ECB rates of the payment date (daily job + history bootstrap), balances, greedy transfers, settlements report/confirm, lock, CSV export |
| 8 Sea routing | ✅ (region-configurable) | `RoutingGraphBuilder`: OSM land polygons (downloaded once, ≈900 MB) → adaptive quadtree over water → pgRouting tables; `SeaRouter`: A* routes with string-pulling smoothing, driving-distance nearby ports, cache; admin rebuild/status/corrections. Default region western Baltic (build ≈75 s, 72k nodes); Europe = set `Routing:Region` and wait hours |
| 8a Trip overview | ✅ | Overview map with recorded (solid) or calculated/straight (dashed) legs, harbour stops, photo/post markers with clustering, stats, day list hover, last position |
| 8b Tracking | ✅ (no AIS provider yet) | Devices with one-time tokens, OsmAnd/Traccar, OwnTracks and JSON ingest, GPX/KML/NMEA import, cleaning, per-day segments with source priority, media/post placement on the track, public delay, privacy zones, source override, stop detection → logbook suggestions |
| 12 PWA / offline | ✅ (basic) | Installable (service worker + manifest), cached reference data, offline upload queue (IndexedDB, retried when online), autosaved post drafts. No background sync API |
| 9 Social | ✅ | Comments/likes on posts, photos, logbook entries, trips, reviews (target visibility enforced), follows, personal feed, public explore, in-app notifications (+ e-mail for invitations/comments/settlements), reports, admin hide/dismiss, user blocks |
| 10 Tasks / pages | ✅ | Trip and ship task trees, admin-editable content pages in footer |
| 11 Reviews / stats | ✅ | Harbour ratings, ship statistics |

## Open (follow-ups)

- **AIS provider** (`12` §12.1 C): `IAisPositionProvider` + MarineTraffic/aisstream polling; the track pipeline already accepts `Ais` points.
- **Track playback** (`12` §12.3, optional animation).
- **Europe-wide routing build**: run with `Routing:Region` = −32…45 / 27…72 on a machine with enough RAM (the builder keeps the region's land polygons and cells in memory); tile-wise building is the next step if that is too large.
- **13 legacy import** (needs the old SQL Server database), **14 canals**, offline drafts for logbook entries.
- Europe-wide OSM harbour import (osmium pipeline); the Overpass import works for bounding boxes.
- PDF export of costs (CSV exists).
- Integration/authorization-matrix tests (`08` §8.4) against a Testcontainers database.

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
| Track segments | Rebuilt per trip after a 20 s debounce (in-process); no separate worker |
