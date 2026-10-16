# 3. Backend: Endpoints, Auth and Server Behaviour

Sources: `Controllers/**`, `Startup.cs`, `Program.cs`, `Services/*`.

## 3.1 Hosting / pipeline (`Startup.cs`)

1. Config: `appsettings.json`, `appsettings.{Env}.json`, environment variables.
2. Services: both DbContexts on SQL Server, Identity (`ApplicationUser`,
   `IdentityRole`, EF stores, default token providers, weak password rules),
   MVC with Newtonsoft (`ReferenceLoopHandling.Ignore`,
   `DateTimeZoneHandling.Local`), `IEmailSender`/`ISmsSender` →
   `AuthMessageSender` (no-op stubs).
3. Pipeline: dev exception page / `Home/Error`; in non-dev runs Identity
   migrations; static files; `UseIdentity()` (cookie auth); CORS allow-all;
   conventional route `{controller=Home}/{action=Index}/{id?}`.

Authentication is **cookie-based** (Identity application cookie). The SPA calls
the API from the same origin, so the cookie is sent automatically. There is no
anti-forgery protection on the JSON API.

## 3.2 Generic CRUD controller `ApiController<T>`

`Controllers/WebApi/ApiController.cs`, `[Produces("application/json")]`.

| Method & route | Auth | Behaviour | Response |
|----------------|------|-----------|----------|
| `GET  {base}` | anonymous | `Context.Set<T>()` – **all rows**, no paging, no `Include` | `200` array |
| `GET  {base}/{id}` | anonymous | `SingleAsync(m => m.Id == id)` | `200` entity (throws → 500 if missing instead of 404) |
| `POST {base}` | `[Authorize]` | `entity.AddOrUpdate(Context)`; `SaveChangesAsync()`; on `DbUpdateException` and entity exists → `409` | `201 Created`, `Location: <request url><id>` (no slash), body = saved entity incl. new `Id`s of nested objects |
| `PUT  {base}/{id}` | `[Authorize]` | `400` if `id != entity.Id`; `AddOrUpdate`; save; concurrency exception & not exists → `404` | `200` body = entity |
| `DELETE {base}/{id}` | `[Authorize]` | load, `RemoveFromContext` (app-level cascade, see `02` §2.5), save | `200` body = deleted entity |

Unauthenticated write requests are redirected by Identity to the login page
(302) rather than returning 401 – the client only shows a generic alert.

Concrete controllers inheriting this (route → entity):

| Route | Controller | Entity | Notes |
|-------|-----------|--------|-------|
| `api/Waypoints` | `WaypointsController` | `Waypoint` | GET returns **Waypoints and Harbours** (TPH). Client uses `Type` to split. |
| `api/Harbours` | `HarboursController` | `Harbour` | used for create/update/delete of harbours |
| `api/Trips` | `TripsController` | `Trip` | |
| `api/Tacks` | `TacksController` | `Tack` | |
| `api/LogBookEntries` | `LogBookEntriesController` | `LogBookEntry` | |
| `api/People` | `PeopleController` | `Person` | |
| `api/Jobs` | `JobsController` | `Job` | |
| `api/Wifis` | `WifiController` | `Wifi` | |
| `api/ContentPages` | `ContentPagesController` | `ContentPage` | |
| `api/Albums` | `AlbumsController` | `Album` | |
| `api/Images` | `ImagesController` | `Image` | + upload action below |
| `api/Addresses` | `AddressController` | `Address` | unused by UI |
| `api/Locations` | `LocationsController` | `Location` | unused by UI |

## 3.3 Read-only join-table controllers

| Route | Auth | Response |
|-------|------|----------|
| `GET api/AlbumImages` | anonymous | all `AlbumImage` rows `[{AlbumId, ImageId, Index, Album:null, Image:null}]` |
| `GET api/Crews` | anonymous | all `Crew` rows `[{TackId, PersonId, Person:null, Tack:null}]` |

## 3.4 Waypoint graph `api/WaypointConnections`

| Method & route | Auth | Behaviour | Response |
|----------------|------|-----------|----------|
| `GET api/WaypointConnections` | anonymous | all edges `[{Waypoint1Id, Waypoint2Id}]` | 200 |
| `POST api/WaypointConnections/{id1}/{id2}` | `[Authorize]` | add edge unless (id1,id2) or (id2,id1) exists | 204 |
| `DELETE api/WaypointConnections/{id1}/{id2}` | `[Authorize]` | remove edge in either direction (`FirstAsync` → 500 if none) | 204 |
| `DELETE api/WaypointConnections/{id}` | `[Authorize]` | remove all edges touching waypoint `id` | 204 |

## 3.5 Image upload `POST api/Images/upload`

`[Authorize]`, `multipart/form-data` (sent by Dropzone).

Form fields:
- `file` – exactly **one** file per request (more → error "Es kann nur eine Datei zurzeit hochgeladen werden"). Client restricts to `image/jpeg,image/png`.
- `album_id` – id of the target album.

Steps:
1. `ticks = DateTime.Now.Ticks`; save original to
   `wwwroot/images/upload/{ticks}/{originalFileName}`.
2. Read dimensions; create `Image { Path = "/images/upload/{ticks}/{fileName}", Width, Height }`
   and `AlbumImage { AlbumId = album_id, Image = … }`.
3. Thumbnail: target height 180 px, width scaled proportionally; if the width
   would exceed 190 px, width = 190 and height scaled instead. Saved as JPEG
   (quality 100) to `wwwroot/thumbs/images/upload/{ticks}/{fileName}.jpg`.
4. `SaveChanges`; respond `200` with the `AlbumImage` JSON
   `{ AlbumId, ImageId, Image: { Id, Path, Width, Height, … } }`.
   Errors → `500` with the exception message as body.

## 3.6 iCal feed `GET api/calendar/seed`

`CalendarController`, anonymous. Builds a `VCALENDAR` (VERSION 2.0,
METHOD PUBLISH) with one `VEVENT` per `Trip` where `IsDummy == true`:
`DTSTART`/`DTEND` (`yyyyMMddTHHmmssZ` of the local time – not converted to UTC),
`SUMMARY` = comma-separated first names of the trip's crew, `DTSTAMP` = now.
Content-Type `text/ical` (should be `text/calendar`). The implementation does
not `Include` `Crew.Person`, so it likely throws on EF Core; treat it as a
feature to re-implement correctly (UID per event, UTC times, proper content type).

## 3.7 MVC pages

| Route | Purpose |
|-------|---------|
| `GET /` (`Home/Index`) | The single-page application (map + everything) |
| `Home/About`, `Home/Contact` | template leftovers |
| `Home/Jobs` | unfinished standalone task page (broken JS) |
| `Home/Seed` | no-op (seed code commented out) → returns Index |
| `Admin/Index`, `Admin/Map`, `Admin/People` | early standalone admin pages, superseded by the modals in Index (Admin/Map references a non-existent script) |
| `Account/*` | Identity template: `Login`, `Register` (anonymous!), `LogOff` (POST), `ForgotPassword`, `ResetPassword`, `ConfirmEmail`, external login, 2FA `SendCode`/`VerifyCode` |
| `GET Account/IsLoggedIn` | returns `true`/`false` (JSON) – the SPA calls this on start to toggle edit UI |
| `Manage/*` | Identity template: change/set password, phone number, 2FA, external logins |

Login uses **e-mail + password** (`LoginViewModel`: Email, Password, RememberMe).
Registration: Email, Password (6–100), ConfirmPassword; new user is signed in
immediately. The navbar's Admin menu links to `Account/Register` ("Neuer
Benutzer") – i.e. registration was meant to be admin-only but is actually open.

## 3.8 What the rewrite needs from the backend (summary)

- CRUD for: Harbours, Waypoints, WaypointConnections, LogBookEntries (with
  crew + album), People, Jobs (tree), Wifis, ContentPages, Albums/Images;
  optionally Trips/Tacks.
- Image upload with thumbnail generation and image deletion.
- Auth: cookie or token login for a small set of users; "who am I" endpoint;
  user management restricted to signed-in users.
- iCal feed for dummy trips.
- Read endpoints anonymous, write endpoints authenticated.
