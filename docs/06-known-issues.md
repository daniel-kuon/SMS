# 6. Known Issues, Dead Code and Inconsistencies

Found while reading the legacy code. Do **not** reproduce these in the rewrite;
several are behaviour the owner probably never intended. Items marked
**(verify)** are derived from code reading only.

## 6.1 Data integrity / server logic
1. **Deleting a person deletes logbook entries (verify).** `Person.RemoveFromContext`
   → `Crew.RemoveFromContext` → `(Tack ?? new Tack{Id = TackId}).RemoveFromContext`,
   i.e. every tack/logbook entry the person crewed is deleted, plus every job
   assigned to them. Rewrite: only remove crew links and unassign jobs.
2. **Crew removal probably not persisted (verify).** `TackBase.AddOrUpdate`
   only removes no-longer-selected crew from the in-memory list, never from the
   context. Rewrite: explicitly sync the crew join table.
3. **Deleting a harbour** cascades in the DB to all `TackBase` rows that start
   or end there (FK cascade) – logbook history silently disappears. Rewrite:
   block or soft-delete.
4. **Orphaned images:** deleting an album removes `AlbumImage` links but never
   the `Image` rows or the files on disk. There is no UI to delete an image.
5. `ProcessOnSever` (server) vs `ProcessOnServer` (client) – the flag never binds.
6. `Album.FixImages` contains `AlbumId = Id = Id` (typo, harmless).
7. Model/DB drift: `LogBookEntry.WindSpeed` is `string` in C# but `decimal` in
   the migration/snapshot (and `number` in TS while the UI uses a text box);
   `Location.LocationType` column exists but the property is commented out.
8. Only `ApplicationDbContext` migrations run at start-up; `SmsDbContext`
   migrations must be applied manually. Exceptions during migration are swallowed.
9. Dates: server stores `DateTime` without offset, JSON uses
   `DateTimeZoneHandling.Local`, client sends UTC ISO strings → time-zone
   ambiguity. Rewrite: `DateTimeOffset` or UTC everywhere, display in
   `Europe/Berlin`.

## 6.2 API behaviour
10. `GET {base}/{id}` uses `SingleAsync` → 500 instead of 404 for unknown ids;
    `DELETE WaypointConnections/{a}/{b}` uses `FirstAsync` → 500 if missing.
11. Unauthenticated write requests get a 302 redirect to the login page instead of 401.
12. Every list endpoint returns the whole table; the client downloads the
    entire database on page load (14 requests).
13. `ServerApi.Locations` uses `"api/Locations"` (no leading slash) and
    `ServerApi.Comments` points to `/api/Comments`, which does not exist.
14. iCal: `Crew.Person` not loaded (`Include` missing) → exception;
    `Aggregate` throws for a trip without crew; local times printed with a `Z`
    (UTC) suffix; no `UID`; content type `text/ical` instead of `text/calendar`.

## 6.3 Security
15. **Open registration**: `Account/Register` is `[AllowAnonymous]` although it
    is only linked from the admin menu. Password policy: min length 4 in
    Identity options vs 6 in the view model.
16. **Stored XSS**: HTML from TinyMCE (harbour content, logbook notes, tasks,
    content pages) is rendered unsanitised; the TinyMCE config even allows
    `<script>`, `<object>`, `<embed>` (`TinyMCEOptions.js`). Rewrite: sanitise
    on the server (e.g. `HtmlSanitizer`) and rely on Angular's `[innerHTML]`
    sanitisation.
17. **Upload**: client-supplied file name is used unchanged in the path (path
    traversal risk), no size limit, content type not verified server-side,
    Windows-only path separators (`"images\\upload"`) break on Linux.
18. No CSRF protection on the cookie-authenticated JSON API; CORS allows any origin.
19. Mapbox access token and host name are hard-coded in source.
20. The drag-and-drop upload modal opens for anonymous users too
    (`if (mapViewModel.IsLoggedIn && …)` tests the observable function, which is
    always truthy); the upload then fails.

## 6.4 Front end
21. Distance algorithm is a greedy approximation of Dijkstra (see `05` §5.1).
22. Home port "Lippe" is hard-coded in the logbook defaults.
23. In the task panels, "Löschen" and "Neue Unteraufgabe" are visible to
    anonymous users; `DueTo` is shown unformatted.
24. `EditingHelper` enables Parsley validation based on
    `$("form:first").length === 1` (global check, not per modal).
25. Route planner template binds `$data.Crew` although the client model uses
    `Persons`; trip creation buttons are commented out; trips are never saved
    from the UI.
26. Helpers for Supermarket/Restaurant/Location/Address/Image/Tack reference
    modals that do not exist.
27. External map services: `mapbox.streets` is a retired Mapbox "classic"
    style and the OpenSeaMap URL uses plain `http` (mixed content on HTTPS).
    Use `https://tiles.openseamap.org/seamark/{z}/{x}/{y}.png` and a current
    base layer (OpenStreetMap or a Mapbox Styles API style).
28. UI typos: "witklich" (WiFi / content page delete text), "Vorkomnisse"
    (logbook column), "versuche er erneut" (error alert). Fix them in the
    new UI.

## 6.5 Dead / legacy code
- `Views/Admin/*`, `wwwroot/js/ViewModels/Admin/*` – early admin pages; `Admin/Map`
  loads a non-existent `~/js/admin/map.js`.
- `Views/Home/Jobs.cshtml` + `ViewModels/Home/Jobs.ts` – calls a non-existent
  `Seed()` method.
- `Home/Seed`, `Home/About`, `Home/Contact`, `Project_Readme.html`,
  `PlatformServicesExtensions.cs`, `Db.ts`, `daterange` binding, `Comment`
  entity, `Location`/`Address` UI, star rating (commented out).
- Identity 2FA / phone / external login pages – the e-mail/SMS senders are
  stubs, so these flows cannot work.
- Committed binary data: sample uploads under `wwwroot/images/upload` and
  `wwwroot/thumbs`, and the full `wwwroot/lib` bower tree.
