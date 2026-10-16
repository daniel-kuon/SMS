# 11. Architecture and Implementation Plan (ASP.NET Core + Angular)

Scope: the multi-ship sailing portal of `07`, with the data model of `08`,
costs of `09` and sea routing of `10`. Use the **current LTS .NET** (e.g.
.NET 10) and the **current Angular major** at implementation time.

## 11.1 Solution structure

```
/                               repo root (legacy app stays in SMS/ until phase 13 is done)
  src/
    Sms.Api/                    ASP.NET Core Web API: controllers, auth, authorization handlers,
                                SignalR hub (notifications), serves the Angular build in production
    Sms.Application/            use cases / services (trips, logbook, posts, costs, feed),
                                DTOs, validation (FluentValidation), visibility filter
    Sms.Domain/                 entities, enums, value objects (Money, Visibility),
                                pure logic: cost splitting & settlement (09)
    Sms.Infrastructure/         EF Core DbContext + migrations, Identity, media storage
                                (local FS / S3 / Azure Blob behind IMediaStorage),
                                image processing (ImageSharp), e-mail (SMTP), iCal, OSM import
    Sms.Routing/                routing graph model, A*/Dijkstra, smoothing, graph loader (10)
    Sms.Routing.Builder/        CLI / background job that builds the routing graph
    Sms.Worker/                 background jobs: media processing, routing rebuild,
                                OSM harbour import, notification e-mails (or hosted services in Sms.Api)
    sms-web/                    Angular app
  tools/
    Sms.LegacyImport/           imports the legacy "Kairos" database + files (phase 13)
  tests/
    Sms.Domain.Tests/           costs, visibility, routing algorithms
    Sms.Api.Tests/              integration tests (WebApplicationFactory + Testcontainers DB),
                                authorization matrix tests (08 §8.4)
    sms-web e2e/                Playwright end-to-end tests
  docs/
  docker-compose.yml            database, (optional) MinIO for S3-compatible storage, MailHog/Mailpit
```

## 11.2 Technology decisions

| Concern | Decision | Notes |
|---------|----------|-------|
| API style | REST controllers, camelCase JSON, `ProblemDetails`, OpenAPI | generate the Angular API client from OpenAPI (e.g. openapi-generator / orval) |
| Database | PostgreSQL + PostGIS **or** SQL Server (`geography`) | both work with EF Core + NetTopologySuite; PostgreSQL recommended for a new multi-tenant service (and pgRouting option, `10` §10.2) |
| Auth | ASP.NET Core Identity, cookie auth for the same-origin SPA, e-mail confirmation, password reset, antiforgery header | optional external logins later; return 401/403, never redirects |
| Authorization | policy + resource handlers, central visibility query filter | `08` §8.3/§8.4 |
| Media | `IMediaStorage` abstraction, ImageSharp variants (thumb 400 px, preview 1600 px), EXIF read (capture time, GPS) + strip | background processing, signed URLs or authorized media endpoint for non-public files |
| Rich text | sanitised HTML (Ganss.Xss HtmlSanitizer) | editor: TinyMCE Angular (self-hosted) or ngx-quill |
| Real-time | SignalR for notifications (optional, polling acceptable first) | |
| Background jobs | hosted services or Hangfire/Quartz | media, e-mail, OSM import, routing rebuild |
| Front end | Angular standalone components, signals, router, reactive forms, Angular Material (or Bootstrap 5 + ng-bootstrap), `@angular/localize` or Transloco (de, en), PWA (`@angular/pwa`) | |
| Map | Leaflet (+ marker clustering), OSM base layer, OpenSeaMap overlay | `04` §4.4, `10` §10.5 |
| Gallery / upload | PhotoSwipe 5; custom drag-and-drop component with progress; offline upload queue (IndexedDB + service worker) | |
| Observability | structured logging (Serilog), health checks, OpenTelemetry | |
| Deployment | Docker images (API incl. SPA, worker), EU hosting | |

## 11.3 Front-end structure (Angular)

```
sms-web/src/app/
  core/            auth service & guard, HTTP interceptors (401, antiforgery), i18n, layout shell (navbar, footer)
  shared/          components: data-table (legacy Pager 04 §4.10), visibility-picker, user-avatar,
                   rich-text-editor/viewer, photo-strip, gallery, upload-dropzone, harbour-select,
                   crew-select (incl. "Neue Person…" pattern 04 §4.8), confirm-dialog, money-input
  features/
    auth/          login, register, confirm-email, reset-password
    profile/       my profile, public profile, settings, data export / delete account
    feed/          personal feed, public explore page
    ships/         ship list, ship page, ship edit, members & invitations, ship logbook
    trips/         trip page (timeline / logbook / photos / costs / tasks tabs), trip edit, crew & invitations, planner
    logbook/       entry edit dialog (legacy fields 04 §4.6), entry detail with day content
    posts/         post editor, post detail with comments/likes
    costs/         expenses, balances, settlement (09 §9.4)
    harbours/      harbour guide map, harbour detail panel, edit, WiFi, reviews, nearby ports
    admin/         content pages, moderation, harbour merge, routing corrections & rebuild
    pages/         content page viewer (Impressum, Datenschutz, …)
```

Main routes: `/` (public explore map + feed), `/feed`, `/ships/:id`,
`/ships/:id/logbook`, `/trips/:id` (`/timeline`, `/logbook`, `/photos`,
`/costs`, `/tasks`), `/trips/:id/days/:date`, `/posts/:id`, `/harbours`,
`/harbours/:id`, `/u/:userName`, `/settings`, `/admin/...`, `/p/:slug`.

## 11.4 Key API endpoints (overview)

| Area | Endpoints |
|------|-----------|
| Auth | `POST /api/auth/register`, `/login`, `/logout`, `/confirm-email`, `/forgot-password`, `/reset-password`; `GET /api/auth/me` |
| Profiles | `GET /api/users/{userName}`, `PUT /api/me/profile`, `GET /api/me/export`, `DELETE /api/me` |
| Social | `POST/DELETE /api/follows`, `GET /api/feed?cursor`, `GET /api/explore`, `GET/POST /api/{targetType}/{id}/comments`, `PUT/DELETE /api/{targetType}/{id}/reactions`, `GET /api/notifications`, `POST /api/reports` |
| Ships | `GET/POST /api/ships`, `GET/PUT/DELETE /api/ships/{id}`, `GET/POST/PUT/DELETE /api/ships/{id}/members`, `POST /api/ships/{id}/invitations`, `POST /api/invitations/{token}/accept` |
| Trips | `GET /api/ships/{id}/trips`, `POST /api/ships/{id}/trips`, `GET/PUT/DELETE /api/trips/{id}`, crew `GET/POST/PUT/DELETE /api/trips/{id}/crew`, `POST /api/trips/{id}/invitations`, planner `GET/PUT /api/trips/{id}/legs`, `GET /api/trips/{id}/timeline?from&to` (days with entries/posts/photos visible to caller) |
| Logbook | `GET /api/ships/{id}/logbook?trip&year&page&sort`, `GET /api/logbook/{id}` (incl. day content), `GET /api/ships/{id}/logbook/new-defaults?trip=`, `POST /api/ships/{id}/logbook`, `PUT/DELETE /api/logbook/{id}` |
| Posts | `POST /api/trips/{id}/posts`, `GET/PUT/DELETE /api/posts/{id}` |
| Media | `POST /api/trips/{id}/media` (multipart, multiple), `PUT/DELETE /api/media/{id}`, `GET /api/media/{id}/{variant}` (authorized) |
| Costs | `09` §9.5 |
| Harbours | `GET /api/harbours?bbox=&q=`, `GET/POST/PUT /api/harbours/{id}`, `GET /api/harbours/{id}/revisions`, WiFi `POST /api/harbours/{id}/wifis`, `PUT/DELETE /api/wifis/{id}`, reviews `GET/POST /api/harbours/{id}/reviews` |
| Routing | `10` §10.4 |
| Tasks | `GET/POST /api/trips/{id}/tasks`, `GET/POST /api/ships/{id}/tasks`, `PUT/DELETE /api/tasks/{id}` |
| Calendar | `GET /api/calendar/users/{token}.ics`, `GET /api/calendar/ships/{token}.ics` (secret token per feed) |
| Content pages | `GET /api/pages`, `GET /api/pages/{slug}`, admin CRUD |
| Admin | moderation, user management, harbour merge, OSM import trigger, routing corrections/rebuild |

## 11.5 Implementation phases

Each phase ends with working, tested software. Phases 1–8 form the MVP;
the order can change, but visibility rules (phase 6) must be in place before
any content is shown publicly.

**Phase 0 – Scaffolding**
Solution and projects (§11.1), Angular shell with layout, docker-compose (DB,
mail catcher, optional MinIO), CI (build, test, lint), OpenAPI client
generation, i18n setup (de default, en).
✅ `docker compose up` + `dotnet run` + `ng serve` show the shell; CI green.

**Phase 1 – Accounts and profiles** (`07` A1–A4)
Registration with e-mail confirmation, login/logout, password reset, profile
edit + public profile page, settings. Rate limiting on auth endpoints.
✅ Integration tests for the full auth flow; unconfirmed users cannot create content.

**Phase 2 – Ships and members** (`07` B1–B2)
Ship CRUD, owner/skipper roles, invitations by e-mail/link, ship page (basic).
✅ Authorization tests: only owners manage members; last owner cannot leave.

**Phase 3 – Harbour catalogue and map** (`07` G1, G3; legacy `04` §4.4–4.5)
Harbour entity with PostGIS/geography, OSM import job (`10` §10.3) for the
launch region, harbour map with clustering + OpenSeaMap overlay, harbour
detail panel (description, WiFi with registered-only passwords, photos
placeholder), create/edit with revision history, admin merge. Straight-line
distances as temporary fallback.
✅ Import creates harbours for the launch region; duplicates merged; map
loads only harbours inside the visible bbox.

**Phase 4 – Trips and crew** (`07` C1, C2, C4 basic, C5)
Trip CRUD, crew with roles, guest crew, invitations/accept/decline, trip page
header and crew list, iCal feeds.
✅ Invited-but-not-accepted users see no crew-only data; guest crew can be
linked to a registered user later.

**Phase 5 – Logbook** (`07` D1–D4; legacy `04` §4.6, `05` §5.4)
Ship logbook list (server paging/sorting, legacy columns + column chooser),
entry dialog with all legacy fields + new ones, defaults from previous entry
of the ship and crew from the trip, GPX upload (parse length/track), entries
without trip.
✅ Defaults test; logbook list sorted by date desc by default; GPX track shown on the trip map.

**Phase 6 – Posts, photos, timeline, visibility** (`07` E1–E3, `08` §8.3)
Media upload pipeline (variants, EXIF day/location, EXIF stripping), photo
strip + gallery, drag-and-drop zone (signed-in crew only), blog post editor,
day timeline per trip, logbook entry detail showing the day's posts/photos,
central visibility filter for every query, authorized media delivery.
✅ Authorization matrix tests (`08` §8.4) for posts, media, logbook entries:
private/crew/public × anonymous/registered/crew/owner/author; public items of
a crew-only trip are not visible publicly.

**Phase 7 – Costs** (`07` F1–F4, `09`)
Expenses with split modes and currencies, balances, suggested transfers,
settlement report/confirm, lock, CSV/PDF export, cost tab UI.
✅ Property-based tests of `09` §9.6; worked example of `09` §9.3 as a test.

**Phase 8 – Sea routing** (`07` G4–G5, C3; `10`)
Graph builder for a small test region first (Kieler Förde), then the launch
region; in-memory routing service; nearby-ports and route endpoints; UI in
harbour panel (distance table, hover route with "`<n> sm`"), "Nächste Häfen
von hier/meiner Position", trip planner legs with distance/ETA; admin
corrections and rebuild.
✅ Golden distance tests (`10` §10.6) within ±10 %; performance targets met.

**Phase 9 – Social layer** (`07` E4–E5)
Follows, personal feed and public explore page, comments, likes,
notifications (in-app + e-mail digest), reports and moderation tools.
✅ Feed never contains items the viewer cannot see (tested); notification
preferences respected.

**Phase 10 – Tasks and content pages** (`07` C6, H1; legacy jobs `04` §4.6)
Trip and ship task trees with assignees; admin content pages (Impressum,
Datenschutz, Hilfe) in footer.
✅ Legacy task behaviour (sub-tasks, done, due date) reproduced.

**Phase 11 – Harbour reviews and statistics** (`07` G2, B3)
Ratings/reviews, ship statistics (nm, engine hours, nights, harbours), trip
map with planned vs. sailed route.

**Phase 12 – PWA / offline** (`07` H2)
Installable app, offline drafts for posts/logbook entries, queued photo
uploads with retry, background sync.
✅ Creating a post and uploading photos in airplane mode syncs after reconnect.

**Phase 13 – Legacy import** (`07` H3, mapping `08` §8.5)
`Sms.LegacyImport` reads the old SQL Server database and files:
- create (or link) the owner's account and the ship "Kairos" (owner = that user);
- harbours → catalogue (`Source = Legacy`), merge with OSM duplicates (≤ 300 m, similar name), WiFis attached;
- persons → guest crew; logbook entries → ship logbook entries;
- reconstruct trips: group consecutive entries into a trip whenever the crew
  changes or a voyage starts from the home port "Lippe" (legacy `05` §5.4) —
  let the owner review/adjust the proposed grouping before committing;
- album images → `Media` (copy originals, regenerate variants), attached to
  the logbook entry (day = entry date) or harbour;
- jobs → ship tasks; content pages → content pages; dummy trips → planned trips;
- fix legacy issues on the way (`06`: sanitise HTML, WindSpeed as string, time zones).
✅ Row counts and spot checks match; legacy distances serve as routing test fixtures.

## 11.6 Definition of done (every phase)
- Server-side validation and authorization for every endpoint; tests for the
  permission rules touched.
- German and English texts; responsive on phone width.
- No N+1 queries on list endpoints; paging for all lists.
- OpenAPI updated, Angular client regenerated.
- Docs in `docs/` updated if behaviour differs from this specification.
