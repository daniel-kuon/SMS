import * as maplibregl from 'maplibre-gl';
import type { ExpressionSpecification, Map as MlMap, MapOptions, StyleSpecification } from 'maplibre-gl';
import { formatPosition, registerSeamarkIcons, seamarkIconExpression, seamarkTypeLabel } from './seamark-icons';

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
interface GlController { on(name: string, cb: () => void): void; isReady?: boolean; addWeatherLayer(id: string, overrides?: unknown): unknown; removeWeatherLayer(id: string): void; hasWeatherLayer(id: string): boolean; }

export class WeatherOverlays {
  readonly selected = new Set<string>();
  private gl?: Promise<GlController | null>;
  private readonly glLayers = new Map<string, string[]>();
  constructor(private readonly map: MlMap, private readonly layers: OverlayLayer[], private access: { clientId: string; clientSecret: string } | null) {
    try { for (const id of JSON.parse(localStorage.getItem(OVERLAY_STORAGE) ?? '[]') as string[]) if (OVERLAY_GROUPS.some(g => g.id === id)) this.selected.add(id); } catch { /* ignore */ }
  }
  get mode(): 'gl' | 'raster' { return this.access ? 'gl' : 'raster'; }

  async apply(): Promise<void> {
    try { localStorage.setItem(OVERLAY_STORAGE, JSON.stringify([...this.selected])); } catch { /* ignore */ }
    const gl = this.access ? await this.controller() : null;
    for (const g of OVERLAY_GROUPS) {
      const on = this.selected.has(g.id);
      if (gl) this.applyGl(gl, g, on); else this.applyRaster(g, on);
    }
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
    const tryAdd = (ids: string[]) => { for (const id of ids) { try { gl.addWeatherLayer(id, id.endsWith('-particles') ? undefined : { paint: { opacity: 0.55 } }); added.push(id); } catch (e) { console.warn('MapsGL layer failed', id, e); } } };
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
        const c = new sdk.MaplibreMapController(this.map as never, { account, units: { temperature: 'C', speed: 'kts', pressure: 'hPa', distance: 'km', height: 'm' } } as never) as unknown as GlController;
        await new Promise<void>(res => (c.isReady ? res() : c.on('load', () => res())));
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
    const refresh = () => { button.textContent = overlays.selected.size ? `☁ ${overlays.selected.size}` : '☁'; button.classList.toggle('active', overlays.selected.size > 0); };
    for (const g of OVERLAY_GROUPS) {
      const label = document.createElement('label'); const cb = document.createElement('input'); cb.type = 'checkbox'; cb.value = g.id; cb.checked = overlays.selected.has(g.id);
      cb.onchange = () => { if (cb.checked) overlays.selected.add(g.id); else overlays.selected.delete(g.id); refresh(); void overlays.apply(); };
      label.append(cb, document.createTextNode(' ' + g.name)); panel.appendChild(label);
    }
    const note = document.createElement('div'); note.className = 'muted'; note.textContent = overlays.mode === 'gl' ? 'Xweather MapsGL' : 'Xweather (Kacheln)'; panel.appendChild(note);
    button.onclick = () => { panel.hidden = !panel.hidden; };
    map.addControl({ onAdd: () => { wrap.append(button, panel); return wrap; }, onRemove: () => wrap.remove() }, 'top-right');
    refresh();
    if (overlays.selected.size) void overlays.apply();
  }).catch(() => { /* no overlay without the API */ });
}

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
  registerSeamarkIcons(map);
  map.addLayer({
    id: 'seamark-icons', type: 'symbol', source: 'seamark', 'source-layer': 'seamark', minzoom: 11,
    layout: { 'icon-image': seamarkIconExpression, 'icon-size': ['interpolate', ['linear'], ['zoom'], 11, 0.45, 14, 0.8, 17, 1.1], 'icon-anchor': 'bottom', 'icon-allow-overlap': true, 'icon-ignore-placement': true },
  });
  for (const layer of ['seamark-points', 'seamark-icons']) {
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
    id: 'seamark-labels', type: 'symbol', source: 'seamark', 'source-layer': 'seamark', minzoom: 13,
    layout: {
      'text-field': ['coalesce', ['get', 'name'], ['get', 'light'], ''], 'text-size': 10, 'text-offset': [0, 1.1], 'text-anchor': 'top',
      'text-font': ['Noto Sans Regular'], 'text-optional': true, 'text-allow-overlap': false,
    },
    paint: { 'text-color': '#263238', 'text-halo-color': '#ffffff', 'text-halo-width': 1 },
  });
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
