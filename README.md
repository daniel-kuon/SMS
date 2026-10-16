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
| `web` | Angular app (dev server with proxy to the API) | dashboard → "Sailing portal" |
| `api` | ASP.NET Core API, OpenAPI at `/api/docs` | https://localhost:7150 |
| `postgres` / `sms` | PostgreSQL 17 + PostGIS + pgRouting (`pgrouting/pgrouting`) | volume `sms-postgres-data` |
| `mailpit` | catches confirmation / invitation mails | dashboard → "Mailpit inbox" |
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
src/Sms.Api              minimal-API endpoints, Identity cookie auth, ECB rates, seed
src/sms-web              Angular 21 + Material SPA (de/en)
tests/Sms.Domain.Tests   xunit (Microsoft.Testing.Platform) – run with `dotnet run --project tests/Sms.Domain.Tests`
infra/signoz             SigNoz / ClickHouse / collector configuration
docs/                    specification
```

Migrations: `dotnet ef migrations add <Name> --project src/Sms.Infrastructure` (applied on API start).
