# 4. Front End: Screens, Components and Interactions

Sources: `Views/Shared/_Layout.cshtml`, `Views/Home/Index.cshtml`,
`wwwroot/js/ViewModels/Home/Map.ts`, `ClientModel.ts`, `Pager.ts`,
`Sidebars.ts`, `wwwroot/css/*.less`.

The whole application is **one page** (`/`). Everything else (logbook, people,
tasks, pages, editing) happens in Bootstrap modals and sidebars on top of a
full-screen map. All UI text is German.

## 4.1 Start-up sequence

1. Page renders with a dark full-screen overlay "**Lade Seite**" (loading).
2. Map is created (see §4.4), `GET /Account/IsLoggedIn` sets `IsLoggedIn`.
3. 14 parallel GETs load **all** data: Waypoints (incl. harbours),
   WaypointConnections, People, Jobs, Trips, Addresses, Images, Albums,
   LogBookEntries, AlbumImages, Crews, Wifis, ContentPages, Tacks.
4. When all have returned, relations are resolved client-side by id:
   - Job → AssignedTo, Trip, SuperJob (and parent's SubJobs list)
   - Harbour → Album; LogBookEntry → Start, End, Album
   - Album → Images (via AlbumImages)
   - WaypointConnection → a map polyline between the two waypoints + a
     midpoint "dummy handle" (see `05` §5.3)
   - Crew → `Persons` list of the LogBookEntry / Tack / Trip with that id
   - Wifi → Harbour.Wifis
5. Knockout bindings applied, overlay removed.

In the rewrite this should become a few purpose-built endpoints (e.g. map data,
logbook page, harbour details) instead of downloading the full database.

## 4.2 Page layout

```
+----------------------------------------------------------------------------+
| Kairos | Logbuch | <content page links…>          [Admin ▾] Hello x! Log off |  ← fixed navbar
+----------------------------------------------------------------------------+
|                                                    |  RIGHT SIDEBAR         |
|                    MAP (fills the rest)            |  harbour details       |
|                                                    |  (when a harbour is    |
|                                                    |   selected)            |
+----------------------------------------------------+------------------------+
|  BOTTOM SIDEBAR (route planner, 320 px high, only while planning a trip)    |
+----------------------------------------------------------------------------+
```

- **Navbar** (`_Layout.cshtml`):
  - Brand "**Kairos**" → `/`.
  - Left: "**Logbuch**" → opens logbook overview modal; followed by one link
    per ContentPage (title) → opens content page detail modal. The links are
    regenerated whenever the ContentPages list changes.
  - Right, only when signed in: dropdown "**Admin**" with
    "Neuer Benutzer" (→ `/Account/Register`), "Personen" (people modal),
    "Aufgaben" (tasks modal), "Neue Seite" (new content page modal),
    "Karte Admin" / "Karte Normal" (toggle map mode; only the applicable one shown).
  - Right: login partial – "Hello {username}!" (→ `/Manage`) + "Log off", or "Log in".
- **Sidebars** (`Sidebars.ts`, `Sidebars.less`): slide in with a 400 ms
  animation; the map shrinks accordingly and Leaflet `invalidateSize` is called.
  Widths for left/right: 25 % (lg), 40 % (md), 50 % (sm), 100 % (xs – then
  stacked under the map, map 50 % height). Bottom sidebar: 320 px high,
  horizontally scrolling. A left sidebar exists but is empty.
- **Mobile**: user-agent sniffing adds `body.mobile` → waypoint handles become
  48 px touch targets.
- Stacked modals get increasing z-index (custom "fv-modal-stack" logic) so
  e.g. "new person" can open on top of "edit logbook entry".

## 4.3 Edit/Detail/Delete pattern (`EditingHelper<T>` in `Map.ts`)

Every entity type has a helper with three observables, each bound to a modal:

| Observable | Modal id pattern | Effect |
|------------|------------------|--------|
| `Detail` | `detailed…Modal` / `detail…Modal` or a sidebar | read-only view |
| `Editing` | `editing…Modal` | form; opening it with no entity creates a new one via a factory |
| `Deleting` | `deleting…Modal` | confirmation "Soll … wirklich gelöscht werden?" |

Behaviour to preserve:
- When editing starts, a **snapshot** of the entity is taken; closing the modal
  without saving (or switching to another entity) **reverts** all changes.
- "Speichern" validates the form (Parsley, `required` fields), then POST (new)
  or PUT (existing); on success the modal closes and the entity is added to /
  updated in its list.
- "Löschen" in the delete modal calls DELETE, removes it from lists (and from
  the map), and closes any open detail/edit modal for it.
- Edit/delete buttons are only visible when signed in.
- Any server error shows `alert("Es gab ein Fehler beim Verarbeiten der Daten auf dem Server. Bitte überprüfe deine Eingaben und versuche er erneut.")`.

Helpers that exist: Waypoint, Harbour (detail = right sidebar), Person, Job,
Trip, Address, Image, Tack, Location, Supermarket, Restaurant, LogBookEntry
(detail modal), ContentPage (detail modal), Wifi (detail modal).

## 4.4 Map

- Leaflet via Mapbox.js, tiles `mapbox.streets` + OpenSeaMap seamark overlay.
- Start view `[54.40774166820069, 10.523529052734373]`, zoom 9.
- Harbour markers: default Leaflet marker icon with a permanent label showing
  the harbour name (Leaflet.label, `direction: auto`); label updates when the
  name changes.

### View mode (default, everyone)
- Only **harbour** markers are shown; waypoints and connection lines are hidden.
- **Click harbour** → becomes `HarbourHelper.Detail`: right sidebar opens,
  distances from this harbour to all others are computed (`05` §5.1).
- **Hover another harbour marker** while one is selected → route highlighted
  as a blue polyline with permanent label "`<distance> sm`"; map zooms out to
  fit if the route is not fully visible, and returns to the previous view when
  the route is hidden again.

### Admin mode (signed-in users, "Karte Admin")
- Additionally shows all plain waypoints (10×10 px handle icon
  `/images/waypointhandle.png`, CSS class `waypoint`, grows to 16 px on hover)
  and all connections as polylines.
- All markers are **draggable**; on `dragend` the waypoint/harbour is saved
  (PUT). While dragging, connected lines follow.
- Marker opacity: handles within 150 px of the mouse are fully opaque, others 0.8.
- Map **context menu** (right-click): "**Neuer Hafen**" → opens harbour edit
  modal for a new harbour at that position.
- Marker context menu: "**Bearbeiten**" / "**Löschen**" (harbour or waypoint helper).
- Graph editing (double-click to draw, midpoint handles to split) → `05` §5.3.

## 4.5 Right sidebar – harbour details (`#harbourInfo`)

Bound to `HarbourHelper.Detail`:
1. Header: harbour name, close icon (×) → clears selection (also hides route).
2. Album strip (§4.9).
3. `Content` rendered as HTML.
4. "**Entfernungen zu anderen Häfen**" – Pager table (§4.10) of all harbours
   with distance > 0: columns "Name" (sortable), "Entfernung" (`"<n> sm"`,
   sortable, default ascending); per-row action "**Route zeigen**" (shows and
   pins the route); clicking a row selects that harbour (its details replace
   the current ones). No edit/delete buttons in this table.
5. "**WLANs**" table (Name, Passwort or `-`); button "**Neu**" (signed in) opens
   the new-WiFi modal pre-filled with this harbour; clicking a row opens the
   WiFi detail modal.

## 4.6 Modals and forms (fields, labels, validation)

### Logbook overview `#logBookOverviewModal` – "Logbuch" (extra-wide, 90 %)
Pager over all LogBookEntries:

| Column | Value | Default |
|--------|-------|---------|
| Start | start harbour name (sortable) | hidden |
| Ziel | end harbour name (sortable, 200 px) | visible |
| Datum | StartDate, format `LL` (sortable, **default sort descending**, 150 px) | visible |
| Dauer | sailing time `h:mm` = EndDate − StartDate | visible |
| Crew | full names separated by `<br/>` (150 px) | visible |
| Besondere Vorkomnisse | HTML | visible |

Row click → detail modal. Signed in: row "Bearbeiten"/"Löschen", global action
"**Neuer Eintrag**". Column selector "Spalten auswählen" and page-size selector.

### Logbook entry edit `#editingLogBookEntryModal` (large, static backdrop)
Two-column form:
| Label | Field | Control | Required |
|-------|-------|---------|----------|
| Start | Start | harbour selector (§4.8) | |
| Ziel | End | harbour selector | |
| Abgelegt | StartDate | date-time picker (pickadate date + time, read-only input) | ✔ |
| Festgemacht | EndDate | date-time picker | ✔ |
| Motorstunden (Start) | MotorHoursStart | number, step 0.01 | ✔ |
| Motorstunden (Ziel) | MotorHoursEnd | number, step 0.01 | ✔ |
| Logge (Start) | LogStart | number, step 0.01 | ✔ |
| Logge (Ziel) | LogEnd | number, step 0.01 | ✔ |
| Windgeschwindigkeit | WindSpeed | text | ✔ |
| Windrichtung | WindDirection | select of 16 compass points (N … NNW, German abbreviations, tooltip with full name e.g. "Nordnordost") | ✔ |
| Crew | Persons | multi person selector (§4.8) | ✔ |
| Besondere Vorkommnisse | SpecialOccurences | TinyMCE | |

Buttons: "Speichern", "Abbrechen". New entries get defaults (`05` §5.4).

### Logbook entry detail `#detailedLogBookEntryModal`
Read-only versions of all fields (dates formatted `LLL`, crew as comma list,
special occurrences as HTML) + album strip.

### Logbook entry delete `#deletingLogBookEntryModal`
"Soll der Logbucheintrag wirklich gelöscht werden?" – Löschen / Abbrechen.

### Harbour edit `#editingHarbourModal`
| Label | Field | Control | Required |
|-------|-------|---------|----------|
| Name | Name | text | ✔ |
| Längengrad | Longitude | number (any step) | ✔ |
| Breitengrad | Latitude | number (any step) | ✔ |
| Webseite | Website | url | |
| Wegpunkt-Nr. | WaypointNumber | number step 1 | |
| Inhalt | Content | TinyMCE | |

Extra button "**Aktuelle Position verwenden**" → browser geolocation fills
lat/lng (error alert "Die Position konnte nicht abgerufen werden").
Changing lat/lng moves the marker live. Cancelling a *new* harbour removes its
temporary marker. Delete: "Soll der Hafen '<Name>' wirklich gelöscht werden?".

### Waypoint edit `#editingWaypointModal`
Name (text), Längengrad ✔, Breitengrad ✔, Wegpunkt-Nr. Delete: "Soll der
Wegpunkt '<Name>' wirklich gelöscht werden?".

### People `#personsOverviewModal` – "Personen"
Table Vorname / Nachname with "Entfernen" / "Bearbeiten" (signed in); footer
"Hinzufügen". Edit modal: Vorname ✔, Nachname ✔. Delete: "'<FullName>'
entfernen – Soll die Person wirklich gelöscht werden?".

### Tasks `#jobOverviewModal` – "Aufgaben"
Recursive panels for top-level jobs (no `SuperJobId`), sub-jobs indented 50 px
below their parent. Each panel: title, HTML content, "Zugewiesen:" (assignee
full name), "Bis zum:" (DueTo), green panel when Done. Buttons: "Neue
Unteraufgabe" (new job with `SuperJobId` = this), "Löschen", "Bearbeiten".
Footer "Neue Aufgabe".
Edit modal: Titel ✔, Fällig bis (date picker), Inhalt (TinyMCE), Zuständig
(single person selector), Erledigt (checkbox). Delete: "Soll die Aufgabe
'<Title>' wirklich gelöscht werden?".

### WiFi
- Detail `#detailWifiModal`: Name, Passwort (or `-`), Geschwindigkeit
  (Schlecht/Mäßig/Gut/Sehr Gut), Gratis (Ja/Nein); buttons Schließen /
  Bearbeiten / Löschen.
- Edit `#editingWifiModal`: Name ✔, Passwort, Geschwindigkeit (select 0–3),
  Gratis (checkbox). New WiFi gets `HarbourId` of the selected harbour.
- Delete: "Soll das WLAN wirklich gelöscht werden?".

### Content pages
- Detail `#detailedContentPageModal`: title header, HTML body; Schließen /
  Bearbeiten / Löschen.
- Edit `#editingContentPageModal`: Titel ✔, Inhalt (TinyMCE) ✔.
- Delete: "Soll die Seite wirklich gelöscht werden?".

### Photo upload `#dropzoneModal` – "Bild hinzufügen"
Dropzone form posting to `/api/Images/upload` with hidden `album_id` = current
album (§4.9). Text: "Dateien hier ablegen oder klicken" and hint "Sobald du
einen Hafen ausgewählt hast und Bilder auf die Webseite ziehst, erscheint
dieses Fenster automatisch". Details of auto-open in `05` §5.5.

## 4.7 Bottom sidebar – route planner (`#routePlanner`)
Shown when `TripHelper.Editing` holds a trip (entry buttons are commented out,
so currently unreachable). Horizontal list of tack panels (250 px wide):
heading = start harbour name; fields "Abfahrt" (StartDate), "Ankunft"
(EndDate, placeholder = start date), Crew; footer buttons ← "Vorziehen" (move
earlier), 🗑 "Löschen" (only if `CanRemoveTack`), → "Weiter nach hinten" (move
later). Between panels a big arrow with "`<distance> sm`". Logic in `05` §5.6.

## 4.8 Selector controls (Knockout templates)
- **harbourSelector**: `<select>` of harbours sorted by name with caption
  "Hafen wählen..." and a last option "**Neuer Hafen...**" which opens the
  harbour edit modal; if saved, the new harbour is selected, otherwise the
  selection is cleared.
- **singlePersonSelector**: same pattern with persons sorted by full name,
  caption "Person wählen...", last option "**Neue Person...**".
- **multiplePersonSelector**: bootstrap-multiselect over all persons plus a
  "+" button ("Neue Person") opening the person edit modal.

## 4.9 Album strip and gallery
- Template `albumTemplateSmall`: horizontally scrolling strip (≈217 px high;
  117 px on small screens) of thumbnails (`/thumbs{Path}.jpg`) and – when
  signed in – a placeholder tile "Neues Bild +" that opens the upload modal.
- Click on a thumbnail → **PhotoSwipe** lightbox with all images of the
  current album (full-size `Path`, `Width`, `Height`), starting at the clicked
  image, with zoom-from-thumbnail animation.
- The "current album" is the top of `AlbumStack` (see `05` §5.5).

## 4.10 Generic table component (`Pager.ts` + `#pager` template)
Reusable sortable, paged table – a good candidate for a single Angular
component (or Angular Material table).
- Options: columns, page size (default 10; choices 5,10,15,20,30,40,50,100;
  label "`<n> pro Seite`"), show page-size selector, show column selector
  ("Spalten auswählen" modal with a checkbox per column), responsive/striped
  table, small controls, `EditingHelper` (row click → Detail; edit/delete
  buttons when signed in and `ShowEditDeleteControls`), per-row special
  actions, global special actions.
- Column: name, value accessor, optional sorter (string/number/date/array
  length), renderer (default; date `LL`; datetime `LLL`; time `LT`; array join),
  width, CSS, visibility, initial sort mode. Clicking a sortable header cycles
  ascending ▲ / descending ▼; only one sort column at a time.
- Pagination: «, page numbers, ».

## 4.11 Formatting conventions
- Dates via moment with German locale: `L`, `LL`, `LLL`, `LT`.
- Durations `h:mm` (e.g. `7:05`).
- Distances in nautical miles with one decimal, suffix " sm" (Seemeilen).
- Rich text produced by TinyMCE (German language pack; plugins: lists, link,
  image, table, media, emoticons, textcolor, code, …) and rendered with
  `html` binding (unsanitised – sanitise in the rewrite).
