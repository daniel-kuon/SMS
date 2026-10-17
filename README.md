# Sailing Portal (SMS v2)

Multi-ship sailing portal: ships, trips with crew, logbook, photo/blog timeline with
visibility levels, shared costs with settlement, harbour guide. ASP.NET Core 10 + Angular 21,
orchestrated by a .NET Aspire AppHost with full OpenTelemetry into SigNoz.

The specification lives in [`docs/`](docs/README.md) (legacy app 01–06, new product 07–12).

## One-click start

Prerequisites: .NET 10 SDK, Node 22, Docker Desktop.

```bash
dotnet run --project src/Sms.AppHost
```

(or open `Sms.slnx` in Rider / Visual Studio and run **Sms.AppHost**, or `aspire run`).

The AppHost starts everything and prints the Aspire dashboard URL:

| Resource | What | Where |
|----------|------|-------|
| `web` | Angular app (dev server with proxy to the API) | http://localhost:5173 |
| `api` | ASP.NET Core API, OpenAPI at `/api/docs` | https://localhost:7150 |
| `tiles` | sea-mark vector tile server (`/tiles/seamark/{z}/{x}/{y}.mvt`, TileJSON at `/tiles/seamark.json`) | http://localhost:5160 (proxied as `/tiles` by the web dev server) |
| `postgres` / `sms` | PostgreSQL 17 + PostGIS + pgRouting (`pgrouting/pgrouting`) | volume `sms-postgres-data` |
| `mailpit` | catches confirmation / invitation mails | http://localhost:8025 |
| `signoz` | SigNoz UI (traces, metrics, logs, APM) | http://localhost:3302 — login `admin@localhost.dev` / `SailingPortal2026!` |
| `signoz-otel-collector` | OTLP receiver (gRPC + HTTP) fed by API and browser | |
| `clickhouse`, `zookeeper-1` | SigNoz storage | volumes `sms-signoz-*` |

First start pulls images and bootstraps SigNoz (≈2 min). A seeded platform admin exists for the
portal itself: `admin@localhost.dev` / `SailingPortal2026!`. New users register in the UI; the
confirmation link arrives in Mailpit.

## Telemetry

* `Sms.ServiceDefaults` exports traces, metrics and logs **twice**: to the Aspire dashboard and to
  the SigNoz collector (`SIGNOZ_OTLP_ENDPOINT`, injected by the AppHost).
* The browser (`src/sms-web/src/app/core/telemetry.ts`) sends page-load and fetch spans through
  the dev proxy (`/otlp`) to the same collector, with `traceparent` propagation into the API, so a
  click in the UI shows up as one distributed trace (`sms-web` → `api` → PostgreSQL).
* SigNoz needs an organisation before its collector accepts data; the `signoz-setup` resource
  registers the admin account automatically on first start.

## Layout

```
src/Sms.AppHost          Aspire orchestration (SigNoz.cs = SigNoz stack)
src/Sms.ServiceDefaults  OpenTelemetry, health checks, service discovery
src/Sms.Domain           entities, enums, cost splitting + settlement, visibility rules
src/Sms.Infrastructure   EF Core (Npgsql + NetTopologySuite), migrations, media, mail, sanitizer
src/Sms.Api              minimal-API endpoints, Identity cookie auth, ECB rates, seed, CMS, sea-mark import
src/Sms.Tiles            sea-mark vector tiles (ST_AsMVT) as its own Aspire resource
src/sms-web              Angular 21 + Material SPA (de/en)
tests/Sms.Domain.Tests   xunit: costs, visibility, track parsers, CMS tree – `dotnet run --project tests/Sms.Domain.Tests`
tests/Sms.Api.Tests      integration tests for every endpoint group + authorization matrix + tiles, Testcontainers PostGIS – `dotnet run --project tests/Sms.Api.Tests` (AppHost stopped)
src/sms-web              `npx ng test --watch=false` (vitest, jsdom); `npm run e2e` (Playwright against the running AppHost)
infra/signoz             SigNoz / ClickHouse / collector configuration
docs/                    specification
```

Migrations: `dotnet ef migrations add <Name> --project src/Sms.Infrastructure` (applied on API start).

## Maps, sea marks and ship websites

* **Vector maps**: MapLibre GL with the OpenFreeMap *Liberty* style (`Map:StyleUrl`, no API key; attribution
  "OpenFreeMap © OpenMapTiles, data from OpenStreetMap"). Self-hosting: point `Map:StyleUrl` at your own style.
* **Sea marks**: Admin page → "Seezeichen (OpenSeaMap, Vektorkacheln)" loads OpenSeaMap data (OSM `seamark:*` tags) for a bounding
  box from Overpass (mirrors in `Overpass:Endpoints`) or from an uploaded Overpass-JSON file; `Sms.Tiles` renders
  them as vector tiles that the map overlays (buoys, beacons, lights, harbours, names from zoom 8 upwards).
* **Ship websites**: every ship gets `/ships/{id}/site` with owner-editable pages and sub pages. Assign a domain on
  the ship's site editor; requests arriving with that `Host` (behind a reverse proxy: `X-Forwarded-Host`) show only
  the ship's pages and latest logbook entries. In development, enter the host on the Admin page ("Domain-Vorschau")
  – the SPA then sends `X-Tenant-Host`.
* **Content pages**: admins edit the global pages (`/pages/{slug}`) as a tree shown in the navigation and footer.

## Sea routing and tracking

* **Routing graph**: Admin page → "Routing-Graph bauen" downloads the OSM land polygons once (≈900 MB into
  `media/cache`) and builds the pgRouting graph for `Routing:Region` in `appsettings.json` (default: western
  Baltic, ≈75 s). Until then distances are straight lines (`method: "straight-line"` in the API).
* **Tracking**: ship page → "Tracking-Geräte" creates a device token with ready-made URLs for Traccar Client /
  OsmAnd / GPSLogger (OsmAnd protocol), OwnTracks (HTTP mode) and a JSON batch endpoint. GPX/KML/NMEA files can be
  imported on the trip overview. Public viewers see tracks delayed (`Trip.PublicPositionDelayMinutes`) and never
  inside privacy zones.
