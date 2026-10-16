# 7. Rewrite Plan: ASP.NET Core + Angular

This is a recommendation. Versions: use the **current LTS .NET** (e.g. .NET 10)
and the **current Angular major** at the time of implementation.

## 7.1 Target architecture

```
/                         (repo root – keep legacy code under SMS/ until the rewrite is done)
  src/
    Sms.Api/              ASP.NET Core Web API (controllers), auth, static hosting of the Angular build
    Sms.Domain/           entities, enums, domain services (distance calculation)
    Sms.Infrastructure/   EF Core DbContext + migrations, file storage, thumbnail generation, iCal
    sms-web/              Angular app (standalone components, signals, new control flow)
  tests/
    Sms.Api.Tests/        xUnit + WebApplicationFactory (+ Testcontainers or SQLite)
    Sms.Domain.Tests/     unit tests (e.g. Dijkstra)
  tools/
    Sms.LegacyImport/     one-off console app: legacy DB + files → new DB + storage
  docs/                   this documentation
```

### Backend choices
- ASP.NET Core Web API, JSON **camelCase**, DTOs (never expose EF entities),
  `ProblemDetails` errors, OpenAPI document (generate the Angular client from
  it or hand-write typed services).
- EF Core with SQL Server (same as legacy → simplest data migration;
  PostgreSQL is fine too). Apply migrations on start-up for the single DbContext.
  One DbContext that includes Identity (`IdentityDbContext<AppUser>`).
- Auth: ASP.NET Core Identity with **cookie authentication**, same-origin SPA
  (Angular served by the API in production, dev proxy in development).
  Endpoints: login, logout, `GET /api/auth/me`. Return **401/403**, not redirects.
  Antiforgery header for state-changing requests (or SameSite=Strict cookies).
  User management only for authenticated users (no open registration).
- Images: ImageSharp; store originals + thumbnails (e.g. 400 px) + optional
  preview (e.g. 1600 px) under a configurable storage root (outside `wwwroot`)
  served by an endpoint or static file middleware; random file names (GUID),
  original name kept as metadata; size/type limits; EXIF auto-orient.
- HTML sanitisation of all rich-text fields on save (e.g. `Ganss.Xss.HtmlSanitizer`).
- Configuration: connection string, storage path, tile-layer URLs/token,
  home-port harbour id, time zone `Europe/Berlin`.

### Frontend choices
- Angular, standalone components, signals, `HttpClient` with typed services,
  reactive forms, router (deep links such as `/harbours/:id`, `/logbook`,
  `/logbook/:id` instead of only modals).
- UI kit: Angular Material (dialogs, tables with sort/paginator, datepicker,
  chips/multi-select, side-nav for sidebars) **or** Bootstrap 5 + ng-bootstrap
  to stay close to the old look – pick one and use it consistently.
- Map: **Leaflet** directly (wrapped in a `MapComponent`/`MapService`), base
  layer OpenStreetMap or Mapbox Styles API, overlay OpenSeaMap seamarks
  (`https://tiles.openseamap.org/seamark/{z}/{x}/{y}.png`), permanent
  tooltips for harbour names (replaces Leaflet.label), context menus via a
  small custom menu or `leaflet-contextmenu`.
- Rich text: `@tinymce/tinymce-angular` (self-hosted, GPL) or `ngx-quill`.
- Gallery: PhotoSwipe 5; upload: drag-and-drop zone component with progress.
- Locale `de-DE` (`LOCALE_ID`, date pipe formats), all texts German.

## 7.2 API design (suggested)

| Area | Endpoints |
|------|-----------|
| Auth | `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`, `GET/POST/DELETE /api/users` (auth), `POST /api/auth/change-password` |
| Map | `GET /api/map` → `{ harbours:[{id,name,lat,lng}], waypoints:[{id,name,lat,lng,number}], connections:[{a,b}] }` (waypoints/connections may be returned only to signed-in users) |
| Harbours | `GET /api/harbours`, `GET /api/harbours/{id}` (incl. content, website, wifis, album images), `POST`, `PUT /{id}`, `DELETE /{id}` (409 if used by logbook) |
| Distances | `GET /api/harbours/{id}/distances` → `[{harbourId, name, distanceNm, path:[{lat,lng}]}]` |
| Waypoints | `POST /api/waypoints`, `PUT /api/waypoints/{id}`, `PATCH /api/waypoints/{id}/position`, `DELETE /api/waypoints/{id}` (works for harbours' position too) |
| Connections | `POST /api/connections {a,b}`, `DELETE /api/connections/{a}/{b}`, `POST /api/connections/split {a,b,lat,lng}` → new waypoint |
| Logbook | `GET /api/logbook?page&pageSize&sort&dir`, `GET /api/logbook/{id}`, `GET /api/logbook/new-defaults`, `POST`, `PUT /{id}`, `DELETE /{id}` |
| People | `GET/POST/PUT/DELETE /api/people` |
| Jobs | `GET /api/jobs` (tree), `POST`, `PUT /{id}`, `DELETE /{id}` (deletes subtree) |
| WiFis | `POST /api/harbours/{id}/wifis`, `PUT /api/wifis/{id}`, `DELETE /api/wifis/{id}` |
| Content pages | `GET /api/pages` (id,title), `GET /api/pages/{id}`, `POST`, `PUT`, `DELETE` |
| Albums/images | `GET /api/albums/{id}/images`, `POST /api/albums/{id}/images` (multipart, multiple files), `DELETE /api/images/{id}`, `GET /media/{...}` |
| Calendar | `GET /api/calendar/trips.ics` |
| Trips (optional) | `GET/POST/PUT/DELETE /api/trips` with ordered tacks |

Reads anonymous, writes `[Authorize]`.

## 7.3 Data model changes (suggested)

Keep the entities of `02` with these adjustments:
- Keep `Waypoint`/`Harbour` TPH (connections reference both).
- `LogBookEntry` as its own table (do not share TPH with Trip/Tack unless
  trips are implemented); `Crew` → `LogBookEntryCrew` (and `TackCrew` later).
- `WindSpeed` as string (e.g. "4 Bft") – confirm with owner; `WindDirection`
  as enum of the 16 compass points (stored as string).
- Dates as `DateTimeOffset`.
- `Image`: add `FileName`, `ContentType`, `ThumbnailPath`, `SortIndex`, `Caption`, `TakenAt`.
- Album: 1:1 owned by Harbour or LogBookEntry (nullable FK), created lazily on
  first upload instead of always.
- Drop or postpone: `Comment`, `Location`, `Address`, `Rating`.
- Explicit delete behaviour: Restrict for Harbour ← LogBookEntry,
  Cascade for Harbour → Wifis/Album/Connections, Cascade Job → SubJobs,
  SetNull Job.AssignedTo on person delete, Cascade Person → crew links only.

## 7.4 Implementation phases and acceptance criteria

**Phase 0 – Scaffolding**
- Solution + projects as in §7.1, Angular app with dev proxy to the API,
  `docker-compose` (DB), `README` with run instructions, CI build (dotnet build/test, ng build/test).
- ✅ `dotnet run` + `ng serve` show an empty Angular shell talking to `/api/health`.

**Phase 1 – Domain, persistence, auth**
- Entities, DbContext, initial migration, seed (one user, a few harbours, waypoints, connections).
- Identity cookie login/logout/me; Angular login page, auth state service, route guard, HTTP interceptor handling 401.
- ✅ Integration tests: anonymous GET ok, anonymous POST → 401, logged-in POST ok.

**Phase 2 – Map (view mode) + harbour details**
- Map component (layers, start view, harbour markers with name tooltips).
- Navbar: brand "Kairos", "Logbuch", content-page links, Admin menu (signed in), login/logout.
- Harbour detail side panel: name, album strip, content, WiFi list (+ detail/edit/delete dialogs).
- Harbour create/edit/delete dialog incl. "Aktuelle Position verwenden".
- ✅ Clicking a harbour opens the side panel; closing it deselects; layout responsive (panel full width on phones).

**Phase 3 – Distances and routes**
- Dijkstra in `Sms.Domain` (unit-tested, nautical miles, one decimal), distances endpoint.
- Distance table (sortable, paged, "Route zeigen"); hover-route with "`<n> sm`" label, fit bounds and restore.
- ✅ Unit test on a small graph; unreachable harbours omitted; results match the legacy display rounding.

**Phase 4 – Logbook**
- Logbook list (server-side paging/sorting; columns of `04` §4.6, column chooser), detail dialog, create/edit/delete dialog with all fields, validation, crew multi-select with inline "Neue Person", harbour select with inline "Neuer Hafen".
- New-entry defaults (`05` §5.4) from `GET /api/logbook/new-defaults`; home port configurable.
- ✅ Creating an entry after an existing one pre-fills start harbour, engine hours, log, crew.

**Phase 5 – Photos**
- Upload endpoint (multi-file), thumbnails, album strip, drag-and-drop zone that appears when files are dragged over a page that shows an album (signed-in only), PhotoSwipe gallery, image delete.
- ✅ Upload JPEG/PNG → thumbnail visible immediately; gallery opens at clicked image; anonymous users cannot upload.

**Phase 6 – People, tasks, content pages**
- People list + CRUD; tasks tree with sub-tasks, assignee, due date, done; content pages CRUD with nav links.
- ✅ Deleting a person keeps logbook entries (crew link removed) and unassigns tasks.

**Phase 7 – Admin map editing**
- Toggle "Karte Admin"/"Karte Normal"; show waypoints + connections; drag to move (save on drag end); context menus; draw connections (double-click start, click map = new waypoint, click marker = finish, Esc = cancel); split by midpoint handle; delete waypoint.
- ✅ All operations persist and survive reload; distances reflect graph edits.

**Phase 8 – iCal feed** (dummy trips or, if trips are dropped, decide with owner)
- ✅ Feed validates (e.g. with an iCal validator), UTC times, stable UIDs.

**Phase 9 – Legacy data import**
- `Sms.LegacyImport`: read legacy tables (`Waypoint`, `TackBase` where `Discriminator='LogBookEntry'`, `Crew`, `Person`, `Job`, `Wifi`, `ContentPage`, `Album`, `AlbumImage`, `Image`, `WaypointConnection`, `AspNetUsers`) and write the new schema; copy files from `wwwroot/images/upload/**` and regenerate thumbnails; keep ids where possible so connections/crew map 1:1; sanitise HTML.
- ✅ Row counts match; spot-check a harbour, a logbook entry with photos, the waypoint graph distances.

**Phase 10 (optional) – Trip planning**
- Only if the owner wants it: trips with ordered legs, route computed via the distance service, drag-to-reorder legs, sum of distances (`05` §5.6).

## 7.5 Open questions for the owner
1. Keep trip planning / dummy-trip calendar, or drop them?
2. Should anonymous visitors see everything (as today) or only selected content (e.g. hide WiFi passwords, logbook)?
3. Wind speed: free text, Beaufort, or knots?
4. Home port: is "Lippe" still the harbour that resets the crew?
5. Keep Points-of-interest (`Location`/`Address`) and comments/ratings on the roadmap?
6. Hosting target (IIS / Linux / container) and database (SQL Server vs PostgreSQL)?
7. Map provider (OpenStreetMap vs Mapbox account).
