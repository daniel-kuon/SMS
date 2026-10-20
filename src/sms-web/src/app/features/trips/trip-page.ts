import { Component, computed, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatListModule } from '@angular/material/list';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import type { GeoJSONSource } from 'maplibre-gl';
import { bounds, createMap, maplibregl, MlMap, textEl } from '../../core/map';
import { TrackPath } from '../../core/track-playback';
import { fmtObservation, WeatherDialog, WeatherObservation } from '../../core/weather';
import { Api, AuthService, errorMessage } from '../../core/api';
import { Day, Harbour, HarbourMarker, Leg, LogbookEntry, Media, RouteWarning, Ship, Trip, unknownBridgesParam } from '../../core/models';
import { fromShip, routeParams } from '../../core/ship-context';
import { confirm, fmtDate, fmtDateTime, Gallery, HarbourSelect, UploadZone } from '../../shared/shared';
import { CrewDialog, TripEditDialog } from './trip-dialogs';
import { TaskList } from './task-list';
import { EntryDialog, EntryMapBridge, LogbookTable } from '../logbook/logbook-page';
import { RouteEditor, SelKind, TripMapLayers } from './trip-map';
import { allPoints, altColor, buildLegs, lastStop, MapHarbour, parsePlan, Plan, removeCandidate, TripMapData } from './trip-map-model';
import { PostEditorDialog } from '../posts/post-detail';
import { CostsTab } from '../costs/costs-tab';
import { SocialBox } from '../social/social';
import { FormsModule } from '@angular/forms';
import { RouteWeather } from './route-weather';

const TABS = ['overview', 'timeline', 'logbook', 'photos', 'costs', 'tasks', 'crew', 'planner'];
type PickMode = { kind: 'next' } | { kind: 'alt'; step: number } | { kind: 'end'; from: string | null };

@Component({
  imports: [DecimalPipe, RouterLink, FormsModule, MatButtonModule, MatIconModule, MatTabsModule, MatListModule, MatCardModule, TranslatePipe, Gallery, UploadZone, TaskList, LogbookTable, CostsTab, HarbourSelect, SocialBox, RouteWeather],
  template: `
    @if (trip(); as t) {
    <div class="trip-layout">
      <div class="map-wrap">
        <div #map class="map"></div>
        @if (pickMode(); as pm) {
          <div class="map-hint"><mat-icon>ads_click</mat-icon>
            <span>{{ (pm.kind === 'next' ? 'trip.pickNext' : pm.kind === 'alt' ? 'trip.pickAlternative' : 'logbook.pickEndHint') | translate }}</span>
            <button mat-button (click)="cancelPick()">{{ 'common.cancel' | translate }}</button></div>
        }
      </div>
    <div class="content" #content>
      <div class="row header">
        <div class="grow"><h1>{{ t.name }} <span class="chip" [class]="'chip ' + t.visibility.toLowerCase()">{{ 'visibility.' + t.visibility | translate }}</span> <span class="chip">{{ 'trip.status' + t.status | translate }}</span></h1>
          <div class="muted"><a [routerLink]="['/ships', t.shipId]">{{ t.shipName }}</a> · {{ fmt(t.startDate) }} – {{ fmt(t.endDate) }} · {{ t.crew.length }} Crew @if (overview()?.stats; as s) { · {{ s.distanceNm | number:'1.0-1' }} sm }</div></div>
        @if (t.me?.status === 'Invited') { <button mat-flat-button (click)="respond(t, 'accept')">{{ 'trip.accept' | translate }}</button><button mat-stroked-button (click)="respond(t, 'decline')">{{ 'trip.decline' | translate }}</button> }
        @if (t.canEdit) { <button mat-stroked-button (click)="edit(t)"><mat-icon>edit</mat-icon> {{ 'common.edit' | translate }}</button> }
      </div>

      <mat-tab-group [selectedIndex]="tabIndex()" (selectedIndexChange)="go($event)" animationDuration="0ms">
        <mat-tab [label]="'trip.overview' | translate">
          <div class="overview">
            <div class="side">
              @if (overview(); as o) {
                <div class="stats"><div><b>{{ o.stats.distanceNm | number:'1.0-1' }}</b><span>sm</span></div><div><b>{{ o.stats.underwayHours }}</b><span>{{ 'trip.underway' | translate }}</span></div><div><b>{{ o.stats.engineHours | number:'1.0-1' }}</b><span>{{ 'ship.engineHours' | translate }}</span></div>
                  <div><b>{{ o.stats.harbours }}</b><span>{{ 'ship.harbours' | translate }}</span></div><div><b>{{ o.stats.nights }}</b><span>{{ 'trip.nights' | translate }}</span></div><div><b>{{ o.stats.photos }}</b><span>{{ 'trip.photos' | translate }}</span></div>
                  @if (o.stats.costsTotal !== null) { <div><b>{{ o.stats.costsTotal | number:'1.2-2' }}</b><span>{{ t.currency }}</span></div> }</div>
                @if (!path().empty) {
                  <div class="playback" [attr.aria-label]="'tracking.playback' | translate">
                    <button mat-icon-button (click)="togglePlay()" [title]="(playing() ? 'tracking.pause' : 'tracking.play') | translate"><mat-icon>{{ playing() ? 'pause' : 'play_arrow' }}</mat-icon></button>
                    <input type="range" min="0" max="1000" [value]="progress() * 1000" (input)="seek($event)" [attr.aria-label]="'tracking.playback' | translate" />
                    <button mat-button class="speed" (click)="cycleSpeed()" [title]="'tracking.speed' | translate">{{ speed() }}×</button>
                    <span class="muted small">{{ playbackLabel() }}</span>
                  </div>
                }
                <mat-list>@for (l of o.legs; track l.id; let i = $index) {
                  <mat-list-item [attr.data-sel]="'entry:' + l.id" [class.sel]="selected() === 'entry:' + l.id" [class.playing-leg]="playing() && playLeg() === i" (click)="selectItem('entry', l.id)"><span matListItemTitle>{{ l.from }} → {{ l.to }}</span><span matListItemLine>{{ fmt(l.day) }} · {{ l.distanceNm | number:'1.0-1' }} sm</span>
                    <a matListItemMeta mat-icon-button [routerLink]="['/logbook', l.id]" (click)="$event.stopPropagation()" [title]="'trip.openEntry' | translate"><mat-icon>open_in_new</mat-icon></a></mat-list-item> }</mat-list>
                @if (mapData()?.photos?.length) {
                  <div class="photo-strip">@for (p of mapData()!.photos; track p.id) { <img [src]="p.thumbUrl" alt="" [attr.data-sel]="'photo:' + p.id" [class.sel]="selected() === 'photo:' + p.id" (click)="selectItem('photo', p.id)" /> }</div>
                }
                @if (position(); as pos) { <p class="muted"><mat-icon inline>my_location</mat-icon> {{ 'tracking.lastPosition' | translate }} {{ fmtTime(pos.time) }} @if (pos.sogKn) { · {{ pos.sogKn | number:'1.0-1' }} kn } @if (pos.delayed) { · {{ 'tracking.delayed' | translate }} }</p> }
                @if (lastWeather(); as w) { <p class="muted"><mat-icon inline>air</mat-icon> {{ 'weather.lastObservation' | translate }} {{ fmtObservation(w) }}</p> }
                @if (suggestions().length) { <h4>{{ 'tracking.suggestions' | translate }}</h4>
                  @for (s of suggestions(); track s.departedAt) { <div class="row suggestion"><span>{{ fmt(s.day) }}: {{ s.startHarbourName ?? '?' }} → {{ s.endHarbourName ?? '?' }} · {{ s.sailedDistanceNm }} sm</span><button mat-stroked-button (click)="adopt(t, s)">{{ 'tracking.adopt' | translate }}</button></div> } }
                @if (t.isCrew) { <div class="import"><label>{{ 'tracking.import' | translate }} <input type="file" accept=".gpx,.kml,.nmea,.txt,.log" multiple (change)="importTrack(t, $event)" /></label> @if (importResult()) { <span class="muted">{{ importResult() }}</span> }</div> }
                <p class="disclaimer">{{ o.attribution }} · {{ 'harbour.disclaimer' | translate }}</p>
              }
            </div>
          </div>
        </mat-tab>

        <mat-tab [label]="'trip.timeline' | translate">
          <div class="row toolbar">@if (t.isCrew) { <button mat-flat-button (click)="newPost(t)"><mat-icon>edit_note</mat-icon> {{ 'post.new' | translate }}</button> }</div>
          @for (d of days(); track d.date) {
            <section class="day" [id]="'day-' + d.date">
              <h3>{{ fmt(d.date, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }) }}</h3>
              @for (e of d.logbook; track e.id) { <mat-card appearance="outlined" class="entry"><mat-card-content><a [routerLink]="['/logbook', e.id]"><b>{{ e.startHarbour }} → {{ e.endHarbour }}</b></a> · {{ fmtTime(e.departedAt) }} – {{ fmtTime(e.arrivedAt) }} ({{ e.duration }} h) @if (e.windSpeed) { · {{ e.windDirection }} {{ e.windSpeed }} }
                <div class="muted">{{ e.crewList }}</div>@if (e.notes) { <div class="rich" [innerHTML]="e.notes"></div> }</mat-card-content></mat-card> }
              @for (p of d.posts; track p.id) { <mat-card appearance="outlined" class="post"><mat-card-content>
                <div class="row"><b>{{ p.title || ('post.by' | translate) + ' ' + p.author }}</b><span class="muted">{{ p.author }} · {{ fmtTime(p.publishedAt) }}</span><span class="chip" [class]="'chip ' + p.visibility.toLowerCase()">{{ 'visibility.' + p.visibility | translate }}</span><span class="grow"></span>
                  @if (p.canEdit) { <button mat-icon-button (click)="editPost(t, p.id)"><mat-icon>edit</mat-icon></button> }</div>
                <div class="rich" [innerHTML]="p.content"></div>
                @if (p.media.length) { <app-gallery [photos]="p.media" /> }
                <app-social target="posts" [id]="p.id" /></mat-card-content></mat-card> }
              @if (d.photos.length) { <app-gallery [photos]="d.photos" /> }
              @if (d.logbook.length + d.posts.length + d.photos.length === 0) { <p class="muted">–</p> }
            </section>
          }
        </mat-tab>

        <mat-tab [label]="'trip.logbook' | translate"><app-logbook-table [shipId]="t.shipId" [tripId]="t.id" [canWrite]="t.canWriteLogbook" [editor]="entryEditor" [selectedId]="selectedOf('entry')" (rowSelect)="selectItem('entry', $event)" /></mat-tab>

        <mat-tab [label]="'trip.photos' | translate">
          @if (t.isCrew) { <div class="toolbar"><app-upload [url]="'/api/trips/' + t.id + '/media'" [fields]="{ visibility: auth.me()?.defaultContentVisibility ?? 'Crew' }" (uploaded)="reload()" /></div> }
          <app-gallery [photos]="allPhotos()" />
        </mat-tab>

        @if (t.costsVisible) { <mat-tab [label]="'trip.costs' | translate"><app-costs-tab [trip]="t" (changed)="reload()" /></mat-tab> }
        @if (t.isCrew) { <mat-tab [label]="'trip.tasks' | translate"><div class="toolbar"><app-task-list [url]="'/api/trips/' + t.id + '/tasks'" /></div></mat-tab> }

        <mat-tab [label]="'trip.crew' | translate">
          <div class="toolbar">
            <app-social target="trips" [id]="t.id" />
            @if (t.canEdit) { <button mat-flat-button (click)="addCrew(t)"><mat-icon>person_add</mat-icon> {{ 'trip.invite' | translate }}</button> }
            <mat-list>@for (c of t.crew; track c.id) {
              <mat-list-item><span matListItemTitle>@if (c.userId) { <a [routerLink]="['/u', c.userId]">{{ c.name }}</a> } @else { {{ c.name }} <span class="chip">Gast</span> }</span>
                <span matListItemLine>{{ 'trip.' + (c.role === 'Skipper' ? 'skipper' : 'crew') | translate }} @if (c.canWriteLogbook && c.role !== 'Skipper') { · {{ 'trip.logbookPermission' | translate }} } @if (c.status !== 'Accepted') { · {{ c.status }} } @if (c.fromDate || c.toDate) { · {{ fmt(c.fromDate) }} – {{ fmt(c.toDate) }} }</span>
                @if (t.canEdit && c.role !== 'Skipper') { <span matListItemMeta><button mat-icon-button (click)="editCrew(t, c.id)"><mat-icon>edit</mat-icon></button><button mat-icon-button (click)="removeCrew(t, c.id)"><mat-icon>delete</mat-icon></button></span> }
              </mat-list-item> }</mat-list>
          </div>
        </mat-tab>

        <mat-tab [label]="'trip.planner' | translate">
          <div class="toolbar plan">
            @if (t.canEdit) {
              <div class="row">
                <button mat-flat-button (click)="togglePick({ kind: 'next' })" [class.picking]="pickMode()?.kind === 'next'"><mat-icon>add_location_alt</mat-icon> {{ 'trip.nextStop' | translate }}</button>
                <app-harbour-select [label]="'trip.addLeg' | translate" (selected)="addStep($event)" />
              </div>
            }
            @if (plan().origin; as o) { <div class="origin"><mat-icon inline>flag</mat-icon> {{ 'trip.planStart' | translate }}: <b>{{ name(o) }}</b></div> }
            @if (!plan().steps.length) { <p class="muted">{{ 'trip.planEmpty' | translate }}</p> }
            @for (step of plan().steps; track $index; let k = $index) {
              <div class="step">
                <div class="row cands"><b>{{ k + 1 }}.</b>
                  @for (h of step; track h; let a = $index) {
                    <span class="cand" [attr.data-sel]="'harbour:' + h" [class.sel]="selected() === 'harbour:' + h" (click)="selectItem('harbour', h)"><span class="dot" [style.background]="altColor(a)"></span>{{ name(h) }}
                      @if (t.canEdit) { <button mat-icon-button class="small-btn" (click)="$event.stopPropagation(); removeCand(k, a)" [title]="(step.length > 1 ? 'trip.removeAlternative' : 'trip.removeStep') | translate"><mat-icon>close</mat-icon></button> }</span>
                  }
                  @if (t.canEdit) { <button mat-button (click)="togglePick({ kind: 'alt', step: k })" [class.picking]="isAltPick(k)"><mat-icon>add</mat-icon> {{ 'trip.addAlternative' | translate }}</button> }
                </div>
                @for (l of stepLegs()[k]; track l.id) {
                  <div class="leg" [attr.data-sel]="'leg:' + l.id" [class.sel]="selected() === 'leg:' + l.id" (click)="selectItem('leg', l.id)">
                    <span class="line" [style.border-color]="altColor(l.alternative)"></span>{{ l.fromName }} → {{ l.toName }} · {{ l.distanceNm ?? '–' }} sm @if (l.etaHours) { · ETA {{ l.etaHours }} h } @if (l.plannedDate) { · {{ fmt(l.plannedDate) }} }
                    @for (w of l.warnings ?? []; track $index) { <div class="route-warning">⚠ {{ w.note }}</div> }
                  </div>
                }
              </div>
            }
            @if (selLeg(); as l) {
              @if (l.geometry?.coordinates?.length) { <app-route-weather [coordinates]="l.geometry!.coordinates" [date]="l.plannedDate" [map]="mapHandle()" /> }
              @else { <p class="muted">{{ 'weather.route.noRoute' | translate }}</p> }
            } @else if (plan().steps.length) { <p class="muted">{{ 'weather.route.selectLeg' | translate }}</p> }
            <p><b>{{ 'trip.totalNm' | translate }}: {{ totalNm() | number:'1.0-1' }}</b> <span class="muted">({{ 'trip.mainLine' | translate }})</span></p>
            <p class="disclaimer">{{ 'harbour.disclaimer' | translate }}</p>
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>
    </div>}`,
  styles: `
    :host { display: block; }
    /* portrait / narrow: content on top, map below (>= 45 % of the height); landscape >= 900 px: map left (55 %, min 480 px), content right */
    .trip-layout { display: grid; height: calc(100dvh - 64px); grid-template-columns: minmax(0, 1fr); grid-template-rows: minmax(0, 1fr) 45dvh; grid-template-areas: 'content' 'map'; }
    @media (min-width: 900px) and (orientation: landscape) { .trip-layout { grid-template-columns: max(480px, 55%) minmax(0, 1fr); grid-template-rows: minmax(0, 1fr); grid-template-areas: 'map content'; } }
    @media (max-width: 599px) { .trip-layout { height: calc(100dvh - 56px); } }
    .map-wrap { grid-area: map; position: relative; min-height: 0; } .map { position: absolute; inset: 0; }
    .content { grid-area: content; overflow-y: auto; padding: 12px 16px; min-width: 0; }
    .map-hint { position: absolute; top: 10px; left: 50%; transform: translateX(-50%); z-index: 2; background: rgba(255,255,255,.96); border-radius: 18px; box-shadow: 0 1px 4px rgba(0,0,0,.3); padding: 2px 4px 2px 12px; display: flex; align-items: center; gap: 6px; font-size: 13px; }
    .header h1 { margin: 0; } .toolbar { padding: 12px 0; }
    .sel { background: #fff3e0 !important; } .picking { outline: 2px solid #e65100; }
    .photo-strip { display: flex; flex-wrap: wrap; gap: 4px; margin: 8px 0; } .photo-strip img { width: 48px; height: 48px; object-fit: cover; border-radius: 4px; cursor: pointer; border: 2px solid transparent; } .photo-strip img.sel { border-color: #e65100; }
    .plan .origin { margin: 8px 0; } .step { border-left: 3px solid #e3eef9; padding: 4px 0 4px 10px; margin: 8px 0; }
    .cand { display: inline-flex; align-items: center; gap: 4px; padding: 0 0 0 8px; border-radius: 14px; background: #f1f5f9; cursor: pointer; }
    .small-btn { transform: scale(.75); } .leg { cursor: pointer; padding: 3px 4px; font-size: 13px; border-radius: 4px; } .leg .route-warning { color: var(--mat-sys-error, #b3261e); font-size: 12px; margin-left: 24px; } .leg .line { display: inline-block; width: 18px; border-top: 3px dashed; vertical-align: middle; margin-right: 6px; }
    .stats { display: flex; gap: 14px; flex-wrap: wrap; margin: 8px 0; } .stats div { display: flex; flex-direction: column; } .stats b { font-size: 20px; } .stats span { font-size: 11px; color: #666; }
    .dot { width: 14px; height: 14px; border-radius: 50%; display: inline-block; } .import { margin: 8px 0; font-size: 13px; } .suggestion { font-size: 13px; padding: 4px 0; border-bottom: 1px solid #eee; }
    .playback { display: flex; align-items: center; gap: 6px; margin: 4px 0; } .playback input[type=range] { flex: 1; min-width: 80px; } .playback .speed { min-width: 48px; padding: 0 6px; } .small { font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; max-width: 45%; }
    .playing-leg { background: #e3eef9; } .boat-marker { font-size: 30px; color: #d32f2f; display: inline-block; transition: transform .1s linear; }
    .day { margin: 12px 0; } .day h3 { border-bottom: 2px solid #e3eef9; padding-bottom: 4px; } .entry, .post { margin: 8px 0; }`,
})
export class TripPage {
  private api = inject(Api); private dialog = inject(MatDialog); private router = inject(Router); readonly auth = inject(AuthService);
  id = input.required<string>(); tab = input<string>();
  mapEl = viewChild<ElementRef<HTMLDivElement>>('map'); contentEl = viewChild<ElementRef<HTMLDivElement>>('content');
  private snack = inject(MatSnackBar);
  trip = signal<Trip | null>(null); days = signal<Day[]>([]); legs = signal<Leg[]>([]); allPhotos = signal<Media[]>([]);
  suggestions = signal<{ departedAt: string; arrivedAt: string; startHarbourId: string | null; startHarbourName: string | null; endHarbourId: string | null; endHarbourName: string | null; sailedDistanceNm: number; day: string }[]>([]);
  position = signal<{ time: string; lat: number; lng: number; sogKn: number | null; cogDeg: number | null; delayed: boolean } | null>(null);
  lastWeather = signal<WeatherObservation | null>(null);
  fmtObservation = fmtObservation;
  importResult = signal('');
  overview = signal<{ stats: { distanceNm: number; underwayHours: number; engineHours: number; harbours: number; nights: number; photos: number; posts: number; costsTotal: number | null }; legs: { id: string; day: string; from: string; to: string; coordinates: number[][]; distanceNm: number; source: string }[]; stops: { name: string; lat: number; lng: number; nights: number }[]; items: { type: string; id: string; lat: number; lng: number; thumbUrl: string | null; title: string | null }[]; attribution: string } | null>(null);
  tabIndex = signal(0);
  fmt = fmtDate; fmtTime = fmtDateTime;
  private map?: MlMap;
  private layers?: TripMapLayers;
  private editor?: RouteEditor;
  altColor = altColor;

  // --- playback (spec 12 §12.3): walk the concatenated legs by distance; 1× = whole trip in 60 s
  path = signal(new TrackPath([]));
  playing = signal(false); progress = signal(0); speed = signal(1); playLeg = signal(-1);
  private boat?: maplibregl.Marker; private boatEl?: HTMLElement; private raf = 0; private lastTs = 0;
  playbackLabel(): string {
    const o = this.overview(); const p = this.path().at(this.progress());
    if (!o || !p) return '';
    const leg = o.legs[p.legIndex];
    return `${p.distanceNm.toFixed(1)} / ${this.path().totalNm.toFixed(1)} sm${leg ? ' · ' + this.fmt(leg.day) + (leg.from ? ` ${leg.from} → ${leg.to}` : '') : ''}`;
  }
  togglePlay(): void {
    if (this.playing()) { this.stopPlayback(); return; }
    if (this.progress() >= 1) this.progress.set(0);
    this.playing.set(true); this.lastTs = 0;
    const step = (ts: number) => {
      if (!this.playing()) return;
      if (this.lastTs) this.progress.set(Math.min(1, this.progress() + (ts - this.lastTs) / 60000 * this.speed()));
      this.lastTs = ts;
      this.renderPlayback();
      if (this.progress() >= 1) { this.playing.set(false); return; }
      this.raf = requestAnimationFrame(step);
    };
    this.raf = requestAnimationFrame(step);
  }
  stopPlayback(): void { this.playing.set(false); cancelAnimationFrame(this.raf); }
  seek(e: Event): void { this.progress.set(Number((e.target as HTMLInputElement).value) / 1000); this.renderPlayback(); }
  cycleSpeed(): void { this.speed.set(this.speed() === 1 ? 4 : this.speed() === 4 ? 16 : 1); }
  private renderPlayback(): void {
    const map = this.map; const p = this.path().at(this.progress()); if (!map || !p) return;
    if (!this.boat) {
      this.boatEl = document.createElement('span'); this.boatEl.className = 'material-symbols-outlined boat-marker'; this.boatEl.textContent = 'navigation';
      this.boat = new maplibregl.Marker({ element: this.boatEl, rotationAlignment: 'map' }).setLngLat([p.lng, p.lat]).addTo(map);
    }
    this.boat.setLngLat([p.lng, p.lat]).setRotation(p.bearingDeg);
    (map.getSource('trail') as GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection', features: [{ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: this.path().trail(this.progress()) } }] });
    if (p.legIndex !== this.playLeg()) { this.playLeg.set(p.legIndex); const leg = this.overview()?.legs[p.legIndex]; if (leg) this.layers?.select('entry', leg.id, false); }
    if (this.playing() && !map.getBounds().contains([p.lng, p.lat])) map.panTo([p.lng, p.lat]);
  }

  constructor() {
    effect(() => { this.id(); untracked(() => { this.fitted = false; this.reload(); }); });
    effect(() => { const i = TABS.indexOf(this.tab() ?? 'overview'); this.tabIndex.set(i < 0 ? 0 : i); });
    // the map element appears with the trip; it is created once and follows layout changes through the ResizeObserver
    effect(() => { const el = this.mapEl()?.nativeElement; if (el && !this.mapInit) this.mapInit = this.initMap(el); });
    effect(() => { const d = this.mapData(); const o = this.overview(); if (this.mapReady() && d) untracked(() => this.draw(d, o)); });
  }

  reload(): void {
    const id = this.id();
    this.api.get<Trip>(`/api/trips/${id}`).subscribe(t => { this.trip.set(t); if (t.canWriteLogbook) this.api.get<NonNullable<ReturnType<TripPage['suggestions']>>>(`/api/trips/${id}/track/suggestions`).subscribe(s => this.suggestions.set(s)); });
    this.api.get<Day[]>(`/api/trips/${id}/timeline`).subscribe(d => { this.days.set(d); this.allPhotos.set(d.flatMap(x => [...x.photos, ...x.posts.flatMap(p => p.media)])); });
    this.api.get<Leg[]>(`/api/trips/${id}/legs`).subscribe(l => this.legs.set(l));
    this.api.get<NonNullable<ReturnType<TripPage['overview']>>>(`/api/trips/${id}/overview`).subscribe(o => this.overview.set(o));
    this.api.get<NonNullable<ReturnType<TripPage['position']>>>(`/api/trips/${id}/position`).subscribe(p => this.position.set(p ?? null));
    this.api.get<WeatherObservation[]>(`/api/trips/${id}/weather`).subscribe(w => this.lastWeather.set(w.at(-1) ?? null));
    this.loadMap();
  }
  private loadMap(): void { this.api.get<TripMapData>(`/api/trips/${this.id()}/map`).subscribe(d => this.mapData.set(d)); }

  // --- map (docs/14): one map for all tabs, trip layers on top
  mapData = signal<TripMapData | null>(null);
  private mapReady = signal(false);
  private mapInit?: Promise<void>;
  private fitted = false;
  private resizeObs?: ResizeObserver;
  private async initMap(el: HTMLDivElement): Promise<void> {
    const map = this.map = await createMap(el, { zoom: 8 });
    this.mapHandle.set(map);
    this.layers = new TripMapLayers(map, (k, id) => this.onMapSelect(k, id));
    this.layers.pickLabel = h => this.routeLabel(h);
    this.editor = new RouteEditor(map);
    map.addSource('trail', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    map.addLayer({ id: 'trail', type: 'line', source: 'trail', paint: { 'line-color': '#d32f2f', 'line-width': 5, 'line-opacity': 0.8 } });
    map.on('contextmenu', e => { if (!this.editor?.hitsVertex(e.point)) this.dialog.open(WeatherDialog, { data: { lat: e.lngLat.lat, lng: e.lngLat.lng }, width: '560px' }); });
    map.on('moveend', () => { if (this.pickMode()) this.loadPickHarbours(); });
    this.resizeObs = new ResizeObserver(() => map.resize());
    this.resizeObs.observe(el);
    this.mapReady.set(true);
  }
  ngOnDestroy(): void { this.stopPlayback(); this.resizeObs?.disconnect(); this.map?.remove(); }

  private draw(d: TripMapData, o: ReturnType<TripPage['overview']>): void {
    // a recorded track (GPS, AIS) wins over the stored/routed line; without either the overview's line is used, then the straight line
    const recorded = (src: string) => src === 'GpsLive' || src === 'GpsFile' || src === 'Ais' || src === 'Mixed';
    const tracks = new Map((o?.legs ?? []).filter(l => l.coordinates.length > 1).map(l => [l.id, l]));
    const logbook = d.logbook.map(e => {
      const t = tracks.get(e.id);
      return t && (recorded(t.source) || !e.geometry?.coordinates.length) ? { ...e, geometry: { type: 'LineString' as const, coordinates: t.coordinates } } : e;
    });
    this.layers!.setData({ ...d, logbook });
    this.drawExtras(o);
    if (!this.fitted) { this.fitted = true; void this.fit(); }
  }

  /** Fit to everything; with nothing on the map the planned start or the ship's home harbour. */
  private async fit(): Promise<void> {
    const layers = this.layers!; const t = this.trip();
    if (allPoints(layers.data).length || !t) { layers.fit(); return; }
    try {
      const id = t.plannedStartHarbourId ?? (await firstValueFrom(this.api.get<Ship>(`/api/ships/${t.shipId}`))).homeHarbourId;
      layers.fit(id ? await firstValueFrom(this.api.get<Harbour>(`/api/harbours/${id}`)) : null);
    } catch { /* keep the default view */ }
  }

  /** Playback track, post markers and the live position (from /overview and /position). */
  private drawExtras(o: ReturnType<TripPage['overview']>): void {
    const map = this.map!;
    this.stopPlayback(); this.path.set(new TrackPath(o?.legs ?? [])); this.progress.set(0); this.playLeg.set(-1); this.boat?.remove(); this.boat = undefined;
    (map.getSource('trail') as GeoJSONSource).setData({ type: 'FeatureCollection', features: [] });
    for (const m of this.markers) m.remove(); this.markers = [];
    for (const it of o?.items ?? []) {
      if (it.type !== 'post') continue; // photos come from /map
      const el = document.createElement('span'); el.className = 'material-symbols-outlined'; el.style.cssText = 'font-size:28px;color:#1b4d7a;cursor:pointer'; el.textContent = 'chat_bubble';
      el.onclick = () => this.router.navigate(['/posts', it.id]);
      this.markers.push(new maplibregl.Marker({ element: el }).setLngLat([it.lng, it.lat]).addTo(map));
    }
    const pos = this.position();
    if (pos) { const el = document.createElement('span'); el.className = 'material-symbols-outlined'; el.style.cssText = 'font-size:30px;color:#d32f2f'; el.textContent = 'sailing'; this.markers.push(new maplibregl.Marker({ element: el }).setLngLat([pos.lng, pos.lat]).setPopup(new maplibregl.Popup({ offset: 20 }).setDOMContent(textEl(fmtDateTime(pos.time)))).addTo(map)); }
  }
  private markers: maplibregl.Marker[] = [];

  // --- selection: list → map eases to the item; map → list scrolls to it (switching to the tab that lists it)
  selected = signal<string | null>(null);
  /** The selected planned leg (the object from /map, so the route-weather panel resets only on another leg or a reload). */
  selLeg = computed(() => { const id = this.selectedOf('leg'); return id ? this.mapData()?.plannedLegs.find(l => l.id === id) ?? null : null; });
  mapHandle = signal<MlMap | null>(null);
  selectedOf(kind: SelKind): string | null { const s = this.selected(); return s?.startsWith(kind + ':') ? s.slice(kind.length + 1) : null; }
  selectItem(kind: SelKind, id: string): void { this.selected.set(`${kind}:${id}`); this.layers?.select(kind, id); }
  private onMapSelect(kind: SelKind, id: string): void {
    this.selected.set(`${kind}:${id}`);
    const find = () => this.contentEl()?.nativeElement.querySelector(`[data-sel="${kind}:${id}"]`);
    if (!find()) this.router.navigate(['/trips', this.id(), kind === 'leg' || kind === 'harbour' ? 'planner' : this.tab() === 'logbook' ? 'logbook' : 'overview'], { replaceUrl: true });
    setTimeout(() => find()?.scrollIntoView({ block: 'nearest', behavior: 'smooth' }), 150);
  }

  go(i: number): void { this.router.navigate(['/trips', this.id(), TABS[i]], { replaceUrl: true }); }
  /** Main line (alternative 0) only. */
  totalNm(): number { return this.legs().filter(l => !l.alternative).reduce((s, l) => s + (l.distanceNm ?? 0), 0); }

  adopt(t: Trip, s: NonNullable<ReturnType<TripPage['suggestions']>>[number]): void {
    void this.openEntry(t, undefined, s);
  }
  importTrack(t: Trip, e: Event): void {
    const files = Array.from((e.target as HTMLInputElement).files ?? []);
    if (!files.length) return;
    this.importResult.set('…');
    this.api.upload<{ fileName: string; parsed: number; added: number }[]>(`/api/ships/${t.shipId}/track-imports`, files, { tripId: t.id }).subscribe({
      next: r => { this.importResult.set(r.map(x => `${x.fileName}: ${x.added}/${x.parsed}`).join(', ')); setTimeout(() => this.reload(), 25000); },
      error: err => this.importResult.set(errorMessage(err)) });
  }
  edit(t: Trip): void { this.dialog.open(TripEditDialog, { data: { shipId: t.shipId, trip: t }, width: '700px' }).afterClosed().subscribe(ok => ok && this.reload()); }
  respond(t: Trip, action: 'accept' | 'decline'): void { this.api.post(`/api/crew/${t.me!.id}/${action}`, {}).subscribe(() => this.reload()); }
  addCrew(t: Trip): void { this.dialog.open(CrewDialog, { data: { tripId: t.id } }).afterClosed().subscribe(ok => ok && this.reload()); }
  editCrew(t: Trip, id: string): void { this.dialog.open(CrewDialog, { data: { tripId: t.id, member: t.crew.find(c => c.id === id) } }).afterClosed().subscribe(ok => ok && this.reload()); }
  async removeCrew(t: Trip, id: string): Promise<void> { if (await confirm(this.dialog, 'Crewmitglied entfernen?')) this.api.delete(`/api/crew/${id}`).subscribe({ next: () => this.reload(), error: e => alert(errorMessage(e)) }); }
  newPost(t: Trip): void { this.dialog.open(PostEditorDialog, { data: { trip: t }, width: '700px' }).afterClosed().subscribe(ok => ok && this.reload()); }
  editPost(t: Trip, postId: string): void { this.dialog.open(PostEditorDialog, { data: { trip: t, postId }, width: '700px' }).afterClosed().subscribe(ok => ok && this.reload()); }

  // --- plan (docs/14): steps with alternative harbours picked on the map, saved as a whole through PUT /legs
  pickMode = signal<PickMode | null>(null);
  private pendingOrigin = signal<string | null>(null);
  private names = new Map<string, string>();
  private translate = inject(TranslateService);
  plan = computed<Plan>(() => { const p = parsePlan(this.legs(), this.trip()?.plannedStartHarbourId ?? null); return p.origin ? p : { ...p, origin: this.pendingOrigin() }; });
  stepLegs = computed(() => {
    const orders = [...new Set(this.legs().map(l => l.order))].sort((a, b) => a - b);
    return orders.map(o => this.legs().filter(l => l.order === o).sort((a, b) => (a.alternative ?? 0) - (b.alternative ?? 0)));
  });
  name(id: string): string {
    const l = this.legs().find(x => x.toHarbourId === id || x.fromHarbourId === id);
    return this.names.get(id) ?? (l ? (l.toHarbourId === id ? l.toName : l.fromName) : this.layers?.name(id) ?? '…');
  }
  isAltPick(k: number): boolean { const m = this.pickMode(); return m?.kind === 'alt' && m.step === k; }
  togglePick(m: PickMode): void {
    const same = this.pickMode()?.kind === m.kind && (m.kind !== 'alt' || this.isAltPick(m.step));
    this.cancelPick();
    if (!same) this.setPick(m);
  }
  private setPick(m: PickMode): void { this.pickMode.set(m); if (this.layers) this.layers.pick = h => this.onPick(h); this.loadPickHarbours(); }
  cancelPick(): void {
    const r = this.endResolve; this.endResolve = null; r?.(null);
    this.pickMode.set(null);
    if (this.layers) { this.layers.pick = null; this.layers.setPickHarbours([]); }
  }
  private endResolve: ((h: { id: string; name: string } | null) => void) | null = null;
  private onPick(h: MapHarbour): void {
    const m = this.pickMode(); if (!m) return;
    this.names.set(h.id, h.name);
    if (m.kind === 'next') this.addStep(h); // stays in the mode for the following stops
    else if (m.kind === 'alt') { this.addAlternative(m.step, h.id); this.cancelPick(); }
    else { const r = this.endResolve; this.endResolve = null; this.cancelPick(); r?.(h); }
  }
  private loadPickHarbours(): void {
    const map = this.map; if (!map || !this.pickMode()) return;
    const b = map.getBounds();
    this.api.get<HarbourMarker[]>('/api/harbours', { bbox: `${b.getWest()},${b.getSouth()},${b.getEast()},${b.getNorth()}` }).subscribe(hs => { if (this.pickMode()) this.layers?.setPickHarbours(hs); });
  }
  /** Hover text in pick mode: the routed distance from the stop the new leg would start at. */
  private routeCache = new Map<string, Promise<string | null>>();
  /** The trip ship's depths and air draft for the routing API (low bridges and shallows as for the planned legs). */
  private shipParams?: Promise<Record<string, number | string>>;
  private routeLabel(h: MapHarbour): Promise<string | null> {
    const m = this.pickMode(); const p = this.plan();
    const from = m?.kind === 'next' ? lastStop(p) : m?.kind === 'alt' ? (p.steps[m.step - 1]?.[0] ?? p.origin) : m?.kind === 'end' ? m.from : null;
    if (!from || from === h.id) return Promise.resolve(null);
    const key = `${from}>${h.id}`;
    const t = this.trip();
    this.shipParams ??= t ? firstValueFrom(this.api.get<Ship>(`/api/ships/${t.shipId}`)).then(s => ({ ...routeParams(fromShip(s)), ...unknownBridgesParam(s) }), () => ({})) : undefined;
    if (!this.routeCache.has(key)) this.routeCache.set(key, (this.shipParams ?? Promise.resolve({})).then(p => firstValueFrom(this.api.get<{ distanceNm: number; warnings?: RouteWarning[] }>('/api/routing/route', { fromHarbour: from, toHarbour: h.id, ...p })))
      .then(r => `${r.distanceNm} sm${r.warnings?.length ? ' ⚠' : ''}`, () => null));
    return this.routeCache.get(key)!;
  }
  private savePlan(p: Plan): void {
    this.api.put<Leg[]>(`/api/trips/${this.id()}/legs`, buildLegs(p)).subscribe({
      next: l => { this.legs.set(l); this.loadMap(); },
      error: e => this.snack.open(errorMessage(e), 'OK', { duration: 6000 }), // e.g. 400 "leg must start at a harbour of the previous step"
    });
  }
  addStep(h: { id: string; name: string }): void {
    this.names.set(h.id, h.name);
    const p = this.plan();
    if (!p.origin) { this.pendingOrigin.set(h.id); this.snack.open(this.translate.instant('trip.originSet', { name: h.name }), undefined, { duration: 3000 }); return; }
    if (lastStop(p) === h.id) return;
    this.savePlan({ ...p, steps: [...p.steps, [h.id]] });
  }
  private addAlternative(step: number, id: string): void {
    const p = this.plan();
    if (!p.steps[step] || p.steps[step].includes(id)) return;
    this.savePlan({ ...p, steps: p.steps.map((s, k) => (k === step ? [...s, id] : s)) });
  }
  removeCand(step: number, alt: number): void { this.savePlan(removeCandidate(this.plan(), step, alt)); }

  // --- logbook entry next to the map: the dialog covers the content column, the map stays usable for the harbour pick and the route
  entryEditor = (e?: LogbookEntry): Promise<boolean> => { const t = this.trip(); return t ? this.openEntry(t, e) : Promise.resolve(false); };
  private async openEntry(t: Trip, entry?: LogbookEntry, suggestion?: NonNullable<ReturnType<TripPage['suggestions']>>[number]): Promise<boolean> {
    const r = this.contentEl()?.nativeElement.getBoundingClientRect();
    const bridge: EntryMapBridge = {
      pickHarbour: from => new Promise(res => { this.cancelPick(); this.endResolve = res; this.setPick({ kind: 'end', from }); }),
      showRoute: c => {
        this.editor?.set(c); if (this.layers) this.layers.busy = !!c?.length;
        if (c && c.length > 1 && this.map) this.map.fitBounds(bounds(c.map(x => [x[0], x[1]] as [number, number])), { padding: 60, maxZoom: 13 });
      },
      route: () => this.editor?.coords ?? null,
    };
    // ponytail: the panel is placed once over the content column; it does not follow a later orientation change
    const place = r ? { position: { left: `${r.left}px`, top: `${r.top}px` }, width: `${r.width}px`, height: `${r.height}px`, maxWidth: '100vw' } : { width: '800px' };
    const ref = this.dialog.open(EntryDialog, { data: { shipId: t.shipId, tripId: t.id, entry, suggestion, map: bridge, defaultStart: this.defaultStart() }, hasBackdrop: false, panelClass: 'entry-side-dialog', ...place });
    const ok = await firstValueFrom(ref.afterClosed());
    this.editor?.set(null); this.cancelPick(); if (this.layers) this.layers.busy = false;
    if (ok) this.reload();
    return !!ok;
  }
  /** Start harbour for a new entry when the API has none: the last entry's end, else the plan's start. */
  private defaultStart(): { id: string; name: string } | null {
    const last = [...(this.mapData()?.logbook ?? [])].sort((a, b) => a.arrivedAt.localeCompare(b.arrivedAt)).at(-1);
    const id = last?.endHarbourId ?? this.plan().origin;
    return id ? { id, name: this.name(id) } : null;
  }
}
