import type { FeatureCollection } from 'geojson';
import type { GeoJSONSource, MapLayerMouseEvent, PointLike } from 'maplibre-gl';
import { bounds, maplibregl, MlMap, textEl } from '../../core/map';
import { allPoints, altColor, insertVertex, MapHarbour, moveVertex, nearestSegment, removeVertex, TripMapData } from './trip-map-model';

export type SelKind = 'leg' | 'entry' | 'harbour' | 'photo';
const EMPTY: FeatureCollection = { type: 'FeatureCollection', features: [] };
const MAX_PHOTOS = 200;

/**
 * Trip layer set on top of the shared map (docs/14): planned legs dashed (colour per alternative), logbook legs solid,
 * harbours (planned outlined, visited filled), photo thumbnails; hover labels, click-to-select, ease-to-selection.
 * In pick mode (`pick` set) harbour clicks go to the callback instead, and the bbox harbours (`setPickHarbours`) are clickable too.
 */
export class TripMapLayers {
  data: TripMapData = { plannedLegs: [], logbook: [], harbours: [], photos: [] };
  pick: ((h: MapHarbour) => void) | null = null;
  /** Extra hover text in pick mode, e.g. the routed distance from the last stop. */
  pickLabel: ((h: MapHarbour) => Promise<string | null>) | null = null;
  /** While a route is being edited, clicks on the trip lines do not select (they belong to the editor). */
  busy = false;
  private photoMarkers = new Map<string, maplibregl.Marker>();
  private popup?: maplibregl.Popup;
  private sel: { kind: SelKind; id: string } | null = null;
  private names = new Map<string, string>();

  constructor(private map: MlMap, private onSelect: (kind: SelKind, id: string) => void) {
    const sel = ['boolean', ['feature-state', 'sel'], false];
    map.addSource('pick-harbours', { type: 'geojson', data: EMPTY, promoteId: 'id' });
    map.addLayer({ id: 'pick-harbour-points', type: 'circle', source: 'pick-harbours', paint: { 'circle-color': '#90a4ae', 'circle-radius': 6, 'circle-stroke-color': '#fff', 'circle-stroke-width': 2 } });
    map.addLayer({ id: 'pick-harbour-labels', type: 'symbol', source: 'pick-harbours', layout: { 'text-field': ['get', 'name'], 'text-size': 11, 'text-anchor': 'left', 'text-offset': [0.9, 0], 'text-font': ['Noto Sans Regular'], 'text-optional': true }, paint: { 'text-color': '#455a64', 'text-halo-color': '#fff', 'text-halo-width': 1.5 } });
    map.addSource('trip-logbook', { type: 'geojson', data: EMPTY, promoteId: 'id' });
    map.addLayer({ id: 'trip-logbook', type: 'line', source: 'trip-logbook', layout: { 'line-cap': 'round' }, paint: { 'line-color': ['case', sel, '#d32f2f', '#1b4d7a'], 'line-width': ['case', sel, 7, 4] } as never });
    map.addSource('trip-planned', { type: 'geojson', data: EMPTY, promoteId: 'id' });
    map.addLayer({ id: 'trip-planned', type: 'line', source: 'trip-planned', paint: { 'line-color': ['get', 'color'], 'line-width': ['case', sel, 7, 4], 'line-dasharray': [2, 1.5] } as never });
    map.addSource('trip-harbours', { type: 'geojson', data: EMPTY, promoteId: 'id' });
    map.addLayer({ id: 'trip-harbour-points', type: 'circle', source: 'trip-harbours', paint: { 'circle-color': ['case', ['get', 'visited'], '#1b4d7a', '#ffffff'], 'circle-radius': ['case', sel, 9, 6], 'circle-stroke-color': '#1b4d7a', 'circle-stroke-width': 2.5 } as never });
    map.addLayer({ id: 'trip-harbour-labels', type: 'symbol', source: 'trip-harbours', layout: { 'text-field': ['get', 'name'], 'text-size': 12, 'text-anchor': 'left', 'text-offset': [0.9, 0], 'text-font': ['Noto Sans Regular'] }, paint: { 'text-color': '#1b4d7a', 'text-halo-color': '#fff', 'text-halo-width': 1.5 } });

    for (const [layer, kind] of [['trip-planned', 'leg'], ['trip-logbook', 'entry'], ['trip-harbour-points', 'harbour']] as const) {
      map.on('mouseenter', layer, e => { map.getCanvas().style.cursor = 'pointer'; this.hover(e); });
      map.on('mouseleave', layer, () => { map.getCanvas().style.cursor = ''; this.popup?.remove(); });
      map.on('click', layer, e => {
        const id = e.features?.[0]?.properties?.['id'] as string | undefined; if (!id) return;
        if (kind === 'harbour' && this.pick) { this.pickById(id); return; }
        if (this.pick || this.busy) return;
        this.select(kind, id, false); this.onSelect(kind, id);
      });
    }
    map.on('mouseenter', 'pick-harbour-points', e => { map.getCanvas().style.cursor = 'pointer'; this.hover(e); });
    map.on('mouseleave', 'pick-harbour-points', () => { map.getCanvas().style.cursor = ''; this.popup?.remove(); });
    map.on('click', 'pick-harbour-points', e => {
      const p = e.features?.[0]?.properties; if (!p || !this.pick) return;
      if (map.queryRenderedFeatures(e.point, { layers: ['trip-harbour-points'] }).length) return; // the trip harbour handler takes it
      const [lng, lat] = (e.features![0].geometry as GeoJSON.Point).coordinates;
      this.pick({ id: p['id'], name: p['name'], lat, lng });
    });
  }

  name = (id: string) => this.names.get(id) ?? '?';
  remember(h: { id: string; name: string }): void { this.names.set(h.id, h.name); }

  setData(d: TripMapData): void {
    this.data = d;
    for (const h of d.harbours) this.names.set(h.id, h.name);
    const pos = new Map(d.harbours.map(h => [h.id, [h.lng, h.lat]]));
    const line = (g: { coordinates: number[][] } | null, a: string, b: string) => g?.coordinates.length ? g.coordinates : [pos.get(a), pos.get(b)].filter((c): c is number[] => !!c);
    const visited = new Set(d.logbook.flatMap(e => [e.startHarbourId, e.endHarbourId]));
    this.set('trip-planned', d.plannedLegs.map(l => ({ type: 'Feature', properties: { id: l.id, color: altColor(l.alternative), label: `${this.name(l.fromHarbourId)} → ${this.name(l.toHarbourId)}${l.distanceNm != null ? ` · ${l.distanceNm} sm` : ''}` }, geometry: { type: 'LineString', coordinates: line(l.geometry, l.fromHarbourId, l.toHarbourId) } })));
    this.set('trip-logbook', d.logbook.map(e => ({ type: 'Feature', properties: { id: e.id, label: `${this.name(e.startHarbourId)} → ${this.name(e.endHarbourId)}` }, geometry: { type: 'LineString', coordinates: line(e.geometry, e.startHarbourId, e.endHarbourId) } })));
    this.set('trip-harbours', d.harbours.map(h => ({ type: 'Feature', properties: { id: h.id, name: h.name, label: h.name, visited: visited.has(h.id) }, geometry: { type: 'Point', coordinates: [h.lng, h.lat] } })));
    for (const m of this.photoMarkers.values()) m.remove();
    this.photoMarkers.clear();
    for (const p of d.photos.slice(0, MAX_PHOTOS)) { // ponytail: HTML markers, fine up to a few hundred; a symbol layer with loaded images beyond that
      const img = document.createElement('img'); img.src = p.thumbUrl; img.className = 'photo-marker'; img.width = 32; img.height = 32; img.alt = '';
      img.onclick = ev => { ev.stopPropagation(); this.select('photo', p.id, false); this.onSelect('photo', p.id); };
      this.photoMarkers.set(p.id, new maplibregl.Marker({ element: img }).setLngLat([p.lng, p.lat]).addTo(this.map));
    }
    if (this.sel) this.select(this.sel.kind, this.sel.id, false);
  }

  /** Fits everything with 40 px padding; without content the fallback position (ship's home, planned start). */
  fit(fallback?: { lat: number; lng: number } | null): void {
    const pts = allPoints(this.data);
    this.map.resize();
    if (pts.length) this.map.fitBounds(bounds(pts), { padding: 40, maxZoom: 12, duration: 0 });
    else if (fallback) this.map.jumpTo({ center: [fallback.lng, fallback.lat], zoom: 10 });
  }

  setPickHarbours(hs: MapHarbour[]): void {
    for (const h of hs) this.names.set(h.id, h.name);
    this.set('pick-harbours', hs.map(h => ({ type: 'Feature', properties: { id: h.id, name: h.name, label: h.name }, geometry: { type: 'Point', coordinates: [h.lng, h.lat] } })));
  }

  /** Highlights an item; `ease` moves the map to it (keeps the zoom when it is already in view). */
  select(kind: SelKind, id: string, ease = true): void {
    const src = (k: SelKind) => (k === 'leg' ? 'trip-planned' : k === 'entry' ? 'trip-logbook' : k === 'harbour' ? 'trip-harbours' : null);
    if (this.sel) { const s = src(this.sel.kind); if (s && this.map.getSource(s)) this.map.setFeatureState({ source: s, id: this.sel.id }, { sel: false }); this.photoMarkers.get(this.sel.id)?.getElement().classList.remove('sel'); }
    this.sel = { kind, id };
    const s = src(kind);
    if (s) this.map.setFeatureState({ source: s, id }, { sel: true }); else this.photoMarkers.get(id)?.getElement().classList.add('sel');
    if (!ease) return;
    const pts = this.coords(kind, id); if (!pts.length) return;
    const b = bounds(pts), view = this.map.getBounds();
    if (view.contains(b.getSouthWest()) && view.contains(b.getNorthEast())) this.map.easeTo({ center: b.getCenter() });
    else this.map.fitBounds(b, { padding: 60, maxZoom: Math.max(this.map.getZoom(), 11) });
  }

  private coords(kind: SelKind, id: string): [number, number][] {
    const pos = new Map(this.data.harbours.map(h => [h.id, [h.lng, h.lat] as [number, number]]));
    const ends = (a: string, b: string) => [pos.get(a), pos.get(b)].filter((c): c is [number, number] => !!c);
    const geo = (g: { coordinates: number[][] } | null) => (g?.coordinates ?? []).map(c => [c[0], c[1]] as [number, number]);
    if (kind === 'leg') { const l = this.data.plannedLegs.find(x => x.id === id); return l ? (l.geometry?.coordinates.length ? geo(l.geometry) : ends(l.fromHarbourId, l.toHarbourId)) : []; }
    if (kind === 'entry') { const e = this.data.logbook.find(x => x.id === id); return e ? (e.geometry?.coordinates.length ? geo(e.geometry) : ends(e.startHarbourId, e.endHarbourId)) : []; }
    if (kind === 'harbour') { const p = pos.get(id); return p ? [p] : []; }
    const p = this.data.photos.find(x => x.id === id); return p ? [[p.lng, p.lat]] : [];
  }

  private set(source: string, features: unknown[]): void { (this.map.getSource(source) as GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection', features } as FeatureCollection); }

  private pickById(id: string): void {
    const h = this.data.harbours.find(x => x.id === id);
    if (h && this.pick) this.pick(h);
  }

  private hover(e: MapLayerMouseEvent): void {
    const f = e.features?.[0]; const label = f?.properties?.['label'] as string | undefined;
    if (!label) return;
    this.popup?.remove();
    const el = textEl(label);
    const popup = this.popup = new maplibregl.Popup({ closeButton: false, closeOnClick: false }).setLngLat(e.lngLat).setDOMContent(el).addTo(this.map);
    if (this.pick && this.pickLabel && f!.geometry.type === 'Point') {
      const [lng, lat] = (f!.geometry as GeoJSON.Point).coordinates;
      void this.pickLabel({ id: f!.properties!['id'], name: f!.properties!['name'], lat, lng }).then(extra => { if (extra && this.popup === popup) el.textContent = `${label} · ${extra}`; });
    }
  }
}

/**
 * Editable route line for a logbook entry: drag vertices to move them, click on the line to insert one, right-click a vertex to remove it.
 * ponytail: mouse only; touch editing (long press) would need touchstart/touchmove handlers on the same layers.
 */
export class RouteEditor {
  coords: number[][] | null = null;
  private drag = -1;

  constructor(private map: MlMap) {
    map.addSource('edit-route', { type: 'geojson', data: EMPTY });
    map.addSource('edit-vertices', { type: 'geojson', data: EMPTY });
    map.addLayer({ id: 'edit-route-line', type: 'line', source: 'edit-route', layout: { 'line-cap': 'round', 'line-join': 'round' }, paint: { 'line-color': '#e65100', 'line-width': 4 } });
    map.addLayer({ id: 'edit-route-hit', type: 'line', source: 'edit-route', paint: { 'line-color': '#000', 'line-opacity': 0, 'line-width': 14 } });
    map.addLayer({ id: 'edit-vertices', type: 'circle', source: 'edit-vertices', paint: { 'circle-color': '#fff', 'circle-radius': 5, 'circle-stroke-color': '#e65100', 'circle-stroke-width': 2 } });

    const move = (e: MapLayerMouseEvent | maplibregl.MapMouseEvent) => { if (this.drag >= 0 && this.coords) { this.coords = moveVertex(this.coords, this.drag, [e.lngLat.lng, e.lngLat.lat]); this.render(); } };
    const up = () => { this.drag = -1; map.dragPan.enable(); map.off('mousemove', move); map.getCanvas().style.cursor = ''; };
    map.on('mousedown', 'edit-vertices', e => {
      if (e.originalEvent.button !== 0) return;
      e.preventDefault(); map.dragPan.disable();
      this.drag = Number(e.features?.[0]?.properties?.['i'] ?? -1);
      map.getCanvas().style.cursor = 'grabbing';
      map.on('mousemove', move); map.once('mouseup', up);
    });
    map.on('click', 'edit-route-hit', e => {
      if (!this.coords || this.hitsVertex(e.point)) return;
      const seg = nearestSegment(this.coords.map(c => { const p = map.project([c[0], c[1]]); return [p.x, p.y] as [number, number]; }), [e.point.x, e.point.y]);
      this.coords = insertVertex(this.coords, seg, [e.lngLat.lng, e.lngLat.lat]); this.render();
    });
    map.on('contextmenu', 'edit-vertices', e => {
      const i = Number(e.features?.[0]?.properties?.['i'] ?? -1);
      if (this.coords && i >= 0) { this.coords = removeVertex(this.coords, i); this.render(); }
    });
    map.on('mouseenter', 'edit-vertices', () => { if (this.drag < 0) map.getCanvas().style.cursor = 'grab'; });
    map.on('mouseleave', 'edit-vertices', () => { if (this.drag < 0) map.getCanvas().style.cursor = ''; });
  }

  set(coords: number[][] | null): void { this.coords = coords?.length ? coords.map(c => [c[0], c[1]]) : null; this.render(); }
  hitsVertex(p: PointLike): boolean { return !!this.coords && this.map.queryRenderedFeatures(p, { layers: ['edit-vertices'] }).length > 0; }

  private render(): void {
    const c = this.coords ?? [];
    (this.map.getSource('edit-route') as GeoJSONSource).setData(c.length ? { type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: c } } : EMPTY);
    (this.map.getSource('edit-vertices') as GeoJSONSource).setData({ type: 'FeatureCollection', features: c.map((x, i) => ({ type: 'Feature', properties: { i }, geometry: { type: 'Point', coordinates: x } })) });
  }
}
