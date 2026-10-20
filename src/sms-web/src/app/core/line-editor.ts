import type { GeoJSONSource, PointLike } from 'maplibre-gl';
import type { Geometry } from 'geojson';
import { maplibregl, MlMap } from './map';
import { nearestOnPolyline } from './map-point';
import { geometryToLines, insertInto, Lines, moveShared, nearestVertex, Pos, removeShared, samePos, startBranch, uniqueVertices } from './line-network';

export interface LineEditorOptions {
  /** A polygon ring (drawn closed, the closing segment can take vertices too; no branches). */
  closed?: boolean;
  colour?: string;
  /** Applied to a dropped/added vertex, e.g. snapping to a buoy. */
  snap?: (p: Pos) => Pos;
  onChange?: (lines: Lines) => void;
  /** The attach node changed (null: none). */
  onAttach?: (p: Pos | null) => void;
}

/** Marker elements sit inside the map container: their clicks reach the map's click handlers too, which should ignore them. */
export const isMarkerEvent = (ev: Event) => !!(ev.target as HTMLElement | null)?.closest?.('.maplibregl-marker');

/**
 * The vertex of another line on the map nearest to a screen point (within tolPx), exact: rendered features carry tile-rounded coordinates,
 * so the line is looked up by its id property (geometryOf) and its stored coordinates are used – a branch attached there joins it exactly.
 */
export function vertexNear(map: MlMap, point: { x: number; y: number }, layers: string[], geometryOf: (id: unknown) => Geometry | null | undefined, tolPx = 10): Pos | null {
  const box: [PointLike, PointLike] = [[point.x - tolPx, point.y - tolPx], [point.x + tolPx, point.y + tolPx]];
  const verts = map.queryRenderedFeatures(box, { layers: layers.filter(l => map.getLayer(l)) }).flatMap(f => geometryToLines(geometryOf(f.properties?.['id'])).flat());
  const i = nearestVertex(verts.map(v => { const q = map.project(v); return [q.x, q.y] as Pos; }), [point.x, point.y], tolPx);
  return i < 0 ? null : verts[i];
}

const HOVER_PX = 10, TAP_PX = 16, VERTEX_PX = 8, ATTACH_COLOUR = '#ff6d00';
let seq = 0;

/**
 * Editable line on a MapLibre map, the route-dragging interaction of the harbour map: every vertex is a draggable handle with a small ×
 * (or right-click) to delete it; near the line a handle follows the mouse along it, a click or drag there inserts a vertex between the two
 * neighbours. Touch has no hover: a tap near the line (tryInsert) inserts there, vertices drag and × taps as with the mouse.
 * Junctions: a double-click on a vertex (or attachAt for a vertex of another line on the map) makes it the attach node; the next added point
 * starts a new branch there. Branches meet at the exact same coordinate, one handle moves or deletes the junction for all of them.
 */
export class LineEditor {
  private readonly id = `line-edit-${++seq}`;
  private ls: Lines = [[]];
  private active = 0;
  private attachNode: Pos | null = null;
  private history: { t: number; p: Pos; ls: Lines; active: number; attach: Pos | null }[] = [];
  private markers: maplibregl.Marker[] = [];
  private readonly ghost: maplibregl.Marker;
  private ghostAt: { branch: number; index: number; p: Pos } | null = null;
  private dragAt: Pos | null = null;
  private dragEndAt = 0;
  private markerTimer?: ReturnType<typeof setTimeout>;
  private readonly onMove = (e: { point: { x: number; y: number } }) => {
    if (this.dragAt) return;
    this.ghostAt = this.near(e.point, HOVER_PX);
    if (this.ghostAt) this.ghost.setLngLat(this.ghostAt.p).addTo(this.map); else this.ghost.remove();
  };

  constructor(private readonly map: MlMap, private readonly opts: LineEditorOptions = {}) {
    const colour = opts.colour ?? '#00c853';
    map.addSource(this.id, { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    map.addLayer({ id: this.id, type: 'line', source: this.id, paint: { 'line-color': colour, 'line-width': 3 } });
    const el = handleEl(colour, 'ghost');
    el.addEventListener('click', ev => {
      ev.stopPropagation();
      if (this.ghostAt && Date.now() - this.dragEndAt > 300) this.insert(this.ghostAt.branch, this.ghostAt.index, this.ghostAt.p);
    });
    el.addEventListener('dblclick', ev => ev.stopPropagation());
    this.ghost = new maplibregl.Marker({ element: el, draggable: true });
    this.ghost.on('dragstart', () => {
      const g = this.ghostAt; if (!g) return;
      this.ls = insertInto(this.ls, g.branch, g.index, g.p); this.dragAt = g.p; this.render();
    });
    this.ghost.on('drag', () => this.dragTo(this.ghost.getLngLat()));
    this.ghost.on('dragend', () => { const l = this.ghost.getLngLat(); this.ghost.remove(); this.ghostAt = null; this.drop(l); });
    map.on('mousemove', this.onMove);
  }

  get lines(): Lines { return this.ls; }
  /** The vertex new points attach to (a junction in the making), or null. */
  get attach(): Pos | null { return this.attachNode; }
  /** Replaces the lines (no onChange); appends go to the last branch. */
  set(lines: Lines): void { this.ls = lines.length ? lines.map(l => [...l]) : [[]]; this.active = this.ls.length - 1; this.attachNode = null; this.rebuild(); }
  /** A clicked map point: continues the active branch, or starts a new branch at the attach node. */
  append(p: Pos): void {
    const q = this.snap(p);
    this.remember(p); // the clicked spot (before snapping): undoAppendsAt compares with it
    if (this.attachNode) { this.ls = startBranch(this.ls, this.attachNode, q); this.active = this.ls.length - 1; this.attachNode = null; this.opts.onAttach?.(null); }
    else this.ls = this.ls.map((l, i) => i === this.active ? [...l, q] : l);
    // the new point's handle comes a moment later: the second click of a double-click must reach the map, not that handle (a double-click
    // on another line's vertex then undoes both points and attaches there, one elsewhere finishes the line)
    this.render(); this.changed();
    clearTimeout(this.markerTimer); this.markerTimer = setTimeout(() => this.rebuild(), 350);
  }
  /** Makes a vertex (of this line or another one on the map) the attach node; the same node again, or null, clears it. Not for rings. */
  attachAt(p: Pos | null): void {
    if (this.opts.closed) return;
    this.attachNode = p && !(this.attachNode && samePos(this.attachNode, p)) ? p : null;
    this.rebuild(); this.opts.onAttach?.(this.attachNode);
  }
  /** Undoes the points the clicks of a double-click at this screen point just added (the double-click meant a vertex, not new points). */
  undoAppendsAt(point: { x: number; y: number }, tolPx = 8, ms = 1000): void {
    const since = Date.now() - ms;
    let i = this.history.length;
    while (i > 0) {
      const h = this.history[i - 1], q = this.map.project(h.p);
      if (h.t < since || Math.hypot(q.x - point.x, q.y - point.y) > tolPx) break;
      i--;
    }
    if (i === this.history.length) return;
    const h = this.history[i]; this.history = this.history.slice(0, i);
    this.ls = h.ls; this.active = h.active; this.attachNode = h.attach; this.rebuild(); this.changed(); this.opts.onAttach?.(this.attachNode);
  }
  /** Backspace: the last point of the active branch (a branch left with its junction only is dropped). */
  removeLast(): void {
    let ls = this.ls.map((l, i) => i === this.active ? l.slice(0, -1) : l);
    if (this.active > 0 && ls[this.active].length <= 1) ls = ls.filter((_, i) => i !== this.active);
    this.setLines(ls);
  }
  /** A tap or click near the line inserts a vertex there; true when it did (the caller then ignores the click). */
  tryInsert(point: PointLike): boolean {
    const p = Array.isArray(point) ? { x: point[0], y: point[1] } : point as { x: number; y: number };
    const h = this.near(p, TAP_PX); if (!h) return false;
    this.insert(h.branch, h.index, h.p); return true;
  }
  destroy(): void {
    clearTimeout(this.markerTimer); this.map.off('mousemove', this.onMove);
    this.ghost.remove(); for (const m of this.markers) m.remove(); this.markers = [];
    if (this.map.getLayer(this.id)) this.map.removeLayer(this.id);
    if (this.map.getSource(this.id)) this.map.removeSource(this.id);
  }

  private snap(p: Pos): Pos { return this.opts.snap?.(p) ?? p; }
  private changed(): void { this.opts.onChange?.(this.ls); }
  private remember(p: Pos): void { this.history = [...this.history.slice(-4), { t: Date.now(), p, ls: this.ls, active: this.active, attach: this.attachNode }]; }
  private setLines(ls: Lines): void {
    this.ls = ls.filter(l => l.length > 0); if (!this.ls.length) this.ls = [[]];
    this.active = Math.min(this.active, this.ls.length - 1);
    this.rebuild(); this.changed();
  }
  private insert(branch: number, index: number, p: Pos): void { this.ghost.remove(); this.ghostAt = null; this.setLines(insertInto(this.ls, branch, index, this.snap(p))); }
  private dragTo(l: maplibregl.LngLat): void {
    if (!this.dragAt) return;
    const to: Pos = [l.lng, l.lat];
    this.ls = moveShared(this.ls, this.dragAt, to);
    if (this.attachNode && samePos(this.attachNode, this.dragAt)) this.attachNode = to;
    this.dragAt = to; this.render();
  }
  /** Drag end: the marker's final position (the last drag event may lag behind it), snapped; a junction moves for all its branches. */
  private drop(l: maplibregl.LngLat): void {
    if (!this.dragAt) return;
    const to = this.snap([l.lng, l.lat]);
    this.ls = moveShared(this.ls, this.dragAt, to);
    if (this.attachNode && samePos(this.attachNode, this.dragAt)) this.attachNode = to;
    this.dragAt = null; this.dragEndAt = Date.now(); this.rebuild(); this.changed();
  }
  /** The insert position near a screen point: on a segment of any branch, not on a vertex (that one has its own handle). */
  private near(at: { x: number; y: number }, tolPx: number): { branch: number; index: number; p: Pos } | null {
    let best: { branch: number; index: number; p: Pos; dist: number } | null = null;
    this.ls.forEach((line, branch) => {
      if (line.length < 2) return;
      const ring = this.opts.closed && branch === 0 && line.length >= 3 ? [...line, line[0]] : line;
      const px = ring.map(p => { const q = this.map.project(p); return [q.x, q.y] as Pos; });
      const r = nearestOnPolyline(px, [at.x, at.y]);
      if (!r || r.dist > tolPx || (best && best.dist <= r.dist)) return;
      const off = (v: Pos) => Math.hypot(v[0] - r.point[0], v[1] - r.point[1]);
      if (off(px[r.index]) < VERTEX_PX || off(px[r.index + 1]) < VERTEX_PX) return;
      const l = this.map.unproject(r.point);
      best = { branch, index: r.index + 1, p: [l.lng, l.lat], dist: r.dist };
    });
    return best;
  }
  private render(): void {
    const coords = this.ls.map((l, i) => this.opts.closed && i === 0 && l.length >= 3 ? [...l, l[0]] : l).filter(l => l.length >= 2);
    (this.map.getSource(this.id) as GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection',
      features: coords.length ? [{ type: 'Feature', properties: {}, geometry: { type: 'MultiLineString', coordinates: coords } }] : [] });
  }
  /** One marker per distinct vertex (a junction is one handle), rebuilt after every insert/delete/drop. */
  private rebuild(): void {
    clearTimeout(this.markerTimer);
    for (const m of this.markers) m.remove();
    const colour = this.opts.colour ?? '#00c853';
    this.markers = uniqueVertices(this.ls).map(({ p, count }) => {
      const attach = !!this.attachNode && samePos(this.attachNode, p);
      const el = handleEl(attach ? ATTACH_COLOUR : colour, attach ? 'attach' : count > 1 ? 'junction' : 'vertex');
      const remove = (ev: Event) => {
        ev.preventDefault(); ev.stopPropagation();
        if (attach) { this.attachNode = null; this.opts.onAttach?.(null); }
        this.setLines(removeShared(this.ls, p));
      };
      el.querySelector('button')!.addEventListener('click', remove);
      el.addEventListener('contextmenu', remove);
      el.addEventListener('click', ev => ev.stopPropagation());
      el.addEventListener('dblclick', ev => { ev.preventDefault(); ev.stopPropagation(); this.attachAt(p); });
      const m = new maplibregl.Marker({ element: el, draggable: true }).setLngLat(p).addTo(this.map);
      m.on('dragstart', () => { this.dragAt = p; });
      m.on('drag', () => this.dragTo(m.getLngLat()));
      m.on('dragend', () => this.drop(m.getLngLat()));
      return m;
    });
    const node = this.attachNode;
    if (node && !this.ls.some(l => l.some(p => samePos(p, node)))) { // a vertex of another line on the map: highlighted, not editable here
      const el = handleEl(ATTACH_COLOUR, 'attach'); el.querySelector('button')?.remove(); el.title = 'Abzweig von hier – Doppelklick löst ihn';
      el.addEventListener('click', ev => ev.stopPropagation());
      el.addEventListener('dblclick', ev => { ev.preventDefault(); ev.stopPropagation(); this.attachAt(null); });
      this.markers.push(new maplibregl.Marker({ element: el }).setLngLat(node).addTo(this.map));
    }
    this.render();
  }
}

/**
 * A white dot ringed in the line colour with a small × to delete it; a junction is filled, the attach node orange and larger, the hover
 * handle smaller and hollow (no ×).
 */
function handleEl(colour: string, kind: 'vertex' | 'junction' | 'attach' | 'ghost'): HTMLElement {
  const el = document.createElement('div');
  const size = kind === 'ghost' ? 10 : kind === 'attach' ? 18 : 14;
  const fill = kind === 'ghost' ? 'rgba(255,255,255,.7)' : kind === 'vertex' ? '#fff' : colour;
  el.style.cssText = `width:${size}px;height:${size}px;border-radius:50%;background:${fill};border:3px solid ${kind === 'junction' ? '#fff' : colour};box-shadow:0 0 3px rgba(0,0,0,.5);cursor:${kind === 'ghost' ? 'copy' : 'grab'};touch-action:none`;
  if (kind !== 'ghost') {
    el.title = 'Ziehen verschiebt, Rechtsklick oder × löscht, Doppelklick: neue Punkte hier anschließen (Abzweig)';
    const x = document.createElement('button');
    x.type = 'button'; x.textContent = '×'; x.setAttribute('aria-label', 'Punkt löschen');
    x.style.cssText = 'position:absolute;left:12px;top:-14px;width:18px;height:18px;padding:0;border:none;border-radius:50%;background:#455a64;color:#fff;font:13px/18px sans-serif;cursor:pointer';
    x.addEventListener('mousedown', ev => ev.stopPropagation()); // no marker drag from the ×
    x.addEventListener('touchstart', ev => ev.stopPropagation());
    x.addEventListener('dblclick', ev => ev.stopPropagation());
    el.append(x);
  }
  return el;
}
