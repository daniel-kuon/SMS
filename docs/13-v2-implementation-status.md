# 13. v2 Implementation Status and Decisions

State of the rewrite on branch `v2` (2026-10-16). Phases refer to `11` §11.5.

## Done

| Phase | Status | Notes |
|-------|--------|-------|
| 0 Scaffolding | ✅ | Aspire AppHost (one-click), SigNoz stack, PostGIS/pgRouting image, Mailpit, Angular shell, i18n de/en, OpenAPI (`/api/docs`), domain tests |
| 1 Accounts | ✅ | Register + e-mail confirmation (Mailpit), login/logout (cookie), password reset, profile, settings, data export, account deletion with anonymisation, rate limiting |
| 2 Ships | ✅ | CRUD, owner/skipper members, invitations by user search or e-mail link, ship page with stats, photo |
| 3 Harbours | ✅ (OSM import for small areas) | PostGIS catalogue, bbox map with clustering + OpenSeaMap, detail panel, WiFi (passwords for registered users), reviews, revisions + revert, admin Overpass import + merge. **Distances are straight lines** (see "Open") |
| 4 Trips | ✅ | CRUD, crew (users, guests, "Logbuch schreiben", from/to), invitations (user, e-mail, link), accept/decline, planner legs, iCal feeds per user/ship |
| 5 Logbook | ✅ | Paged/sorted ship logbook, entry dialog with legacy + new fields, defaults from previous entry and trip crew, entries without trip, visibility-filtered views |
| 6 Posts / photos / timeline | ✅ | Upload pipeline (ImageSharp thumb/preview/original, EXIF day + GPS, EXIF stripped), day timeline, entry detail with day content, posts with photos, central visibility filter (`Viewer`), authorised media delivery |
| 7 Costs | ✅ | Split modes, ECB rates of the payment date (daily job + history bootstrap), balances, greedy transfers, settlements report/confirm, lock, CSV export |
| 8a Trip overview | partial | Overview map with legs (straight lines between logbook harbours), harbour stops, photo/post markers with clustering, stats, day list hover |
| 10 Tasks / pages | ✅ | Trip and ship task trees, admin-editable content pages in footer |
| 11 Reviews / stats | ✅ | Harbour ratings, ship statistics |

## Open (follow-ups)

- **8 Sea routing graph** (`10`): land polygons → quadtree graph → pgRouting. Today `/api/routing/*` returns straight-line distances with `method: "straight-line"`; the API shape already matches `10` §10.4 so the UI will not change.
- **8b Tracking** (`12`): GPS/AIS ingest, track segments, live position, privacy zones.
- **9 Social**: follows, feed, comments, likes, notifications, moderation.
- **12 PWA/offline**, **13 legacy import**, **14 canals**.
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
| Images | ImageSharp **3.1** (Six Labors Split License) — 4.x requires a paid license for Release builds |
| Mail | `System.Net.Mail.SmtpClient` to Mailpit; switch to MailKit for a real provider |
| SigNoz | Pinned `signoz/signoz:v0.116.1` + collector `v0.144.2` (last upstream docker-compose before Foundry); a `signoz-setup` container registers the admin org on first start because the collector replaces its pipelines with `nop` until an org exists |
| Angular | 21 (Node 22.14 on the dev machine is below Angular 22's minimum) |
| Calendar tokens | Data-protection-encrypted ids instead of stored secrets |
