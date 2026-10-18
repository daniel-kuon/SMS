import { Component, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
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
import { haversineM } from '../../core/track-playback';

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

/**
 * Spec 10 §10.4/§10.5 admin review: all routing corrections (manual blocks/passages, derived waterways and
 * fairways) on a map and in a list; activate/deactivate, delete, draw new ones by clicking on the map.
 */
@Component({
  selector: 'app-routing-corrections',
  imports: [DecimalPipe, FormsModule, MatButtonModule, MatIconModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    <h4>{{ 'admin.corrections' | translate }} <span class="muted">({{ items().length }})</span></h4>
    <p class="muted">{{ 'admin.correctionsHint' | translate }}</p>
    <div class="row toolbar">
      @if (!drawing()) {
        <button mat-stroked-button (click)="startDraw('Passage')"><mat-icon>timeline</mat-icon> {{ 'admin.drawPassage' | translate }}</button>
        <button mat-stroked-button (click)="startDraw('Block')"><mat-icon>block</mat-icon> {{ 'admin.drawBlock' | translate }}</button>
      } @else {
        <span class="muted">{{ 'admin.drawHint' | translate }} ({{ points().length }})</span>
        <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'admin.note' | translate }}</mat-label><input matInput [(ngModel)]="note" /></mat-form-field>
        <button mat-flat-button (click)="finishDraw()" [disabled]="!canFinish()">{{ 'common.save' | translate }}</button>
        <button mat-button (click)="cancelDraw()">{{ 'common.cancel' | translate }}</button>
      }
    </div>
    <div #map class="map"></div>
    @if (error()) { <div class="error">{{ error() }}</div> }
    <table class="list">
      <thead><tr><th></th><th>{{ 'admin.note' | translate }}</th><th>{{ 'admin.source' | translate }}</th><th>sm</th><th>{{ 'admin.active' | translate }}</th><th></th></tr></thead>
      <tbody>
        @for (c of items(); track c.id) {
          <tr [class.inactive]="!c.active">
            <td><mat-icon [title]="c.kind">{{ icon[c.kind] }}</mat-icon></td>
            <td><a (click)="zoomTo(c)">{{ c.note || c.kind }}</a></td>
            <td>{{ c.source }}</td>
            <td>{{ c.geometry.type === 'LineString' ? (lengthNm(c.geometry) | number:'1.0-1') : '' }}</td>
            <td><mat-checkbox [checked]="c.active" (change)="toggle(c, $event.checked)" /></td>
            <td><button mat-icon-button (click)="remove(c)" [title]="'common.delete' | translate"><mat-icon>delete</mat-icon></button></td>
          </tr>
        }
      </tbody>
    </table>`,
  styles: `
    .map { height: 380px; border-radius: 8px; margin: 8px 0; } .toolbar { align-items: center; flex-wrap: wrap; gap: 8px; }
    .list { width: 100%; border-collapse: collapse; font-size: 13px; } .list th { text-align: left; color: #666; font-weight: 500; padding: 4px; } .list td { padding: 2px 4px; border-top: 1px solid #eee; }
    .list a { cursor: pointer; color: #1b4d7a; } .inactive td { color: #999; }`,
})
export class RoutingCorrections {
  private api = inject(Api);
  mapEl = viewChild<ElementRef<HTMLDivElement>>('map');
  items = signal<Correction[]>([]); error = signal('');
  drawing = signal<'Block' | 'Passage' | null>(null); points = signal<[number, number][]>([]); note = '';
  lengthNm = lengthNm; icon = KIND_ICON;
  private map?: MlMap;

  constructor() {
    this.load();
    effect(() => { const el = this.mapEl()?.nativeElement; if (el && !this.map) void this.initMap(el); });
    effect(() => { const map = this.map; const fc = toFeatureCollection(this.items()); if (map?.getSource('corrections')) (map.getSource('corrections') as GeoJSONSource).setData(fc); });
  }

  /** The derived corrections run to hundreds of thousands: only the map's viewport is loaded (manual ones before the map exists). */
  load(): void {
    const b = this.map?.getBounds();
    const q = b ? `?bbox=${b.getSouth()},${b.getWest()},${b.getNorth()},${b.getEast()}&limit=3000` : '';
    this.api.get<Correction[]>('/api/admin/routing/corrections' + q).subscribe(c => this.items.set(c));
  }
  private loadTimer?: ReturnType<typeof setTimeout>;

  private async initMap(el: HTMLElement): Promise<void> {
    const map = this.map = await createMap(el, { zoom: 8 });
    map.addSource('corrections', { type: 'geojson', data: toFeatureCollection(this.items()) });
    map.addLayer({ id: 'corr-blocks', type: 'fill', source: 'corrections', filter: ['==', ['geometry-type'], 'Polygon'], paint: { 'fill-color': ['case', ['==', ['get', 'kind'], 'Fairway'], '#1e88e5', ['==', ['get', 'kind'], 'Avoid'], '#f9a825', ['==', ['get', 'kind'], 'Water'], '#26a69a', '#d32f2f'], 'fill-opacity': ['case', ['get', 'active'], ['case', ['==', ['get', 'kind'], 'Fairway'], 0.2, 0.35], 0.12] } });
    map.addLayer({ id: 'corr-lines', type: 'line', source: 'corrections', filter: ['==', ['geometry-type'], 'LineString'],
      paint: { 'line-color': ['case', ['==', ['get', 'source'], 'Derived'], '#fb8c00', '#1976d2'], 'line-width': 3, 'line-opacity': ['case', ['get', 'active'], 0.9, 0.35], 'line-dasharray': [2, 1] } });
    map.addLayer({ id: 'corr-points', type: 'circle', source: 'corrections', filter: ['==', ['geometry-type'], 'Point'], paint: { 'circle-radius': 5, 'circle-color': ['case', ['==', ['get', 'kind'], 'Lock'], '#6d4c41', ['==', ['get', 'kind'], 'OpeningBridge'], '#00897b', '#7b1fa2'], 'circle-stroke-color': '#fff', 'circle-stroke-width': 1.5, 'circle-opacity': ['case', ['get', 'active'], 1, 0.4] } });
    map.addSource('draft', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    map.addLayer({ id: 'draft-line', type: 'line', source: 'draft', paint: { 'line-color': '#333', 'line-width': 2 } });
    map.addLayer({ id: 'draft-points', type: 'circle', source: 'draft', filter: ['==', ['geometry-type'], 'Point'], paint: { 'circle-radius': 5, 'circle-color': '#333' } });
    map.on('click', (e: MapMouseEvent) => { if (this.drawing()) { this.points.update(p => [...p, [e.lngLat.lng, e.lngLat.lat]]); this.renderDraft(); } });
    map.on('mouseenter', 'corr-lines', e => { const n = e.features?.[0]?.properties?.['note']; if (n) map.getCanvas().title = String(n); });
    map.on('mouseleave', 'corr-lines', () => { map.getCanvas().title = ''; });
    map.on('moveend', () => { clearTimeout(this.loadTimer); this.loadTimer = setTimeout(() => this.load(), 300); });
    setTimeout(() => { map.resize(); this.load(); }, 100);
  }

  startDraw(kind: 'Block' | 'Passage'): void { this.drawing.set(kind); this.points.set([]); this.note = ''; this.map?.getCanvas().style.setProperty('cursor', 'crosshair'); }
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

  toggle(c: Correction, active: boolean): void {
    this.api.put(`/api/admin/routing/corrections/${c.id}`, { kind: c.kind, geometry: c.geometry, note: c.note, active, clearanceM: c.clearanceM ?? null }).subscribe({ next: () => this.load(), error: e => this.error.set(errorMessage(e)) });
  }
  remove(c: Correction): void { this.api.delete(`/api/admin/routing/corrections/${c.id}`).subscribe({ next: () => this.load(), error: e => this.error.set(errorMessage(e)) }); }
  zoomTo(c: Correction): void {
    const coords: [number, number][] = c.geometry.type === 'LineString' ? c.geometry.coordinates as [number, number][] : c.geometry.type === 'Polygon' ? c.geometry.coordinates[0] as [number, number][] : c.geometry.type === 'Point' ? [c.geometry.coordinates as [number, number]] : [];
    if (coords.length && this.map) this.map.fitBounds(bounds(coords), { padding: 40, maxZoom: 13 });
  }
}
