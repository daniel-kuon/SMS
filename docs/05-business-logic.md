# 5. Business Logic and Algorithms

Sources: `wwwroot/js/ViewModels/Home/Map.ts`, `ClientModel.ts`,
`Models/*.cs`, `Controllers/WebApi/ImagesController.cs`.

## 5.1 Harbour distances (shortest path over the waypoint graph)

Implemented client-side in `MapViewModel.CalculateDistances(start, target?)`
using `ClientModel.WaypointDistance`. It is a Dijkstra-like expansion:

- **Graph**: nodes = all Waypoints and Harbours; undirected edges =
  `WaypointConnection`s. Edge weight = great-circle distance between the two
  points (`L.LatLng.distanceTo`, metres).
- Start node distance 0. Repeatedly pick, among all "frontier" nodes, the
  (node, nearest unvisited neighbour) pair with minimal `dist(node) + edge`,
  record the neighbour's predecessor (`Precessor`) and distance, and add it to
  the frontier. Nodes are visited once (first assignment wins).
  Note: the legacy code only considers `ConnectedWayPoints[0]` per frontier
  node (neighbours are pre-sorted by straight-line distance to that node), which
  is a greedy approximation of Dijkstra – **implement proper Dijkstra** in the
  rewrite.
- Unit conversion: internal value = metres / 1.852; displayed value =
  `round(value / 100) / 10` → **nautical miles with one decimal**
  (1 sm = 1852 m).
- Results stored per node in `Distance` + `Precessor` (or `RouteDistance` +
  `RoutePrecessor` when a `target` is given – used by the trip planner).
- Unreachable harbours keep distance 0 and are filtered out of the distance
  table (filter `Distance > 0`).
- Triggered when a harbour is selected; on deselection all distances reset to 0.

**Rewrite recommendation:** compute on the server (or in a shared TS service)
with a real Dijkstra/A*; expose e.g.
`GET /api/harbours/{id}/distances → [{harbourId, distanceNm, path:[waypointIds]}]`.

## 5.2 Route highlighting

`ShowRoute(harbour)`: follow `Precessor` links from the hovered harbour back
to the selected one, draw a polyline through their coordinates, bind a
permanent label "`<Distance> sm`", and fit the map to it if it is not fully
visible (remembering the previous bounds). `HideRoute()` removes it (unless
pinned) and restores the previous bounds after 100 ms. `FixRoute()` pins it
(used by "Route zeigen"). Selecting another harbour unpins/hides.

## 5.3 Waypoint graph editing (admin mode)

Client state: each connection is an `L.Polyline` with exactly two waypoints
(`polyline.Waypoints`) and one **dummy handle** – an invisible, draggable
`Waypoint` (marker type `Dummy`, not persisted) at the segment midpoint.

Operations:

| User action | Effect (client) | Server calls |
|-------------|-----------------|--------------|
| Drag waypoint/harbour | marker moves; connected polylines and their midpoint handles are redrawn live | `PUT` waypoint/harbour on drag end |
| Hover a polyline | its dummy handle follows the mouse along the segment (closest point), becomes visible (opacity 0.8) except within 20 px of the endpoints | – |
| Click on a polyline, or click/drag its dummy handle | **split**: the dummy becomes a real waypoint at that position; segment A–B is replaced by A–W and W–B, each with a new dummy handle | `POST api/Waypoints` (new W), then `DELETE api/WaypointConnections/A/B`, `POST …/A/W`, `POST …/B/W` |
| Double-click a waypoint/harbour marker | start **drawing mode**: a rubber-band line follows the mouse from that waypoint | – |
| In drawing mode: click on empty map | create a new waypoint there, connect it to the line's start, and continue drawing from the new waypoint | `POST api/Waypoints`, then `POST api/WaypointConnections/{new}/{start}` |
| In drawing mode: click an existing marker | connect start → that marker and finish drawing (clicking the start marker itself cancels) | `POST api/WaypointConnections/{clicked}/{start}` |
| In drawing mode: double-click map | adds an extra vertex to the rubber band (quirk) | – |
| `Esc` in drawing mode | cancel (remove rubber band) | – |
| Marker context menu "Löschen" | delete waypoint; all its polylines (and their handles) are removed | `DELETE api/Waypoints/{id}` (server also deletes its connections) |
| Map context menu "Neuer Hafen" | open harbour edit modal with a new harbour at the click position | `POST api/Harbours` on save |

In the rewrite, consider a dedicated endpoint to split a connection atomically
(`POST /api/waypoint-connections/split {a, b, lat, lng}`).

## 5.4 Logbook entry defaults (factory for a new entry)

When "Neuer Eintrag" is used and entries exist, find the entry with the
**latest `EndDate`** ("last entry") and prefill:
- `Start` = last entry's `End` harbour
- `MotorHoursStart` = last `MotorHoursEnd`
- `LogStart` = last `LogEnd`
- `Persons` (crew) = copy of last entry's crew, **unless** the last entry's end
  harbour is named **"Lippe"** (the home port – a new crew starts from there).
  Make the home port configurable in the rewrite.
- `StartDate`/`EndDate` default to "now" when the picker initialises.
- A new empty `Album` is attached and created together with the entry.

Derived values:
- **Sailing time** ("Dauer") = `EndDate − StartDate` formatted `h:mm`
  (hours not wrapped at 24).
- **Crew list** = full names joined by ", ".
- Distance sailed could be derived from `LogEnd − LogStart`; engine hours from
  `MotorHoursEnd − MotorHoursStart` (not shown in legacy UI – nice to have).

## 5.5 Albums, "current album" and upload flow

- Albums are created implicitly: a new Harbour or LogBookEntry (any TackBase)
  is created with a nested empty album (`Album: {Images: []}`) so photos can be
  added right away.
- `AlbumStack` (observable array) tracks which album is "in context": opening
  a harbour detail sidebar, logbook detail modal or any edit modal pushes that
  entity's album on top; closing pops it; a delete modal pushes `undefined`.
  The top of the stack is the upload target (`album_id`) and the gallery
  source.
- **Drag-and-drop auto-open**: if the user is signed in, drags files over the
  page, the upload modal is not open yet and an album is in context, the upload
  modal opens automatically. When the drag leaves and no file is queued, it
  closes again after 1 s; after all uploads finish (`queuecomplete`) a modal
  that was opened by dragging closes automatically. The modal refuses to close
  while uploads are running ("Das Fenster kann nicht geschlossen werden,
  während Dateien hochgeladen werden.").
- Accepted types: JPEG, PNG ("Dieser Dateityp wird nicht unterstützt").
- On each successful upload the returned `AlbumImage.Image` is added to the
  global image list and to the album, so the thumbnail appears immediately.
- Server side: see `03` §3.5 (original + 190×180 max JPEG thumbnail).

**Rewrite recommendation:** explicit "album context" via the component that is
showing it (no global stack); store files outside `wwwroot` or in blob storage;
generate thumbnails with ImageSharp; also store a larger "preview" size;
respect EXIF orientation; support deleting images.

## 5.6 Trip planning (partially implemented)

Model: a `Trip` contains ordered `Tack`s; each tack goes Start → End harbour.
- `StartRoute()`: new Trip with one Tack starting at the selected harbour;
  opens the bottom sidebar; creates a green route polyline.
- `AddToRoute()`: target = harbour; compute route from the last tack's start
  to the target (`CalculateDistances(target, start)` → `RoutePrecessor`
  chain), set last tack `Distance` and `End`, append a new tack starting at
  the target; extend the green polyline.
- `PullTack` / `PushTack`: swap a tack with its predecessor / successor,
  re-linking `End`s so the chain stays continuous; then `RedrawTrip()`
  recomputes all tack distances and the polyline.
- `RemoveTack`: remove a tack; previous tack's `End` = removed tack's `End`.
  Allowed only if it does not produce two consecutive tacks from the same
  harbour (`CanRemoveTack`).
- `GetRouteDistance`: sum of tack distances.
- Trips with `IsDummy = true` are exported to the iCal feed.

The UI buttons to start/extend a trip are commented out and Trip saving was
never finished. Treat as **optional / future feature**; clarify with the owner.

## 5.7 Client-side change tracking (`ClientModel.Entity`)

Worth knowing to understand the API payloads, not to replicate:
- Every client entity gets a sequential `ClientId`; all instances are kept in a
  global registry so server responses (which echo `ClientId`) can be merged
  back into nested objects (e.g. new album id after creating a harbour).
- `ConvertToServerEntity()`: serialises all writable observables; nested
  entities/arrays are only included when they are **new** (no `Id`) unless the
  observable is marked `TransferMode.Include`; existing ones are omitted.
- `SaveState()` / `RevertState()`: deep snapshot stack used for
  cancel-on-close of edit modals.

In Angular, use typed DTOs + reactive forms; cancel = discard the form copy.
