import type { GeoJSONSource, PointLike } from 'maplibre-gl';
import { maplibregl, MlMap } from './map';
import { insertVia, nearestOnPolyline, removeAt, replaceAt } from './map-point';

type Pos = [number, number];
export interface LineEditorOptions {
  /** A polygon ring (drawn closed, the closing segment can take vertices too). */
  closed?: boolean;
  colour?: string;
  /** Applied to a dropped/added vertex, e.g. snapping to a buoy. */
  snap?: (p: Pos) => Pos;
  onChange?: (points: Pos[]) => void;
}

/** Marker elements sit inside the map container: their clicks reach the map's click handlers too, which should ignore them. */
export const isMarkerEvent = (ev: Event) => !!(ev.target as HTMLElement | null)?.closest?.('.maplibregl-marker');

const HOVER_PX = 10, TAP_PX = 16, VERTEX_PX = 8;
let seq = 0;

/**
 * Editable line on a MapLibre map, the route-dragging interaction of the harbour map: every vertex is a draggable handle with a small ×
 * (or right-click) to delete it; near the line a handle follows the mouse along it, a click or drag there inserts a vertex between the two
 * neighbours. Touch has no hover: a tap near the line (tryInsert) inserts there, vertices drag and × taps as with the mouse.
 */
export class LineEditor {
  private readonly id = `line-edit-${++seq}`;
  private pts: Pos[] = [];
  private markers: maplibregl.Marker[] = [];
  private readonly ghost: maplibregl.Marker;
  private ghostAt: { index: number; p: Pos } | null = null;
  private dragIndex: number | null = null;
  private dragEndAt = 0;
  private readonly onMove = (e: { point: { x: number; y: number } }) => {
    if (this.dragIndex != null) return;
    this.ghostAt = this.near(e.point, HOVER_PX);
    if (this.ghostAt) this.ghost.setLngLat(this.ghostAt.p).addTo(this.map); else this.ghost.remove();
  };

  constructor(private readonly map: MlMap, private readonly opts: LineEditorOptions = {}) {
    const colour = opts.colour ?? '#00c853';
    map.addSource(this.id, { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    map.addLayer({ id: this.id, type: 'line', source: this.id, paint: { 'line-color': colour, 'line-width': 3 } });
    const el = handleEl(colour, false, true);
    el.addEventListener('click', ev => {
      ev.stopPropagation();
      if (this.ghostAt && Date.now() - this.dragEndAt > 300) this.insert(this.ghostAt.index, this.ghostAt.p);
    });
    this.ghost = new maplibregl.Marker({ element: el, draggable: true });
    this.ghost.on('dragstart', () => {
      if (!this.ghostAt) return;
      this.dragIndex = this.ghostAt.index; this.pts = insertVia(this.pts, this.ghostAt.index, this.ghostAt.p); this.render();
    });
    this.ghost.on('drag', () => this.dragTo(this.ghost.getLngLat()));
    this.ghost.on('dragend', () => { const l = this.ghost.getLngLat(); this.ghost.remove(); this.ghostAt = null; this.drop(l); });
    map.on('mousemove', this.onMove);
  }

  get points(): Pos[] { return this.pts; }
  /** Replaces the line (no onChange). */
  set(points: Pos[]): void { this.pts = [...points]; this.rebuild(); }
  append(p: Pos): void { this.pts = [...this.pts, this.snap(p)]; this.rebuild(); this.changed(); }
  /** A tap or click near the line inserts a vertex there; true when it did (the caller then ignores the click). */
  tryInsert(point: PointLike): boolean {
    const p = Array.isArray(point) ? { x: point[0], y: point[1] } : point as { x: number; y: number };
    const h = this.near(p, TAP_PX); if (!h) return false;
    this.insert(h.index, h.p); return true;
  }
  destroy(): void {
    this.map.off('mousemove', this.onMove);
    this.ghost.remove(); for (const m of this.markers) m.remove(); this.markers = [];
    if (this.map.getLayer(this.id)) this.map.removeLayer(this.id);
    if (this.map.getSource(this.id)) this.map.removeSource(this.id);
  }

  private snap(p: Pos): Pos { return this.opts.snap?.(p) ?? p; }
  private changed(): void { this.opts.onChange?.(this.pts); }
  private insert(index: number, p: Pos): void { this.pts = insertVia(this.pts, index, this.snap(p)); this.ghost.remove(); this.ghostAt = null; this.rebuild(); this.changed(); }
  private dragTo(l: maplibregl.LngLat): void { if (this.dragIndex == null) return; this.pts = replaceAt(this.pts, this.dragIndex, [l.lng, l.lat]); this.render(); }
  /** Drag end: the marker's final position (the last drag event may lag behind it), snapped. */
  private drop(l: maplibregl.LngLat): void {
    if (this.dragIndex == null) return;
    this.pts = replaceAt(this.pts, this.dragIndex, this.snap([l.lng, l.lat]));
    this.dragIndex = null; this.dragEndAt = Date.now(); this.rebuild(); this.changed();
  }
  /** The insert position near a screen point: on a segment, not on a vertex (that one has its own handle). */
  private near(at: { x: number; y: number }, tolPx: number): { index: number; p: Pos } | null {
    if (this.pts.length < 2) return null;
    const px = this.ring().map(p => { const q = this.map.project(p); return [q.x, q.y] as Pos; });
    const r = nearestOnPolyline(px, [at.x, at.y]);
    if (!r || r.dist > tolPx) return null;
    const off = (v: Pos) => Math.hypot(v[0] - r.point[0], v[1] - r.point[1]);
    if (off(px[r.index]) < VERTEX_PX || off(px[r.index + 1]) < VERTEX_PX) return null;
    const l = this.map.unproject(r.point);
    return { index: r.index + 1, p: [l.lng, l.lat] };
  }
  private ring(): Pos[] { return this.opts.closed && this.pts.length >= 3 ? [...this.pts, this.pts[0]] : this.pts; }
  private render(): void {
    const ring = this.ring();
    (this.map.getSource(this.id) as GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection',
      features: ring.length >= 2 ? [{ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: ring } }] : [] });
  }
  /** One marker per vertex, rebuilt after every insert/delete/drop (the closures hold the index). */
  private rebuild(): void {
    for (const m of this.markers) m.remove();
    this.markers = this.pts.map((p, i) => {
      const el = handleEl(this.opts.colour ?? '#00c853', true, false);
      const remove = (ev: Event) => { ev.preventDefault(); ev.stopPropagation(); this.pts = removeAt(this.pts, i); this.rebuild(); this.changed(); };
      el.querySelector('button')!.addEventListener('click', remove);
      el.addEventListener('contextmenu', remove);
      el.addEventListener('click', ev => ev.stopPropagation());
      const m = new maplibregl.Marker({ element: el, draggable: true }).setLngLat(p).addTo(this.map);
      m.on('dragstart', () => { this.dragIndex = i; });
      m.on('drag', () => this.dragTo(m.getLngLat()));
      m.on('dragend', () => this.drop(m.getLngLat()));
      return m;
    });
    this.render();
  }
}

/** A white dot ringed in the line colour; vertices carry a small × to delete them, the hover handle is smaller and hollow. */
function handleEl(colour: string, removable: boolean, ghost: boolean): HTMLElement {
  const el = document.createElement('div');
  const size = ghost ? 10 : 14;
  el.style.cssText = `width:${size}px;height:${size}px;border-radius:50%;background:${ghost ? 'rgba(255,255,255,.7)' : '#fff'};border:3px solid ${colour};box-shadow:0 0 3px rgba(0,0,0,.4);cursor:${ghost ? 'copy' : 'grab'};touch-action:none`;
  if (removable) {
    el.title = 'Ziehen verschiebt, Rechtsklick oder × löscht';
    const x = document.createElement('button');
    x.type = 'button'; x.textContent = '×'; x.setAttribute('aria-label', 'Punkt löschen');
    x.style.cssText = 'position:absolute;left:12px;top:-14px;width:18px;height:18px;padding:0;border:none;border-radius:50%;background:#455a64;color:#fff;font:13px/18px sans-serif;cursor:pointer';
    x.addEventListener('mousedown', ev => ev.stopPropagation()); // no marker drag from the ×
    x.addEventListener('touchstart', ev => ev.stopPropagation());
    el.append(x);
  }
  return el;
}
