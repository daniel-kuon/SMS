import { Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import type { FeatureCollection, Geometry, LineString, Polygon } from 'geojson';
import type { ExpressionSpecification, GeoJSONSource, MapMouseEvent } from 'maplibre-gl';
import { bounds, createMap, MlMap } from '../../core/map';
import { Api, errorMessage } from '../../core/api';
import { haversineM } from '../../core/track-playback';
import { formatPosition } from '../../core/seamark-icons';

export interface Correction { id: string; kind: 'Block' | 'Passage' | 'Bridge' | 'Lock' | 'OpeningBridge' | 'Fairway' | 'Avoid' | 'Water'; source: 'Manual' | 'Derived'; geometry: Geometry; note: string | null; active: boolean; createdAt: string; clearanceM?: number | null; }
export const KIND_ICON: Record<Correction['kind'], string> = { Block: 'block', Passage: 'timeline', Bridge: 'vertical_align_top', Lock: 'lock', OpeningBridge: 'open_in_full', Fairway: 'water', Avoid: 'warning', Water: 'waves' };

/** Length of a LineString in nautical miles (0 for other geometries). */
export function lengthNm(g: Geometry): number {
  if (g.type !== 'LineString') return 0;
  let m = 0;
  for (let i = 1; i < g.coordinates.length; i++) m += haversineM(g.coordinates[i - 1][1], g.coordinates[i - 1][0], g.coordinates[i][1], g.coordinates[i][0]);
  return m / 1852;
}

export function toFeatureCollection(list: Correction[]): FeatureCollection {
  return { type: 'FeatureCollection', features: list.map(c => ({ type: 'Feature', id: c.id, properties: { id: c.id, kind: c.kind, source: c.source, active: c.active, note: c.note ?? '' }, geometry: c.geometry })) };
}

/** Closes a ring for a Block polygon (first point repeated) – needs at least 3 distinct points. */
export function draftGeometry(kind: 'Block' | 'Passage', points: [number, number][]): LineString | Polygon | null {
  if (kind === 'Passage') return points.length >= 2 ? { type: 'LineString', coordinates: points } : null;
  return points.length >= 3 ? { type: 'Polygon', coordinates: [[...points, points[0]]] } : null;
}

export const KINDS: Correction['kind'][] = ['Block', 'Avoid', 'Water', 'Passage', 'Fairway', 'Bridge', 'Lock', 'OpeningBridge'];
export const SOURCES: Correction['source'][] = ['Manual', 'Derived'];
export interface CorrectionFilter { kinds: Correction['kind'][]; sources: Correction['source'][]; }
export const FILTER_STORAGE = 'sms.correctionFilter';

/** Stored filter (localStorage JSON); anything unreadable falls back to "show everything". */
export function parseFilter(raw: string | null): CorrectionFilter {
  try {
    const f = JSON.parse(raw ?? '') as Partial<CorrectionFilter>;
    if (Array.isArray(f.kinds) && Array.isArray(f.sources)) return { kinds: KINDS.filter(k => f.kinds!.includes(k)), sources: SOURCES.filter(s => f.sources!.includes(s)) };
  } catch { /* default */ }
  return { kinds: [...KINDS], sources: [...SOURCES] };
}
export const matchesFilter = (c: Correction, f: CorrectionFilter) => f.kinds.includes(c.kind) && f.sources.includes(c.source);
/** The same filter as a MapLibre expression over the feature properties of toFeatureCollection. */
export function filterExpression(f: CorrectionFilter): ExpressionSpecification {
  return ['all', ['in', ['get', 'kind'], ['literal', f.kinds]], ['in', ['get', 'source'], ['literal', f.sources]]];
}

/** All positions of a geometry, flattened (for fitBounds and the first point). */
export function geometryCoords(g: Geometry): [number, number][] {
  switch (g.type) {
    case 'Point': return [g.coordinates as [number, number]];
    case 'MultiPoint': case 'LineString': return g.coordinates as [number, number][];
    case 'MultiLineString': case 'Polygon': return g.coordinates.flat() as [number, number][];
    case 'MultiPolygon': return g.coordinates.flat(2) as [number, number][];
    default: return [];
  }
}

const LIMIT = 3000;
const GEOM: Record<string, ExpressionSpecification> = {
  'corr-blocks': ['in', ['geometry-type'], ['literal', ['Polygon', 'MultiPolygon']]],
  'corr-outline': ['!=', ['geometry-type'], 'Point'],
  'corr-lines': ['in', ['geometry-type'], ['literal', ['LineString', 'MultiLineString']]],
  'corr-points': ['==', ['geometry-type'], 'Point'],
};
const HOVER: ExpressionSpecification = ['boolean', ['feature-state', 'hover'], false];
const SEL: ExpressionSpecification = ['boolean', ['feature-state', 'selected'], false];
const MARKED: ExpressionSpecification = ['any', HOVER, SEL];

/**
 * Spec 10 §10.4/§10.5 admin review, full screen (/admin/corrections): the routing corrections of the viewport (manual
 * blocks/passages, derived waterways and fairways) on the map and in the sidebar; filter, hover/selection sync, details with
 * note editing, activate/deactivate, delete, draw new ones by clicking on the map.
 */
@Component({
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, MatButtonModule, MatIconModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <div class="layout">
      <div #map class="map"></div>
      <aside class="panel">
        <div class="row head"><a mat-icon-button routerLink="/admin" [title]="'admin.backToAdmin' | translate"><mat-icon>arrow_back</mat-icon></a><h2 class="grow">{{ 'admin.corrections' | translate }}</h2></div>
        @if (error()) { <div class="error">{{ error() }}</div> }
        <div class="row toolbar">
          @if (!drawing()) {
            <button mat-stroked-button (click)="startDraw('Passage')"><mat-icon>timeline</mat-icon> {{ 'admin.drawPassage' | translate }}</button>
            <button mat-stroked-button (click)="startDraw('Block')"><mat-icon>block</mat-icon> {{ 'admin.drawBlock' | translate }}</button>
          } @else {
            <span class="muted">{{ 'admin.drawHint' | translate }} ({{ points().length }})</span>
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="full"><mat-label>{{ 'admin.note' | translate }}</mat-label><input matInput [(ngModel)]="note" /></mat-form-field>
            <button mat-flat-button (click)="finishDraw()" [disabled]="!canFinish()">{{ 'common.save' | translate }}</button>
            <button mat-button (click)="cancelDraw()">{{ 'common.cancel' | translate }}</button>
          }
        </div>
        @if (selected(); as c) {
          <div class="details">
            <div class="row head"><mat-icon>{{ icon[c.kind] }}</mat-icon><h3 class="grow">{{ ('admin.kinds.' + c.kind) | translate }}</h3>
              <button mat-icon-button (click)="select(null)" [title]="'admin.backToList' | translate"><mat-icon>close</mat-icon></button></div>
            <div class="row head">
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow"><mat-label>{{ 'admin.note' | translate }}</mat-label><input matInput [(ngModel)]="noteDraft" (keydown.enter)="saveNote(c)" /></mat-form-field>
              <button mat-stroked-button (click)="saveNote(c)" [disabled]="noteDraft === (c.note ?? '')">{{ 'common.save' | translate }}</button>
            </div>
            <dl>
              <dt>{{ 'admin.source' | translate }}</dt><dd>{{ ('admin.sources.' + c.source) | translate }}</dd>
              <dt>{{ 'admin.active' | translate }}</dt><dd><mat-checkbox [checked]="c.active" (change)="toggle(c, $event.checked)" /></dd>
              @if (c.clearanceM != null) { <dt>{{ 'admin.clearance' | translate }}</dt><dd>{{ c.clearanceM | number:'1.0-1' }} m</dd> }
              @if (c.geometry.type === 'LineString') { <dt>{{ 'admin.length' | translate }}</dt><dd>{{ lengthNm(c.geometry) | number:'1.0-2' }} sm</dd> }
              <dt>{{ 'admin.created' | translate }}</dt><dd>{{ c.createdAt | date:'dd.MM.yyyy HH:mm' }}</dd>
              <dt>{{ 'admin.position' | translate }}</dt><dd>{{ firstPoint(c) }}</dd>
            </dl>
            <div class="row">
              <button mat-flat-button (click)="showOnMap(c)"><mat-icon>center_focus_strong</mat-icon> {{ 'admin.showOnMap' | translate }}</button>
              <button mat-button (click)="remove(c)"><mat-icon>delete</mat-icon> {{ 'common.delete' | translate }}</button>
            </div>
          </div>
        } @else {
          <div class="filter">
            @for (k of kinds; track k) { <mat-checkbox [checked]="filter().kinds.includes(k)" (change)="toggleKind(k)"><mat-icon inline>{{ icon[k] }}</mat-icon> {{ ('admin.kinds.' + k) | translate }}</mat-checkbox> }
          </div>
          <div class="filter sources">
            @for (s of sources; track s) { <mat-checkbox [checked]="filter().sources.includes(s)" (change)="toggleSource(s)">{{ ('admin.sources.' + s) | translate }}</mat-checkbox> }
          </div>
          <p class="muted">{{ visible().length }} / {{ items().length }} {{ 'admin.inView' | translate }}</p>
          @if (items().length >= limit) { <p class="hint">{{ 'admin.limitHint' | translate: { limit } }}</p> }
          <table class="list" (mouseenter)="listHovered = true" (mouseleave)="listHovered = false">
            <thead><tr><th></th><th>{{ 'admin.note' | translate }}</th><th>{{ 'admin.source' | translate }}</th><th>sm</th><th>{{ 'admin.active' | translate }}</th><th></th></tr></thead>
            <tbody>
              @for (c of visible(); track c.id) {
                <tr [attr.data-id]="c.id" [class.inactive]="!c.active" [class.hover]="hoverId() === c.id" (mouseenter)="hover(c.id)" (mouseleave)="hover(null)" (click)="select(c)">
                  <td><mat-icon [title]="c.kind">{{ icon[c.kind] }}</mat-icon></td>
                  <td>{{ c.note || (('admin.kinds.' + c.kind) | translate) }}</td>
                  <td>{{ ('admin.sources.' + c.source) | translate }}</td>
                  <td>{{ c.geometry.type === 'LineString' ? (lengthNm(c.geometry) | number:'1.0-1') : '' }}</td>
                  <td (click)="$event.stopPropagation()"><mat-checkbox [checked]="c.active" (change)="toggle(c, $event.checked)" /></td>
                  <td (click)="$event.stopPropagation()"><button mat-icon-button (click)="remove(c)" [title]="'common.delete' | translate"><mat-icon>delete</mat-icon></button></td>
                </tr>
              }
            </tbody>
          </table>
        }
      </aside>
    </div>`,
  styles: `
    .layout { flex: 1; display: flex; position: relative; height: calc(100vh - 64px - 40px); }
    .map { flex: 1; min-height: 60vh; }
    .panel { width: 380px; max-width: 100vw; box-sizing: border-box; padding: 12px; overflow-y: auto; background: white; box-shadow: -2px 0 8px rgba(0,0,0,.15); }
    .panel h2 { margin: 0; font-size: 20px; } .panel h3 { margin: 0; } .head { align-items: center; gap: 8px; }
    .toolbar { align-items: center; flex-wrap: wrap; gap: 8px; margin: 8px 0; } .full { width: 100%; }
    .filter { display: grid; grid-template-columns: 1fr 1fr; } .filter mat-icon { vertical-align: middle; } .sources { border-top: 1px solid #eee; margin-top: 4px; }
    .hint { color: #e65100; font-size: 12px; }
    .details dl { display: grid; grid-template-columns: auto 1fr; gap: 6px 12px; align-items: center; font-size: 13px; } .details dt { color: #666; } .details dd { margin: 0; }
    .list { width: 100%; border-collapse: collapse; font-size: 13px; } .list th { text-align: left; color: #666; font-weight: 500; padding: 4px; } .list td { padding: 2px 4px; border-top: 1px solid #eee; }
    .list tbody tr { cursor: pointer; } .list tr.hover td { background: #fff8e1; } .inactive td { color: #999; }
    @media (max-width: 800px) { .layout { flex-direction: column; height: auto; } .panel { width: 100%; } }`,
})
export class RoutingCorrections {
  private api = inject(Api);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);
  mapEl = viewChild.required<ElementRef<HTMLDivElement>>('map');
  items = signal<Correction[]>([]); error = signal('');
  filter = signal<CorrectionFilter>(parseFilter(storageGet(FILTER_STORAGE)));
  visible = computed(() => { const f = this.filter(); return this.items().filter(c => matchesFilter(c, f)); });
  hoverId = signal<string | null>(null);
  selected = signal<Correction | null>(null); noteDraft = '';
  drawing = signal<'Block' | 'Passage' | null>(null); points = signal<[number, number][]>([]); note = '';
  listHovered = false;
  lengthNm = lengthNm; icon = KIND_ICON; kinds = KINDS; sources = SOURCES; limit = LIMIT;
  private map?: MlMap;
  private loadTimer?: ReturnType<typeof setTimeout>;

  async ngAfterViewInit(): Promise<void> {
    const map = this.map = await createMap(this.mapEl().nativeElement, { zoom: 8 });
    // promoteId: the string ids become feature ids, so hover/selection is a feature-state change, not a setData of 3000 features
    map.addSource('corrections', { type: 'geojson', data: toFeatureCollection(this.items()), promoteId: 'id' });
    map.addLayer({ id: 'corr-blocks', type: 'fill', source: 'corrections', paint: { 'fill-color': ['case', ['==', ['get', 'kind'], 'Fairway'], '#1e88e5', ['==', ['get', 'kind'], 'Avoid'], '#f9a825', ['==', ['get', 'kind'], 'Water'], '#26a69a', '#d32f2f'],
      'fill-opacity': ['case', MARKED, 0.55, ['get', 'active'], ['case', ['==', ['get', 'kind'], 'Fairway'], 0.2, 0.35], 0.12] } });
    // outline for polygons and lines: hover yellow, selection a red glow
    map.addLayer({ id: 'corr-outline', type: 'line', source: 'corrections',
      paint: { 'line-color': ['case', SEL, '#ff1744', '#ffd600'], 'line-width': ['case', SEL, 9, HOVER, 5, 0], 'line-blur': ['case', SEL, 4, 0], 'line-opacity': ['case', MARKED, 0.9, 0] } });
    map.addLayer({ id: 'corr-lines', type: 'line', source: 'corrections',
      paint: { 'line-color': ['case', ['==', ['get', 'source'], 'Derived'], '#fb8c00', '#1976d2'], 'line-width': ['case', MARKED, 5, 3], 'line-opacity': ['case', MARKED, 1, ['get', 'active'], 0.9, 0.35], 'line-dasharray': [2, 1] } });
    map.addLayer({ id: 'corr-points', type: 'circle', source: 'corrections', paint: { 'circle-radius': ['case', MARKED, 8, 5], 'circle-color': ['case', ['==', ['get', 'kind'], 'Lock'], '#6d4c41', ['==', ['get', 'kind'], 'OpeningBridge'], '#00897b', '#7b1fa2'],
      'circle-stroke-color': ['case', SEL, '#ff1744', HOVER, '#ffd600', '#fff'], 'circle-stroke-width': ['case', MARKED, 3, 1.5], 'circle-opacity': ['case', MARKED, 1, ['get', 'active'], 1, 0.4] } });
    this.applyFilter();
    map.addSource('draft', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    map.addLayer({ id: 'draft-line', type: 'line', source: 'draft', paint: { 'line-color': '#333', 'line-width': 2 } });
    map.addLayer({ id: 'draft-points', type: 'circle', source: 'draft', filter: ['==', ['geometry-type'], 'Point'], paint: { 'circle-radius': 5, 'circle-color': '#333' } });
    const featureAt = (e: MapMouseEvent) => map.queryRenderedFeatures(e.point, { layers: ['corr-points', 'corr-lines', 'corr-blocks'] })[0]?.properties?.['id'] as string | undefined;
    map.on('click', (e: MapMouseEvent) => {
      if (this.drawing()) { this.points.update(p => [...p, [e.lngLat.lng, e.lngLat.lat]]); this.renderDraft(); return; }
      const id = featureAt(e); const c = id ? this.items().find(x => x.id === id) : undefined;
      if (c) this.select(c);
    });
    map.on('mousemove', (e: MapMouseEvent) => {
      if (this.drawing()) return;
      const id = featureAt(e) ?? null;
      map.getCanvas().style.cursor = id ? 'pointer' : '';
      if (id === this.hoverId()) return;
      this.hover(id);
      if (id && !this.listHovered) this.host.nativeElement.querySelector(`tr[data-id="${id}"]`)?.scrollIntoView({ block: 'nearest' });
    });
    map.on('mouseout', () => { if (!this.listHovered) this.hover(null); });
    map.on('moveend', () => { clearTimeout(this.loadTimer); this.loadTimer = setTimeout(() => this.load(), 300); });
    setTimeout(() => { map.resize(); this.load(); }, 100);
  }

  ngOnDestroy(): void { clearTimeout(this.loadTimer); this.map?.remove(); }

  /** The derived corrections run to hundreds of thousands: only the map's viewport is loaded. */
  load(): void {
    const b = this.map?.getBounds(); if (!b) return;
    this.api.get<Correction[]>(`/api/admin/routing/corrections?bbox=${b.getSouth()},${b.getWest()},${b.getNorth()},${b.getEast()}&limit=${LIMIT}`).subscribe(list => {
      this.items.set(list);
      (this.map?.getSource('corrections') as GeoJSONSource | undefined)?.setData(toFeatureCollection(list));
      // the selection survives the reload (by id); the panel keeps the old copy when it left the viewport
      const sel = this.selected();
      if (sel) { const fresh = list.find(c => c.id === sel.id); if (fresh) this.selected.set(fresh); this.state(sel.id, 'selected', true); }
      this.state(this.hoverId(), 'hover', true);
    });
  }

  private state(id: string | null | undefined, key: 'hover' | 'selected', on: boolean): void {
    if (id && this.map?.getSource('corrections')) this.map.setFeatureState({ source: 'corrections', id }, { [key]: on });
  }
  hover(id: string | null): void { this.state(this.hoverId(), 'hover', false); this.hoverId.set(id); this.state(id, 'hover', true); }
  select(c: Correction | null): void {
    this.state(this.selected()?.id, 'selected', false);
    this.selected.set(c); this.noteDraft = c?.note ?? '';
    this.state(c?.id, 'selected', true);
    if (!c) this.listHovered = false; // the list is re-rendered, its mouseleave never fires
  }
  showOnMap(c: Correction): void {
    const coords = geometryCoords(c.geometry); const map = this.map;
    if (!coords.length || !map) return;
    if (c.geometry.type === 'Point') map.easeTo({ center: coords[0], zoom: Math.max(map.getZoom(), 13) });
    else map.fitBounds(bounds(coords), { padding: 60, maxZoom: 14 });
  }
  firstPoint(c: Correction): string { const p = geometryCoords(c.geometry)[0]; return p ? formatPosition(p[1], p[0]) : ''; }

  toggleKind(k: Correction['kind']): void { this.setFilter({ ...this.filter(), kinds: toggled(this.filter().kinds, k) }); }
  toggleSource(s: Correction['source']): void { this.setFilter({ ...this.filter(), sources: toggled(this.filter().sources, s) }); }
  private setFilter(f: CorrectionFilter): void {
    this.filter.set(f);
    try { localStorage.setItem(FILTER_STORAGE, JSON.stringify(f)); } catch { /* ignore */ }
    this.applyFilter();
  }
  private applyFilter(): void {
    const map = this.map; if (!map) return;
    const f = filterExpression(this.filter());
    for (const id of Object.keys(GEOM)) if (map.getLayer(id)) map.setFilter(id, ['all', GEOM[id], f]);
  }

  startDraw(kind: 'Block' | 'Passage'): void { this.hover(null); this.drawing.set(kind); this.points.set([]); this.note = ''; this.map?.getCanvas().style.setProperty('cursor', 'crosshair'); }
  cancelDraw(): void { this.drawing.set(null); this.points.set([]); this.map?.getCanvas().style.removeProperty('cursor'); this.renderDraft(); }
  canFinish(): boolean { const k = this.drawing(); return !!k && draftGeometry(k, this.points()) !== null; }
  finishDraw(): void {
    const kind = this.drawing(); if (!kind) return;
    const geometry = draftGeometry(kind, this.points()); if (!geometry) return;
    this.api.post('/api/admin/routing/corrections', { kind, geometry, note: this.note || null, active: true }).subscribe({ next: () => { this.cancelDraw(); this.load(); }, error: e => this.error.set(errorMessage(e)) });
  }
  private renderDraft(): void {
    const src = this.map?.getSource('draft') as GeoJSONSource | undefined; if (!src) return;
    const pts = this.points(); const kind = this.drawing();
    const features: FeatureCollection['features'] = pts.map(p => ({ type: 'Feature', properties: {}, geometry: { type: 'Point', coordinates: p } }));
    if (pts.length >= 2) features.push({ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: kind === 'Block' && pts.length >= 3 ? [...pts, pts[0]] : pts } });
    src.setData({ type: 'FeatureCollection', features });
  }

  private save(c: Correction, patch: Partial<Pick<Correction, 'note' | 'active'>>): void {
    const body = { kind: c.kind, geometry: c.geometry, note: c.note, active: c.active, clearanceM: c.clearanceM ?? null, ...patch };
    this.api.put(`/api/admin/routing/corrections/${c.id}`, body).subscribe({ next: () => this.load(), error: e => this.error.set(errorMessage(e)) });
  }
  toggle(c: Correction, active: boolean): void { this.save(c, { active }); }
  saveNote(c: Correction): void { this.save(c, { note: this.noteDraft.trim() || null }); }
  remove(c: Correction): void {
    this.api.delete(`/api/admin/routing/corrections/${c.id}`).subscribe({ next: () => { if (this.selected()?.id === c.id) this.select(null); this.load(); }, error: e => this.error.set(errorMessage(e)) });
  }
}

const toggled = <T>(list: T[], v: T): T[] => list.includes(v) ? list.filter(x => x !== v) : [...list, v];
function storageGet(key: string): string | null { try { return localStorage.getItem(key); } catch { return null; } }
