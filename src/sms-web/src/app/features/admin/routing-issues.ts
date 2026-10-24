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
import type { GeoJSONSource, MapMouseEvent } from 'maplibre-gl';
import { bounds, createMap, MlMap } from '../../core/map';
import { Api, errorMessage } from '../../core/api';
import { around, isMarkerEvent, LineEditor } from '../../core/line-editor';
import { geometryCoords } from './routing-corrections';
import { firstValueFrom } from 'rxjs';

export type IssueType = 'UncertainWater' | 'BridgeUnknown' | 'FairwayGap' | 'FairwayDeadEnd' | 'FairwayIsolated' | 'HarbourOffWater';
export const ISSUE_TYPES: IssueType[] = ['UncertainWater', 'BridgeUnknown', 'FairwayGap', 'FairwayDeadEnd', 'FairwayIsolated', 'HarbourOffWater'];
export const ISSUE_ICON: Record<IssueType, string> = { UncertainWater: 'waves', BridgeUnknown: 'vertical_align_top', FairwayGap: 'linear_scale', FairwayDeadEnd: 'last_page', FairwayIsolated: 'link_off', HarbourOffWater: 'anchor' };
export type IssueStatusFilter = 'open' | 'fixed' | 'ignored' | 'outdated' | 'all';
export interface IssueDecision { id: string; status: 'Fixed' | 'Ignored'; correctionId: string | null; note: string | null; basisKind: string; basisRefs: string | null; basedAt: string; checkedAt: string | null; outdatedAt: string | null; outdatedReason: string | null; createdAt: string; }
export interface Issue { key: string; type: IssueType; title: string; detail: string | null; priority: number | null; geometry: Geometry; refId: string | null; detected: boolean; decision: IssueDecision | null; }
export interface IssueCounts { open: number; fixed: number; ignored: number; outdated: number; }
export interface IssueList { detectedAt: string | null; graphVersion: number | null; counts: Record<string, IssueCounts>; items: Issue[]; actions: Record<IssueType, string[]>; minDepthM: number; }

/** open / fixed / ignored / outdated, for the list badge and the map colour. */
export function issueState(i: Issue): 'open' | 'fixed' | 'ignored' | 'outdated' {
  return !i.decision ? 'open' : i.decision.outdatedAt ? 'outdated' : i.decision.status === 'Fixed' ? 'fixed' : 'ignored';
}
/** Actions that need an area (polygon) and those that need a line, for drawing. */
export const AREA_ACTIONS = ['known', 'avoid', 'block'];
export const drawShape = (action: string): 'area' | 'line' | null => AREA_ACTIONS.includes(action) ? 'area' : action === 'passage' ? 'line' : null;
/** The drawn geometry for an action, or null (the server then takes the issue's own: the cell, the chord or the buffered stretch). */
export function drawnGeometry(action: string, points: [number, number][]): LineString | Polygon | null {
  const shape = drawShape(action);
  if (shape === 'line') return points.length >= 2 ? { type: 'LineString', coordinates: points } : null;
  if (shape === 'area') return points.length >= 3 ? { type: 'Polygon', coordinates: [[...points, points[0]]] } : null;
  return null;
}
export function issueFeatures(items: Issue[]): FeatureCollection {
  return { type: 'FeatureCollection', features: items.map(i => ({ type: 'Feature', id: i.key, properties: { key: i.key, type: i.type, state: issueState(i) }, geometry: i.geometry })) };
}
/** The map's centre for the corrections editor link (?lat&lng&zoom). */
export function centreOf(g: Geometry): { lat: number; lng: number } {
  const c = geometryCoords(g); if (!c.length) return { lat: 0, lng: 0 };
  const xs = c.map(p => p[0]), ys = c.map(p => p[1]);
  return { lat: (Math.min(...ys) + Math.max(...ys)) / 2, lng: (Math.min(...xs) + Math.max(...xs)) / 2 };
}

const STATE_COLOUR = ['match', ['get', 'state'], 'fixed', '#2e7d32', 'ignored', '#9e9e9e', 'outdated', '#ff6d00', '#d32f2f'];

/**
 * Probleme (/admin/issues): where the routing data is uncertain - uncertain water, bridges without a clearance, fairway gaps and dead
 * ends, harbours off the water - as a list (by priority) and on the map. A fix creates a Manual correction (it survives every
 * re-import), "kein Problem" is stored; both turn "veraltet" when the data they were based on changes.
 */
@Component({
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, MatButtonModule, MatIconModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <div class="layout">
      <div #map class="map"></div>
      <aside class="panel">
        <div class="row head"><a mat-icon-button routerLink="/admin" [title]="'admin.backToAdmin' | translate"><mat-icon>arrow_back</mat-icon></a><h2 class="grow">{{ 'admin.issues.title' | translate }}</h2></div>
        @if (error()) { <div class="error">{{ error() }}</div> }
        <p class="muted">{{ 'admin.issues.detected' | translate }}: {{ data()?.detectedAt ? (data()!.detectedAt | date:'dd.MM.yyyy HH:mm') : '–' }} (v{{ data()?.graphVersion ?? '–' }})
          · {{ 'admin.issues.minDepth' | translate: { m: data()?.minDepthM ?? 2.3 } }}</p>
        <div class="row">
          <button mat-stroked-button (click)="detect()" [disabled]="busy()"><mat-icon>refresh</mat-icon> {{ 'admin.issues.detect' | translate }}</button>
          <button mat-stroked-button (click)="check()" [disabled]="busy()"><mat-icon>fact_check</mat-icon> {{ 'admin.issues.check' | translate }}</button>
        </div>
        @if (selected(); as i) {
          <div class="details">
            <div class="row head"><mat-icon>{{ icon[i.type] }}</mat-icon><h3 class="grow">{{ i.title }}</h3>
              <button mat-icon-button (click)="select(null)" [title]="'admin.backToList' | translate"><mat-icon>close</mat-icon></button></div>
            <p><b>{{ ('admin.issues.types.' + i.type) | translate }}</b>@if (i.priority != null) { · {{ 'admin.issues.priority' | translate }} {{ i.priority | number:'1.0-1' }} }</p>
            @if (i.detail) { <p class="muted">{{ i.detail }}</p> }
            @if (!i.detected) { <p class="muted">{{ 'admin.issues.notDetected' | translate }}</p> }
            @if (i.decision; as d) {
              <dl>
                <dt>{{ 'admin.issues.decision' | translate }}</dt><dd>{{ ('admin.issues.state.' + state(i)) | translate }} ({{ d.createdAt | date:'dd.MM.yyyy' }})</dd>
                @if (d.note) { <dt>{{ 'admin.note' | translate }}</dt><dd>{{ d.note }}</dd> }
                @if (d.correctionId) { <dt>{{ 'admin.corrections' | translate }}</dt><dd><a routerLink="/admin/corrections" [queryParams]="editorAt(i)">{{ 'admin.issues.openEditor' | translate }}</a></dd> }
                <dt>{{ 'admin.issues.basis' | translate }}</dt><dd>{{ d.basisRefs }} ({{ d.basedAt | date:'dd.MM.yyyy' }})</dd>
                @if (d.outdatedAt) { <dt class="warn">{{ 'admin.issues.state.outdated' | translate }}</dt><dd class="warn">{{ d.outdatedReason }}</dd> }
              </dl>
              <div class="row">
                @if (d.outdatedAt) { <button mat-flat-button (click)="confirm(d)"><mat-icon>done</mat-icon> {{ 'admin.issues.confirm' | translate }}</button> }
                <button mat-button (click)="undo(d)"><mat-icon>undo</mat-icon> {{ 'admin.issues.undo' | translate }}</button>
              </div>
            } @else {
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="full"><mat-label>{{ 'admin.note' | translate }}</mat-label><input matInput [(ngModel)]="note" /></mat-form-field>
              @if (i.type === 'BridgeUnknown') {
                <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'admin.clearance' | translate }} (m)</mat-label><input matInput type="number" min="0" [(ngModel)]="clearance" /></mat-form-field>
              }
              @if (drawing(); as a) {
                <p class="muted">{{ (drawShape(a) === 'area' ? 'admin.issues.drawArea' : 'admin.issues.drawLine') | translate }} ({{ points().length }})</p>
                <div class="row"><button mat-flat-button (click)="fix(i, a)" [disabled]="!drawnGeometry(a, points())">{{ ('admin.issues.actions.' + a) | translate }}</button>
                  <button mat-button (click)="stopDraw()">{{ 'common.cancel' | translate }}</button></div>
              } @else {
                <div class="actions">
                  @for (a of actionsFor(i); track a) {
                    <span class="action">
                      <button mat-stroked-button (click)="fix(i, a)" [disabled]="busy() || (a === 'clearance' && !(clearance > 0)) || (a === 'passage' && i.type === 'FairwayIsolated')">{{ ('admin.issues.actions.' + a) | translate }}</button>
                      @if (drawShape(a)) { <button mat-icon-button (click)="startDraw(a)" [title]="'admin.issues.draw' | translate"><mat-icon>draw</mat-icon></button> }
                    </span>
                  }
                  @if (i.type === 'HarbourOffWater' && i.refId) { <a mat-stroked-button [routerLink]="['/harbours', i.refId]">{{ 'admin.issues.movePin' | translate }}</a> }
                  <button mat-button (click)="fix(i, 'ignore')" [disabled]="busy()"><mat-icon>visibility_off</mat-icon> {{ 'admin.issues.actions.ignore' | translate }}</button>
                </div>
                <p class="muted small">{{ 'admin.issues.fixHint' | translate }}</p>
              }
            }
            <div class="row"><button mat-button (click)="showOnMap(i)"><mat-icon>center_focus_strong</mat-icon> {{ 'admin.showOnMap' | translate }}</button>
              <a mat-button routerLink="/admin/corrections" [queryParams]="editorAt(i)"><mat-icon>edit_location</mat-icon> {{ 'admin.issues.openEditor' | translate }}</a></div>
          </div>
        } @else {
          <div class="filter">
            @for (t of types; track t) {
              <mat-checkbox [checked]="typeFilter().includes(t)" (change)="toggleType(t)"><mat-icon inline>{{ icon[t] }}</mat-icon> {{ ('admin.issues.types.' + t) | translate }}
                <span class="muted">({{ count(t) }})</span></mat-checkbox>
            }
          </div>
          <div class="row">
            <select [ngModel]="status()" (ngModelChange)="status.set($event); load()">
              @for (s of statuses; track s) { <option [value]="s">{{ ('admin.issues.state.' + s) | translate }}</option> }
            </select>
            <mat-checkbox [checked]="inView()" (change)="inView.set($event.checked); load()">{{ 'admin.issues.inView' | translate }}</mat-checkbox>
          </div>
          <table class="list">
            <thead><tr><th></th><th>{{ 'admin.issues.issue' | translate }}</th><th>{{ 'admin.issues.priority' | translate }}</th><th></th></tr></thead>
            <tbody>
              @for (i of items(); track i.key) {
                <tr [class.hover]="hoverKey() === i.key" (mouseenter)="hover(i.key)" (mouseleave)="hover(null)" (click)="select(i)">
                  <td><mat-icon [title]="i.type">{{ icon[i.type] }}</mat-icon></td>
                  <td>{{ i.title }}</td>
                  <td>{{ i.priority | number:'1.0-1' }}</td>
                  <td><span class="badge" [class]="state(i)">{{ ('admin.issues.state.' + state(i)) | translate }}</span></td>
                </tr>
              } @empty { <tr><td colspan="4" class="muted">{{ 'admin.issues.none' | translate }}</td></tr> }
            </tbody>
          </table>
        }
      </aside>
    </div>`,
  styles: `
    .layout { flex: 1; display: flex; position: relative; height: calc(100vh - 64px - 40px); }
    .map { flex: 1; min-height: 60vh; }
    .panel { width: 400px; max-width: 100vw; box-sizing: border-box; padding: 12px; overflow-y: auto; background: white; box-shadow: -2px 0 8px rgba(0,0,0,.15); }
    .panel h2 { margin: 0; font-size: 20px; } .panel h3 { margin: 0; } .head { align-items: center; gap: 8px; } .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; margin: 6px 0; }
    .full { width: 100%; } .small { font-size: 12px; } .warn { color: #e65100; }
    .filter { display: grid; grid-template-columns: 1fr; } .filter mat-icon { vertical-align: middle; }
    .actions { display: flex; flex-wrap: wrap; gap: 4px; align-items: center; } .action { display: inline-flex; align-items: center; }
    .details dl { display: grid; grid-template-columns: auto 1fr; gap: 6px 12px; font-size: 13px; } .details dt { color: #666; } .details dd { margin: 0; word-break: break-word; }
    .list { width: 100%; border-collapse: collapse; font-size: 13px; } .list th { text-align: left; color: #666; font-weight: 500; padding: 4px; } .list td { padding: 2px 4px; border-top: 1px solid #eee; }
    .list tbody tr { cursor: pointer; } .list tr.hover td { background: #fff8e1; }
    .badge { font-size: 11px; padding: 1px 6px; border-radius: 8px; background: #ffebee; color: #b71c1c; } .badge.fixed { background: #e8f5e9; color: #1b5e20; }
    .badge.ignored { background: #eee; color: #555; } .badge.outdated { background: #fff3e0; color: #e65100; }
    @media (max-width: 800px) { .layout { flex-direction: column; height: auto; } .panel { width: 100%; } }`,
})
export class RoutingIssues {
  private api = inject(Api);
  mapEl = viewChild.required<ElementRef<HTMLDivElement>>('map');
  data = signal<IssueList | null>(null); error = signal(''); busy = signal(false);
  items = computed(() => this.data()?.items ?? []);
  typeFilter = signal<IssueType[]>([...ISSUE_TYPES]);
  status = signal<IssueStatusFilter>('open'); inView = signal(true);
  selected = signal<Issue | null>(null); hoverKey = signal<string | null>(null);
  drawing = signal<string | null>(null); points = signal<[number, number][]>([]);
  note = ''; clearance = 0;
  icon = ISSUE_ICON; types = ISSUE_TYPES; statuses: IssueStatusFilter[] = ['open', 'outdated', 'fixed', 'ignored', 'all'];
  state = issueState; drawShape = drawShape; drawnGeometry = drawnGeometry;
  private map?: MlMap; private editor?: LineEditor;
  private loadTimer?: ReturnType<typeof setTimeout>;

  async ngAfterViewInit(): Promise<void> {
    const map = this.map = await createMap(this.mapEl().nativeElement, { zoom: 7 });
    map.addSource('issues', { type: 'geojson', data: issueFeatures([]), promoteId: 'key' });
    map.addSource('uncertain', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    map.addLayer({ id: 'uncertain-edges', type: 'line', source: 'uncertain', paint: { 'line-color': '#ff9800', 'line-width': 1, 'line-opacity': 0.6 } });
    const marked = ['any', ['boolean', ['feature-state', 'hover'], false], ['boolean', ['feature-state', 'selected'], false]];
    map.addLayer({ id: 'issue-areas', type: 'fill', source: 'issues', filter: ['==', ['geometry-type'], 'Polygon'], paint: { 'fill-color': STATE_COLOUR as never, 'fill-opacity': ['case', marked, 0.45, 0.2] as never } });
    map.addLayer({ id: 'issue-lines', type: 'line', source: 'issues', filter: ['!=', ['geometry-type'], 'Point'], paint: { 'line-color': STATE_COLOUR as never, 'line-width': ['case', marked, 6, 3] as never } });
    map.addLayer({ id: 'issue-points', type: 'circle', source: 'issues', filter: ['==', ['geometry-type'], 'Point'], paint: { 'circle-color': STATE_COLOUR as never, 'circle-radius': ['case', marked, 9, 6] as never, 'circle-stroke-color': '#fff', 'circle-stroke-width': 1.5 } });
    const keyAt = (e: MapMouseEvent) => map.queryRenderedFeatures(around(e.point), { layers: ['issue-points', 'issue-lines', 'issue-areas'] })[0]?.properties?.['key'] as string | undefined;
    map.on('click', e => {
      if (this.drawing()) { if (!isMarkerEvent(e.originalEvent) && !this.editor?.tryInsert(e.point)) this.editor?.append([e.lngLat.lng, e.lngLat.lat]); return; }
      const k = keyAt(e); const i = k ? this.items().find(x => x.key === k) : undefined;
      if (i) this.select(i);
    });
    map.on('mousemove', e => { if (this.drawing()) return; const k = keyAt(e) ?? null; map.getCanvas().style.cursor = k ? 'pointer' : ''; if (k !== this.hoverKey()) this.hover(k); });
    map.on('moveend', () => { clearTimeout(this.loadTimer); this.loadTimer = setTimeout(() => { if (this.inView()) this.load(); this.loadEdges(); }, 300); });
    setTimeout(() => { map.resize(); this.load(); }, 100);
  }

  ngOnDestroy(): void { clearTimeout(this.loadTimer); this.editor?.destroy(); this.map?.remove(); }

  private bbox(): string | null { const b = this.map?.getBounds(); return b ? `${b.getSouth()},${b.getWest()},${b.getNorth()},${b.getEast()}` : null; }
  load(): void {
    const bbox = this.inView() ? this.bbox() : null;
    this.api.get<IssueList>('/api/admin/issues', { status: this.status(), types: this.typeFilter().join(','), bbox, limit: 300 }).subscribe({
      next: d => {
        this.data.set(d);
        (this.map?.getSource('issues') as GeoJSONSource | undefined)?.setData(issueFeatures(d.items));
        const sel = this.selected(); if (sel) { const fresh = d.items.find(i => i.key === sel.key); if (fresh) this.selected.set(fresh); this.state_(sel.key, 'selected', true); }
      },
      error: e => this.error.set(errorMessage(e)),
    });
  }
  /** The uncertain edges as an overlay from zoom 11 (drawing a fix needs to see them). */
  private loadEdges(): void {
    const src = this.map?.getSource('uncertain') as GeoJSONSource | undefined; const bbox = this.bbox();
    if (!src || !bbox || (this.map?.getZoom() ?? 0) < 11) { src?.setData({ type: 'FeatureCollection', features: [] }); return; }
    this.api.get<Geometry>('/api/admin/issues/uncertain-edges', { bbox }).subscribe({ next: g => src.setData({ type: 'Feature', properties: {}, geometry: g }), error: () => src.setData({ type: 'FeatureCollection', features: [] }) });
  }
  count(t: IssueType): number { const c = this.data()?.counts[t]; if (!c) return 0; const s = this.status(); return s === 'all' ? c.open + c.fixed + c.ignored : c[s]; }
  actionsFor(i: Issue): string[] { return this.data()?.actions[i.type] ?? []; }
  toggleType(t: IssueType): void { const f = this.typeFilter(); this.typeFilter.set(f.includes(t) ? f.filter(x => x !== t) : [...f, t]); this.load(); }

  private state_(key: string | null | undefined, k: 'hover' | 'selected', on: boolean): void { if (key && this.map?.getSource('issues')) this.map.setFeatureState({ source: 'issues', id: key }, { [k]: on }); }
  hover(key: string | null): void { this.state_(this.hoverKey(), 'hover', false); this.hoverKey.set(key); this.state_(key, 'hover', true); }
  select(i: Issue | null): void {
    this.stopDraw();
    this.state_(this.selected()?.key, 'selected', false); this.selected.set(i); this.state_(i?.key, 'selected', true);
    this.note = ''; this.clearance = 0;
    if (i) this.showOnMap(i);
  }
  showOnMap(i: Issue): void {
    const c = geometryCoords(i.geometry); const map = this.map; if (!c.length || !map) return;
    if (c.length === 1) map.easeTo({ center: c[0], zoom: Math.max(map.getZoom(), 14) }); else map.fitBounds(bounds(c), { padding: 80, maxZoom: 15 });
  }
  editorAt(i: Issue): { lat: number; lng: number; zoom: number } { return { ...centreOf(i.geometry), zoom: 14 }; }

  startDraw(action: string): void {
    const map = this.map; if (!map) return;
    this.editor?.destroy(); this.points.set([]); this.drawing.set(action);
    this.editor = new LineEditor(map, { closed: drawShape(action) === 'area', noBranches: true, colour: '#333', onChange: l => this.points.set((l[0] ?? []) as [number, number][]) });
    map.getCanvas().style.setProperty('cursor', 'crosshair');
  }
  stopDraw(): void { this.drawing.set(null); this.points.set([]); this.editor?.destroy(); this.editor = undefined; this.map?.getCanvas().style.removeProperty('cursor'); }

  async fix(i: Issue, action: string): Promise<void> {
    const geometry = this.drawing() === action ? drawnGeometry(action, this.points()) : null;
    this.busy.set(true); this.error.set('');
    try {
      await firstValueFrom(this.api.post('/api/admin/issues/fix', { key: i.key, action, geometry, clearanceM: this.clearance > 0 ? this.clearance : null, note: this.note.trim() || null }));
      this.stopDraw(); this.select(null); this.load();
    } catch (e) { this.error.set(errorMessage(e)); } finally { this.busy.set(false); }
  }
  confirm(d: IssueDecision): void { this.api.post(`/api/admin/issues/decisions/${d.id}/confirm`, {}).subscribe({ next: () => this.load(), error: e => this.error.set(errorMessage(e)) }); }
  undo(d: IssueDecision): void { this.api.delete(`/api/admin/issues/decisions/${d.id}`).subscribe({ next: () => { this.select(null); this.load(); }, error: e => this.error.set(errorMessage(e)) }); }
  async detect(): Promise<void> { await this.run('/api/admin/issues/detect'); }
  async check(): Promise<void> { await this.run('/api/admin/issues/check'); }
  private async run(url: string): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await firstValueFrom(this.api.post(url, {})); this.load(); } catch (e) { this.error.set(errorMessage(e)); } finally { this.busy.set(false); }
  }
}
