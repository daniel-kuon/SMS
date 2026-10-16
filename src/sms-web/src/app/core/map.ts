import * as maplibregl from 'maplibre-gl';
import type { ExpressionSpecification, Map as MlMap, MapOptions, StyleSpecification } from 'maplibre-gl';

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
  return map;
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
  map.addLayer({
    id: 'seamark-points', type: 'circle', source: 'seamark', 'source-layer': 'seamark', minzoom: 8,
    paint: {
      'circle-color': colour,
      'circle-radius': ['interpolate', ['linear'], ['zoom'], 8, 2.5, 12, 4, 16, 8],
      'circle-stroke-color': ['case', ['in', ['get', 'type'], ['literal', ['buoy_safe_water', 'buoy_cardinal', 'beacon_cardinal']]], '#d32f2f', '#ffffff'],
      'circle-stroke-width': 1.2,
      'circle-opacity': 0.95,
    },
  });
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

/** HTML element with user text for popups/markers (never parsed as HTML). */
export function textEl(text: string, tag = 'span'): HTMLElement { const el = document.createElement(tag); el.textContent = text; return el; }

export function bounds(coords: [number, number][]): maplibregl.LngLatBounds {
  const b = new maplibregl.LngLatBounds(coords[0], coords[0]);
  for (const c of coords) b.extend(c);
  return b;
}

export { maplibregl };
export type { MlMap, StyleSpecification };
