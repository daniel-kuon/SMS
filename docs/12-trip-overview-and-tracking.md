# 12. Trip Overview, Ship Tracks and Media on the Map

Every trip gets an **overview map** showing where the ship actually sailed and
where photos and posts were created. The track comes from the best available
source:

1. **GPS track** – recorded by a GPS tracking app (live upload) or imported
   from a file (GPX/KML/NMEA),
2. **AIS track** – pulled from the **MarineTraffic API** (or another AIS
   provider) if the ship owner configured it,
3. **Calculated legs** – sea routes computed by the routing service (`10`)
   between the start and end harbours of the logbook entries,
4. **Planned legs** – the skipper's plan (`07` C3), shown separately as a plan.

Related docs: requirements `07` (C4, E1–E3, I1–I6), data model `08`, routing `10`,
phases `11`.

## 12.1 Track sources

### A. Calculated legs (always available)
- For each logbook entry: route from start harbour to end harbour via
  `/api/routing/route` (`10` §10.2), cached on the entry
  (`LogbookEntry.CalculatedRoute`, `CalculatedDistanceNm`).
- Timing: linear interpolation between `DepartedAt` and `ArrivedAt` along the
  route (needed to place media by time, §12.4).
- Recomputed when start/end harbour change or the routing graph version changes.
- Shown dashed: "berechnete Route".

### B. GPS tracking (app or file)
**Live upload from tracking apps** – one `TrackingDevice` per phone/boat
computer, created either by a ship member (ship-wide device, e.g. boat
computer) or by a crew member for one trip (trip-scoped, e.g. a guest's phone);
each device has a secret token (shown
once, stored hashed). Supported protocols (all over HTTPS):

| Protocol | Typical apps | Endpoint |
|----------|-------------|----------|
| OsmAnd protocol (HTTP GET/POST with query parameters `id`, `lat`, `lon`, `timestamp`, `speed`, `bearing`, `altitude`, `accuracy`) | Traccar Client, OsmAnd, GPSLogger (custom URL) | `/api/tracking/osmand?id={token}&…` |
| OwnTracks HTTP mode (JSON `_type: location`, `lat`, `lon`, `tst`, `vel`, `cog`, `acc`) | OwnTracks (Android/iOS) | `POST /api/tracking/owntracks` (token as Basic-auth password or header) |
| Native JSON batch | the portal's PWA / future native app, Signal K boat server or other integrations | `POST /api/tracking/points` `{ deviceToken, points: [{t, lat, lon, sog, cog, acc}] }` |

Verify the exact parameter names of each app protocol when implementing.

Rules:
- Points are assigned to the ship of the device and to the trip whose date
  range (in trip time zone) contains the timestamp and whose status is
  Active/Completed; otherwise they stay as "unassigned" ship track (visible to
  ship members, can be assigned later).
- Duplicates (same device + timestamp) ignored; points with accuracy > 100 m
  or implausible speed (> 30 kn for sailing yachts, configurable per ship)
  dropped as outliers.
- The PWA itself can record (button "Track aufzeichnen") but browsers only
  deliver positions reliably while the app is open; recommend Traccar Client /
  OwnTracks for background recording, a native app (e.g. Capacitor) later.

**File import** – crew uploads GPX (tracks/routes), KML/KMZ, or NMEA 0183 log
(`$GPRMC`/`$GPGGA`) exported from chart plotters, Navionics, Garmin,
OpenCPN, etc.; assigned to a trip (default: by timestamps) and source
`GpsFile`.

### C. AIS via MarineTraffic API (optional, configured by ship owners)
- Ship owner enters in ship settings: provider (`MarineTraffic` first), API
  key(s), MMSI (already a ship field) or IMO, polling interval (default 30 min),
  enabled yes/no. API keys are stored encrypted (ASP.NET Core Data Protection),
  never returned to the client (only "configured ✔").
- **Live:** during Active trips a background job polls the provider's
  single-vessel position service and stores new positions as source `Ais`.
- **History:** at trip end (or on demand) the job requests the vessel's
  historical track for the trip period to fill gaps.
- MarineTraffic API services are paid per call/credits and the API has moved
  under Kpler — check current product names, endpoints, pricing and terms when
  implementing; show the owner an estimate of calls per trip and stop polling
  when the trip ends.
- Implement as `IAisPositionProvider` so free/cheaper providers can be added
  (e.g. aisstream.io websocket, AISHub, VesselFinder, Datalastic).
- Many small yachts send AIS class B only (lower terrestrial coverage) or have
  no AIS — the UI must handle sparse AIS tracks (fill gaps with calculated legs).

## 12.2 Combining sources into the trip track

The trip track is built **per logbook entry / per day** (segments) using the
best source with enough data:

```
priority: GpsLive / GpsFile  >  Ais  >  Calculated (from logbook entries)
```
- A segment counts as covered by a source if it has points spanning ≥ 80 % of
  the time between departure and arrival and no gap > 30 min (config).
- Gaps inside a recorded segment longer than 30 min are bridged with the
  calculated sea route between the gap's end points (dashed).
- Days without logbook entries but with GPS/AIS points (e.g. day trips not
  logged) are still shown; the UI may suggest creating a logbook entry
  (§12.3 stop detection).
- The trip skipper and crew with "Logbuch schreiben" can override the source per segment
  ("GPS-Track verwenden" / "Berechnete Route verwenden") and hide points
  (privacy, e.g. home address).

Track processing job (after each upload/poll batch, debounced):
1. Clean (outliers, duplicates), sort by time.
2. Build per-day LineStrings; store simplified geometries for display
   (`ST_SimplifyPreserveTopology`, ~10 m tolerance; plus a coarse version for
   the ship page).
3. Statistics per segment/day/trip: distance over ground (geodesic, nm),
   time underway, max/avg speed (SOG).
4. **Stop detection**: positions staying within 200 m for > 30 min near a
   harbour → "Aufenthalt in <Harbour>"; used to suggest logbook entries
   (start/end harbour and times pre-filled — extends legacy defaults `05` §5.4)
   and to fill the sailed distance of entries.
5. Re-place media whose location is derived from the track (§12.4).

## 12.3 Trip overview page (`/trips/:id`, tab "Übersicht")

Layout (desktop: map left ~65 %, panel right; phone: map on top, panel below):

- **Map** (Leaflet, OSM + OpenSeaMap overlay):
  - Track coloured **per day** (legend with day colours); solid = GPS/AIS,
    dashed = calculated, dotted light = planned legs (toggle).
  - Harbour markers for overnight stops with nights count.
  - **Photo markers**: small round thumbnails at the photo location, clustered
    at low zoom (cluster shows count + first thumbnail); click opens the
    lightbox (PhotoSwipe) with all photos of that cluster/day.
  - **Post markers**: speech-bubble icon; click shows a popup with title,
    author avatar, first lines, first photo, link "Weiterlesen".
  - Live position (Active trips with GPS/AIS): boat icon with heading,
    timestamp "vor 12 min"; for public viewers delayed (§12.5).
  - Optional "Abspielen" (playback): animate the boat along the track by time,
    showing photos/posts as they appear.
- **Side panel**:
  - Header stats: total distance (track-based, else calculated), time
    underway, engine hours (from logbook), harbours visited, nights, number of
    photos/posts; costs total (crew only).
  - Day list: date, from → to, distance, weather/wind from logbook, photo
    thumbnails; hovering a day highlights its segment and zooms the map;
    clicking opens the day in the timeline (`07` E2).
  - Filters: days, content type (Fotos, Beiträge, Logbuch), author.
- Everything shown respects visibility (`08` §8.3): a viewer only gets
  segments, media and posts they may see.

**Ship page** (`07` B3): all trips' tracks on one map (coarse geometries),
colour per trip, click → trip overview.

## 12.4 Location of photos and posts

Each `Media` and `Post` gets `Location` + `LocationSource`, determined in this
order (first that applies):

| # | Source (`LocationSource`) | Applies to |
|---|---------------------------|------------|
| 1 | `Manual` – user placed it on the map / picked a harbour | posts, photos |
| 2 | `Exif` – GPS from photo EXIF (read before EXIF is stripped from the delivered file) | photos |
| 3 | `Device` – browser geolocation at creation time (opt-in prompt "Standort hinzufügen") | posts, photos taken with the PWA camera |
| 4 | `Track` – interpolated on the ship track at the capture/creation time (EXIF `DateTimeOriginal` or post time), if a track point exists within ±30 min | posts, photos |
| 5 | `Harbour` – harbour where the ship was at that time (from logbook entry / stop detection) | posts, photos |
| 6 | none – shown in the day list but not on the map | |

- Sources 4/5 are recomputed when the track or logbook changes; 1–3 are fixed.
- Users can move a marker ("Position korrigieren") → becomes `Manual`.
- Camera clocks are often wrong: per upload batch allow a time offset
  ("Kamerazeit korrigieren: +1 h").

## 12.5 Privacy rules for tracks and positions

- Track visibility follows the trip (`08` §8.3) but can be lowered separately
  (`Trip.TrackVisibility`, default = trip visibility).
- **Public live position delay** (`Trip.PublicPositionDelayMinutes`, default
  120, options 0 / 60 / 120 / 360 / "nur nach Ende"): public viewers see the
  track and boat position only up to now − delay; crew sees live.
- **Privacy zones**: ship owners can define circles (e.g. home address,
  private moorings) where points are hidden from everyone except the crew of
  the trip (and ship members for unassigned points).
- Ship owners who are not crew of a trip see its track only as far as it is
  public (same rules as the public, incl. the delay).
- Photo/post location precision for public viewers can be reduced
  ("ungefähr", rounded to ~1 km) per user setting.
- Track points are personal data of the crew: included in data export, deleted
  with the trip/ship; device tokens revocable at any time.

## 12.6 Data model additions (see `08` §8.1)

| Entity | Key fields |
|--------|-----------|
| `TrackingDevice` | Id, ShipId, UserId (owner of the device), Name, Protocol (`OsmAnd`/`OwnTracks`/`Json`), TokenHash, Active, LastSeenAt |
| `ShipAisConfig` | ShipId (PK), Provider, ApiKeyEncrypted, Mmsi, Imo, PollIntervalMinutes, Enabled, LastPolledAt, CallsThisMonth |
| `TrackPoint` | Id (bigint), ShipId, TripId?, Source (`GpsLive`/`GpsFile`/`Ais`), DeviceId?, ImportId?, Time (timestamptz), Location (geography Point), SogKn?, CogDeg?, AccuracyM?, IsOutlier, IsHidden | 
| `TrackImport` | Id, ShipId, TripId?, UploadedById, FileName, Format (`Gpx`/`Kml`/`Nmea`), PointCount, CreatedAt |
| `TrackSegment` | Id, TripId, LogbookEntryId?, Day, Source (`GpsLive`/`GpsFile`/`Ais`/`Calculated`/`Mixed`), SourceOverride?, StartTime, EndTime, DistanceNm, DurationMinutes, MaxSogKn, AvgSogKn, Geometry (simplified LineString), GeometryCoarse | materialised result of §12.2 |
| `PrivacyZone` | Id, ShipId, Center (Point), RadiusM, Name |
| additions | `LogbookEntry.CalculatedRoute` (LineString), `CalculatedDistanceNm`; `Media.Location`, `Media.LocationSource`; `Post.Location`, `Post.LocationSource`; `Trip.TrackVisibility`, `Trip.PublicPositionDelayMinutes` |

`TrackPoint` grows fast (one point every few seconds): index
`(ShipId, Time)` (BRIN on `Time` works well), partition by month if needed,
and allow thinning old raw points to e.g. one per 30 s after processing.

## 12.7 API (suggested)

| Method & route | Purpose |
|----------------|---------|
| `GET /api/trips/{id}/overview` | stats, days, harbour stops (visibility-filtered) |
| `GET /api/trips/{id}/track?detail=full|coarse` | GeoJSON FeatureCollection of `TrackSegment`s (properties: day, source, distance, colour index) + planned legs |
| `GET /api/trips/{id}/map-items?day=` | photos/posts with location: `{type, id, lat, lng, thumbUrl, title, authorName, day}` |
| `GET /api/trips/{id}/position` | latest position (delayed for public viewers) |
| `PUT /api/track-segments/{id}/source` | override source (skipper/owner) |
| `POST /api/ships/{id}/track-imports` | upload GPX/KML/NMEA (multipart) |
| `GET/POST/DELETE /api/ships/{id}/tracking-devices` | manage devices; POST returns the token + setup instructions (URL per app, QR code) |
| `GET|POST /api/tracking/osmand`, `POST /api/tracking/owntracks`, `POST /api/tracking/points` | ingest (token auth, rate limited, no cookies) |
| `GET/PUT/DELETE /api/ships/{id}/ais-config`, `POST /api/ships/{id}/ais-config/test`, `POST /api/trips/{id}/ais/fetch-history` | AIS configuration (owners only) |
| `GET/POST/DELETE /api/ships/{id}/privacy-zones` | privacy zones (owners) |
| `PUT /api/media/{id}/location`, `PUT /api/posts/{id}/location` | manual correction |

## 12.8 Tests
- Parsers: GPX (tracks + routes, multiple segments), KML, NMEA RMC/GGA, OsmAnd
  query, OwnTracks JSON.
- Segment building: priority rules, gap bridging, override, days across time zones.
- Media placement order (§12.4) incl. interpolation and camera time offset.
- Privacy: public delay, privacy zones, track visibility lower than trip;
  ingest endpoints reject unknown/revoked tokens.
- AIS provider: mocked HTTP client; polling stops after trip end; API key
  never serialised.
