# Measured weather: free observation sources and integration proposal

Status: research only, nothing implemented (2026-10-30). All sample requests below were run for real on 2026-10-30 between 16:50 and 17:10 UTC, keyless, without creating accounts or accepting terms. Region: 4–20 E, 52–60 N (North Sea, Wadden Sea, Skagerrak, Kattegat, Belts, western/southern Baltic, Norwegian south coast) plus inland waters of DE/NL/DK/SE/NO/PL.

Goal: when the weather is queried for a point, show next to the Open-Meteo forecast the **measured** values (wind, gust, direction, pressure, air temperature, visibility, waves, water temperature, water level) of the nearest fresh station or buoy.

## TL;DR

- **Open-Meteo has no observations.** Its `current` block is model output ("Current conditions are based on 15-minutely weather model data", open-meteo.com/en/docs); its historical API is reanalysis. Same for OpenWeatherMap (model/station blend) and api.met.no (nowcast).
- The good sources are the **national services' own open data**. Almost all are keyless and licensed CC BY 4.0 / CC0 / DL-DE-Zero, which means commercial use is allowed and you need attribution only:
  - **DMI** (Denmark): best overall, ~2 min latency, no key since 2025-12-02.
  - **DWD** (Germany), via **Bright Sky** JSON or the raw files.
  - **SMHI** (Sweden): the only open national source with **wave buoys** inside the region.
  - **Rijkswaterstaat** (Netherlands): waves, wind, water level and pressure at the Wadden and North Sea stations, CC0.
  - **Pegelonline** (German water levels, plus wind at about 10 spots).
  - **Kartverket** (Norwegian water level).
  - **METAR** (airports and three Dutch North Sea platforms).
- Gaps:
  - **Norway wind** needs a free MET Norway Frost client id (registration not done).
  - **German, Danish and Polish waves** are only in the Copernicus Marine in-situ files, which are 1.5–3.5 h late and NetCDF.
  - **Poland** (IMGW) has a restrictive commercial licence.
- Side finding: **the Open-Meteo free API is non-commercial only** ("Free API = non-commercial", open-meteo.com/en/pricing). The app already uses it. If the portal ever becomes commercial, it needs a paid Open-Meteo plan.

## Ranked sources

Latency means *now minus the newest value* when we tested. "Key" means a key or registration is needed.

| Rank | Source | Verdict | Region / density | Measured variables | Interval / latency | Access | Licence |
|---|---|---|---|---|---|---|---|
| 1 | **DMI metObs + oceanObs** (`opendataapi.dmi.dk/v2`) | **use** | DK incl. lighthouses, islands, Bornholm: 73 synop stations; 180 tide gauges (≈130 live) | wind, `wind_max` (10-min max, gust proxy), dir, MSL pressure, temp, visibility; water level, water temp | 10 min / **~2 min** (met), ~10 min (tide) | REST GeoJSON (OGC API Features), no key | CC BY 4.0, commercial OK |
| 2 | **DWD via Bright Sky** (`api.brightsky.dev`) | **use** (start); self-host or raw DWD files later | DE: ~160 stations north of 53.5 N (Helgoland, Sylt, Fehmarn, Arkona, Kiel-Holtenau…) | wind 10/30/60 min, gust, dir, MSL pressure, temp, visibility, cloud | 10 min / ~20–30 min | REST JSON, no key, CORS | DWD data CC BY 4.0; Bright Sky is a one-person, donation-funded service with no SLA |
| 2b | DWD Open Data files (`opendata.dwd.de`) | use (as the Bright Sky fallback) | same stations | 10-min `FF_10/DD_10`, `FX_10` gust, `PP_10` (station-level pressure), `TT_10`; hourly POI CSV with visibility | 10 min / ~25–35 min | zip/CSV per station, no key | CC BY 4.0 |
| 3 | **SMHI metObs + ocobs** (`opendata-download-*.smhi.se`) | **use** | SE west coast, Öresund, Skåne, Baltic, Gotland/Öland: ~80 met stations in bbox, 47 sea-level, 47 water-temp, **6 wave buoys** (Väderöarna, Brofjorden, Vinga, Knolls Grund…) | wind (1-min and 10-min), gust, dir, MSL pressure, temp, visibility; Hs, Tp, wave dir, water level, water temp | met: 1 min (per station) / ~8 min, hourly sets ~55 min; ocean: hourly / 1–2 h | REST JSON, no key | CC BY 4.0, commercial OK |
| 4 | **Rijkswaterstaat WaterWebservices** (`ddapi20-waterwebservices.rijkswaterstaat.nl`) | **use** | NL coast, Wadden Sea, IJsselmeer, North Sea platforms: ≈29 live wind, 28 Hm0, 105 water level, 43 water temp | wind, dir, pressure (few), Hm0, wave period, water level, water temp | 10 min / 15–20 min | POST JSON or WFS "latest value" layer, no key | **CC0** |
| 5 | **Pegelonline** (`pegelonline.wsv.de/webservices/rest-api/v2`) | **use** | DE: 155 coastal gauges (incl. LT Kiel, Helgoland, Travemünde, Warnemünde), ~440 in bbox including inland waters | water level, water temp; wind and air temp at only ~10 points (Travemünde, Alte Weser, Borkum…) | 1–15 min / **~3 min** | REST JSON, no key, one bulk call for all stations | **DL-DE→Zero-2.0** (no attribution duty) |
| 6 | **Kartverket tide API** (`vannstand.kartverket.no/tideapi.php`) | **use** (water level only) | NO south coast/Oslofjord: 11 permanent gauges (Oslo, Oscarsborg, Viker, Helgeroa, Tregde…) | water level | 10 min / 5–12 min | XML, no key | CC BY 4.0 "© Kartverket" |
| 7 | **METAR** (`aviationweather.gov/api/data`) | **use** (supplement) | 85 METAR stations in bbox incl. **EHFD, EHMG, EHHW** (Dutch offshore platforms), EDXW Sylt, EKRN Bornholm, ENCN, ESGG; sparse along the coast between airports (none at Skagen) | wind kt, gust, dir, QNH, temp, visibility (capped at 10 km) | 30–60 min / ~2–30 min | REST or 1-min cache file, no key, 100 req/min | US Government work, no licence text (public domain in practice); credit NOAA/NWS AWC |
| 8 | MET Norway **Frost** (`frost.met.no`) | **maybe**, the best candidate for NO wind; needs a free client id (registration by email, not done) | NO lighthouses/coast (Torungen, Lindesnes, Færder…; unverified) | wind, gust, dir, pressure, temp, visibility, some waves/water temp | minutes (unverified) | REST JSON, client id, about 1 req/s fair use | NLOD 2.0 / CC BY 4.0 |
| 9 | KNMI Data Platform (10-min in-situ) | maybe | NL ~50 stations incl. offshore | wind, gust, pressure, temp, visibility | 10 min / minutes | the shared anonymous key returned **429**; production needs an own free key; NetCDF per 10 min | CC BY 4.0 |
| 10 | Copernicus Marine in-situ NRT (INSITU_BAL_…_013_032 / INSITU_NWS_…_013_036) | maybe (batch waves layer) | the only keyless route to **BSH MARNET/buoys, LKN/NLWKN, Polish gauges**: BAL 181, NWS 238 platforms today | waves (VHM0, VTPK), water level, water temp, a few wind | hourly files / **1.5–3.5 h** | anonymous S3 works, but the licence assumes a registered user; NetCDF | CMEMS licence, free, commercial OK, credit "E.U. Copernicus Marine Service Information" |
| 11 | WMO WIS2 global broker (MQTT) | maybe | SMHI (10-min SYNOP, ~1 min), DWD (BUFR file links); DMI/MET Norway/KNMI not seen | synop set | ~1 min | MQTT `globalbroker.meteo.fr:8883` (everyone/everyone), BUFR decoder needed | WMO "core" data / CC BY 4.0 |
| 12 | IMGW-PIB (`danepubliczne.imgw.pl/api/data`) | maybe | PL coast: Świnoujście, Kołobrzeg, Ustka, Łeba, Hel; hydro sea level | hourly synop (integer wind, no gust), 10-min meteo, sea level | 10–60 min | REST JSON, no key | **custom terms**: private use free; commercial needs a contract unless the data counts as "high-value". Ask biznes@imgw.pl |
| 13 | FMI WFS (`opendata.fmi.fi/wfs`) | maybe/no | only the Åland tip (~19–20 E) inside the bbox | full synop, waves, mareographs | 10 min / ~3 min | WFS XML, no key | CC BY 4.0 |
| 14 | Meteostat (`data.meteostat.net`) | no for live (history ok) | DE/DK/SE land stations | temp, wind, gust, pressure | observed values ~10 h late, followed by **MOSMIX forecast rows** | keyless CSV; JSON API via RapidAPI key | CC BY 4.0 |
| 15 | NOAA ISD / GHCNh | no for live | global | synop | ISD ended in 2025; GHCNh ~2 days late | files | CC0 |
| — | Open-Meteo | **no** | — | model only | — | — | CC BY 4.0, free tier non-commercial |
| — | OpenWeatherMap current | **no** | — | model/station blend | — | key | ODbL, visible attribution |
| — | api.met.no (nowcast, oceanforecast) | **no** | — | model/radar nowcast | — | no key | NLOD/CC BY |
| — | EMODnet Physics | **no** | the platform registry still exists, but its latest-value services are gone (old ERDDAP NRT sets ended 2023/24) | — | — | WFS metadata only | CC BY 4.0 |
| — | BSH MARNET / Seegangsportal | **no** (direct) | needs a BSH login or an e-mail request | — | — | — | get it via CMEMS |
| — | BarentsWatch / Kystverket | **no** | OAuth registration; the wave layer is a forecast | — | — | — | NLOD |
| — | Netatmo, Weather Underground, Weathercloud, aprs.fi | **no** | crowd stations on land, rarely wind | — | — | OAuth/key; terms forbid storing or third-party apps | — |
| — | sensor.community | no (maybe for temperature/pressure) | land, low quality, no wind | temp, pressure | 1–5 min | keyless | DbCL (third-party claim only) |
| — | Ogimet | no (dev fallback) | DWD synop | synop text | ~10 min | keyless scraping site | no terms; "please do not abuse" |

### What each test point gets (live, 2026-10-30)

| Point | Wind/pressure | Waves | Water level / temp |
|---|---|---|---|
| Kiel Lighthouse 54.50 N 10.27 E | DWD Kiel-Holtenau, 16 km (Leuchtturm Kiel 10044 has had empty data since 2026-08); DWD Schönhagen 14 km; METAR EDHK | none live (CMEMS: BSH Kiel lighthouse/MARNET, hours late) | Pegelonline LT KIEL (1 min) |
| Fehmarnbelt 54.6 N 11.15 E | DWD Fehmarn 10 km; DMI stations on Lolland | none live | DMI Rødby / Gedser; Pegelonline Marienleuchte |
| Skagen 57.72 N 10.59 E | DMI Skagen Fyr 06041 (2 min) | SMHI Väderöarna / Brofjorden (~60 km) | DMI Frederikshavn, Hirtshals |
| Göteborg 57.7 N 11.9 E | SMHI Göteborg A, Vinga A, Måseskär A; METAR ESGG | SMHI Vinga buoy | SMHI Göteborg-area gauges, Vinga water temp |
| Oslofjord 59.4 N 10.6 E | METAR ENRY/ENTO only (Frost needs a client id) | none | Kartverket Oscarsborg, Oslo, Viker |
| Dutch coast / Wadden Sea | RWS, METAR EHFD/EHMG/EHHW, KNMI (needs a key) | RWS Hm0 (e.g. Eierlandse Gat) | RWS |

## Licence quotes and attribution

| Source | Quote / link | Attribution string for the UI |
|---|---|---|
| DMI | "as of Dec 2nd 2025 authentication and API keys are no longer required" (dmi.dk/friedata/dokumentation/authentication). Licence "Creative Commons License CC BY 4.0", use "for any purpose, even commercially"; "DMI may apply rate limiting if misuse is detected" (dmi.dk/friedata/dokumentation/terms-of-use) | "Data: DMI (CC BY 4.0)" |
| DWD | "The Creative Commons BY 4.0 - Licence "CC BY 4.0" apply." (opendata.dwd.de/climate_environment/CDC/Terms_of_use.txt, dwd.de/copyright) | "Quelle: Deutscher Wetterdienst" (modified: "Datenbasis: Deutscher Wetterdienst, eigene Bearbeitung"); with Bright Sky add "via Bright Sky" |
| SMHI | "Creative commons Erkännande 4.0 SE … Detta är tillåtet även för kommersiella ändamål"; "Du ska ange SMHI som källa" (smhi.se/data/om-smhis-data/villkor-for-anvandning) | "Källa: SMHI (CC BY 4.0)" |
| Rijkswaterstaat | "Op de inhoud van de WaterWebservices is de Creative Commons zero verklaring (CC0) van toepassing" (rijkswaterstaatdata.nl/waterdata) | none required, "Rijkswaterstaat" as a courtesy |
| Pegelonline | data "unter der Lizenz DL-DE->Zero-2.0 frei verfügbar"; "ungeprüfte Rohdaten" (pegelonline.wsv.de/webservice/ueberblick) | none required, "WSV / PEGELONLINE" as a courtesy |
| Kartverket | `licenseurl` points to kartverket.no/api-og-data, CC BY 4.0 | "© Kartverket" |
| METAR | no licence text on aviationweather.gov/data/api; US government work. Rules: "maximum 100 requests per minute", custom User-Agent | "NOAA/NWS Aviation Weather Center" |
| MET Norway Frost | NLOD 2.0 and CC BY 4.0 (api.met.no/doc/License); error payload links CC BY 3.0 NO | "Data from MET Norway" |
| KNMI | dataset licence "Creative Commons Attribution 4.0" | "Bron: KNMI" |
| CMEMS | "free of charge", "worldwide, non exclusive, royalty free, perpetual"; users "receive a login and password" (marine.copernicus.eu/user-corner/service-commitments-and-licence) | "E.U. Copernicus Marine Service Information" + product DOI |
| IMGW | "Źródłem pochodzenia danych jest Instytut Meteorologii i Gospodarki Wodnej – Państwowy Instytut Badawczy"; commercial use "requires signing a contract" for non-high-value data (danepubliczne.imgw.pl/pl/regulations) | the quoted sentence |
| Open-Meteo | "API data are offered under Attribution 4.0 International (CC BY 4.0)"; "You must include a link next to any location Open-Meteo data are displayed"; free API non-commercial (open-meteo.com/en/licence, /en/pricing) | "Weather data by Open-Meteo.com" (already due today) |

All the "use" sources allow showing **and storing** the data in non-commercial and commercial apps, with attribution where CC BY applies. Common terms: no guarantee of availability; the data is raw and not quality controlled; poll at the update cycle and cache. SMHI adds "don't distribute outdated data", so always show the observation time.

## Sample requests and responses (real, trimmed)

**DMI, Skagen Fyr (06041), newest wind:**
```
curl -s "https://opendataapi.dmi.dk/v2/metObs/collections/observation/items?stationId=06041&parameterId=wind_speed&limit=1&sortorder=observed,DESC"
{"type":"FeatureCollection","features":[{"type":"Feature","geometry":{"type":"Point","coordinates":[10.6316,57.7364]},
 "properties":{"parameterId":"wind_speed","created":"2026-10-30T17:01:39.490812Z","value":4.45,"observed":"2026-10-30T17:00:00Z","stationId":"06041"}}],
 "timeStamp":"2026-10-30T17:05:58Z","numberReturned":1, ...}
```
All parameters of a station since a time: `...items?stationId=06041&datetime=2026-10-30T16:40:00Z/..&limit=100` returns `pressure_at_sea 1030.16`, `temp_dry 11.3`, `visibility 18408`, `wind_dir 274.2`, `wind_max 6.17`, `wind_speed 4.78` (all 16:50Z). Station catalogue: `.../metObs/collections/station/items?bbox=4,52,20,60&limit=1000`. Tide gauge: `.../oceanObs/collections/observation/items?stationId=31623&parameterId=tw&limit=1&sortorder=observed,DESC` returns `{"parameterId":"tw","value":11.1,"observed":"2026-10-30T16:50:00Z","stationId":"31623"}` (Gedser).

**Bright Sky (DWD), Fehmarnbelt:**
```
curl -s "https://api.brightsky.dev/current_weather?lat=54.6&lon=11.15"
{"weather":{"source_id":11659,"timestamp":"2026-10-30T17:00:00+00:00","cloud_cover":100,"condition":"dry","dew_point":10.61,
 "pressure_msl":1031.9,"relative_humidity":90,"visibility":21278,"wind_direction_10":310,"wind_speed_10":16.6,"wind_speed_60":15.8,
 "wind_gust_direction_10":310, ...},
 "sources":[{"id":11659,"station_name":"Fehmarn","distance":9800, ...}]}
```
Default units: km/h, hPa, °C, m. With `max_dist=3000` at Kiel Lighthouse the answer is `{"detail":"Could not find current weather for your location criteria"}`. So pass `max_dist` and expect a 404.

**DWD raw 10-min file, Kiel-Holtenau (02564):**
```
curl -s -o w.zip https://opendata.dwd.de/climate_environment/CDC/observations_germany/climate/10_minutes/wind/now/10minutenwerte_wind_02564_now.zip
STATIONS_ID;MESS_DATUM;  QN;FF_10;DD_10;eor
       2564;202610301620;    2;   2.8; 260;eor
```

**SMHI, Nidingen A (71190), 1-min wind (m/s, epoch ms):**
```
curl -s "https://opendata-download-metobs.smhi.se/api/version/1.0/parameter/47/station/71190/period/latest-hour/data.json"
... {"date":1793379360000,"value":"3.905267","quality":"G"},{"date":1793379420000,"value":"3.854518","quality":"G"}]}
```
All stations, one parameter, latest hour: `.../parameter/{4|21|9|1|12}/station-set/all/period/latest-hour/data.json`. Waves, Väderöarna buoy: `https://opendata-download-ocobs.smhi.se/api/version/latest/parameter/1/station/33015/period/latest-day/data.json` gives Hs `14:00 0.49`, `15:00 0.46`, `16:00 0.42` m.

**Rijkswaterstaat, significant wave height at Eierlandse Gat:**
```
curl -s -X POST https://ddapi20-waterwebservices.rijkswaterstaat.nl/ONLINEWAARNEMINGENSERVICES/OphalenLaatsteWaarnemingen -H "Content-Type: application/json" \
 -d '{"AquoPlusWaarnemingMetadataLijst":[{"AquoMetadata":{"Compartiment":{"Code":"OW"},"Grootheid":{"Code":"Hm0"}}}],"LocatieLijst":[{"Code":"eierlandsegat"}]}'
{"Succesvol":true,"WaarnemingenLijst":[{"Locatie":{"Code":"eierlandsegat","Lat":53.2762,"Lon":4.660347,...},
 "MetingenLijst":[{"Meetwaarde":{"Waarde_Numeriek":41.0},"Tijdstip":"2026-10-30T17:40:00.000+01:00","WaarnemingMetadata":{"Statuswaarde":"Ongecontroleerd",...}}],
 "Grootheid":{"Code":"Hm0"},"Eenheid":{"Code":"cm"}}]}
```
All latest wind values in one call (WFS): `https://geo.rijkswaterstaat.nl/services/ogc/hws/DDAPI20/ows?SERVICE=WFS&VERSION=1.1.0&REQUEST=GetFeature&TYPENAME=locatiesmetlaatstewaarneming&outputFormat=application/json&CQL_FILTER=GROOTHEIDCODE='WINDSHD'`. Filter by `TIJDSTIP_LAATSTE_METING`, because many catalogue stations are retired.

**Pegelonline, Travemünde wind (m/s) and all coastal gauges in one call:**
```
curl -s "https://www.pegelonline.wsv.de/webservices/rest-api/v2/stations/TRAVEM%C3%9CNDE/WG/currentmeasurement.json"
{ "timestamp": "2026-10-30T18:03:00+01:00", "value": 4.3 }
curl -s "https://www.pegelonline.wsv.de/webservices/rest-api/v2/stations.json?includeTimeseries=true&includeCurrentMeasurement=true&latitudeMin=52&latitudeMax=60&longitudeMin=4&longitudeMax=20"   # 754 KB, all stations
```

**Kartverket, Oscarsborg (Oslofjord) water level, cm above chart datum:**
```
curl -s "https://vannstand.kartverket.no/tideapi.php?tide_request=stationdata&stationcode=OSC&fromtime=2026-10-30T16:00&totime=2026-10-30T19:00&datatype=obs&refcode=cd&lang=en&interval=10&dst=0&tzone=0"
<location name="Oscarsborg" code="OSC" latitude="59.678073" longitude="10.604861">
 <waterlevel value="72.9" time="2026-10-30T16:50:00+00:00" flag="obs"/>
```
Use `stationdata`. `locationdata` blends the prediction with the observed weather effect, so it is not a pure measurement.

**METAR, bbox (lat1,lon1,lat2,lon2):**
```
curl -s -A "sms-portal/1.0" "https://aviationweather.gov/api/data/metar?bbox=54.2,9.9,54.7,10.6&format=raw"
METAR EDHK 301620Z 25006KT 7000 FEW004 BKN026 11/11 Q1032
format=json, offshore platform:
{"icaoId":"EHFD","obsTime":1793377500,"temp":14,"dewp":11,"wdir":210,"wspd":2,"visib":"6+","altim":1032,
 "rawOb":"METAR EHFD 301625Z AUTO 21002KT 9999 FEW013/// 14/11 Q1032 W///H6","lat":54.854,"lon":4.696,"name":"F3-FB-1 Sea, , NL"}
```
For polling, use `https://aviationweather.gov/data/cache/metars.cache.csv.gz`: the whole world in one request, refreshed every minute. A bbox query with `hours=3` returned 504; `hours=1` works.

**Open-Meteo, for comparison (model, not a measurement):**
```
curl -s "https://api.open-meteo.com/v1/forecast?latitude=54.5&longitude=10.27&current=temperature_2m,wind_speed_10m,wind_gusts_10m,pressure_msl"
"current":{"time":"2026-10-30T16:45","interval":900,"temperature_2m":12.7,"wind_speed_10m":7.7,"wind_gusts_10m":11.5,"pressure_msl":1032.0}
```

## How weather works in the code today

- `src/Sms.Api/Weather.cs`: `IWeatherProvider` → `OpenMeteoProvider` (forecast + marine API, `IMemoryCache` 15 min per 0.01° cell, 1 min negative cache on failure); `XweatherOverlayProvider` (raster/MapsGL tiles, key in user-secrets); `WeatherRecorder` stores the provider's **model `current`** into `WeatherObservation` at live positions (debounce 1 nm / 15 min).
- `src/Sms.Api/Endpoints/Weather.cs`: `GET /api/weather?lat&lng`, `GET /api/harbours/{id}/weather`, `POST /api/weather/route`, `GET /api/trips/{id}/weather[/summary]`.
- **Precedent for a nearest-station lookup:** `src/Sms.Api/Tides.cs`. `ITideProvider.ForecastAsync(lat, lng, maxKm)` returns `TideForecast(Provider, StationId, StationName, StationLat, StationLng, DistanceKm, …, Attribution, CommercialUse, …)`, and `GET /api/weather/tides` returns 204 when no station lies within `Tides:MaxStationKm`. The observation lookup should mirror this shape.
- Naming caveat: the entity `WeatherObservation` already exists, but most of its rows are **model** values; only `Provider = "onboard"` rows are measured, by the boat. A station value needs its own marker (see below) so that `WeatherSummary.From` does not average model and station values together.
- Background job precedent: `EcbRateJob` / `AisPoller` (`BackgroundService`), registered in `Program.cs`.

## Integration plan (top 3: DMI, DWD/Bright Sky, SMHI)

These three cover the DE/DK/SE coasts with wind, gust, pressure, temperature and visibility. They also bring DK and SE water level and SE waves. Phase 2 adds the cheap marine feeds (Pegelonline, Rijkswaterstaat, Kartverket) and METAR. Phase 3 adds Frost and KNMI, once someone registers the free keys.

### Data model

One new table: the station catalogue. Latest values stay in memory (cache) and are not stored, except when a live position records them.

```csharp
// Sms.Domain
public class ObservationStation
{
    public string Id { get; set; } = "";          // "dmi:06041", "smhi:71190", "dwd:02564"
    public string Provider { get; set; } = "";    // "DMI" | "SMHI" | "DWD"
    public string ExternalId { get; set; } = "";
    public string Name { get; set; } = "";
    public Point Location { get; set; } = null!;  // PostGIS, GiST index
    public ObservedVariables Variables { get; set; }  // [Flags] Wind, Pressure, Temperature, Visibility, Waves, WaterLevel, WaterTemp
    public DateTimeOffset? LastSeen { get; set; }  // newest value seen; stations silent > 7 d are skipped
}

// Sms.Api, the API shape (like TideForecast)
public sealed record ObservedValue(string StationId, string StationName, double StationLat, double StationLng, double DistanceKm, DateTimeOffset Time, string Attribution);
public sealed record MeasuredWeather(
    (ObservedValue Src, double Kn, double? GustKn, double? DirDeg)? Wind,
    (ObservedValue Src, double Hpa)? Pressure, (ObservedValue Src, double C)? Temperature, (ObservedValue Src, double M)? Visibility,
    (ObservedValue Src, double HeightM, double? PeriodS, double? DirDeg)? Waves,
    (ObservedValue Src, double Cm, string Datum)? WaterLevel, (ObservedValue Src, double C)? WaterTemp);
```

Units are normalised at the provider boundary, the same as `OpenMeteoProvider` asks for knots:

- m/s and km/h become kn;
- Pa becomes hPa, using the MSL pressure where it exists (DWD `PP_10` is station level);
- cm stays cm, and the datum travels with the value (DVR90, NAP, chart datum…).

### Provider interface

```csharp
public interface IObservationProvider
{
    string Name { get; }             // "DMI"
    string Attribution { get; }      // "Data: DMI (CC BY 4.0)"
    Task<IReadOnlyList<ObservationStation>> StationsAsync(CancellationToken ct);           // catalogue import
    Task<IReadOnlyList<StationReading>> LatestAsync(IReadOnlyList<string> externalIds, CancellationToken ct); // newest value per variable, cached inside
}
```

Each provider fetches its data in whatever way is cheapest for that source and caches the result:

| Provider | Catalogue | Latest values | Cache |
|---|---|---|---|
| DMI | `metObs/collections/station/items?bbox=4,52,20,60` (keep `type=Synop`, `validTo` empty; deduplicate rows) + `oceanObs/collections/station/items?bbox=…` | per station: `observation/items?stationId={id}&datetime={now-40min}/..&limit=300`, keep the newest per `parameterId` | 5 min per station (data every 10 min, ~2 min latency) |
| DWD (Bright Sky) | `/sources?lat&lon&max_dist` over a coarse grid, or the DWD `zehn_now_ff_Beschreibung_Stationen.txt` | `/current_weather?dwd_station_id={id}` | 10 min per station |
| SMHI | `/parameter/{p}.json` (`active: true`, inside the bbox) for met parameters 4, 3, 21, 9, 1, 12 and ocean parameters 1, 5, 6 | met: `/parameter/{p}/station-set/all/period/latest-hour/data.json` (6 calls cover all stations); ocean: per station `latest-day` | met 10 min (one entry per parameter, shared by all stations); ocean 30 min |

The provider classes go in a new `src/Sms.Api/Observations.cs`, next to `Weather.cs`, all using the existing `"weather"` named `HttpClient`. Register them as `IEnumerable<IObservationProvider>`, the same way `AisPoller` takes `IEnumerable<IAisPositionProvider>`.

### Station catalogue import

- `ObservationCatalogJob : BackgroundService`, built like `EcbRateJob`. It runs at startup when the table is empty and then weekly. For each provider it calls `StationsAsync`, upserts by `Id` and leaves stations that have disappeared in place, because `LastSeen` ages them out.
- Size: about 73 + 180 (DMI), ~250 (DWD in bbox), ~200 (SMHI) rows. Phase 2 adds ~500 (Pegelonline + RWS + Kartverket + METAR).
- An admin "re-import now" button can follow later if needed.

### Nearest-measured query

`GET /api/weather/observed?lat&lng[&maxKm=40][&maxAgeMin=90]`, built like `/api/weather/tides`. It returns 204 when nothing is in range. The steps:

1. PostGIS: `stations WHERE ST_DWithin(Location::geography, p, maxKm*1000) AND (LastSeen IS NULL OR LastSeen > now-7d) ORDER BY Location <-> p LIMIT 15`. These are the same EF calls as `Pois.cs` (`IsWithinDistance`, `OrderBy(Distance)`).
2. Group the candidates by provider and call `LatestAsync` in parallel. Most calls hit the cache.
3. For each variable group (wind+gust+dir, pressure, temperature, visibility, waves, water level, water temp), take the **nearest station that has that variable fresher than the limit**. The limit is 90 min for met data and 3 h for ocean data, because SMHI ocean data runs 1–2 h late. So wind may come from Skagen Fyr and waves from Väderöarna. Each value carries its station, distance, time and attribution.
4. Update `LastSeen` as a side effect, or once a day.

The same endpoint serves:

- the harbour panel and the "Wettervorhersage hier" context menu: a "Gemessen" block under "Aktuell (Modell)", with station name, distance and "vor 6 min";
- the companion app's live positions.

Keep it separate from `GET /api/weather` so that the forecast cache and its response shape stay as they are. The UI then lists the attributions of the stations it actually shows.

### Live tracking

`WeatherRecorder` can, in the same debounced call, also store the nearest measured values. This is optional and can come in phase 2.

The work in progress on the companion app (uncommitted on 2026-10-30) already separates rows by `Provider`:

- `Provider = "onboard"` means boat sensors, recorded by `RecordOnboardAsync` with `WaterTempC` and `DepthM`;
- any other value is model data;
- the summary prefers onboard wind.

Station values would be a third kind, for example `Provider = "station"`, plus the following nullable columns:

- `StationId` and `StationDistanceKm`;
- `VisibilityM` and `WaterLevelCm`.

With these columns, `DueAsync` and `WeatherSummary` filter by kind. The priority would be onboard, then station, then model. The catch: storing a station value at the boat's position is misleading when the station is far away. Only store it when `StationDistanceKm` ≤ ~20 km, and show the distance.

### Refresh, limits and failure

- No background polling of values in phase 1. Every value is fetched on demand and cached per station or per parameter set. With N boats near the same coast this costs at most one request per station per 5–10 min, which is well inside "do not misuse" for DMI and SMHI. Bright Sky is a donation service: if usage grows, run its Docker image or read the DWD files directly.
- A provider failure caches `null` for 1 min, as `OpenMeteoProvider` does. The other providers still answer, and the response simply lacks those variables.
- Phase 2 sources with cheap bulk endpoints should be fetched in bulk with one cache entry each:
  - Pegelonline `stations.json?…includeCurrentMeasurement=true`: 1 call, 2 min cache;
  - RWS WFS per quantity: 4 calls, 10 min;
  - METAR `metars.cache.csv.gz`: 1 call, 5 min;
  - Kartverket all stations: 1 call, 10 min.
- Set a descriptive `User-Agent` on the `"weather"` client. METAR requests one and api.met.no requires one.

### Config

None of the top 3 needs a key. Phase 3 keys go in user-secrets, the same way as Xweather:

- `Observations:FrostClientId`;
- `Observations:KnmiApiKey`.

Each provider gets an `Observations:{Provider}:Enabled` switch, so a source can be turned off without a deploy.

## Open questions / next steps

1. Register a free **Frost client id** (MET Norway) for Norwegian wind. Today there is no keyless measured wind on the Oslofjord or the south coast, only METAR at ENRY/ENTO/ENCN.
2. Waves on the German, Danish and Polish coasts: either accept the CMEMS in-situ NetCDF (1.5–3.5 h late, licence assumes registration), or show "keine Messung" there.
3. IMGW (Poland): fine while the portal is non-commercial; ask biznes@imgw.pl before going commercial.
4. Open-Meteo free tier is **non-commercial**. Budget a paid plan, or switch the forecast to DWD/MET Norway models, before any commercial use.
5. Raw research notes, with every request tried, failures and station counts, are in the side-session notes folder (`weather-observations/*-findings.md`). They are not in the repo.
