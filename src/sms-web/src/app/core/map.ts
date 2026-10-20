import * as maplibregl from 'maplibre-gl';
import { readableQuery } from './route-link';
import type { ExpressionSpecification, GeoJSONSource, Map as MlMap, MapOptions, StyleSpecification } from 'maplibre-gl';
import type { Feature, LineString, Point } from 'geojson';
import { formatPosition, seamarkTypeLabel } from './seamark-icons';
import { appI18n } from './site';
import { hoursRow, PlaceRow } from './service-place';
import { chartIconExpression, chartTopmarkExpression, chartTopmarkOffset, hazardFilter, lightIconExpression } from './chart-symbols';

/** Public map configuration served by the API (/api/config). */
export interface MapConfig { mapStyleUrl: string; seamarkTilesUrl: string; attribution: string; }

let configPromise: Promise<MapConfig> | null = null;
export function mapConfig(): Promise<MapConfig> {
  configPromise ??= fetch('/api/config').then(r => r.json() as Promise<MapConfig>).catch(() => ({
    mapStyleUrl: 'https://tiles.openfreemap.org/styles/liberty', seamarkTilesUrl: '/tiles/seamark/{z}/{x}/{y}.mvt', attribution: '© OpenStreetMap-Mitwirkende · OpenSeaMap · OpenFreeMap',
  }));
  return configPromise;
}

// MapLibre's worker is a separate ES module; under Vite/esbuild pre-bundling its relative URL breaks, so it is served as an asset (angular.json).
maplibregl.setWorkerUrl('/maplibre-gl-worker.mjs');

export const LEGACY_VIEW = { center: [10.523529052734373, 54.40774166820069] as [number, number], zoom: 9 };

/**
 * Creates a MapLibre map with the vector base style (OpenFreeMap/OpenMapTiles by default) and the
 * OpenSeaMap sea-mark overlay from our own vector tile server. Resolves once the style has loaded.
 */
export async function createMap(container: HTMLElement, options: Partial<MapOptions> = {}): Promise<MlMap> {
  const cfg = await mapConfig();
  const map = new maplibregl.Map({
    container, style: cfg.mapStyleUrl, center: LEGACY_VIEW.center, zoom: LEGACY_VIEW.zoom, attributionControl: false, ...options,
  });
  map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-left');
  map.addControl(new maplibregl.AttributionControl({ compact: true, customAttribution: cfg.attribution }));
  await new Promise<void>(res => (map.loaded() ? res() : map.once('load', () => res())));
  addSeamarkOverlay(map, cfg.seamarkTilesUrl);
  addPoiOverlay(map, cfg.seamarkTilesUrl.replace('/seamark/', '/poi/'));
  addDepthOverlay(map, cfg.seamarkTilesUrl.replace('/seamark/', '/depth/'));
  addWeatherOverlayControl(map);
  (window as unknown as { smsMap?: MlMap }).smsMap = map; // debugging/e2e hook: the current map instance
  return map;
}

/**
 * Weather overlays. Four groups the user can combine (rain, waves, wind, temperature). With the provider's GL credentials
 * (Xweather MapsGL) they render client-side as animated particles / colour fields; otherwise the API's raster tiles are used.
 */
export interface OverlayLayer { id: string; name: string; attribution: string; }
export interface OverlayGroup { id: string; name: string; gl: string[]; glFallback?: string[]; raster: string; }
export const OVERLAY_GROUPS: OverlayGroup[] = [
  { id: 'rain', name: 'Regen', gl: ['radar'], raster: 'radar' },
  { id: 'waves', name: 'Wellen', gl: ['wave-particles'], glFallback: ['wave-heights'], raster: 'wave-heights' },
  { id: 'wind', name: 'Wind', gl: ['wind-speeds', 'wind-particles'], raster: 'wind-speeds' },
  { id: 'temperature', name: 'Temperatur', gl: ['temperatures'], raster: 'temperatures' },
];
const OVERLAY_STORAGE = 'sms.weatherOverlays';
interface GlTimeline { startDate: Date; endDate: Date; goToDate(d: Date): void; }
interface GlController { on(name: string, cb: () => void): void; isReady?: boolean; timeline: GlTimeline; addWeatherLayer(id: string, overrides?: unknown): unknown; removeWeatherLayer(id: string): void; hasWeatherLayer(id: string): boolean; }
/** Single-colour particles: MapsGL's default colours them by speed, the same scale as the wind-speed field beneath, so they vanished in it. */
const particleStyle = (color: string) => ({ paint: { particle: { size: 2 }, sample: { colorscale: { normalized: true, stops: [0, color, 1, color] } } } });
const PARTICLE_STYLE: Record<string, unknown> = { 'wind-particles': particleStyle('rgba(255,255,255,0.9)'), 'wave-particles': particleStyle('rgba(13,48,110,0.85)') };

/** Forecast steps of the weather time slider in hours from now (0 = current conditions). */
export const FORECAST_STEPS = [0, 1, 2, 3, 6, 9, 12, 18, 24, 30, 36, 48, 60, 72];
/** The time shown for a forecast step: now for 0, otherwise now + h rounded to the full hour (model data is hourly). */
export function forecastTime(hours: number, now = Date.now()): Date {
  return hours ? new Date(Math.round((now + hours * 3_600_000) / 3_600_000) * 3_600_000) : new Date(now);
}
export function forecastLabel(hours: number, now = Date.now()): string {
  if (!hours) return appI18n.t('weather.time.now');
  const at = forecastTime(hours, now).toLocaleString(appI18n.lang() === 'en' ? 'en-GB' : 'de-DE', { weekday: 'short', hour: '2-digit', minute: '2-digit' });
  return `${at} (+${hours} h)`;
}

export class WeatherOverlays {
  readonly selected = new Set<string>();
  private gl?: Promise<GlController | null>;
  private readonly glLayers = new Map<string, string[]>();
  /** Selected forecast offset in hours (0 = now). */
  hours = 0;
  constructor(private readonly map: MlMap, private readonly layers: OverlayLayer[], private access: { clientId: string; clientSecret: string } | null) {
    try { for (const id of JSON.parse(localStorage.getItem(OVERLAY_STORAGE) ?? '[]') as string[]) if (OVERLAY_GROUPS.some(g => g.id === id)) this.selected.add(id); } catch { /* ignore */ }
  }
  get mode(): 'gl' | 'raster' { return this.access ? 'gl' : 'raster'; }

  async apply(): Promise<void> {
    try { localStorage.setItem(OVERLAY_STORAGE, JSON.stringify([...this.selected])); } catch { /* ignore */ }
    const gl = this.access ? await this.controller() : null;
    if (gl) this.applyTime(gl); // before adding layers: new layers take over the timeline's range
    for (const g of OVERLAY_GROUPS) {
      const on = this.selected.has(g.id);
      if (gl) this.applyGl(gl, g, on); else this.applyRaster(g, on);
    }
  }

  /** Moves all GL weather layers to now + hours (the raster endpoint only serves current tiles). */
  async setHours(hours: number): Promise<void> {
    this.hours = hours;
    const gl = this.access ? await this.controller() : null;
    if (gl) this.applyTime(gl);
  }

  private applyTime(gl: GlController): void {
    const now = Date.now(), tl = gl.timeline;
    tl.startDate = new Date(now - 3_600_000); tl.endDate = new Date(now + FORECAST_STEPS.at(-1)! * 3_600_000 + 3_600_000);
    const at = forecastTime(this.hours, now);
    // layers added in the same tick still carry their old range and make goToDate throw a RangeError; they sync a tick later
    try { tl.goToDate(at); } catch { setTimeout(() => { try { tl.goToDate(at); } catch (e) { console.warn('MapsGL time', e); } }, 100); }
  }

  private applyRaster(g: OverlayGroup, on: boolean): void {
    const map = this.map, id = `weather-${g.id}`;
    const has = !!map.getLayer(id);
    if (on === has) return;
    if (!on) { map.removeLayer(id); map.removeSource(id); return; }
    const layer = this.layers.find(l => l.id === g.raster); if (!layer) return;
    map.addSource(id, { type: 'raster', tiles: [`${location.origin}/api/weather/overlay/${layer.id}/{z}/{x}/{y}.png`], tileSize: 256, maxzoom: 12, attribution: layer.attribution });
    map.addLayer({ id, type: 'raster', source: id, paint: { 'raster-opacity': 0.65 } }, map.getLayer('seamark-points') ? 'seamark-points' : undefined);
  }

  private applyGl(gl: GlController, g: OverlayGroup, on: boolean): void {
    const current = this.glLayers.get(g.id) ?? [];
    if (on === current.length > 0) return;
    if (!on) { for (const id of current) if (gl.hasWeatherLayer(id)) gl.removeWeatherLayer(id); this.glLayers.delete(g.id); return; }
    const added: string[] = [];
    const tryAdd = (ids: string[]) => { for (const id of ids) { try { gl.addWeatherLayer(id, PARTICLE_STYLE[id] ?? { paint: { opacity: 0.55 } }); added.push(id); } catch (e) { console.warn('MapsGL layer failed', id, e); } } };
    tryAdd(g.gl);
    if (added.length === 0 && g.glFallback) tryAdd(g.glFallback);
    this.glLayers.set(g.id, added);
  }

  /** Lazy-loads the MapsGL SDK (1.7 MB) the first time a GL overlay is switched on. */
  private controller(): Promise<GlController | null> {
    return this.gl ??= (async () => {
      try {
        const sdk = await import('@xweather/mapsgl');
        // MapLibre 6 moved the transform to the camera; MapsGL 1.10 still reads map.transform (fov) at construction
        const m = this.map as unknown as { transform?: unknown; _camera?: { transform?: unknown } };
        if (!('transform' in m)) Object.defineProperty(m, 'transform', { get: () => m._camera?.transform, configurable: true });
        const account = new sdk.Account(this.access!.clientId, this.access!.clientSecret);
        // MapsGL initialises at once only if map.isStyleLoaded(), else it waits for the map's 'load' event. Our map fired 'load'
        // long ago (createMap awaits it), but isStyleLoaded() is false whenever tiles/sources are still loading (panning,
        // a just-added source), so it waited forever for a second 'load' and no weather layer ever appeared.
        const ml = this.map as unknown as { isStyleLoaded: () => boolean };
        const isStyleLoaded = ml.isStyleLoaded;
        ml.isStyleLoaded = () => true;
        let c: GlController;
        try { c = new sdk.MaplibreMapController(this.map as never, { account, units: { temperature: 'C', speed: 'kts', pressure: 'hPa', distance: 'km', height: 'm' } } as never) as unknown as GlController; }
        finally { ml.isStyleLoaded = isStyleLoaded; }
        await new Promise<void>((res, rej) => { if (c.isReady) res(); else { c.on('load', () => res()); setTimeout(() => rej(new Error('MapsGL did not get ready within 20 s')), 20_000); } });
        return c;
      } catch (e) { console.warn('MapsGL unavailable, using raster tiles', e); this.access = null; return null; }
    })();
  }
}

/** Map control: a "Wetter" button opening a checklist of the overlay groups (several may be active). */
export function addWeatherOverlayControl(map: MlMap): void {
  fetch('/api/weather/overlay/layers').then(r => r.json() as Promise<{ configured: boolean; layers: OverlayLayer[]; mapsGl: { clientId: string; clientSecret: string } | null }>).then(cfg => {
    if (!cfg.configured || !cfg.layers.length) return;
    const overlays = new WeatherOverlays(map, cfg.layers, cfg.mapsGl);
    (map as unknown as { weatherOverlays?: WeatherOverlays }).weatherOverlays = overlays; // e2e/debug hook
    const wrap = document.createElement('div'); wrap.className = 'maplibregl-ctrl maplibregl-ctrl-group weather-overlay-ctrl';
    const button = document.createElement('button'); button.type = 'button'; button.className = 'weather-overlay-button'; button.title = 'Wetter-Overlays'; button.setAttribute('aria-label', 'Wetter-Overlays');
    const panel = document.createElement('div'); panel.className = 'weather-overlay-panel'; panel.hidden = true;
    // forecast time slider at the bottom of the map, only while a GL overlay is on (the raster tiles are current-only)
    const time = document.createElement('div'); time.className = 'weather-time-ctrl';
    const range = document.createElement('input'); range.type = 'range'; range.min = '0'; range.max = String(FORECAST_STEPS.length - 1); range.step = '1'; range.value = '0';
    range.setAttribute('aria-label', appI18n.t('weather.time.label'));
    const timeLabel = document.createElement('span'); timeLabel.className = 'weather-time-label';
    const showTime = () => { timeLabel.textContent = forecastLabel(FORECAST_STEPS[+range.value]); };
    range.oninput = showTime;
    range.onchange = () => { showTime(); void overlays.setHours(FORECAST_STEPS[+range.value]); };
    time.append(range, timeLabel); map.getContainer().appendChild(time);
    const refresh = () => {
      button.textContent = overlays.selected.size ? `☁ ${overlays.selected.size}` : '☁'; button.classList.toggle('active', overlays.selected.size > 0);
      time.hidden = !overlays.selected.size || overlays.mode !== 'gl'; showTime();
    };
    for (const g of OVERLAY_GROUPS) {
      const label = document.createElement('label'); const cb = document.createElement('input'); cb.type = 'checkbox'; cb.value = g.id; cb.checked = overlays.selected.has(g.id);
      cb.onchange = () => { if (cb.checked) overlays.selected.add(g.id); else overlays.selected.delete(g.id); refresh(); void overlays.apply().then(refresh); };
      label.append(cb, document.createTextNode(' ' + g.name)); panel.appendChild(label);
    }
    const note = document.createElement('div'); note.className = 'muted'; note.textContent = overlays.mode === 'gl' ? 'Xweather MapsGL' : 'Xweather (Kacheln)'; panel.appendChild(note);
    button.onclick = () => { panel.hidden = !panel.hidden; };
    map.addControl({ onAdd: () => { wrap.append(button, panel); return wrap; }, onRemove: () => wrap.remove() }, 'top-right');
    refresh();
    if (overlays.selected.size) void overlays.apply().then(refresh);
  }).catch(() => { /* no overlay without the API */ });
}

/** Material Symbols glyph per POI kind (the font is already loaded for mat-icon). */
export const POI_ICON: Record<string, string> = {
  restaurant: 'restaurant', cafe: 'local_cafe', bar: 'local_bar', pub: 'sports_bar', fast_food: 'fastfood', ice_cream: 'icecream', biergarten: 'sports_bar', bakery: 'bakery_dining',
  supermarket: 'shopping_cart', convenience: 'shopping_basket', chandler: 'sailing', boat: 'sailing', boat_repair: 'build', fuel: 'local_gas_station', toilets: 'wc', shower: 'shower', drinking_water: 'water_drop',
  hotel: 'hotel', guest_house: 'hotel', hostel: 'hotel', apartment: 'apartment', camp_site: 'camping', caravan_site: 'rv_hookup', pharmacy: 'local_pharmacy', hospital: 'local_hospital', doctors: 'medical_services', dentist: 'dentistry',
  atm: 'local_atm', bank: 'account_balance', police: 'local_police', post_office: 'local_post_office', bicycle_rental: 'pedal_bike', bicycle: 'pedal_bike', laundry: 'local_laundry_service', marina: 'anchor', slipway: 'anchor',
  museum: 'museum', attraction: 'attractions', information: 'info', viewpoint: 'visibility', beach: 'beach_access', swimming: 'pool', sports_centre: 'fitness_center', theatre: 'theater_comedy', cinema: 'movie', library: 'local_library',
  clothes: 'checkroom', hairdresser: 'content_cut', kiosk: 'storefront', gift: 'redeem', books: 'menu_book', electronics: 'devices', hardware: 'hardware', doityourself: 'hardware', butcher: 'kebab_dining', seafood: 'set_meal', greengrocer: 'nutrition', alcohol: 'wine_bar',
  bus_stop: 'directions_bus', bus_station: 'directions_bus', station: 'train', tram_stop: 'tram', ferry_terminal: 'directions_boat',
};
const POI_COLOUR: Record<string, string> = { amenity: '#6d4c41', shop: '#1565c0', tourism: '#2e7d32', leisure: '#00838f', craft: '#4e342e', office: '#546e7a', healthcare: '#c62828', public_transport: '#283593' };

/** 26 px round badge with a Material Symbols glyph, drawn once per kind; inverted = filled in the POI's colour with a white glyph (highlighted places). */
async function poiBadge(kind: string, cls: string, inverted = false): Promise<ImageData> {
  const size = 52, c = document.createElement('canvas'); c.width = c.height = size;
  const g = c.getContext('2d')!;
  try { await document.fonts.load('26px "Material Symbols Outlined"'); } catch { /* fallback glyph box */ }
  const colour = POI_COLOUR[cls] ?? '#616161';
  g.fillStyle = inverted ? colour : '#ffffff'; g.beginPath(); g.arc(size / 2, size / 2, size / 2 - 2, 0, Math.PI * 2); g.fill();
  g.lineWidth = 3; g.strokeStyle = inverted ? '#ffffff' : colour; g.stroke();
  g.fillStyle = inverted ? '#ffffff' : colour; g.font = '30px "Material Symbols Outlined"'; g.textAlign = 'center'; g.textBaseline = 'middle';
  g.fillText(POI_ICON[kind] ?? (cls === 'shop' ? 'storefront' : cls === 'tourism' ? 'attractions' : 'place'), size / 2, size / 2 + 1);
  return g.getImageData(0, 0, size, size);
}

/** Shops, restaurants, services near harbours (own tile layer from Sms.Tiles, z ≥ 13); a click shows the OSM details. */
const DEPTH_STORAGE = 'sms.depthOverlay';
/**
 * Depth shading as one blue ramp (the shallower the darker; drying a greenish grey like paper charts; from 18 m clear). The ship's settings add
 * two violet steps, the same rules the routes use: too shallow (the band's middle below the minimum depth: routes avoid it) indigo with a thin
 * red hatch from z12, and "check depth" (the band's floor below the warning depth: routes warn there) lavender. Water with no charted depth (the
 * strip along the shore, the Wadden flats) comes from the tiles as the shallowest step with unknown = 1: one quieter even colour (a grey teal,
 * lavender with ship settings), never hatched - as the hatched shallowest step it ringed every island.
 */
const UNKNOWN = ['==', ['get', 'unknown'], 1];
const MIDDLE = ['/', ['+', ['get', 'min_depth'], ['get', 'max_depth']], 2];
export function depthFillColour(minDepthM: number | null, warnDepthM: number | null = null): unknown {
  const steps = ['step', ['get', 'min_depth'], '#9fb98a', 0, '#1d4f9c', 1.8, '#3274c4', 3.6, '#5b9bdc', 5.4, '#8dbbea', 9, '#bdd8f4', 18, 'rgba(0,0,0,0)'];
  if (minDepthM == null && warnDepthM == null) return ['case', UNKNOWN, '#6f9fa0', steps];
  return ['case', UNKNOWN, '#9c97d6',
    ...(minDepthM == null ? [] : [['<', MIDDLE, minDepthM], '#34357a']),
    ...(warnDepthM == null ? [] : [['<', ['get', 'min_depth'], warnDepthM], '#9c97d6']), steps];
}
export const depthHatchFilter = (minDepthM: number | null): unknown => minDepthM == null ? ['<', ['get', 'max_depth'], -1e9] : ['all', ['!', UNKNOWN], ['<', MIDDLE, minDepthM]];
/** Thin diagonal red stripes for the "too shallow for the ship" hatch. */
function depthHatchImage(): ImageData {
  const size = 24, c = document.createElement('canvas'); c.width = c.height = size;
  const g = c.getContext('2d')!; g.strokeStyle = 'rgba(229,57,53,0.45)'; g.lineWidth = 1.5;
  for (const o of [-size, 0, size]) { g.beginPath(); g.moveTo(o, size); g.lineTo(o + size, 0); g.stroke(); }
  return g.getImageData(0, 0, size, size);
}
/** Charted depth areas as a toggleable overlay below the sea marks; the toggle is remembered per browser. */
export function addDepthOverlay(map: MlMap, tilesUrl: string): void {
  if (map.getSource('depth')) return;
  const abs = tilesUrl.startsWith('/') ? `${location.origin}${tilesUrl}` : tilesUrl;
  map.addSource('depth', { type: 'vector', tiles: [abs], minzoom: 7, maxzoom: 14, attribution: 'Tiefen: Open Waters Seascape (EMODnet, GEBCO)' });
  let on = false; try { on = localStorage.getItem(DEPTH_STORAGE) === '1'; } catch { /* ignore */ }
  const before = ['seamark-sectors-fill', 'seamark-lines', 'seamark-icons', 'seamark-points'].find(id => map.getLayer(id));
  map.addLayer({ id: 'depth-fill', type: 'fill', source: 'depth', 'source-layer': 'depth', minzoom: 7, layout: { visibility: on ? 'visible' : 'none' },
    paint: { 'fill-color': depthFillColour(null) as never, 'fill-opacity': ['case', UNKNOWN, 0.35, 0.55] as never, 'fill-antialias': false } }, before);
  if (!map.hasImage('depth-hatch')) map.addImage('depth-hatch', depthHatchImage(), { pixelRatio: 2 });
  map.addLayer({ id: 'depth-hatch', type: 'fill', source: 'depth', 'source-layer': 'depth', minzoom: 12, layout: { visibility: on ? 'visible' : 'none' }, filter: depthHatchFilter(null) as never,
    paint: { 'fill-pattern': 'depth-hatch', 'fill-antialias': false } }, before);
  const wrap = document.createElement('div'); wrap.className = 'maplibregl-ctrl maplibregl-ctrl-group depth-overlay-ctrl';
  const button = document.createElement('button'); button.type = 'button'; button.className = 'depth-overlay-button'; button.title = 'Tiefen anzeigen'; button.setAttribute('aria-label', 'Tiefen anzeigen');
  const refresh = () => { button.textContent = '≈'; button.classList.toggle('active', on); };
  button.onclick = () => { on = !on; for (const id of ['depth-fill', 'depth-hatch']) map.setLayoutProperty(id, 'visibility', on ? 'visible' : 'none'); try { localStorage.setItem(DEPTH_STORAGE, on ? '1' : '0'); } catch { /* ignore */ } refresh(); };
  map.addControl({ onAdd: () => { wrap.append(button); return wrap; }, onRemove: () => wrap.remove() }, 'top-right');
  refresh();
}
/** Recolours the depth overlay for the ship's minimum and warning depth (null = chart steps only). */
export function applyDepthShading(map: MlMap, minDepthM: number | null, warnDepthM: number | null = null): void {
  if (map.getLayer('depth-fill')) map.setPaintProperty('depth-fill', 'fill-color', depthFillColour(minDepthM, warnDepthM) as never);
  if (map.getLayer('depth-hatch')) map.setFilter('depth-hatch', depthHatchFilter(minDepthM) as never);
}

export function addPoiOverlay(map: MlMap, tilesUrl: string): void {
  if (map.getSource('poi')) return;
  const abs = tilesUrl.startsWith('/') ? `${location.origin}${tilesUrl}` : tilesUrl;
  map.addSource('poi', { type: 'vector', tiles: [abs], minzoom: 13, maxzoom: 16 });
  const pending = new Set<string>();
  map.on('styleimagemissing', async e => {
    if (!/^(poi|inv)\|/.test(e.id) || pending.has(e.id)) return;
    pending.add(e.id);
    if (!map.hasImage(e.id)) map.addImage(e.id, { width: 1, height: 1, data: new Uint8Array(4) });
    const [prefix, cls, kind] = e.id.split('|');
    try { const img = await poiBadge(kind, cls, prefix === 'inv'); if (map.hasImage(e.id)) map.removeImage(e.id); map.addImage(e.id, img, { pixelRatio: 2 }); } catch { /* keep placeholder */ } finally { pending.delete(e.id); }
  });
  // everywhere from z16; from z13 only around the selected harbour (setPoiFocus). Our icons sit above the basemap, so the
  // basemap's label for the same restaurant loses the collision and is hidden.
  const before = map.getLayer('seamark-points') ? 'seamark-points' : undefined;
  const layout = { 'icon-image': ['concat', 'poi|', ['get', 'class'], '|', ['get', 'kind']], 'icon-size': ['interpolate', ['linear'], ['zoom'], 13, 0.6, 16, 0.9], 'icon-allow-overlap': false, 'text-field': ['step', ['zoom'], '', 15, ['coalesce', ['get', 'name'], '']], 'text-size': 10, 'text-offset': [0, 1.2], 'text-anchor': 'top', 'text-font': ['Noto Sans Regular'], 'text-optional': true } as const;
  const paint = { 'text-color': '#37474f', 'text-halo-color': '#ffffff', 'text-halo-width': 1.2 };
  map.addLayer({ id: 'poi-icons', type: 'symbol', source: 'poi', 'source-layer': 'poi', minzoom: 16, layout: layout as never, paint }, before);
  map.addLayer({ id: 'poi-icons-near', type: 'symbol', source: 'poi', 'source-layer': 'poi', minzoom: 13, maxzoom: 16, layout: { ...layout, visibility: 'none' } as never, paint }, before);
  for (const id of ['poi-icons', 'poi-icons-near']) clickable(map, id, e => { const f = e.features?.[0]; if (f && !hlAt(map, e.point)) void showPoiPopup(map, f.properties as Record<string, unknown>, e.lngLat); });
  // the basemap's own POI labels (OpenMapTiles "poi" layer): name and kind, details from our Pois table when it knows the place
  for (const l of map.getStyle().layers) if ('source-layer' in l && l['source-layer'] === 'poi' && l.type === 'symbol') clickable(map, l.id, e => {
    const p = e.features?.[0]?.properties as Record<string, unknown> | undefined; if (!p || hlAt(map, e.point)) return;
    const name = p['name:de'] ?? p['name'];
    void showPoiPopup(map, { name, kind: p['subclass'] ?? p['class'] }, e.lngLat, name ? `/api/pois/lookup?${new URLSearchParams({ name: String(name), lat: e.lngLat.lat.toFixed(6), lng: e.lngLat.lng.toFixed(6) })}` : null);
  });
}

/** POI layers that open a popup on click: pages with their own map click (e.g. routes) skip clicks on them. */
const clickableLayers = new Set<string>();
function clickable(map: MlMap, id: string, onClick: (e: maplibregl.MapLayerMouseEvent) => void): void {
  clickableLayers.add(id);
  map.on('click', id, onClick);
  map.on('mouseenter', id, () => { map.getCanvas().style.cursor = 'pointer'; });
  map.on('mouseleave', id, () => { map.getCanvas().style.cursor = ''; });
}
/** True when a POI with a popup is rendered at the point. */
export const poiAt = (map: MlMap, point: maplibregl.PointLike) => map.queryRenderedFeatures(point, { layers: [...clickableLayers].filter(id => map.getLayer(id)) }).length > 0;

/** Shows our POIs from z13 within ~1.5 km of the given position (the selected harbour); null = only from z16 as usual. */
export function setPoiFocus(map: MlMap, at: { lat: number; lng: number } | null): void {
  if (!map.getLayer('poi-icons-near')) return;
  map.setLayoutProperty('poi-icons-near', 'visibility', at ? 'visible' : 'none');
  if (!at) return;
  const dLat = 1500 / 111_320, dLng = dLat / Math.cos(at.lat * Math.PI / 180);
  const ring = [[at.lng - dLng, at.lat - dLat], [at.lng + dLng, at.lat - dLat], [at.lng + dLng, at.lat + dLat], [at.lng - dLng, at.lat + dLat], [at.lng - dLng, at.lat - dLat]];
  map.setFilter('poi-icons-near', ['within', { type: 'Polygon', coordinates: [ring] }] as never);
}

/** A highlighted service place: drawn with the inverted variant (inv|class|kind) of the icon image of our POI tiles (poi|class|kind), bigger and on top, even where the tiles do not have it at this zoom. */
export interface ServiceSpot { lat: number; lng: number; icon: string; popup?: () => HTMLElement; }
let hlSpots: ServiceSpot[] = [], hlPick: ((i: number) => void) | undefined, hlPopup: maplibregl.Popup | undefined;
type HlFeature = Feature<Point | LineString, { i: number; icon?: string; walk?: boolean }>;
let hlFeatures: HlFeature[] = [];
const HL_LAYERS = ['service-hl-icons', 'service-hl-icons-active'];
const hlAt = (map: MlMap, point: maplibregl.PointLike) => HL_LAYERS.every(id => map.getLayer(id)) && map.queryRenderedFeatures(point, { layers: HL_LAYERS }).length > 0;
const dimmed = new Map<string, unknown>();
/** Harbour facility / marina / anchorage chart symbols (the coloured circles with a white glyph) and the unselected harbour markers; buoys, lights and the selected harbour stay. */
const FACILITY_TYPES = ['small_craft_facility', 'harbour', 'anchorage', 'anchor_berth'];
const dimFacilities = ['case', ['in', ['get', 'type'], ['literal', FACILITY_TYPES]], 0.5, 1];
const dimUnselected = ['case', ['boolean', ['get', 'selected'], false], 1, 0.5];
/** While a service is selected all other POIs (ours, the basemap's labels, the harbour facility symbols and other harbours) are half transparent. */
function dimOtherPois(map: MlMap, on: boolean): void {
  const targets: [string, string, unknown][] = [];
  for (const id of ['poi-icons', 'poi-icons-near', ...map.getStyle().layers.filter(l => 'source-layer' in l && l['source-layer'] === 'poi' && l.type === 'symbol').map(l => l.id)])
    for (const prop of ['icon-opacity', 'text-opacity']) targets.push([id, prop, undefined]);   // undefined = half of the original
  targets.push(['seamark-icons', 'icon-opacity', dimFacilities], ['harbour-points', 'circle-opacity', dimUnselected], ['harbour-points', 'circle-stroke-opacity', dimUnselected]);
  for (const [id, prop, expr] of targets) {
    if (!map.getLayer(id)) continue;
    const key = `${id}|${prop}`;
    if (on) {
      if (!dimmed.has(key)) dimmed.set(key, map.getPaintProperty(id, prop as 'icon-opacity'));
      const o = dimmed.get(key);
      map.setPaintProperty(id, prop as 'icon-opacity', (expr ?? (typeof o === 'number' || o == null ? 0.5 * (o as number ?? 1) : 0.5)) as never);   // ponytail: a zoom-dependent original opacity becomes a flat 0.5
    } else if (dimmed.has(key)) { map.setPaintProperty(id, prop as 'icon-opacity', (dimmed.get(key) ?? null) as never); dimmed.delete(key); }
  }
}
/** Highlights the places (empty = clear): bigger POI icons on top, all other POIs dimmed, the map fitted to the places and the harbour. A click on an icon opens its popup and reports its index to onPick. */
export function setServiceHighlight(map: MlMap, spots: ServiceSpot[], harbour: { lat: number; lng: number } | null, onPick?: (i: number) => void): void {
  hlSpots = spots; hlPick = onPick; hlPopup?.remove(); hlPopup = undefined;
  if (!map.getSource('service-hl')) {
    if (!spots.length) return;
    map.addSource('service-hl', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    const point = ['==', ['geometry-type'], 'Point'];
    map.addLayer({ id: 'service-hl-line', type: 'line', source: 'service-hl', filter: ['all', ['==', ['geometry-type'], 'LineString'], ['==', ['get', 'i'], -1]], layout: { 'line-cap': 'round' }, paint: { 'line-color': '#d50000', 'line-width': 3, 'line-dasharray': [3, 2] } });
    map.addLayer({ id: 'service-hl-walk', type: 'line', source: 'service-hl', filter: ['all', ['==', ['geometry-type'], 'LineString'], ['==', ['get', 'i'], -1]], layout: { 'line-cap': 'round', 'line-join': 'round' }, paint: { 'line-color': '#d50000', 'line-width': 4 } });
    const icon = (size: number) => ({ 'icon-image': ['get', 'icon'], 'icon-size': ['interpolate', ['linear'], ['zoom'], 13, 0.9 * size, 16, 1.3 * size], 'icon-allow-overlap': true, 'icon-ignore-placement': true });
    map.addLayer({ id: 'service-hl-icons', type: 'symbol', source: 'service-hl', filter: point as never, layout: icon(1) as never });
    map.addLayer({ id: 'service-hl-icons-active', type: 'symbol', source: 'service-hl', filter: ['all', point, ['==', ['get', 'i'], -1]] as never, layout: icon(1.4) as never });
    for (const id of HL_LAYERS) clickable(map, id, e => {
      const i = e.features?.[0]?.properties?.['i'] as number | undefined, s = i === undefined ? undefined : hlSpots[i];
      if (i === undefined || !s) return;
      hlPopup?.remove();
      if (s.popup) hlPopup = new maplibregl.Popup({ offset: 18, maxWidth: '300px' }).setLngLat([s.lng, s.lat]).setDOMContent(s.popup()).addTo(map);
      hlPick?.(i);
    });
  }
  hlFeatures = spots.flatMap((s, i): HlFeature[] => [
    { type: 'Feature', properties: { i, icon: s.icon.replace(/^poi\|/, 'inv|') }, geometry: { type: 'Point', coordinates: [s.lng, s.lat] } },
    ...(harbour && (s.lat !== harbour.lat || s.lng !== harbour.lng) ? [{ type: 'Feature' as const, properties: { i }, geometry: { type: 'LineString' as const, coordinates: [[harbour.lng, harbour.lat], [s.lng, s.lat]] } }] : []),
  ]);
  (map.getSource('service-hl') as GeoJSONSource).setData({ type: 'FeatureCollection', features: hlFeatures });
  setServiceActive(map, null);
  dimOtherPois(map, spots.length > 0);
  if (!spots.length) return;
  const pts = [...spots, ...(harbour ? [harbour] : [])];
  const b = new maplibregl.LngLatBounds([pts[0].lng, pts[0].lat], [pts[0].lng, pts[0].lat]);
  for (const p of pts) b.extend([p.lng, p.lat]);
  // eased, slow camera move; a single point (no extent) still gets a gentle zoom/centre change instead of nothing
  const easing = (t: number) => t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2, duration = 1400;
  if (b.getWest() !== b.getEast() || b.getSouth() !== b.getNorth()) map.fitBounds(b, { padding: 70, maxZoom: 17, duration, easing, essential: true });
  else map.easeTo({ center: b.getCenter(), zoom: Math.min(17, Math.max(map.getZoom() + 0.5, 15)), duration, easing, essential: true });
}
/** Replaces the straight (dashed) line from the harbour to place i by the walking path (solid), e.g. from GET /api/pois/walk. */
export function setServicePath(map: MlMap, i: number, coordinates: [number, number][]): void {
  const source = map.getSource('service-hl') as GeoJSONSource | undefined;
  if (!source || !hlSpots[i]) return;
  hlFeatures = [...hlFeatures.filter(f => f.properties.i !== i || f.geometry.type === 'Point'), { type: 'Feature', properties: { i, walk: true }, geometry: { type: 'LineString', coordinates } }];
  source.setData({ type: 'FeatureCollection', features: hlFeatures });
}
/** Emphasises one place (null = none): its icon grows and the line from the harbour is drawn (the walking path once known, else straight and dashed), e.g. for the hovered list item. */
export function setServiceActive(map: MlMap, i: number | null): void {
  if (!map.getLayer('service-hl-line')) return;
  const on = ['==', ['get', 'i'], i ?? -1], line = ['==', ['geometry-type'], 'LineString'], walk = ['boolean', ['get', 'walk'], false];
  map.setFilter('service-hl-line', ['all', line, on, ['!', walk]] as never);
  map.setFilter('service-hl-walk', ['all', line, on, walk] as never);
  map.setFilter('service-hl-icons-active', ['all', ['==', ['geometry-type'], 'Point'], on] as never);
  map.setFilter('service-hl-icons', ['all', ['==', ['geometry-type'], 'Point'], ['!=', ['get', 'i'], i ?? -1]] as never);
}

/** Map view in the URL (?c=lat,lng,zoom), like Google Maps: survives reloads, Back returns to the previous view. */
export interface MapView { lat: number; lng: number; zoom: number; }
export function parseView(c: string | null | undefined): MapView | null {
  const [lat, lng, zoom] = (c ?? '').split(',').map(s => (s.trim() === '' ? NaN : Number(s)));
  return Number.isFinite(lat) && Number.isFinite(lng) && Number.isFinite(zoom) && Math.abs(lat) <= 90 && Math.abs(lng) <= 180 && zoom >= 0 && zoom <= 24 ? { lat, lng, zoom } : null;
}
export const formatView = (v: MapView) => `${v.lat.toFixed(5)},${v.lng.toFixed(5)},${Math.round(v.zoom * 100) / 100}`;
/** The view in a path with query string ("/harbours/x?c=…"), or null. */
export const viewFromUrl = (url: string) => parseView(new URLSearchParams(url.split('?')[1] ?? '').get('c'));
/** Map options for createMap: the URL's view, if any (it wins over any default). */
export const urlViewOptions = (url: string): Partial<MapOptions> => { const v = viewFromUrl(url); return v ? { center: [v.lng, v.lat], zoom: v.zoom } : {}; };

/** What syncViewWithUrl needs from Angular's Location (kept minimal so map.ts stays framework-free and testable). */
export interface UrlHistory { path(): string; replaceState(path: string, query?: string, state?: unknown): void; go(path: string, query?: string, state?: unknown): void; getState(): unknown; subscribe(fn: (e: { url?: string }) => void): { unsubscribe(): void }; }
/**
 * Keeps ?c= in step with the map: every settled move replaces the current entry (reload keeps the view); once the map rested for
 * a second away from the view the entry was created with, that entry gets its old view back and a new one is pushed, so Back
 * returns to the previous view. Back/forward (popstate) jumps the map to the entry's view. Returns the teardown.
 */
export function syncViewWithUrl(map: MlMap, history: UrlHistory, settleMs = 1000): () => void {
  const current = (): MapView => { const c = map.getCenter(); return { lat: c.lat, lng: c.lng, zoom: map.getZoom() }; };
  const withView = (url: string, v: MapView): [string, string] => { const [path, q = ''] = url.split('?'); const p = new URLSearchParams(q); p.set('c', formatView(v)); return [path, readableQuery(p)]; };
  let entry = { path: history.path().split('?')[0], view: current() };
  let timer: ReturnType<typeof setTimeout> | undefined;
  const moved = (a: MapView, b: MapView) => Math.abs(a.zoom - b.zoom) >= 0.5 || map.project([a.lng, a.lat]).dist(map.project([b.lng, b.lat])) > 100;
  const onMoveEnd = () => {
    const v = current();
    history.replaceState(...withView(history.path(), v), history.getState());
    clearTimeout(timer);
    timer = setTimeout(() => {
      const url = history.path(), path = url.split('?')[0], now = current();
      if (path === entry.path && moved(entry.view, now)) {
        history.replaceState(...withView(url, entry.view), history.getState());
        history.go(...withView(url, now));
      }
      entry = { path, view: now };
    }, settleMs);
  };
  map.on('moveend', onMoveEnd);
  const sub = history.subscribe(e => {
    clearTimeout(timer);
    const v = viewFromUrl(e.url ?? history.path());
    entry = { path: (e.url ?? history.path()).split('?')[0], view: v ?? current() };
    if (v) map.jumpTo({ center: [v.lng, v.lat], zoom: v.zoom });
  });
  return () => { clearTimeout(timer); sub.unsubscribe(); map.off('moveend', onMoveEnd); };
}

export interface PoiDetails { osmType: string; osmId: number; name: string | null; class: string; kind: string; openingHours: string | null; website: string | null; phone: string | null; email: string | null; address: string | null; cuisine: string | null; wheelchair: string | null; description: string | null; osmUrl: string; }
export async function showPoiPopup(map: MlMap, p: Record<string, unknown>, lngLat: maplibregl.LngLat, detailsUrl: string | null = `/api/pois/${p['osm_type']}/${p['osm_id']}`): Promise<void> {
  const div = document.createElement('div'); div.className = 'poi-popup';
  div.appendChild(textEl(String(p['name'] ?? kindLabel(String(p['kind']))), 'b'));
  div.appendChild(textEl(kindLabel(String(p['kind'])), 'div')).className = 'muted';
  const popup = new maplibregl.Popup({ offset: 12 }).setLngLat(lngLat).setDOMContent(div).addTo(map);
  if (!detailsUrl) return;
  try {
    const r = await fetch(detailsUrl); if (!r.ok) return;
    const d = await r.json() as PoiDetails;
    const row = (label: string, value: string | null, href?: string) => {
      if (!value) return;
      const r = document.createElement('div'); r.appendChild(textEl(label + ': '));
      if (href) { const a = document.createElement('a'); a.href = href; a.target = '_blank'; a.rel = 'noreferrer'; a.textContent = value; r.appendChild(a); } else r.appendChild(textEl(value));
      div.appendChild(r);
    };
    row('Adresse', d.address);
    if (d.openingHours) { const r = document.createElement('div'); r.appendChild(textEl('Öffnungszeiten: ')); r.appendChild(rowValueEl(hoursRow(d.openingHours, '', appI18n.lang()))); div.appendChild(r); } row('Küche', d.cuisine); row('Telefon', d.phone, d.phone ? `tel:${d.phone}` : undefined); row('E-Mail', d.email, d.email ? `mailto:${d.email}` : undefined);
    row('Website', d.website, httpUrl(d.website)); if (d.description) div.appendChild(textEl(d.description, 'div')); row('OSM', `${d.osmType}/${d.osmId}`, d.osmUrl);
    popup.setDOMContent(div);
  } catch { /* the tile properties stay */ }
}
/** A row's value as an element: one line per opening-hours rule plus the open/closed badge (colour set inline, popups are outside the component styles). */
export function rowValueEl(row: PlaceRow): HTMLElement {
  const el = textEl(row.value); el.style.whiteSpace = 'pre-line';
  if (row.badge) { const b = textEl(' ' + row.badge.text); b.style.cssText = `font-weight:600;white-space:nowrap;color:${row.badge.open ? '#2e7d32' : '#c62828'}`; el.append(b); }
  return el;
}
/** Only http(s) links become clickable; anything else from the OSM tag (javascript:, data:, …) stays plain text. */
export function httpUrl(u: string | null | undefined): string | undefined {
  if (!u) return undefined;
  try { const x = new URL(u.includes('://') ? u : `https://${u}`); return x.protocol === 'http:' || x.protocol === 'https:' ? x.href : undefined; } catch { return undefined; }
}
const KIND_DE: Record<string, string> = { restaurant: 'Restaurant', cafe: 'Café', bar: 'Bar', pub: 'Kneipe', fast_food: 'Imbiss', ice_cream: 'Eisdiele', bakery: 'Bäckerei', supermarket: 'Supermarkt', convenience: 'Nahversorger', chandler: 'Yachtausrüster', boat_repair: 'Bootswerft', fuel: 'Tankstelle', toilets: 'Toiletten', shower: 'Duschen', drinking_water: 'Trinkwasser', hotel: 'Hotel', guest_house: 'Pension', pharmacy: 'Apotheke', hospital: 'Krankenhaus', doctors: 'Arzt', atm: 'Geldautomat', bank: 'Bank', police: 'Polizei', post_office: 'Post', bicycle_rental: 'Fahrradverleih', laundry: 'Waschsalon', marina: 'Marina', museum: 'Museum', information: 'Information', viewpoint: 'Aussichtspunkt', beach: 'Strand', kiosk: 'Kiosk', hairdresser: 'Friseur', butcher: 'Metzgerei', seafood: 'Fischgeschäft', alcohol: 'Spirituosen', clothes: 'Bekleidung' };
export const kindLabel = (kind: string) => KIND_DE[kind] ?? kind.replace(/_/g, ' ');

/** OpenSeaMap-like rendering of the seamark vector layer (buoys, beacons, lights, names). */
export function addSeamarkOverlay(map: MlMap, tilesUrl: string): void {
  if (map.getSource('seamark')) return;
  const abs = tilesUrl.startsWith('/') ? `${location.origin}${tilesUrl}` : tilesUrl;
  map.addSource('seamark', { type: 'vector', tiles: [abs], minzoom: 8, maxzoom: 16, attribution: 'OpenSeaMap' });
  const lateral: ExpressionSpecification = ['in', ['get', 'type'], ['literal', ['buoy_lateral', 'beacon_lateral']]];
  const colour: ExpressionSpecification = ['case',
    ['all', lateral, ['in', ['get', 'category'], ['literal', ['port', 'preferred_channel_starboard']]]], '#d32f2f',
    ['all', lateral, ['in', ['get', 'category'], ['literal', ['starboard', 'preferred_channel_port']]]], '#2e7d32',
    ['in', ['get', 'type'], ['literal', ['buoy_cardinal', 'beacon_cardinal']]], '#fbc02d',
    ['in', ['get', 'type'], ['literal', ['buoy_safe_water']]], '#ffffff',
    ['in', ['get', 'type'], ['literal', ['buoy_isolated_danger', 'beacon_isolated_danger']]], '#000000',
    ['in', ['get', 'type'], ['literal', ['buoy_special_purpose', 'beacon_special_purpose']]], '#ffeb3b',
    ['in', ['get', 'type'], ['literal', ['light_major', 'light_minor', 'light']]], '#ff9800',
    ['in', ['get', 'type'], ['literal', ['harbour', 'anchorage', 'mooring']]], '#1976d2',
    '#78909c'];
  // zoom 8–10: coloured dots (dense), from zoom 11: the OpenSeaMap symbols composed per mark (seamark-icons.ts)
  map.addLayer({
    id: 'seamark-points', type: 'circle', source: 'seamark', 'source-layer': 'seamark', minzoom: 8, maxzoom: 11,
    paint: { 'circle-color': colour, 'circle-radius': ['interpolate', ['linear'], ['zoom'], 8, 2.5, 11, 4], 'circle-stroke-color': '#ffffff', 'circle-stroke-width': 1, 'circle-opacity': 0.95 },
  });
  map.addSprite('fnc', `${location.origin}/seamarks/fnc`); // chart symbols (quantenschaum/mapping via Open Waters Seamap, CC-BY)
  // line marks: leading lines / recommended tracks (dashed black), cables and pipelines (magenta), fairway and restricted edges
  map.addLayer({
    id: 'seamark-lines', type: 'line', source: 'seamark', 'source-layer': 'seamark', minzoom: 10, filter: ['==', ['geometry-type'], 'LineString'],
    paint: {
      // quays, piers and breakwaters solid grey; ferry routes blue; cables and pipelines magenta; leading lines and tracks black – all but the shore dashed
      'line-color': ['case', ['==', ['get', 'type'], 'shoreline_construction'], '#757575', ['==', ['get', 'type'], 'ferry_route'], '#1565c0', ['in', ['get', 'type'], ['literal', ['cable_submarine', 'cable_overhead', 'pipeline_submarine', 'pipeline_overhead']]], '#a30075', ['in', ['get', 'type'], ['literal', ['fairway', 'restricted_area', 'separation_line', 'separation_boundary', 'separation_zone']]], '#7b1fa2', '#212121'],
      // one zoom curve only (MapLibre rejects a layer with two zoom-based subexpressions, which silently dropped the whole lines layer)
      'line-width': ['interpolate', ['linear'], ['zoom'], 10, ['case', ['==', ['get', 'type'], 'shoreline_construction'], 0.6, 0.8], 17, ['case', ['==', ['get', 'type'], 'shoreline_construction'], 4, 2.3]],
      'line-dasharray': ['case', ['==', ['get', 'type'], 'shoreline_construction'], ['literal', [1, 0]], ['literal', [4, 3]]], 'line-opacity': 0.9,
    },
  });
  map.addLayer({
    id: 'seamark-line-labels', type: 'symbol', source: 'seamark', 'source-layer': 'seamark', minzoom: 12, filter: ['==', ['geometry-type'], 'LineString'],
    layout: { 'symbol-placement': 'line', 'text-field': ['coalesce', ['get', 'name'], ''], 'text-size': 10, 'text-font': ['Noto Sans Regular'], 'text-offset': [0, -0.8] },
    paint: { 'text-color': '#263238', 'text-halo-color': '#ffffff', 'text-halo-width': 1 },
  });
  // light sectors: arcs computed from the "sectors" property of the lights in view (OpenSeaMap-style coloured segments)
  map.addSource('seamark-sectors', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
  map.addLayer({ id: 'seamark-sectors', type: 'line', source: 'seamark-sectors', minzoom: 11, paint: { 'line-color': ['get', 'colour'], 'line-width': 2.5, 'line-opacity': 0.9 } });
  map.addLayer({ id: 'seamark-sector-legs', type: 'line', source: 'seamark-sectors', minzoom: 11, filter: ['==', ['get', 'leg'], true], paint: { 'line-color': '#616161', 'line-width': 0.8, 'line-dasharray': [3, 3], 'line-opacity': 0.8 } });
  const updateSectors = () => { if (map.getZoom() >= 11) (map.getSource('seamark-sectors') as maplibregl.GeoJSONSource).setData(sectorFeatures(map)); };
  map.on('idle', updateSectors);
  // chart symbols from the sprite: body (buoy, beacon, light, landmark, hazard, facility …), then topmark and light flare on top
  map.addLayer({
    id: 'seamark-icons', type: 'symbol', source: 'seamark', 'source-layer': 'seamark', minzoom: 11, filter: hazardFilter(null),
    layout: { 'icon-image': chartIconExpression, 'icon-size': ['interpolate', ['linear'], ['zoom'], 11, 0.5, 13, 0.8, 16, 1], 'icon-anchor': 'bottom', 'icon-allow-overlap': true, 'icon-ignore-placement': true },
  });
  map.addLayer({
    id: 'seamark-topmarks', type: 'symbol', source: 'seamark', 'source-layer': 'seamark', minzoom: 12, filter: ['all', ['has', 'topmark_shape'], ['any', ['in', 'buoy', ['get', 'type']], ['in', 'beacon', ['get', 'type']]]],
    layout: { 'icon-image': chartTopmarkExpression, 'icon-size': ['interpolate', ['linear'], ['zoom'], 11, 0.5, 13, 0.8, 16, 1], 'icon-anchor': 'bottom', 'icon-offset': chartTopmarkOffset, 'icon-allow-overlap': true, 'icon-ignore-placement': true },
  });
  map.addLayer({
    id: 'seamark-light-flares', type: 'symbol', source: 'seamark', 'source-layer': 'seamark', minzoom: 12, filter: ['all', ['has', 'light_colour'], ['!', ['in', ['get', 'type'], ['literal', ['light_major', 'light_minor']]]]],
    layout: { 'icon-image': lightIconExpression, 'icon-size': ['interpolate', ['linear'], ['zoom'], 12, 0.6, 14, 0.9], 'icon-anchor': 'top', 'icon-offset': [0, 2], 'icon-rotate': -45, 'icon-rotation-alignment': 'viewport', 'icon-allow-overlap': true, 'icon-ignore-placement': true },
  });
  map.addLayer({
    id: 'seamark-hazard-depths', type: 'symbol', source: 'seamark', 'source-layer': 'seamark', minzoom: 13, filter: ['all', hazardFilter(null), ['in', ['get', 'type'], ['literal', ['rock', 'wreck', 'obstruction']]], ['has', 'depth']],
    layout: { 'text-field': ['to-string', ['get', 'depth']], 'text-font': ['Noto Sans Italic'], 'text-size': 10, 'text-variable-anchor': ['left', 'right', 'bottom', 'top'], 'text-radial-offset': 1.2, 'text-optional': true },
    paint: { 'text-color': ['case', ['<=', ['get', 'depth'], 2], '#000', '#768c97'], 'text-halo-color': 'rgba(255,255,255,0.8)', 'text-halo-width': 1.5 },
  });
  for (const layer of ['seamark-points', 'seamark-icons', 'seamark-topmarks']) {
    map.on('click', layer, e => { const f = e.features?.[0]; if (f) showSeamarkPopup(map, f.properties as Record<string, unknown>, e.lngLat); });
    map.on('mouseenter', layer, () => { map.getCanvas().style.cursor = 'pointer'; });
    map.on('mouseleave', layer, () => { map.getCanvas().style.cursor = ''; });
  }
  map.addLayer({
    id: 'seamark-light-halo', type: 'circle', source: 'seamark', 'source-layer': 'seamark', minzoom: 8,
    filter: ['in', ['get', 'type'], ['literal', ['light_major', 'light_minor']]],
    paint: { 'circle-color': '#ffeb3b', 'circle-opacity': 0.25, 'circle-radius': ['interpolate', ['linear'], ['zoom'], 8, 6, 14, 16] },
  });
  map.addLayer({
    id: 'seamark-labels', type: 'symbol', source: 'seamark', 'source-layer': 'seamark', minzoom: 13, filter: ['==', ['geometry-type'], 'Point'],
    layout: {
      // name on the first line, the light characteristic ("Oc(2)WRG.9s16m13-9M") below it, like a chart
      'text-field': ['case', ['all', ['has', 'name'], ['has', 'light']], ['concat', ['get', 'name'], '\n', ['get', 'light']], ['coalesce', ['get', 'name'], ['get', 'light'], '']], 'text-size': 10, 'text-offset': [0, 1.1], 'text-anchor': 'top',
      'text-font': ['Noto Sans Regular'], 'text-optional': true, 'text-allow-overlap': false,
    },
    paint: { 'text-color': '#263238', 'text-halo-color': '#ffffff', 'text-halo-width': 1 },
  });
}

/** Hides rocks, wrecks and obstructions that lie deeper than this (the ship's warning or minimum depth, margin included; null = show all). */
export function applyHazardFilter(map: MlMap, depthM: number | null): void {
  const f = hazardFilter(depthM, 0);
  for (const id of ['seamark-icons', 'seamark-points']) if (map.getLayer(id)) map.setFilter(id, f);
  if (map.getLayer('seamark-hazard-depths')) map.setFilter('seamark-hazard-depths', ['all', f, ['in', ['get', 'type'], ['literal', ['rock', 'wreck', 'obstruction']]], ['has', 'depth']]);
}

/** Arc polylines for every light with sectors currently rendered: radius shrinks with zoom so arcs stay readable. */
export function sectorFeatures(map: MlMap): GeoJSON.FeatureCollection {
  const features: GeoJSON.Feature[] = [];
  const seen = new Set<string>();
  const radiusM = 900 * Math.pow(2, 14 - map.getZoom());
  for (const f of map.queryRenderedFeatures({ layers: ['seamark-icons', 'seamark-points'] }) as unknown as { properties: Record<string, unknown>; geometry: { type: string; coordinates: [number, number] } }[]) {
    const sectors = f.properties['sectors'] as string | undefined;
    if (!sectors || f.geometry.type !== 'Point') continue;
    const key = `${f.properties['osm_type']}/${f.properties['osm_id']}`;
    if (seen.has(key)) continue; seen.add(key);
    const [lng, lat] = f.geometry.coordinates;
    for (const s of sectors.split(';')) {
      const [c, start, end] = s.split(':');
      const colour = { W: '#ffeb3b', R: '#d32f2f', G: '#2e7d32', Y: '#fbc02d', Bu: '#1565c0', Or: '#ef6c00', Am: '#ffb300', Vi: '#8e24aa' }[c] ?? '#9e9e9e';
      if (start === '' || end === '') continue; // all-round light: no arc
      // sector bearings are given from seaward (towards the light); the arc is drawn on the opposite side, as on charts
      const a0 = (Number(start) + 180) % 360, a1 = (Number(end) + 180) % 360;
      const span = ((a1 - a0) % 360 + 360) % 360 || 360;
      const steps = Math.max(4, Math.ceil(span / 6));
      const arc: [number, number][] = [];
      for (let i = 0; i <= steps; i++) arc.push(destination(lat, lng, a0 + span * i / steps, radiusM));
      features.push({ type: 'Feature', properties: { colour, leg: false }, geometry: { type: 'LineString', coordinates: arc } });
      features.push({ type: 'Feature', properties: { colour, leg: true }, geometry: { type: 'LineString', coordinates: [[lng, lat], arc[0]] } });
      features.push({ type: 'Feature', properties: { colour, leg: true }, geometry: { type: 'LineString', coordinates: [[lng, lat], arc[arc.length - 1]] } });
    }
  }
  return { type: 'FeatureCollection', features };
}
function destination(lat: number, lng: number, bearingDeg: number, distanceM: number): [number, number] {
  const r = Math.PI / 180, d = distanceM / 6371008.8, b = bearingDeg * r, la = lat * r;
  const la2 = Math.asin(Math.sin(la) * Math.cos(d) + Math.cos(la) * Math.sin(d) * Math.cos(b));
  const lo2 = lng * r + Math.atan2(Math.sin(b) * Math.sin(d) * Math.cos(la), Math.cos(d) - Math.sin(la) * Math.sin(la2));
  return [lo2 / r, la2 / r];
}

/** Popup with the mark's properties, its chart position and a link to the OSM object (all text nodes, no HTML parsing). */
export function showSeamarkPopup(map: MlMap, p: Record<string, unknown>, lngLat: maplibregl.LngLat): void {
  const lang = localStorage.getItem('lang') === 'en' ? 'en' : 'de';
  const root = document.createElement('div'); root.className = 'seamark-popup';
  const title = document.createElement('b'); title.textContent = String(p['name'] ?? seamarkTypeLabel(String(p['type'] ?? ''), lang)); root.append(title);
  const line = (label: string, value: unknown) => { if (value === null || value === undefined || value === '') return; const d = document.createElement('div'); d.append(textEl(label + ': ', 'span'), textEl(String(value), 'span')); root.append(d); };
  if (p['name']) line(lang === 'en' ? 'Type' : 'Typ', seamarkTypeLabel(String(p['type'] ?? ''), lang));
  line(lang === 'en' ? 'Category' : 'Kategorie', p['category']); line(lang === 'en' ? 'Shape' : 'Form', p['shape']); line(lang === 'en' ? 'Colour' : 'Farbe', p['colour']); line(lang === 'en' ? 'Light' : 'Feuer', p['light']);
  const pos = document.createElement('div'); pos.className = 'pos';
  const lat = Number(lngLat.lat.toFixed(5)), lng = Number(lngLat.lng.toFixed(5));
  pos.append(textEl(formatPosition(lat, lng), 'b'), document.createElement('br'), textEl(`${lat}, ${lng}`, 'span'));
  root.append(pos);
  const links = document.createElement('div'); links.className = 'links';
  if (p['osm_id']) { const a = document.createElement('a'); a.href = `https://www.openstreetmap.org/${p['osm_type'] ?? 'node'}/${p['osm_id']}`; a.target = '_blank'; a.rel = 'noreferrer'; a.textContent = 'OpenStreetMap'; links.append(a); }
  const copy = document.createElement('button'); copy.type = 'button'; copy.textContent = lang === 'en' ? 'Copy position' : 'Position kopieren';
  copy.onclick = () => { void navigator.clipboard?.writeText(`${lat}, ${lng}`); copy.textContent = '✔'; };
  links.append(copy); root.append(links);
  new maplibregl.Popup({ offset: 12, maxWidth: '280px' }).setLngLat(lngLat).setDOMContent(root).addTo(map);
}

/** HTML element with user text for popups/markers (never parsed as HTML). */
export function textEl(text: string, tag = 'span'): HTMLElement { const el = document.createElement(tag); el.textContent = text; return el; }

export function bounds(coords: [number, number][]): maplibregl.LngLatBounds {
  const b = new maplibregl.LngLatBounds(coords[0], coords[0]);
  for (const c of coords) b.extend(c);
  return b;
}

export { maplibregl };
export type { MlMap, StyleSpecification };
