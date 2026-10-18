# 14 – Törn section on the map (design)

Requested 2026-10-18: the whole Törn (trip) section becomes map-centred, larger than the harbour popup map, with the content next to or below the map depending on the screen orientation; the map always brings the relevant view into frame; planning and logbook editing happen on the map.

## Layout

- Route `/trips/:id` (and its tabs: overview, plan, logbook, costs, tasks, crew) renders one full-height page: a map and a content column.
- Landscape (viewport wider than tall, ≥ 900 px): map on the left (≈ 55 % of the width, min 480 px), content column on the right, scrollable on its own.
- Portrait or narrow: content on top, map below, the map at least 45 % of the viewport height, the content scrollable above it; a drag handle is nice but not required.
- The map is the shared `createMap` map (sea marks, POIs, depth toggle, weather overlays, ship context) with a trip layer set on top.

## What the map shows per context

- **Trip overview**: all planned legs (dashed, colour per alternative, see below), all logbook legs (solid, actual route geometry), harbours of the plan and the logbook as markers (planned: outlined, visited: filled), photos taken on the trip as small thumbnails at their position (photos without a position are not shown). The map fits all of it with padding on open.
- **Plan tab**: the plan's legs; the next stop is chosen by clicking a harbour on the map (hover shows name and distance from the last stop via the routing API); the clicked harbour is appended to the plan as the next stop. Alternatives: a "+ Alternative" action on a stop lets the user click another harbour; it becomes an alternative for that step (same step, different harbour). Each alternative continues with its own following leg(s): e.g. Kiel → Wendtorf → {Bagenkop | Marstal} → Svendborg means step 2 has two candidate harbours, and step 3 has one leg from each of them to Svendborg. Legs render as routed geometry (routing API, cached in `PlannedLeg.RouteGeometry`).
- **Logbook tab**: the entry being added or edited is highlighted; start harbour defaults to the previous entry's end (or the plan's current stop); the end harbour is picked by clicking on the map; the actual route defaults to the routed geometry between the two harbours and can be adjusted by dragging its vertices (inserting a vertex on click on the line, removing with right-click/long press); the adjusted line is stored on the entry. Track imports (GPS) still override when present.
- **Entry detail / photos**: selecting a leg or a photo in the content column pans the map to it (easeTo, no zoom change if already visible); selecting on the map scrolls the content column to it.

## Data model

- `PlannedLeg` gets `int Alternative` (0 = main line, 1… = alternative at the same `Order`). `FromHarbourId`/`ToHarbourId` stay explicit, so a leg after an alternative references that alternative's harbour. Migration `PlannedLegAlternative`.
- `LogbookEntry` gets `LineString? RouteGeometry` (WGS84, the actual route as adjusted; null = use the routed line between the harbours, or the imported track). Migration `LogbookEntryRoute`.
- `Media` gets `Point? Location` and `DateTimeOffset? TakenAt` filled from EXIF on upload (GPS + DateTimeOriginal) when present; existing rows can be back-filled by an admin endpoint that re-reads the stored files. Migration `MediaLocation`. (Check first whether these columns exist.)

## API

- `GET /api/trips/{id}/map` – one payload for the map: planned legs (with geometry), logbook entries (harbours, geometry), harbours (id, name, position), photos (id, thumb url, position, takenAt, logbookEntryId).
- `PUT /api/trips/{id}/legs` accepts `alternative` per leg; the server validates that every leg's `FromHarbourId` is a harbour of the previous step (any alternative) or the trip's planned start.
- `PUT /api/logbook/{id}` accepts `routeGeometry` (GeoJSON LineString); the server stores it after a sanity check (≤ 2 000 points, inside the routing region, starts/ends within 3 km of the harbours).
- `GET /api/routing/route?fromHarbour=&toHarbour=` stays the source for planned/default geometries (the client calls it; the server caches per harbour pair already).

## Forms, in general

Material form fields are too tall and have no gaps. A global density and spacing rule in `styles.scss`: `mat-form-field` with `appearance="outline"` at density −2, 12 px vertical gap between stacked fields, a 16 px gap in `.row` groups, labels not clipped; dialogs get a max width of 560 px and 16 px padding. Done as its own change before the trip work (it touches only `styles.scss`).

## Delivery order

1. Global form spacing (Sonnet subagent, styles only).
2. Migrations + API (`Alternative`, `RouteGeometry`, media EXIF position, `/trips/{id}/map`).
3. Trip page layout with the map, fit-to-content, legs and photos.
4. Plan tab: click-to-add stops, alternatives.
5. Logbook tab: harbour pick on the map, route adjustment.
Each step keeps `dotnet run --project tests/Sms.Domain.Tests`, `npx ng test`, `npx ng build` green; API tests run with the AppHost stopped.
