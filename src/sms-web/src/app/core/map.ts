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

/** Weather raster layers (rain radar, wind, …) proxied by the API; the user picks one in a small control, none by default. */
export interface OverlayLayer { id: string; name: string; attribution: string; }
export function setWeatherOverlay(map: MlMap, layer: OverlayLayer | null): void {
  if (map.getLayer('weather-overlay')) map.removeLayer('weather-overlay');
  if (map.getSource('weather-overlay')) map.removeSource('weather-overlay');
  if (!layer) return;
  map.addSource('weather-overlay', { type: 'raster', tiles: [`${location.origin}/api/weather/overlay/${layer.id}/{z}/{x}/{y}.png`], tileSize: 256, maxzoom: 12, attribution: layer.attribution });
  const before = map.getLayer('seamark-points') ? 'seamark-points' : undefined;
  map.addLayer({ id: 'weather-overlay', type: 'raster', source: 'weather-overlay', paint: { 'raster-opacity': 0.65 } }, before);
}
export function addWeatherOverlayControl(map: MlMap): void {
  fetch('/api/weather/overlay/layers').then(r => r.json() as Promise<{ configured: boolean; layers: OverlayLayer[] }>).then(cfg => {
    if (!cfg.configured || !cfg.layers.length) return;
    const select = document.createElement('select');
    select.className = 'weather-overlay-select'; select.title = 'Wetter-Overlay'; select.setAttribute('aria-label', 'Wetter-Overlay');
    const none = document.createElement('option'); none.value = ''; none.textContent = '☁ Wetter: aus'; select.appendChild(none);
    for (const l of cfg.layers) { const o = document.createElement('option'); o.value = l.id; o.textContent = l.name; select.appendChild(o); }
    select.onchange = () => setWeatherOverlay(map, cfg.layers.find(l => l.id === select.value) ?? null);
    map.addControl({
      onAdd: () => { const div = document.createElement('div'); div.className = 'maplibregl-ctrl maplibregl-ctrl-group'; div.appendChild(select); return div; },
      onRemove: () => { select.parentElement?.remove(); },
    }, 'top-right');
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
