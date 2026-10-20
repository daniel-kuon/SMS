import { Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { DatePipe, DecimalPipe, JsonPipe, KeyValuePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { firstValueFrom, Observable } from 'rxjs';
import type { FeatureCollection, Geometry, LineString, Point } from 'geojson';
import type { ExpressionSpecification, GeoJSONSource, MapMouseEvent } from 'maplibre-gl';
import { bounds, createMap, MlMap } from '../../core/map';
import { Api, errorMessage } from '../../core/api';
import { haversineM } from '../../core/track-playback';
import { formatPosition } from '../../core/seamark-icons';
import { isMarkerEvent, LineEditor } from '../../core/line-editor';
import { geometryCoords, lengthNm } from './routing-corrections';

export type PassageCategory = 'fairway' | 'assumed' | 'narrow' | 'osm' | 'manual';
export const CATEGORIES: PassageCategory[] = ['fairway', 'assumed', 'narrow', 'osm', 'manual'];
export const CATEGORY_COLOUR: Record<PassageCategory, string> = { fairway: '#1565c0', assumed: '#8e24aa', narrow: '#00897b', osm: '#6d4c41', manual: '#fb8c00' };
const DASHED: PassageCategory[] = ['assumed', 'narrow'];
export type Layer = PassageCategory | 'buoys' | 'items';
export const LAYERS: Layer[] = [...CATEGORIES, 'buoys', 'items'];

export interface Passage { id: string; source: 'Manual' | 'Derived'; note: string | null; active: boolean; geometry: Geometry; createdAt: string; category: PassageCategory; }
export interface Buoy { id: number; type: string; category: string | null; name: string | null; colour: string | null; side: 'Port' | 'Starboard' | null; lat: number; lng: number; tags: Record<string, string> | null; }
export interface Layers { passages: Passage[]; buoys: Buoy[]; truncated: boolean; }
export type FeedbackKind = 'WrongObject' | 'MissingFairway' | 'Comment';
export interface FeedbackGroup { id: string; name: string; description: string | null; status: 'Open' | 'Resolved'; createdAt: string; updatedAt: string; items: number; wrong: number; missing: number; }
export interface FeedbackItem { id: string; groupId: string; kind: FeedbackKind; objectRef: string | null; snapshot: Record<string, unknown> | null; geometry: Geometry | null; note: string | null; expectation: string | null; createdAt: string; }
export interface RegressionRow { id: string; status: string; satisfied: boolean | null; coverage: number | null; }
type Pos = [number, number];
type Selection = { type: 'passage'; p: Passage } | { type: 'buoy'; b: Buoy };
interface Pending { kind: FeedbackKind; objectRef: string | null; geometry: Geometry | null; title: string; }

/** Phrases offered for "Erwartung" (a datalist: free text stays possible). */
export const EXPECTATIONS = ['sollte nicht existieren', 'sollte an Tonne … enden', 'sollte hier verbinden', 'sollte dem Tonnenstrich folgen', 'Tonnen gehören zu verschiedenen Fahrwassern', 'Tonne fehlt in der Kette', 'Tonne gehört nicht zum Fahrwasser'];
export const MIN_ZOOM = 10;
const LIMIT = 4000;
const SNAP_PX = 14;
const LAYER_STORAGE = 'sms.feedbackLayers', GROUP_STORAGE = 'sms.feedbackGroup';
const EMPTY: FeatureCollection = { type: 'FeatureCollection', features: [] };

export const passagesToFc = (list: Passage[]): FeatureCollection => ({ type: 'FeatureCollection', features: list.map(p => ({ type: 'Feature', properties: { id: p.id, category: p.category, label: p.note ?? '', active: p.active }, geometry: p.geometry })) });
export const buoysToFc = (list: Buoy[]): FeatureCollection => ({ type: 'FeatureCollection', features: list.map(b => ({ type: 'Feature', properties: { id: b.id, side: b.side ?? '', name: b.name ?? '' }, geometry: { type: 'Point', coordinates: [b.lng, b.lat] } })) });
export const itemsToFc = (list: FeedbackItem[]): FeatureCollection => ({ type: 'FeatureCollection', features: list.filter(i => i.geometry).map(i => ({ type: 'Feature', properties: { id: i.id, kind: i.kind }, geometry: i.geometry! })) });

/** Consecutive equal vertices removed (the two clicks of a double-click land on the same spot). */
export const dedupe = (points: Pos[]): Pos[] => points.filter((p, i) => i === 0 || p[0] !== points[i - 1][0] || p[1] !== points[i - 1][1]);
export function draftLine(points: Pos[]): LineString | null { const p = dedupe(points); return p.length >= 2 ? { type: 'LineString', coordinates: p } : null; }
/** Ground metres per screen pixel at a latitude and zoom (512 px tiles, as MapLibre). */
export const metresPerPixel = (lat: number, zoom: number) => 40075016.686 * Math.cos(lat * Math.PI / 180) / (512 * 2 ** zoom);
/** The nearest buoy within radiusM replaces the clicked position. */
export function snapToBuoy(p: Pos, buoys: Buoy[], radiusM: number): Pos {
  let best: Buoy | null = null, bestM = radiusM;
  for (const b of buoys) { const m = haversineM(p[1], p[0], b.lat, b.lng); if (m <= bestM) { best = b; bestM = m; } }
  return best ? [best.lng, best.lat] : p;
}
export const exportFileName = (name: string, date = new Date()) =>
  `fahrwasser-feedback-${name.toLowerCase().replace(/ø/g, 'o').replace(/æ/g, 'ae').replace(/ß/g, 'ss').normalize('NFKD').replace(/[\u0300-\u036f]/g, '').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'gruppe'}-${date.toISOString().slice(0, 10)}.json`;
export function parseLayers(raw: string | null): Layer[] {
  try { const l = JSON.parse(raw ?? ''); if (Array.isArray(l)) return LAYERS.filter(x => l.includes(x)); } catch { /* default */ }
  return [...LAYERS];
}
export const buoyLabel = (b: Buoy) => b.name || `${b.type} ${b.id}`;

const ITEM_LAYERS = ['fb-items-wrong', 'fb-items-missing', 'fb-items-points'];

/**
 * Fahrwasser-Feedback (/admin/fairway-feedback): the derived passages and lateral marks of the viewport; objects the fairway detection got
 * wrong are marked into groups (with a snapshot), fairways that should exist are drawn; the groups export as JSON (GeoJSON) for review and
 * are checked against the current derivation (GET /api/admin/feedback/regression).
 */
@Component({
  imports: [DatePipe, DecimalPipe, JsonPipe, KeyValuePipe, FormsModule, RouterLink, MatButtonModule, MatIconModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe],
  template: `
    <div class="layout">
      <div #map class="map"></div>
      <aside class="panel">
        <div class="row head"><a mat-icon-button routerLink="/admin" [title]="'admin.backToAdmin' | translate"><mat-icon>arrow_back</mat-icon></a><h2 class="grow">{{ 'admin.fb.title' | translate }}</h2></div>
        @if (error()) { <div class="error" (click)="error.set('')">{{ error() }}</div> }
        @if (info()) { <div class="info">{{ info() }}</div> }
        @if (zoomTooLow()) { <p class="hint">{{ 'admin.fb.zoomIn' | translate: { zoom: minZoom } }}</p> }
        @if (data().truncated) { <p class="hint">{{ 'admin.fb.truncated' | translate }}</p> }
        <div class="filter">
          @for (l of layers; track l) {
            <mat-checkbox [checked]="shown().includes(l)" (change)="toggleLayer(l)"><span class="swatch" [class]="'sw-' + l" [style.background]="colour[l] ?? null"></span>{{ ('admin.fb.layer.' + l) | translate }}</mat-checkbox>
          }
        </div>

        <div class="row toolbar">
          @switch (mode()) {
            @case ('draw') {
              <span class="muted">@if (editing()) { <b>{{ 'admin.fb.editing' | translate }}</b> · } {{ 'admin.fb.drawHint' | translate }} ({{ points().length }})</span>
              <mat-checkbox [(ngModel)]="snap">{{ 'admin.fb.snap' | translate }}</mat-checkbox>
              <button mat-flat-button (click)="finishDraw()" [disabled]="!canFinish()">{{ (editing() ? 'common.save' : 'admin.fb.finish') | translate }}</button>
              <button mat-button (click)="cancel()">{{ 'common.cancel' | translate }}</button>
            }
            @case ('comment') { <span class="muted">{{ 'admin.fb.commentHint' | translate }}</span><button mat-button (click)="cancel()">{{ 'common.cancel' | translate }}</button> }
            @default {
              <button mat-stroked-button (click)="startDraw()"><mat-icon>draw</mat-icon> {{ 'admin.fb.draw' | translate }}</button>
              <button mat-stroked-button (click)="startComment()"><mat-icon>add_comment</mat-icon> {{ 'admin.fb.comment' | translate }}</button>
            }
          }
        </div>

        @if (pending(); as p) {
          <div class="card form">
            <h3>{{ p.title }}</h3>
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="full"><mat-label>{{ 'admin.fb.group' | translate }}</mat-label>
              <mat-select [(ngModel)]="formGroup"><mat-option value="">+ {{ 'admin.fb.newGroup' | translate }}</mat-option>
                @for (g of groups(); track g.id) { <mat-option [value]="g.id">{{ g.name }}</mat-option> }</mat-select></mat-form-field>
            @if (!formGroup) { <mat-form-field appearance="outline" subscriptSizing="dynamic" class="full"><mat-label>{{ 'admin.fb.groupName' | translate }}</mat-label><input matInput [(ngModel)]="formGroupName" /></mat-form-field> }
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="full"><mat-label>{{ 'admin.fb.note' | translate }}</mat-label><textarea matInput rows="2" [(ngModel)]="formNote"></textarea></mat-form-field>
            @if (p.kind !== 'Comment') {
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="full"><mat-label>{{ 'admin.fb.expectation' | translate }}</mat-label><input matInput [(ngModel)]="formExpectation" list="fb-expectations" /></mat-form-field>
              <datalist id="fb-expectations">@for (e of expectations; track e) { <option [value]="e"></option> }</datalist>
            }
            <div class="row"><button mat-flat-button (click)="savePending()" [disabled]="!canSave()">{{ 'common.save' | translate }}</button><button mat-button (click)="cancel()">{{ 'common.cancel' | translate }}</button></div>
          </div>
        }

        @if (selected(); as s) {
          <div class="card details">
            <div class="row head">
              @if (s.type === 'passage') { <span class="swatch" [style.background]="colour[s.p.category]"></span><h3 class="grow">{{ s.p.note || ('admin.fb.layer.' + s.p.category | translate) }}</h3> }
              @else { <span class="swatch round" [class.port]="s.b.side === 'Port'" [class.starboard]="s.b.side === 'Starboard'"></span><h3 class="grow">{{ buoyLabel(s.b) }}</h3> }
              <button mat-icon-button (click)="select(null)"><mat-icon>close</mat-icon></button>
            </div>
            <dl>
              @if (s.type === 'passage') {
                <dt>{{ 'admin.fb.category' | translate }}</dt><dd>{{ ('admin.fb.layer.' + s.p.category) | translate }}</dd>
                <dt>{{ 'admin.source' | translate }}</dt><dd>{{ ('admin.sources.' + s.p.source) | translate }}@if (!s.p.active) { · {{ 'admin.fb.inactive' | translate }} }</dd>
                <dt>{{ 'admin.length' | translate }}</dt><dd>{{ lengthNm(s.p.geometry) | number:'1.0-2' }} sm</dd>
                <dt>{{ 'admin.created' | translate }}</dt><dd>{{ s.p.createdAt | date:'dd.MM.yyyy HH:mm' }}</dd>
                <dt>Id</dt><dd class="mono">correction:{{ s.p.id }}</dd>
              } @else {
                <dt>{{ 'admin.fb.side' | translate }}</dt><dd>{{ s.b.side ? (('admin.fb.sides.' + s.b.side) | translate) : '–' }} ({{ s.b.category ?? '–' }})</dd>
                <dt>{{ 'admin.position' | translate }}</dt><dd>{{ position(s.b) }}</dd>
                <dt>Id</dt><dd class="mono">seamark:{{ s.b.id }}</dd>
                @for (t of (s.b.tags ?? {}) | keyvalue; track t.key) { <dt class="tag">{{ t.key }}</dt><dd class="tag">{{ t.value }}</dd> }
              }
            </dl>
            <div class="row"><button mat-flat-button color="warn" (click)="markWrong(s)"><mat-icon>report</mat-icon> {{ 'admin.fb.markWrong' | translate }}</button>
              <button mat-button (click)="zoomTo(s.type === 'passage' ? s.p.geometry : point(s.b))"><mat-icon>center_focus_strong</mat-icon> {{ 'admin.showOnMap' | translate }}</button></div>
          </div>
        }

        <h3 class="section">{{ 'admin.fb.groups' | translate }}</h3>
        <div class="row"><mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow"><mat-label>{{ 'admin.fb.newGroup' | translate }}</mat-label><input matInput [(ngModel)]="newGroupName" (keydown.enter)="createGroup()" /></mat-form-field>
          <button mat-stroked-button (click)="createGroup()" [disabled]="!newGroupName.trim()"><mat-icon>add</mat-icon></button></div>
        <ul class="groups">
          @for (g of groups(); track g.id) {
            <li [class.active]="g.id === groupId()" [class.resolved]="g.status === 'Resolved'" (click)="openGroup(g.id)">
              <span class="grow">{{ g.name }}</span><span class="muted">{{ g.wrong }} ✗ · {{ g.missing }} ＋ · {{ g.items }}</span>
              <span class="chip" [class.done]="g.status === 'Resolved'">{{ ('admin.fb.status.' + g.status) | translate }}</span>
            </li>
          } @empty { <li class="muted">{{ 'admin.fb.noGroups' | translate }}</li> }
        </ul>
        <button mat-button (click)="exportAll()"><mat-icon>download</mat-icon> {{ 'admin.fb.exportOpen' | translate }}</button>

        @if (group(); as g) {
          <div class="card">
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="full"><mat-label>{{ 'admin.fb.groupName' | translate }}</mat-label><input matInput [(ngModel)]="groupName" /></mat-form-field>
            <mat-form-field appearance="outline" subscriptSizing="dynamic" class="full"><mat-label>{{ 'common.description' | translate }}</mat-label><textarea matInput rows="2" [(ngModel)]="groupDescription"></textarea></mat-form-field>
            <div class="row wrap">
              <button mat-stroked-button (click)="saveGroup(g, g.status)" [disabled]="!groupName.trim()">{{ 'common.save' | translate }}</button>
              <button mat-stroked-button (click)="saveGroup(g, g.status === 'Open' ? 'Resolved' : 'Open')">{{ (g.status === 'Open' ? 'admin.fb.resolve' : 'admin.fb.reopen') | translate }}</button>
              <button mat-icon-button (click)="deleteGroup(g)" [title]="'common.delete' | translate"><mat-icon>delete</mat-icon></button>
            </div>
            <div class="row wrap">
              <button mat-stroked-button (click)="download(g)"><mat-icon>download</mat-icon> {{ 'admin.fb.download' | translate }}</button>
              <button mat-stroked-button (click)="copy(g)"><mat-icon>content_copy</mat-icon> {{ 'admin.fb.copy' | translate }}</button>
              <button mat-stroked-button (click)="check(g)" [title]="'admin.fb.checkHint' | translate"><mat-icon>rule</mat-icon> {{ 'admin.fb.check' | translate }}</button>
            </div>
            <ul class="items">
              @for (i of items(); track i.id) {
                <li (click)="zoomTo(i.geometry)" [title]="i.snapshot ? (i.snapshot | json) : ''">
                  <mat-icon [class]="'k-' + i.kind">{{ i.kind === 'WrongObject' ? 'report' : i.kind === 'MissingFairway' ? 'add_road' : 'chat' }}</mat-icon>
                  <span class="grow"><b>{{ ('admin.fb.kind.' + i.kind) | translate }}</b>@if (snapshotLabel(i); as l) { · {{ l }} }
                    @if (i.note) { <br />{{ i.note }} } @if (i.expectation) { <br /><i>→ {{ i.expectation }}</i> }
                    @if (regression()[i.id]; as r) { <br /><span class="chip" [class.done]="r.satisfied === true" [class.fail]="r.satisfied === false">{{ ('admin.fb.result.' + r.status) | translate }}@if (r.coverage != null) { · {{ r.coverage * 100 | number:'1.0-0' }} % }</span> }</span>
                  @if (i.kind === 'MissingFairway') { <button mat-icon-button (click)="startDraw(i); $event.stopPropagation()" [title]="'admin.fb.editLine' | translate"><mat-icon>edit</mat-icon></button> }
                  <button mat-icon-button (click)="removeItem(i); $event.stopPropagation()" [title]="'common.delete' | translate"><mat-icon>delete</mat-icon></button>
                </li>
              } @empty { <li class="muted">{{ 'admin.fb.noItems' | translate }}</li> }
            </ul>
          </div>
        }
      </aside>
    </div>`,
  styles: `
    .layout { flex: 1; display: flex; position: relative; height: calc(100vh - 64px - 40px); }
    .map { flex: 1; min-height: 60vh; }
    .panel { width: 400px; max-width: 100vw; box-sizing: border-box; padding: 12px; overflow-y: auto; background: white; box-shadow: -2px 0 8px rgba(0,0,0,.15); }
    .panel h2 { margin: 0; font-size: 20px; } .panel h3 { margin: 0; font-size: 15px; } .head { align-items: center; gap: 8px; } .section { margin-top: 16px !important; }
    .toolbar { align-items: center; flex-wrap: wrap; gap: 8px; margin: 8px 0; } .full { width: 100%; margin-bottom: 6px; } .wrap { flex-wrap: wrap; gap: 6px; margin: 6px 0; }
    .filter { display: grid; grid-template-columns: 1fr 1fr; font-size: 13px; }
    .swatch { display: inline-block; width: 14px; height: 4px; margin-right: 6px; vertical-align: middle; background: #999; }
    .swatch.round, .sw-buoys { width: 10px; height: 10px; border-radius: 50%; background: linear-gradient(90deg, #d50000 50%, #00a000 50%) !important; }
    .swatch.port { background: #d50000 !important; } .swatch.starboard { background: #00a000 !important; } .sw-items { height: 4px; background: repeating-linear-gradient(90deg, #00c853 0 4px, transparent 4px 6px) !important; }
    .sw-assumed, .sw-narrow { opacity: .7; }
    .hint { color: #e65100; font-size: 12px; } .info { color: #2e7d32; font-size: 13px; margin: 4px 0; } .error { cursor: pointer; }
    .card { border: 1px solid #e0e0e0; border-radius: 8px; padding: 10px; margin: 8px 0; } .form { border-color: #1565c0; }
    .details dl { display: grid; grid-template-columns: auto 1fr; gap: 4px 12px; font-size: 13px; margin: 8px 0; } .details dt { color: #666; } .details dd { margin: 0; word-break: break-all; }
    .details .tag { font-size: 11px; } .mono { font-family: monospace; font-size: 11px; }
    .groups, .items { list-style: none; padding: 0; margin: 4px 0; font-size: 13px; }
    .groups li, .items li { display: flex; align-items: center; gap: 6px; padding: 4px 6px; border-top: 1px solid #eee; cursor: pointer; }
    .groups li.active { background: #e3f2fd; } .groups li.resolved .grow { color: #999; text-decoration: line-through; }
    .items li { align-items: flex-start; } .items mat-icon { flex: none; } .k-WrongObject { color: #d50000; } .k-MissingFairway { color: #00a000; } .k-Comment { color: #fb8c00; }
    .chip { font-size: 11px; padding: 1px 6px; border-radius: 8px; background: #fff3e0; color: #e65100; white-space: nowrap; } .chip.done { background: #e8f5e9; color: #2e7d32; } .chip.fail { background: #ffebee; color: #c62828; }
    @media (max-width: 800px) { .layout { flex-direction: column; height: auto; } .panel { width: 100%; } }`,
})
export class FairwayFeedback {
  private api = inject(Api);
  private i18n = inject(TranslateService);
  mapEl = viewChild.required<ElementRef<HTMLDivElement>>('map');
  error = signal(''); info = signal('');
  data = signal<Layers>({ passages: [], buoys: [], truncated: false });
  zoomTooLow = signal(false);
  shown = signal<Layer[]>(parseLayers(storageGet(LAYER_STORAGE)));
  selected = signal<Selection | null>(null);
  mode = signal<'draw' | 'comment' | null>(null);
  /** Set while a saved drawn fairway is being corrected (draw mode on its line; finishing saves it back). */
  editing = signal<FeedbackItem | null>(null);
  points = signal<Pos[]>([]); snap = true;
  pending = signal<Pending | null>(null); formGroup = ''; formGroupName = ''; formNote = ''; formExpectation = '';
  groups = signal<FeedbackGroup[]>([]);
  groupId = signal<string | null>(storageGet(GROUP_STORAGE));
  group = computed(() => this.groups().find(g => g.id === this.groupId()) ?? null);
  groupName = ''; groupDescription = ''; newGroupName = '';
  items = signal<FeedbackItem[]>([]);
  regression = signal<Record<string, RegressionRow>>({});
  layers = LAYERS; colour = CATEGORY_COLOUR as Partial<Record<Layer, string>>; expectations = EXPECTATIONS; minZoom = MIN_ZOOM;
  lengthNm = lengthNm; buoyLabel = buoyLabel;
  private map?: MlMap;
  private loadTimer?: ReturnType<typeof setTimeout>;
  private editor?: LineEditor;
  private onKey = (e: KeyboardEvent) => this.key(e);

  async ngAfterViewInit(): Promise<void> {
    // default: the Smålandsfarvandet / Bøgestrøm / Grønsund fairways; ?lat=&lng=&zoom= like the corrections editor
    const q = new URLSearchParams(location.search); const lat = Number(q.get('lat')), lng = Number(q.get('lng'));
    const at = q.has('lat') && q.has('lng') && Number.isFinite(lat) && Number.isFinite(lng) ? { center: [lng, lat] as Pos, zoom: Number(q.get('zoom')) || 12 } : { center: [11.95, 55.1] as Pos, zoom: 10 };
    const map = this.map = await createMap(this.mapEl().nativeElement, at);
    for (const id of ['fb-passages', 'fb-buoys', 'fb-items', 'fb-sel']) map.addSource(id, { type: 'geojson', data: EMPTY });
    const colour = ['match', ['get', 'category'], ...CATEGORIES.flatMap(c => [c, CATEGORY_COLOUR[c]]), '#999'] as unknown as ExpressionSpecification;
    const opacity: ExpressionSpecification = ['case', ['get', 'active'], 0.9, 0.35];
    map.addLayer({ id: 'fb-pass-solid', type: 'line', source: 'fb-passages', layout: { 'line-cap': 'round' }, paint: { 'line-color': colour, 'line-width': ['case', ['==', ['get', 'category'], 'fairway'], 4, 2.5], 'line-opacity': opacity } });
    map.addLayer({ id: 'fb-pass-dashed', type: 'line', source: 'fb-passages', paint: { 'line-color': colour, 'line-width': 3, 'line-opacity': opacity, 'line-dasharray': [2, 1.5] } });
    map.addLayer({ id: 'fb-pass-label', type: 'symbol', source: 'fb-passages', minzoom: 11,
      layout: { 'symbol-placement': 'line', 'text-field': ['get', 'label'], 'text-font': ['Noto Sans Regular'], 'text-size': 11, 'text-offset': [0, -0.9] }, paint: { 'text-color': colour, 'text-halo-color': '#fff', 'text-halo-width': 1.5 } });
    map.addLayer({ id: 'fb-buoys', type: 'circle', source: 'fb-buoys', paint: { 'circle-radius': ['interpolate', ['linear'], ['zoom'], 10, 3, 14, 6], 'circle-color': ['match', ['get', 'side'], 'Port', '#d50000', 'Starboard', '#00a000', '#9e9e9e'], 'circle-stroke-color': '#fff', 'circle-stroke-width': 1.5 } });
    map.addLayer({ id: 'fb-items-wrong', type: 'line', source: 'fb-items', filter: ['all', ['==', ['get', 'kind'], 'WrongObject'], ['!=', ['geometry-type'], 'Point']], paint: { 'line-color': '#d50000', 'line-width': 9, 'line-opacity': 0.45 } });
    map.addLayer({ id: 'fb-items-missing', type: 'line', source: 'fb-items', filter: ['all', ['==', ['get', 'kind'], 'MissingFairway'], ['!=', ['geometry-type'], 'Point']], paint: { 'line-color': '#00c853', 'line-width': 4, 'line-dasharray': [2, 1] } });
    map.addLayer({ id: 'fb-items-points', type: 'circle', source: 'fb-items', filter: ['==', ['geometry-type'], 'Point'],
      paint: { 'circle-radius': 11, 'circle-color': ['case', ['==', ['get', 'kind'], 'Comment'], '#fb8c00', 'rgba(0,0,0,0)'], 'circle-opacity': 0.8, 'circle-stroke-color': ['case', ['==', ['get', 'kind'], 'Comment'], '#fff', '#d50000'], 'circle-stroke-width': 3 } });
    map.addLayer({ id: 'fb-sel-line', type: 'line', source: 'fb-sel', paint: { 'line-color': '#ffd600', 'line-width': 10, 'line-blur': 3, 'line-opacity': 0.8 } }, 'fb-pass-solid');
    map.addLayer({ id: 'fb-sel-point', type: 'circle', source: 'fb-sel', filter: ['==', ['geometry-type'], 'Point'], paint: { 'circle-radius': 12, 'circle-color': '#ffd600', 'circle-opacity': 0.7 } }, 'fb-buoys');
    this.applyLayers();

    map.on('click', (e: MapMouseEvent) => this.click(e));
    map.on('dblclick', (e: MapMouseEvent) => { if (this.mode() === 'draw') { e.preventDefault(); this.finishDraw(); } });
    map.on('mousemove', (e: MapMouseEvent) => {
      if (this.mode()) return;
      map.getCanvas().style.cursor = map.queryRenderedFeatures(e.point, { layers: this.clickable() }).length ? 'pointer' : '';
    });
    map.on('moveend', () => { clearTimeout(this.loadTimer); this.loadTimer = setTimeout(() => this.load(), 300); });
    document.addEventListener('keydown', this.onKey);
    this.loadGroups();
    setTimeout(() => { map.resize(); this.load(); }, 100);
  }

  ngOnDestroy(): void { clearTimeout(this.loadTimer); this.editor?.destroy(); document.removeEventListener('keydown', this.onKey); this.map?.remove(); }

  /** Only the viewport, and only from zoom 10 on: the derived passages of a whole region run to hundreds of thousands. */
  load(): void {
    const map = this.map; if (!map) return;
    const low = map.getZoom() < MIN_ZOOM; this.zoomTooLow.set(low);
    if (low) { this.setData({ passages: [], buoys: [], truncated: false }); return; }
    const b = map.getBounds();
    this.api.get<Layers>('/api/admin/feedback/layers', { bbox: `${b.getSouth()},${b.getWest()},${b.getNorth()},${b.getEast()}`, limit: LIMIT })
      .subscribe({ next: d => this.setData(d), error: e => this.error.set(errorMessage(e)) });
  }
  private setData(d: Layers): void {
    this.data.set(d);
    this.source('fb-passages')?.setData(passagesToFc(d.passages));
    this.source('fb-buoys')?.setData(buoysToFc(d.buoys));
  }
  private source(id: string): GeoJSONSource | undefined { return this.map?.getSource(id) as GeoJSONSource | undefined; }
  private clickable(): string[] { return ['fb-buoys', 'fb-pass-solid', 'fb-pass-dashed'].filter(id => this.map?.getLayer(id)); }

  private click(e: MapMouseEvent): void {
    const map = this.map!; const p: Pos = [e.lngLat.lng, e.lngLat.lat];
    if (isMarkerEvent(e.originalEvent)) return; // a vertex handle, × or the insert handle of the line editor
    if (this.mode() === 'draw') { if (!this.editor?.tryInsert(e.point)) this.editor?.append(p); return; }
    if (this.pending()?.kind === 'MissingFairway' && this.editor?.tryInsert(e.point)) return; // the finished line stays editable
    if (this.mode() === 'comment') { this.mode.set(null); map.getCanvas().style.cursor = ''; this.openForm({ kind: 'Comment', objectRef: null, geometry: { type: 'Point', coordinates: p }, title: this.t('admin.fb.kind.Comment') }); return; }
    const f = map.queryRenderedFeatures(e.point, { layers: this.clickable() })[0];
    const id = f?.properties?.['id'];
    if (f?.layer.id === 'fb-buoys') { const b = this.data().buoys.find(x => x.id === id); if (b) this.select({ type: 'buoy', b }); }
    else if (f) { const x = this.data().passages.find(q => q.id === id); if (x) this.select({ type: 'passage', p: x }); }
    else this.select(null);
  }
  private snapped(p: Pos): Pos {
    const map = this.map; if (!this.snap || !map) return p;
    return snapToBuoy(p, this.data().buoys, SNAP_PX * metresPerPixel(p[1], map.getZoom()));
  }
  private key(e: KeyboardEvent): void {
    if (!this.mode()) return;
    const tag = (e.target as HTMLElement | null)?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA') return;
    if (e.key === 'Escape') this.cancel();
    else if (e.key === 'Enter' && this.mode() === 'draw') this.finishDraw();
    else if (e.key === 'Backspace' && this.mode() === 'draw') { e.preventDefault(); this.points.set(this.points().slice(0, -1)); this.editor?.set(this.points()); }
  }

  select(s: Selection | null): void {
    this.selected.set(s);
    const g: Geometry | null = s?.type === 'passage' ? s.p.geometry : s ? this.point(s.b) : null;
    this.source('fb-sel')?.setData(g ? { type: 'FeatureCollection', features: [{ type: 'Feature', properties: {}, geometry: g }] } : EMPTY);
  }
  point(b: Buoy): Point { return { type: 'Point', coordinates: [b.lng, b.lat] }; }
  position(b: Buoy): string { return formatPosition(b.lat, b.lng); }
  zoomTo(g: Geometry | null): void {
    const map = this.map; const coords = g ? geometryCoords(g) : [];
    if (!map || !coords.length) return;
    if (g!.type === 'Point') map.easeTo({ center: coords[0], zoom: Math.max(map.getZoom(), 13) });
    else map.fitBounds(bounds(coords), { padding: 60, maxZoom: 15 });
  }

  toggleLayer(l: Layer): void {
    const s = this.shown(); this.shown.set(s.includes(l) ? s.filter(x => x !== l) : [...s, l]);
    try { localStorage.setItem(LAYER_STORAGE, JSON.stringify(this.shown())); } catch { /* ignore */ }
    this.applyLayers();
  }
  private applyLayers(): void {
    const map = this.map; if (!map) return;
    const s = this.shown(); const cats = CATEGORIES.filter(c => s.includes(c));
    map.setFilter('fb-pass-solid', ['in', ['get', 'category'], ['literal', cats.filter(c => !DASHED.includes(c))]]);
    map.setFilter('fb-pass-dashed', ['in', ['get', 'category'], ['literal', cats.filter(c => DASHED.includes(c))]]);
    map.setFilter('fb-pass-label', ['in', ['get', 'category'], ['literal', cats]]);
    map.setLayoutProperty('fb-buoys', 'visibility', s.includes('buoys') ? 'visible' : 'none');
    for (const id of ITEM_LAYERS) map.setLayoutProperty(id, 'visibility', s.includes('items') ? 'visible' : 'none');
  }

  // ---- drawing / marking ----
  startDraw(item: FeedbackItem | null = null): void {
    const map = this.map; if (!map) return;
    this.cancel(); this.select(null); this.editing.set(item); this.mode.set('draw');
    this.editor = new LineEditor(map, { snap: p => this.snapped(p), onChange: pts => this.points.set(pts) });
    this.points.set(item?.geometry?.type === 'LineString' ? item.geometry.coordinates as Pos[] : []); this.editor.set(this.points());
    map.doubleClickZoom.disable(); map.getCanvas().style.setProperty('cursor', 'crosshair');
    if (item) this.zoomTo(item.geometry);
  }
  startComment(): void { this.cancel(); this.select(null); this.pending.set(null); this.mode.set('comment'); this.map?.getCanvas().style.setProperty('cursor', 'crosshair'); }
  canFinish(): boolean { return draftLine(this.points()) !== null; }
  finishDraw(): void {
    const line = draftLine(this.points()); if (!line) return;
    this.endMode();
    const item = this.editing();
    if (item) { // a corrected drawn fairway: saved back right away
      this.api.put(`/api/admin/feedback/items/${item.id}`, { kind: item.kind, note: item.note, expectation: item.expectation, geometry: line }).subscribe({
        next: () => { this.cancel(); this.loadItems(); this.flash(this.t('admin.fb.saved')); }, error: e => this.error.set(errorMessage(e)) });
      return;
    }
    this.openForm({ kind: 'MissingFairway', objectRef: null, geometry: line, title: this.t('admin.fb.kind.MissingFairway') });
    this.points.set(line.coordinates as Pos[]); this.editor?.set(this.points()); // stays editable until saved or cancelled
  }
  markWrong(s: Selection): void {
    this.openForm(s.type === 'passage'
      ? { kind: 'WrongObject', objectRef: `correction:${s.p.id}`, geometry: s.p.geometry, title: `${this.t('admin.fb.markWrong')}: ${s.p.note ?? s.p.category}` }
      : { kind: 'WrongObject', objectRef: `seamark:${s.b.id}`, geometry: this.point(s.b), title: `${this.t('admin.fb.markWrong')}: ${buoyLabel(s.b)}` });
  }
  private openForm(p: Pending): void {
    this.pending.set(p); this.formNote = ''; this.formExpectation = '';
    this.formGroup = this.groupId() && this.groups().some(g => g.id === this.groupId()) ? this.groupId()! : ''; this.formGroupName = '';
  }
  canSave(): boolean { const p = this.pending(); return !!p && (!!this.formGroup || !!this.formGroupName.trim()) && (p.kind !== 'Comment' || !!this.formNote.trim()); }
  async savePending(): Promise<void> {
    const p = this.pending(); if (!p || !this.canSave()) return;
    try {
      const groupId = this.formGroup || (await this.call(this.api.post<{ id: string }>('/api/admin/feedback/groups', { name: this.formGroupName.trim() }))).id;
      const geometry = p.kind === 'MissingFairway' ? draftLine(this.points()) ?? p.geometry : p.objectRef ? null : p.geometry;
      await this.call(this.api.post(`/api/admin/feedback/groups/${groupId}/items`, { kind: p.kind, objectRef: p.objectRef, geometry, note: this.formNote, expectation: this.formExpectation }));
      this.cancel(); this.select(null);
      this.openGroup(groupId); this.loadGroups();
      this.flash(this.t('admin.fb.saved'));
    } catch (e) { this.error.set(errorMessage(e)); }
  }
  cancel(): void { this.endMode(); this.pending.set(null); this.editing.set(null); this.points.set([]); this.editor?.destroy(); this.editor = undefined; }
  private endMode(): void { this.mode.set(null); this.map?.doubleClickZoom.enable(); this.map?.getCanvas().style.removeProperty('cursor'); }

  // ---- groups ----
  loadGroups(): void {
    this.api.get<FeedbackGroup[]>('/api/admin/feedback/groups').subscribe({ next: list => {
      this.groups.set(list);
      const g = this.group(); if (g) { this.groupName = g.name; this.groupDescription = g.description ?? ''; if (!this.items().length) this.loadItems(); }
    }, error: e => this.error.set(errorMessage(e)) });
  }
  openGroup(id: string): void {
    this.groupId.set(id); this.regression.set({});
    try { localStorage.setItem(GROUP_STORAGE, id); } catch { /* ignore */ }
    const g = this.group(); this.groupName = g?.name ?? ''; this.groupDescription = g?.description ?? '';
    this.loadItems();
  }
  loadItems(): void {
    const id = this.groupId(); if (!id) return;
    this.api.get<FeedbackItem[]>(`/api/admin/feedback/groups/${id}/items`).subscribe({
      next: list => { this.items.set(list); this.source('fb-items')?.setData(itemsToFc(list)); },
      error: () => { this.groupId.set(null); this.items.set([]); this.source('fb-items')?.setData(EMPTY); } });
  }
  async createGroup(): Promise<void> {
    const name = this.newGroupName.trim(); if (!name) return;
    try { const { id } = await this.call(this.api.post<{ id: string }>('/api/admin/feedback/groups', { name })); this.newGroupName = ''; this.groups.update(g => [{ id, name, description: null, status: 'Open', createdAt: '', updatedAt: '', items: 0, wrong: 0, missing: 0 }, ...g]); this.openGroup(id); this.loadGroups(); }
    catch (e) { this.error.set(errorMessage(e)); }
  }
  saveGroup(g: FeedbackGroup, status: FeedbackGroup['status']): void {
    this.api.put(`/api/admin/feedback/groups/${g.id}`, { name: this.groupName.trim() || g.name, description: this.groupDescription, status }).subscribe({ next: () => this.loadGroups(), error: e => this.error.set(errorMessage(e)) });
  }
  deleteGroup(g: FeedbackGroup): void {
    if (!confirm(this.t('admin.fb.deleteGroup', { name: g.name }))) return;
    this.api.delete(`/api/admin/feedback/groups/${g.id}`).subscribe({ next: () => { this.groupId.set(null); this.items.set([]); this.source('fb-items')?.setData(EMPTY); this.loadGroups(); }, error: e => this.error.set(errorMessage(e)) });
  }
  removeItem(i: FeedbackItem): void {
    if (!confirm(this.t('common.confirmDelete'))) return;
    this.api.delete(`/api/admin/feedback/items/${i.id}`).subscribe({ next: () => { this.loadItems(); this.loadGroups(); }, error: e => this.error.set(errorMessage(e)) });
  }
  snapshotLabel(i: FeedbackItem): string {
    const s = i.snapshot; if (!s) return '';
    return String(s['note'] ?? s['name'] ?? s['ref'] ?? '');
  }

  // ---- export / check ----
  async download(g: FeedbackGroup): Promise<void> { await this.saveJson(`/api/admin/feedback/groups/${g.id}/export`, exportFileName(g.name)); }
  async exportAll(): Promise<void> { await this.saveJson('/api/admin/feedback/export?status=open', exportFileName('offen')); }
  private async saveJson(url: string, name: string): Promise<void> {
    try {
      const json = JSON.stringify(await this.call(this.api.get<unknown>(url)), null, 2);
      const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([json], { type: 'application/json' })); a.download = name; a.click();
      setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    } catch (e) { this.error.set(errorMessage(e)); }
  }
  async copy(g: FeedbackGroup): Promise<void> {
    try { await navigator.clipboard.writeText(JSON.stringify(await this.call(this.api.get<unknown>(`/api/admin/feedback/groups/${g.id}/export`)), null, 2)); this.flash(this.t('admin.fb.copied')); }
    catch (e) { this.error.set(errorMessage(e)); }
  }
  check(g: FeedbackGroup): void {
    this.api.get<{ groups: { items: RegressionRow[] }[] }>('/api/admin/feedback/regression', { group: g.id }).subscribe({
      next: r => this.regression.set(Object.fromEntries((r.groups[0]?.items ?? []).map(x => [x.id, x]))), error: e => this.error.set(errorMessage(e)) });
  }

  private call<T>(o: Observable<T>): Promise<T> { return firstValueFrom(o); }
  private t(key: string, params?: Record<string, unknown>): string { return this.i18n.instant(key, params); }
  private flash(msg: string): void { this.info.set(msg); setTimeout(() => this.info.set(''), 2500); }
}

function storageGet(key: string): string | null { try { return localStorage.getItem(key); } catch { return null; } }
