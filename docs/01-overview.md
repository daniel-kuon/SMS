# 1. Overview

## 1.1 Purpose and users

| Role | How identified | Can do |
|------|----------------|--------|
| Anonymous visitor | not signed in | View map, harbours, distances, routes, WiFi list, logbook, photos, content pages, tasks |
| Crew / admin | signed in via ASP.NET Identity (email + password) | Everything: create/edit/delete all data, upload photos, edit waypoint graph, register further users |

There is **no role/permission model** beyond "signed in or not". All write
endpoints carry `[Authorize]`; all read endpoints are anonymous.

## 1.2 Feature list (functional scope)

1. **Map** (`Home/Index`)
   - Base layer Mapbox `mapbox.streets`, overlay OpenSeaMap seamarks
     (`http://t1.openseamap.org/seamark/{z}/{x}/{y}.png`).
   - Initial view: lat `54.40774166820069`, lng `10.523529052734373`, zoom `9`.
   - Harbour markers with permanent name labels.
   - Two map modes: **View** (default) and **Admin** (toggle in the "Admin" menu).
2. **Harbour details** (right sidebar): name, photo album strip, HTML content,
   paged table of distances to all reachable harbours ("Route zeigen" button),
   WiFi table.
3. **Distance / route**: shortest path over the waypoint graph; hovering another
   harbour marker highlights the route with a label "`<n> sm`".
4. **Admin map editing**: create harbour via map context menu, drag markers
   (auto-save), draw connections between waypoints, split connections by
   dragging/clicking the midpoint handle, edit/delete via marker context menu.
5. **Logbook** (`Logbuch` navbar entry): paged/sortable table; detail modal;
   create/edit/delete modal with smart defaults from the previous entry.
6. **Photos**: every harbour and logbook entry owns an album; images are
   uploaded via drag-and-drop (Dropzone), thumbnails generated server-side,
   viewed in a PhotoSwipe lightbox.
7. **WiFi** per harbour: name, password, speed (4 levels), free yes/no.
8. **People** (crew): first/last name; used for logbook crew and task assignment.
9. **Tasks/Jobs**: hierarchical (sub-tasks), title, rich-text content, due
   date, assignee, done flag.
10. **Content pages**: title + rich-text HTML; each page appears as a link in the
    top navbar and opens in a modal.
11. **Trip planning** (bottom sidebar, Trip/Tack): partially implemented; the
    UI entry buttons are commented out. See `05` §5.6 and `06`.
12. **iCal feed** `GET /api/calendar/seed`: events for all trips with
    `IsDummy = true`.
13. **Account management**: standard ASP.NET Identity template pages (login,
    register, forgot/reset password, manage account, 2FA scaffolding — most of
    the latter is unused because e-mail/SMS senders are stubs).

## 1.3 Legacy technology stack

### Backend (`SMS/src/SMS/project.json`)
- .NET Core `1.0.0-rc2`, ASP.NET Core MVC `1.0.0-rc2-final`, Kestrel + IIS integration
- Entity Framework Core `1.0.0-rc2-final` with **SQL Server**
- ASP.NET Core Identity (EF stores) – **separate** `ApplicationDbContext`
- `ImageProcessorCore 1.0.0-alpha-869` for thumbnails
- `BootstrapTagHelpers 0.3.0-beta` (custom tags like `<modal>`, `<panel>`, `<form-group>`, `b-label`, …)
- Newtonsoft.Json with `ReferenceLoopHandling.Ignore`, `DateTimeZoneHandling.Local`;
  property names are serialized **PascalCase** (RC2 default) – the client relies on that.

### Frontend (`bower.json`, `package.json`, `gulpfile.js`)
- TypeScript (internal `module` namespaces, compiled by gulp-typescript, no bundler; scripts loaded via `<script>` tags in order)
- Knockout.js 3.4 (MVVM), jQuery 2, jQuery UI effects, Bootstrap 3
- Leaflet 0.7 + Mapbox.js 2.3, Leaflet.label, Leaflet.contextmenu
- Parsley.js (form validation, German i18n), TinyMCE 4 (rich text, German), knockout-tinymce binding
- moment 2.10.6 (+ de locale), moment-datepicker, pickadate, bootstrap-daterangepicker
- bootstrap-multiselect, Dropzone 4.0.1, PhotoSwipe 4, js-cookie
- LESS for styles (`wwwroot/css/*.less`)

## 1.4 Repository layout

```
SMS/
  SMS.sln, global.json            # SDK 1.0.0-preview1-002702
  src/SMS/
    Program.cs                    # Kestrel host, UseUrls("http://lady-helmsman:5000")
    Startup.cs                    # DI, Identity, MVC, CORS(any), migrations in prod
    PlatformServicesExtensions.cs # unused path helpers
    appsettings.json              # connection string Data:DefaultConnection:ConnectionString
    Controllers/
      HomeController.cs           # Index (the SPA), About, Contact, Jobs, Error, Seed(no-op)
      AdminController.cs          # Index, Map, People – legacy pages, mostly broken
      AccountController.cs        # Identity template + IsLoggedIn()
      ManageController.cs         # Identity template
      CalendarController.cs       # GET api/calendar/seed (iCal)
      WebApi/
        ApiController.cs          # generic CRUD base class ApiController<T>
        *Controller.cs            # one per entity (see 03-backend-api.md)
    Models/                       # EF entities + SmsDbContext + Identity context
    Migrations/                   # m1, m2 + snapshot (SmsDbContext)
    Services/                     # IEmailSender/ISmsSender stubs (AuthMessageSender)
    ViewModels/Account|Manage/    # Identity template view models
    Views/
      Shared/_Layout.cshtml       # navbar (Logbuch, Admin dropdown, login partial)
      Home/Index.cshtml           # THE application: map + all modals/sidebars/templates
      Home/Jobs.cshtml, Admin/*.cshtml  # legacy/unfinished
      Account/*, Manage/*         # Identity template views
    wwwroot/
      js/ClientModel.ts           # client entity classes (Knockout observables)
      js/ServerModel.ts           # DTO interfaces
      js/ServerApi.ts             # REST client (jQuery.ajax)
      js/KnockoutHelper.ts        # observable options (TransferMode etc.)
      js/Pager.ts                 # generic sortable/paged table component
      js/Sidebars.ts              # sidebar show/hide helper
      js/ViewModels/Home/Map.ts   # main view model (≈1900 lines) – map, helpers, algorithms
      js/ViewModels/Admin/*.ts, Home/Jobs.ts   # legacy pages
      js/knockout.pickadateime.js, daterangepicker-ko.js, TinyMCEOptions.js, parsleyConfig.js
      css/*.less                  # Map.less, Sidebars.less, mixins.less
      images/upload/<ticks>/<file>          # uploaded originals (committed sample data!)
      thumbs/images/upload/<ticks>/<file>.jpg  # generated thumbnails
      lib/                        # bower packages (committed)
    typings/                      # TypeScript d.ts files (tsd/typings)
```

## 1.5 Runtime configuration

| Setting | Where | Value / note |
|---------|-------|--------------|
| DB connection | `appsettings.json` → `Data:DefaultConnection:ConnectionString` | `Server=localhost;Database=sms;Trusted_Connection=True;MultipleActiveResultSets=true` – used by **both** DbContexts |
| Listening URL | `Program.cs` | hard-coded `http://lady-helmsman:5000` (project.json commands: `http://*:80`, `http://lady-helmsman.de:5000`) |
| Mapbox access token | hard-coded in `Map.ts` (`L.mapbox.accessToken = "pk.…"`) | move to configuration in the rewrite |
| Identity password policy | `Startup.ConfigureServices` | min length 4, no digit/upper/lower/non-alphanumeric required (Register view model still demands ≥ 6) |
| Migrations | `Startup.Configure` (non-Development only) | runs `ApplicationDbContext.Database.Migrate()` only – **not** `SmsDbContext`; errors are swallowed |
| CORS | `Startup.Configure` | any origin/method/header |
| Upload storage | `ImagesController` | `wwwroot/images/upload/<DateTime.Now.Ticks>/<original file name>` and thumbnails under `wwwroot/thumbs/...` |
