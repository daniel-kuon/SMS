# SMS Companion (Windows)

Tray app that sends the boat's position and on-board sensor values to the SMS portal, as an alternative to MarineTraffic.
It reads NMEA 0183 (UDP, TCP, serial), sends one point per interval to `POST /api/ingest/points`, and uploads new track files from watch folders to `POST /api/ingest/files`.

## Setup

1. In the portal, open the ship, go to Tracking and create a device (protocol "JSON"). Copy the token; it is shown only once.
2. Start `SmsCompanion.exe`. The first time it opens the settings: enter the server URL and the device token, then add the sources and folders.
   - A **UDP** source listens on a local port (TimeZero, OpenCPN, most Wi-Fi multiplexers: 10110 or 2000).
   - A **TCP** source connects to `host:port` (a multiplexer, or TimeZero's TCP output).
   - A **Serial** source opens a COM port: 4800 baud for NMEA 0183 devices, 38400 for AIS multiplexers.
   - **Watch folders** take semicolon-separated patterns.
3. Optional: tick "Start with Windows". This writes `HKCU\...\Run\SmsCompanion`.

Settings live in `%APPDATA%\SmsCompanion\settings.json`; the token is DPAPI-encrypted for the current Windows user. The folder also holds:
- `queue.jsonl`: unsent points, resent oldest first.
- `rejected.jsonl`: batches the server refused with 400.
- `uploads.json`: uploaded files by path, size, time and SHA-256.
- `companion.log`.

## Sending

- **One point per send interval** (by fix time; default 60 s). It holds the latest fix plus the sensor values received in the last 2 minutes: heading, depth, true/apparent wind, pressure, air/water temperature.
- **First fix:** sent at once.
- **Little movement:** if the boat moved less than the minimum movement, at most one point per 15 minutes is sent.
- **Offline:** points stay in the queue, and the app retries every 30 s.
- **Weather:** after a successful send, the status window shows the portal's model weather for the position (`GET /api/weather`). The portal itself records the weather for every live position. The status window also shows the on-board values the app sends.

## Parsed NMEA sentences

| Sentence | Data |
|---|---|
| RMC, GGA, GLL, VTG | position, SOG/COG, accuracy (HDOP x 5 m) |
| HDT, HDG | heading. HDG is used only with a known variation, and HDT wins. |
| DPT, DBT | depth. DPT adds a positive offset (to the waterline). |
| MWV | wind. R gives apparent wind (−180..180, + = starboard); T gives true wind, made a direction with the heading. |
| MWD | wind direction and speed |
| MDA | pressure, air/water temperature, wind |
| XDR | barometer in bar or Pa; air and water temperature by sensor name |
| MTW | water temperature |

- **Checksums:** a present checksum must match; sentences without one are accepted.
- **Talker IDs:** any talker ID is accepted (GP, GN, II, WI, SD, HC, YX, ...).
- **Ignored input:** NMEA 4.10 tag blocks and log-file timestamps in front of `$` are skipped. Proprietary `$P...` sentences and AIS `!AIVDM` sentences are ignored.

## TimeZero

What works:
- **Live position and instruments over NMEA output.** In TimeZero (Navigator or Professional), open the Connection Wizard > *Data Output* > UDP.
  1. Choose the network adapter and port 10110.
  2. Select RMC, GGA, VTG, HDT, MWV, DPT, MTW, MWD. Under *Advanced*, set MWV to "Both".
  3. Click Finish.
  TimeZero broadcasts on the LAN. The **TimeZero preset** in the settings adds the matching UDP 10110 source.
  With TCP, TimeZero listens as a server: add a TCP source with the TimeZero PC's address and the port.
  If TimeZero relays wind, depth and heading it got from NMEA 2000 or NavNet, those values come along too. Its documentation does not say so explicitly, so test it on board.
- **Tracks via manual export.** Use the Track list > Action > Export, or the Import/Export wizard, as GPX or KML into `Documents\TimeZero Export`. The preset creates that folder and watches it, and the app uploads new files automatically.

Not possible:
- **No automation interface.** TimeZero has no command line, API, SDK, COM interface or scripting to query the position or export tracks.
- **No automatic export.** Track export and "Backup Collection" are manual.
- **Data folder and track database:** their location and format are not documented, so the app does not read them.
- **TZ Professional's Voyage Data Recorder** writes a proprietary replay database, not NMEA logs.
- **TZ Cloud** ("Report my position to My TIMEZERO") has no public API.

## Command line

```
SmsCompanion.exe --upload track.gpx                       upload one file, wait for the import status
SmsCompanion.exe --send-position [--timeout 30]           read the configured sources until a fix, send it, exit
SmsCompanion.exe --simulate log.nmea --once               replay a file headless (one point per interval of fix time)
SmsCompanion.exe --simulate track.gpx [--rate 20]         tray app fed from a file (GPX is replayed as RMC)
  options: --server URL  --token T (or env SMS_DEVICE_TOKEN)  --interval S  --data-dir DIR
```

Exit codes: 0 ok, 1 send or upload failed (the points stay queued), 2 no fix or bad arguments. Scheduled tasks can use `--send-position` for periodic reports, or `--upload` for files that other software writes.

## Build

`dotnet publish src/Sms.Companion -c Release -r win-x64 -p:PublishSingleFile=true -o out` builds one self-contained exe of about 50 MB.
