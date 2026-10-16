# 7. New Product: Vision and Requirements

The new portal keeps **all functions of the legacy app** (docs `01`–`05`) but
turns the single-boat site into a **multi-ship platform and social network for
sailors**. This document is the functional specification; the data model and
permission rules are in `08`, cost splitting in `09`, sea routing in `10`, and
architecture plus implementation phases in `11`.

Working title in this documentation: **"the portal"**. UI language: German
first, English as a second language (all texts via i18n from day one).

## 7.1 Vision in one paragraph

Anyone can register, add their own ship(s) and keep the ship's logbook online.
A skipper plans **trips** and invites a **crew**. During the trip every crew
member can post **photos** and **blog posts**; they are automatically grouped
into the trip's **days** and shown together with the logbook entries of that
day. Each item can be **private**, **crew-only** or **public**, so a trip can
become a public travel blog while personal or crew-internal content stays
protected. The crew records shared **costs** and the portal shows who owes
whom to settle up. A **harbour guide** with sea distances computed
automatically from OpenStreetMap/OpenSeaMap data shows how far the next ports
are.

## 7.2 Roles

| Role | Scope | Description |
|------|-------|-------------|
| Anonymous visitor | platform | sees public content only (public ships, trips, posts, photos, logbook entries, harbour guide) |
| Registered user | platform | has a profile; can follow, comment, like; can create ships; can edit the harbour guide |
| Ship owner | ship | created the ship or was made owner; manages ship data and ship members; full rights on everything of the ship |
| Ship skipper | ship | may create trips for the ship and write the ship's logbook (granted by an owner) |
| Trip skipper | trip | the person leading a trip (normally its creator); manages crew, logbook entries and settings of the trip |
| Crew member | trip | accepted participant of a trip; can post, upload photos, add costs, see crew-only content |
| Guest crew | trip | crew member **without an account** (name only), managed by the skipper; appears in crew lists, logbook and cost splitting; can be linked to an account later |
| Platform admin | platform | moderation, content pages (Impressum, Datenschutz, help), harbour data import, user management |

Exact permission matrix: `08` §8.4.

## 7.3 Feature areas and user stories

Each story has acceptance criteria (AC). "Legacy" marks features that already
exist in the old app (see the referenced doc).

### A. Accounts and profiles
- **A1 Register / login.** Users register with e-mail + password (e-mail
  confirmation required), log in/out, reset password. Optional later: Google /
  Apple login.
  AC: unconfirmed accounts cannot post; password reset mail works.
- **A2 Profile.** Display name, avatar, short bio, home harbour, sailing
  experience (free text), profile visibility (public / registered users only).
  AC: public profile page lists the user's public trips/posts.
- **A3 Privacy.** User can export all their data (JSON + media ZIP) and delete
  the account (posts/photos deleted; costs and logbook crew entries are kept
  but anonymised to "Ehemaliges Mitglied"). EXIF GPS data is removed from
  photos unless the user opts in to keep location.
- **A4 Settings.** Default visibility for new posts/photos, language, e-mail
  notification preferences.

### B. Ships
- **B1 Create ship.** Name (required), type/model, length, beam, draft, air
  draft, sail number, call sign, MMSI, home harbour, cruising speed (kn, used
  for time estimates, default 5), photo, description, visibility (public /
  private).
  AC: creator becomes owner.
- **B2 Manage members.** Owners invite users by e-mail or user search and
  assign ship roles (owner / skipper). AC: at least one owner must remain.
- **B3 Ship page.** Public ship page: photo, data, public trips, map of all
  sailed legs, statistics (nm sailed, engine hours, nights, harbours visited).
- **B4 Ship logbook** (legacy `04` §4.6): logbook across all trips of the
  ship; legacy table, filters by trip/year.

### C. Trips and crew
- **C1 Create trip.** Ship skippers/owners create a trip: name, description,
  start/end date, planned start/end harbour, trip skipper (default: creator),
  visibility (crew / public), currency for costs (default EUR), status
  (Geplant, Aktiv, Abgeschlossen, Abgesagt).
- **C2 Crew.** Skipper invites users (e-mail / search / invite link) or adds
  guest crew by name. Invitees accept or decline. Crew roles: Skipper,
  Co-Skipper (may also write logbook entries), Crew. A member may join for
  only part of the trip (from/to date).
  AC: an invited but not accepted user sees nothing crew-only.
- **C3 Trip planning** (legacy `05` §5.6, unfinished there). Skipper plans legs
  as an ordered list of harbours; distances and ETA (at ship cruising speed)
  come from sea routing (`10`). Reorder/remove legs. Plan is a proposal; actual
  legs are logbook entries.
- **C4 Trip page.** Header (ship, crew avatars, dates, total nm) and tabs:
  **Übersicht** (trip overview map, see section I), Timeline (E2), Logbuch,
  Fotos, Kosten (crew only), Aufgaben (crew only).
- **C5 Calendar.** iCal feed per user (all my trips) and per ship (legacy
  `03` §3.6, done correctly).
- **C6 Tasks** (legacy jobs): hierarchical to-dos per trip (packing list,
  provisioning) and per ship (maintenance), assignable to crew/ship members,
  due date, done.

### D. Logbook entries
- **D1 Write entry** (legacy fields `02` §2.2 LogBookEntry): start/end harbour,
  cast off / moored times, engine hours start/end, log start/end, wind speed,
  wind direction, crew (default = trip crew aboard that day), notes. New:
  weather (free text), sea state, sailed distance (from log or computed),
  sails/reef notes (free text). The leg's route comes from the ship track
  or is calculated (section I). Written by trip
  skipper/co-skippers or ship skippers/owners.
- **D2 Defaults** (legacy `05` §5.4): start harbour, engine hours and log are
  taken from the ship's previous entry; crew from the trip.
- **D3 Visibility.** Each entry is crew or public (set by its author; private
  allowed for drafts).
- **D4 Entries without trip.** Allowed (e.g. delivery trips, legacy data);
  they appear only in the ship logbook.

### E. Posts, photos and the day timeline (social core)
- **E1 Blog post.** Any crew member can write a post during or after the trip:
  title (optional), rich text, location (device position on creation,
  picked harbour/map point, or derived from the ship track – `12` §12.4),
  photos, visibility (privat / crew / öffentlich). The post's **day** defaults
  to today (local time of the trip) and can be changed within the trip dates.
- **E2 Day timeline.** For every trip day the trip page shows: the logbook
  entries of that day, then posts and photos of that day ordered by time —
  only items the viewer may see. A logbook entry detail page shows the posts
  and photos of the days it covers ("become part of the logbook entry for
  this day").
- **E3 Photos.** Crew members upload photos (multi-file, drag and drop, mobile
  camera); each photo gets a day (from EXIF capture time if inside the trip,
  else upload day), location (EXIF GPS, device, ship track or harbour –
  `12` §12.4), caption, visibility, optional post. Gallery with lightbox
  (legacy `05` §5.5). Thumbnails and preview sizes generated server-side.
- **E4 Social interactions.** Comments and likes on posts, photos, logbook
  entries, trips (only by users who can see the item). Follow users and
  ships. Personal **feed**: new public/crew items from followed users/ships and
  my trips. Notifications (in-app + optional e-mail): invitations, comments,
  new costs, settlement requests.
- **E5 Moderation.** Report content; platform admin can hide items and block
  users.

### I. Trip overview and ship track (see `12`)
- **I1 Trip overview map.** Shows the ship's track coloured per day, overnight
  harbours, and **photos and posts at the location where they were created**
  (clustered thumbnails / post markers opening lightbox or post popup), plus
  stats and a day list that highlights segments on hover. Optional playback.
- **I2 Track sources.** Best available per leg/day: GPS track from a tracking
  app (live) or imported file (GPX/KML/NMEA) > AIS track from the
  MarineTraffic API > sea route calculated from the logbook entries' harbours.
  Planned legs shown separately. Skipper/owner can override the source.
- **I3 GPS tracking apps.** Ship members register tracking devices and get a
  token + setup instructions for Traccar Client/OsmAnd/GPSLogger (OsmAnd
  protocol), OwnTracks, or the JSON endpoint (PWA, Signal K, integrations).
- **I4 MarineTraffic / AIS.** Ship owners can configure an AIS provider
  (MarineTraffic first) with their own API key and the ship's MMSI/IMO; live
  polling during active trips and historical track at the end.
- **I5 Ship page map.** All trips' tracks of a ship on one map.
- **I6 Track privacy.** Separate track visibility, delayed public live
  position (default 2 h), privacy zones, reduced location precision for public
  viewers; suggestions for logbook entries from detected harbour stops.

### F. Costs (see `09`)
- **F1 Add expense.** Any crew member: amount, currency, date, category
  (Hafengeld, Diesel, Proviant, Restaurant, Charter, Sonstiges, …),
  description, paid by (default me; skipper may enter for others), split
  between (default all crew aboard that day, equal shares; option weights or
  exact amounts), optional receipt photo.
- **F2 Balances.** Every crew member sees the balance of everybody (paid,
  share, net) and a **suggested list of transfers** to become even.
- **F3 Settle up.** Mark a transfer as paid; receiver confirms. Skipper can
  close the cost account ("Abrechnung abschließen"), which locks it.
- **F4 Export.** CSV/PDF of expenses and settlement.

### G. Harbour guide and map (legacy, extended)
- **G1 Global harbour catalogue.** Harbours are shared by all ships. Seeded
  from OpenStreetMap/OpenSeaMap (`10` §10.3); registered users can add and
  edit (with change history); admins merge duplicates. Legacy fields: name,
  position, website, description, WiFi list (legacy `04` §4.5 — passwords
  visible to registered users only).
- **G2 Reviews.** Registered users rate (1–5 stars) and review harbours
  (realises the legacy `Comment`/`Rating` idea).
- **G3 Map.** Legacy map (`04` §4.4) with harbour markers (clustered at low
  zoom), OpenSeaMap seamark overlay, harbour detail side panel, public photos
  near the harbour.
- **G4 Distances to next ports** (legacy distances, now automatic, `10`):
  from a selected harbour **or any map point / my GPS position**, list the
  nearest ports by **sea distance** with route preview and ETA at the ship's
  cruising speed. Clear disclaimer: "Nicht zur Navigation geeignet".
  Coverage at start: **European sea areas** only (`10` §10.1).
- **G5 Routing corrections.** Platform admins (and later trusted users) can
  add manual corrections where automatic routing is wrong (block an area, add
  a passage) — replaces the legacy manual waypoint graph editor (`05` §5.3).
- **G6 Canals, rivers and buoyed channels (stage 2).** Extend routing into
  canals/rivers and prefer marked channels, derived from OpenSeaMap
  lateral buoys/beacons and OSM waterway lines (`10` §10.5).

### H. Platform
- **H1 Content pages** (legacy): admin-managed pages (Impressum,
  Datenschutzerklärung, Hilfe, Über uns) linked in the footer/navbar.
- **H2 Mobile / offline.** Responsive, installable PWA; drafts of posts and
  queued photo uploads survive poor connectivity on board and sync later.
- **H3 Legacy import.** The existing "Kairos" data becomes one ship with its
  logbook, people (as guest crew), harbours (merged into the catalogue),
  photos and content pages (`11` phase 13).

## 7.4 Non-functional requirements
- **Security:** visibility rules enforced on the server for every query
  (never only in the UI); authorization tests per role (`08` §8.4); HTML
  sanitised; upload validation; rate limiting for login/registration.
- **Privacy (GDPR):** consent for e-mails, data export, deletion, EXIF
  stripping, Impressum/Datenschutz pages, data hosted in the EU.
- **Licences:** OSM data is ODbL — show "© OpenStreetMap-Mitwirkende" and
  OpenSeaMap attribution on maps and routing results.
- **Performance:** map and timeline load in < 2 s on 4G; images served as
  thumbnails/previews; list endpoints paged.
- **Time zones:** store UTC (`DateTimeOffset`); a trip has a time zone (default
  `Europe/Berlin`) that defines its "days".
- **Money:** `decimal` amounts + ISO 4217 currency; no floating point.

## 7.5 Decisions taken
| Topic | Decision |
|-------|----------|
| Database | PostgreSQL + PostGIS (+ pgRouting for routing) |
| Routing coverage | Europe, sea areas only at start; canals/rivers later via OpenSeaMap buoys and OSM waterways (`10` §10.5) |

## 7.6 Open questions (defaults assumed in these docs)
| # | Question | Default assumed |
|---|----------|-----------------|
| 1 | Do ship owners who are not on a trip see that trip's crew-only content? | Yes for logbook entries and costs summary; **no** for crew-only posts/photos |
| 2 | Can a public post exist inside a crew-only trip? | No — effective visibility = the most restrictive of item, trip and ship (`08` §8.3) |
| 3 | Who may write logbook entries? | Trip skipper, co-skippers, ship skippers/owners |
| 4 | Multi-currency costs? | Yes; exchange rate entered per expense (prefilled from an ECB rate feed later) |
| 5 | Are harbour edits moderated? | No pre-moderation; history + revert + report |
| 6 | Monetisation / limits (storage quotas)? | Not in scope; configurable per-user storage quota |
