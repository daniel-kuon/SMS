# SMS / "Kairos" – Documentation of the Legacy Application

This folder documents the **existing** application in this repository so that a
new implementation can be built with **ASP.NET Core (current .NET)** and
**Angular** without having to reverse-engineer the old code again.

The legacy code lives in `SMS/src/SMS/` and was written in 2016 against
ASP.NET Core **1.0 RC2** (`project.json`/`.xproj`), Entity Framework Core RC2,
SQL Server, Razor views + BootstrapTagHelpers, and a TypeScript/Knockout.js
single-page front end built around a Leaflet/Mapbox map. None of that tooling
builds on a current SDK; treat the old code as a **specification**, not as a
base to upgrade.

## What the app is

A private web app for the crew of a sailing yacht (brand name in the navbar:
**"Kairos"**, site title **"Lady-Helmsman.de"**, sailing area: western Baltic /
Kiel Bay). The UI is entirely **German**. It provides:

- An interactive nautical map (Mapbox streets + OpenSeaMap seamarks) with
  **harbours** and a graph of **waypoints/connections** (navigable routes).
- **Distance calculation** (shortest path over the waypoint graph, in nautical
  miles) from a selected harbour to all other harbours, and route highlighting.
- An electronic **logbook** (log book entries: from/to harbour, times, engine
  hours, log reading, wind, crew, notes, photos).
- **Photo albums** per harbour / logbook entry with drag-and-drop upload,
  thumbnails and a PhotoSwipe gallery.
- **WiFi** information per harbour.
- **People** (crew members), hierarchical **tasks/jobs**, free **content pages**.
- An admin mode for editing the waypoint graph directly on the map.
- An iCal feed of "dummy" trips (calendar of planned trips).
- Login via ASP.NET Identity; anonymous users can read everything, logged-in
  users can edit.

## Reading order

| # | File | Content |
|---|------|---------|
| 1 | [01-overview.md](01-overview.md) | Purpose, features, legacy tech stack, repository layout, runtime config |
| 2 | [02-domain-model.md](02-domain-model.md) | All entities, fields, types, relations, DB schema (TPH), delete cascades, ER diagram |
| 3 | [03-backend-api.md](03-backend-api.md) | Every HTTP endpoint, auth rules, save semantics, image upload, iCal, Identity |
| 4 | [04-frontend.md](04-frontend.md) | Page layout, every screen/modal/sidebar, map modes, interactions, German labels |
| 5 | [05-business-logic.md](05-business-logic.md) | Algorithms: distance/shortest path, route display, waypoint-graph editing, logbook defaults, album context, upload flow |
| 6 | [06-known-issues.md](06-known-issues.md) | Bugs, dead code, half-finished features and inconsistencies — do **not** copy these |
| 7 | [07-rewrite-plan.md](07-rewrite-plan.md) | Recommended target architecture for .NET + Angular and a step-by-step implementation plan with acceptance criteria |

## Instructions for the session implementing the new version

1. Read all files in this folder first (they are short enough to read fully).
2. Treat `02` + `03` as the functional contract for the backend and `04` + `05`
   as the functional contract for the front end.
3. Follow the phases in `07-rewrite-plan.md`. Each phase lists acceptance
   criteria that can be checked independently.
4. When something here is ambiguous, the source of truth is the legacy code;
   every section names the files it was derived from.
5. Keep the UI in German (labels are listed in `04-frontend.md`), unless the
   owner decides otherwise.
