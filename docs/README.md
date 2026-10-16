# Sailing Portal – Documentation

This folder is the specification for a **new sailing portal built with
ASP.NET Core (current .NET) and Angular**. It has two parts:

- **Part A – Legacy app (01–06):** what the existing single-ship application in
  `SMS/src/SMS/` does (2016, ASP.NET Core 1.0 RC2 + Knockout.js). None of its
  tooling builds on a current SDK; treat it as a specification, not as code to
  upgrade.
- **Part B – New product (07–12):** the multi-ship portal / social network
  for sailors that replaces it. It keeps all legacy functions and adds ships
  per user, trips with crew, posts and photos per trip day with visibility
  levels, shared costs with settlement, automatic sea routing from
  OpenStreetMap/OpenSeaMap data, and a trip overview map with the ship's track
  (GPS app, file, MarineTraffic AIS or calculated) and photos/posts placed
  where they were created.

## The legacy app in short

A private web app for the crew of the sailing yacht **"Kairos"**
(site "Lady-Helmsman.de", western Baltic). German UI. Interactive nautical map
with harbours and a hand-drawn waypoint graph for distance calculation,
electronic logbook, photo albums, harbour WiFi info, people, tasks, content
pages, iCal feed. Anonymous users read everything; logged-in users edit.

## The new product in short

Anyone registers, adds ships and keeps the ship logbook online. Skippers plan
trips and invite crew (also guests without account). During a trip every crew
member posts photos and blog posts that are grouped into the trip's days and
shown with that day's logbook entries; each item is private, crew-only or
public. The crew tracks shared costs and sees who owes whom. A global harbour
guide shows sea distances to the next ports, computed automatically (estimate
only, not for navigation).

## Reading order

| # | File | Content |
|---|------|---------|
| **A** | | **Legacy application** |
| 1 | [01-overview.md](01-overview.md) | Purpose, features, legacy tech stack, repository layout, runtime config |
| 2 | [02-domain-model.md](02-domain-model.md) | Legacy entities, fields, relations, DB schema (TPH), delete cascades |
| 3 | [03-backend-api.md](03-backend-api.md) | Legacy HTTP endpoints, auth, save semantics, image upload, iCal |
| 4 | [04-frontend.md](04-frontend.md) | Legacy screens, modals, map modes, interactions, German labels |
| 5 | [05-business-logic.md](05-business-logic.md) | Legacy algorithms: distances, route display, graph editing, logbook defaults, albums |
| 6 | [06-known-issues.md](06-known-issues.md) | Legacy bugs and dead code — do **not** copy these |
| **B** | | **New product** |
| 7 | [07-product-requirements.md](07-product-requirements.md) | Vision, roles, user stories with acceptance criteria, non-functional requirements, open questions |
| 8 | [08-domain-model-v2.md](08-domain-model-v2.md) | New entities, visibility rules, permission matrix, mapping from legacy |
| 9 | [09-costs-and-settlement.md](09-costs-and-settlement.md) | Expense splitting, rounding, balances, "who pays whom" algorithm, UI, API, tests |
| 10 | [10-sea-routing.md](10-sea-routing.md) | Automatic sea routing from OSM/OpenSeaMap data, harbour import, API, validation |
| 11 | [11-architecture-and-plan.md](11-architecture-and-plan.md) | Solution structure, technology decisions, Angular structure, API overview, implementation phases (0–14, incl. 8a/8b) with acceptance criteria |
| 12 | [12-trip-overview-and-tracking.md](12-trip-overview-and-tracking.md) | Trip overview map, ship tracks from GPS apps / files / MarineTraffic AIS / calculated legs, photos and posts on the map, track privacy |

## Instructions for the session implementing the new version

1. Read `07`–`12` fully; read `01`–`05` for the details of features that are
   carried over (they are referenced from `07` as "legacy").
2. `07` is the functional contract, `08` the data and permission contract,
   `09`, `10` and `12` specify costs, sea routing and tracking, `11` gives the
   architecture and the order of work.
3. Implement phase by phase (`11` §11.5); each phase lists acceptance
   criteria. Visibility and permission rules (`08` §8.3–8.4) must be enforced
   server-side and covered by tests before content is shown publicly.
4. Decisions taken are in `07` §7.5 (PostgreSQL; routing for European sea
   areas first, canals later via OpenSeaMap). Defaults for open questions are
   listed in `07` §7.6 — use them unless the
   owner decides otherwise, and record decisions in these docs.
5. UI is German first with English as a second language; legacy German labels
   are in `04`.
