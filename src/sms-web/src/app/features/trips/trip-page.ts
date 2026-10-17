import { Component, effect, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { MatListModule } from '@angular/material/list';
import { MatDialog } from '@angular/material/dialog';
import { MatCardModule } from '@angular/material/card';
import { TranslatePipe } from '@ngx-translate/core';
import type { GeoJSONSource, MapLayerMouseEvent } from 'maplibre-gl';
import { bounds, createMap, maplibregl, MlMap, textEl } from '../../core/map';
import { TrackPath } from '../../core/track-playback';
import { Api, AuthService, errorMessage } from '../../core/api';
import { Day, Leg, Media, Trip } from '../../core/models';
import { confirm, fmtDate, fmtDateTime, Gallery, HarbourSelect, UploadZone } from '../../shared/shared';
import { CrewDialog, TripEditDialog } from './trip-dialogs';
import { TaskList } from './task-list';
import { EntryDialog, LogbookTable } from '../logbook/logbook-page';
import { PostEditorDialog } from '../posts/post-detail';
import { CostsTab } from '../costs/costs-tab';
import { SocialBox } from '../social/social';
import { FormsModule } from '@angular/forms';

const TABS = ['overview', 'timeline', 'logbook', 'photos', 'costs', 'tasks', 'crew', 'planner'];
const DAY_COLORS = ['#e53935', '#8e24aa', '#3949ab', '#039be5', '#00897b', '#7cb342', '#fdd835', '#fb8c00', '#6d4c41', '#546e7a'];

@Component({
  imports: [DecimalPipe, RouterLink, FormsModule, MatButtonModule, MatIconModule, MatTabsModule, MatListModule, MatCardModule, TranslatePipe, Gallery, UploadZone, TaskList, LogbookTable, CostsTab, HarbourSelect, SocialBox],
  template: `
    @if (trip(); as t) {
    <div class="page">
      <div class="row header">
        <div class="grow"><h1>{{ t.name }} <span class="chip" [class]="'chip ' + t.visibility.toLowerCase()">{{ 'visibility.' + t.visibility | translate }}</span> <span class="chip">{{ 'trip.status' + t.status | translate }}</span></h1>
          <div class="muted"><a [routerLink]="['/ships', t.shipId]">{{ t.shipName }}</a> · {{ fmt(t.startDate) }} – {{ fmt(t.endDate) }} · {{ t.crew.length }} Crew @if (overview()?.stats; as s) { · {{ s.distanceNm | number:'1.0-1' }} sm }</div></div>
        @if (t.me?.status === 'Invited') { <button mat-flat-button (click)="respond(t, 'accept')">{{ 'trip.accept' | translate }}</button><button mat-stroked-button (click)="respond(t, 'decline')">{{ 'trip.decline' | translate }}</button> }
        @if (t.canEdit) { <button mat-stroked-button (click)="edit(t)"><mat-icon>edit</mat-icon> {{ 'common.edit' | translate }}</button> }
      </div>

      <mat-tab-group [selectedIndex]="tabIndex()" (selectedIndexChange)="go($event)" animationDuration="0ms">
        <mat-tab [label]="'trip.overview' | translate">
          <div class="overview">
            <div #map class="map"></div>
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
                  <a mat-list-item [routerLink]="['/logbook', l.id]" [class.playing-leg]="playing() && playLeg() === i" (mouseenter)="highlight(i)" (mouseleave)="highlight(-1)"><span matListItemIcon class="dot" [style.background]="color(i)"></span><span matListItemTitle>{{ l.from }} → {{ l.to }}</span><span matListItemLine>{{ fmt(l.day) }} · {{ l.distanceNm | number:'1.0-1' }} sm</span></a> }</mat-list>
                @if (position(); as pos) { <p class="muted"><mat-icon inline>my_location</mat-icon> {{ 'tracking.lastPosition' | translate }} {{ fmtTime(pos.time) }} @if (pos.sogKn) { · {{ pos.sogKn | number:'1.0-1' }} kn } @if (pos.delayed) { · {{ 'tracking.delayed' | translate }} }</p> }
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

        <mat-tab [label]="'trip.logbook' | translate"><app-logbook-table [shipId]="t.shipId" [tripId]="t.id" [canWrite]="t.canWriteLogbook" /></mat-tab>

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
          <div class="toolbar">
            <mat-list>@for (l of legs(); track l.id; let i = $index) {
              <mat-list-item><span matListItemTitle>{{ i + 1 }}. {{ l.fromName }} → {{ l.toName }}</span><span matListItemLine>{{ l.distanceNm }} sm · ETA {{ l.etaHours }} h @if (l.plannedDate) { · {{ fmt(l.plannedDate) }} }</span>
                @if (t.canEdit) { <span matListItemMeta><button mat-icon-button (click)="moveLeg(i, -1)"><mat-icon>arrow_upward</mat-icon></button><button mat-icon-button (click)="moveLeg(i, 1)"><mat-icon>arrow_downward</mat-icon></button><button mat-icon-button (click)="removeLeg(i)"><mat-icon>delete</mat-icon></button></span> }</mat-list-item> }</mat-list>
            <p><b>{{ 'trip.totalNm' | translate }}: {{ totalNm() | number:'1.0-1' }}</b></p>
            @if (t.canEdit) { <div class="row"><app-harbour-select [label]="'trip.addLeg' | translate" (selected)="addLeg($event.id)" /></div> }
            <p class="disclaimer">{{ 'harbour.disclaimer' | translate }}</p>
          </div>
        </mat-tab>
      </mat-tab-group>
    </div>}`,
  styles: `
    .header h1 { margin: 0; } .toolbar { padding: 12px 0; }
    .overview { display: flex; gap: 12px; min-height: 480px; } .map { flex: 2; min-height: 480px; border-radius: 8px; } .side { flex: 1; max-width: 420px; }
    .stats { display: flex; gap: 14px; flex-wrap: wrap; margin: 8px 0; } .stats div { display: flex; flex-direction: column; } .stats b { font-size: 20px; } .stats span { font-size: 11px; color: #666; }
    .dot { width: 14px; height: 14px; border-radius: 50%; display: inline-block; } .import { margin: 8px 0; font-size: 13px; } .suggestion { font-size: 13px; padding: 4px 0; border-bottom: 1px solid #eee; }
    .playback { display: flex; align-items: center; gap: 6px; margin: 4px 0; } .playback input[type=range] { flex: 1; min-width: 80px; } .playback .speed { min-width: 48px; padding: 0 6px; } .small { font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; max-width: 45%; }
    .playing-leg { background: #e3eef9; } .boat-marker { font-size: 30px; color: #d32f2f; display: inline-block; transition: transform .1s linear; }
    .day { margin: 12px 0; } .day h3 { border-bottom: 2px solid #e3eef9; padding-bottom: 4px; } .entry, .post { margin: 8px 0; }
    @media (max-width: 800px) { .overview { flex-direction: column; } .side { max-width: none; } }`,
})
export class TripPage {
  private api = inject(Api); private dialog = inject(MatDialog); private router = inject(Router); readonly auth = inject(AuthService);
  id = input.required<string>(); tab = input<string>();
  mapEl = viewChild<ElementRef<HTMLDivElement>>('map');
  trip = signal<Trip | null>(null); days = signal<Day[]>([]); legs = signal<Leg[]>([]); allPhotos = signal<Media[]>([]);
  suggestions = signal<{ departedAt: string; arrivedAt: string; startHarbourId: string | null; startHarbourName: string | null; endHarbourId: string | null; endHarbourName: string | null; sailedDistanceNm: number; day: string }[]>([]);
  position = signal<{ time: string; lat: number; lng: number; sogKn: number | null; cogDeg: number | null; delayed: boolean } | null>(null);
  importResult = signal('');
  overview = signal<{ stats: { distanceNm: number; underwayHours: number; engineHours: number; harbours: number; nights: number; photos: number; posts: number; costsTotal: number | null }; legs: { id: string; day: string; from: string; to: string; coordinates: number[][]; distanceNm: number; source: string }[]; stops: { name: string; lat: number; lng: number; nights: number }[]; items: { type: string; id: string; lat: number; lng: number; thumbUrl: string | null; title: string | null }[]; attribution: string } | null>(null);
  tabIndex = signal(0);
  fmt = fmtDate; fmtTime = fmtDateTime;
  private map?: MlMap;
  color = (i: number) => DAY_COLORS[i % DAY_COLORS.length];

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
    if (p.legIndex !== this.playLeg()) { this.playLeg.set(p.legIndex); this.highlight(p.legIndex, false); }
    if (this.playing() && !map.getBounds().contains([p.lng, p.lat])) map.panTo([p.lng, p.lat]);
  }

  constructor() { effect(() => { this.id(); this.reload(); }); effect(() => { const i = TABS.indexOf(this.tab() ?? 'overview'); this.tabIndex.set(i < 0 ? 0 : i); }); }

  reload(): void {
    const id = this.id();
    this.api.get<Trip>(`/api/trips/${id}`).subscribe(t => { this.trip.set(t); if (t.canWriteLogbook) this.api.get<NonNullable<ReturnType<TripPage['suggestions']>>>(`/api/trips/${id}/track/suggestions`).subscribe(s => this.suggestions.set(s)); });
    this.api.get<Day[]>(`/api/trips/${id}/timeline`).subscribe(d => { this.days.set(d); this.allPhotos.set(d.flatMap(x => [...x.photos, ...x.posts.flatMap(p => p.media)])); });
    this.api.get<Leg[]>(`/api/trips/${id}/legs`).subscribe(l => this.legs.set(l));
    this.api.get<NonNullable<ReturnType<TripPage['overview']>>>(`/api/trips/${id}/overview`).subscribe(o => { this.overview.set(o); setTimeout(() => this.drawMap(), 50); });
    this.api.get<NonNullable<ReturnType<TripPage['position']>>>(`/api/trips/${id}/position`).subscribe(p => this.position.set(p ?? null));
  }

  private async drawMap(): Promise<void> {
    const el = this.mapEl()?.nativeElement; const o = this.overview();
    if (!el || !o) return;
    if (!this.map) {
      this.map = await createMap(el, { zoom: 8 });
      const map = this.map;
      map.addSource('legs', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
      map.addLayer({ id: 'legs-dashed', type: 'line', source: 'legs', filter: ['==', ['get', 'recorded'], false], paint: { 'line-color': ['get', 'color'], 'line-width': ['case', ['boolean', ['feature-state', 'hl'], false], 8, 4], 'line-dasharray': [2, 1.5], 'line-opacity': ['case', ['boolean', ['feature-state', 'dim'], false], 0.35, 1] } });
      map.addLayer({ id: 'legs-solid', type: 'line', source: 'legs', filter: ['==', ['get', 'recorded'], true], paint: { 'line-color': ['get', 'color'], 'line-width': ['case', ['boolean', ['feature-state', 'hl'], false], 8, 4], 'line-opacity': ['case', ['boolean', ['feature-state', 'dim'], false], 0.35, 1] } });
      map.addSource('stops', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
      map.addLayer({ id: 'stops', type: 'circle', source: 'stops', paint: { 'circle-color': '#1976d2', 'circle-radius': 6, 'circle-stroke-color': '#fff', 'circle-stroke-width': 2 } });
      map.addSource('trail', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
      map.addLayer({ id: 'trail', type: 'line', source: 'trail', paint: { 'line-color': '#d32f2f', 'line-width': 5, 'line-opacity': 0.8 } });
      map.addLayer({ id: 'stop-labels', type: 'symbol', source: 'stops', layout: { 'text-field': ['get', 'label'], 'text-size': 11, 'text-anchor': 'left', 'text-offset': [0.8, 0], 'text-font': ['Noto Sans Regular'] }, paint: { 'text-color': '#1b4d7a', 'text-halo-color': '#fff', 'text-halo-width': 1.5 } });
      map.on('mouseenter', 'legs-solid', e => this.showLegTooltip(e)); map.on('mouseenter', 'legs-dashed', e => this.showLegTooltip(e));
      map.on('mouseleave', 'legs-solid', () => this.legPopup?.remove()); map.on('mouseleave', 'legs-dashed', () => this.legPopup?.remove());
    }
    const map = this.map;
    this.stopPlayback(); this.path.set(new TrackPath(o.legs)); this.progress.set(0); this.playLeg.set(-1); this.boat?.remove(); this.boat = undefined;
    (map.getSource('trail') as GeoJSONSource).setData({ type: 'FeatureCollection', features: [] });
    const recorded = (src: string) => src === 'GpsLive' || src === 'GpsFile' || src === 'Ais' || src === 'Mixed';
    (map.getSource('legs') as GeoJSONSource).setData({ type: 'FeatureCollection', features: o.legs.map((l, i) => ({ type: 'Feature', id: i, properties: { color: this.color(i), recorded: recorded(l.source), label: `${l.from ? l.from + ' → ' + l.to + ': ' : ''}${l.distanceNm} sm (${l.source})` }, geometry: { type: 'LineString', coordinates: l.coordinates } })) });
    (map.getSource('stops') as GeoJSONSource).setData({ type: 'FeatureCollection', features: o.stops.map(s => ({ type: 'Feature', properties: { label: `${s.name} (${s.nights}×)` }, geometry: { type: 'Point', coordinates: [s.lng, s.lat] } })) });
    for (const m of this.markers) m.remove(); this.markers = [];
    for (const it of o.items) {
      let el: HTMLElement;
      if (it.type === 'photo' && it.thumbUrl) { const img = document.createElement('img'); img.src = it.thumbUrl; img.className = 'photo-marker'; img.width = 40; img.height = 40; el = img; }
      else { el = document.createElement('span'); el.className = 'material-symbols-outlined'; el.style.cssText = 'font-size:28px;color:#1b4d7a;cursor:pointer'; el.textContent = 'chat_bubble'; }
      const marker = new maplibregl.Marker({ element: el }).setLngLat([it.lng, it.lat]).addTo(map);
      if (it.type === 'post') el.onclick = () => this.router.navigate(['/posts', it.id]);
      else { const div = document.createElement('div'); const big = document.createElement('img'); big.src = it.thumbUrl ?? ''; big.style.maxWidth = '200px'; div.append(big, document.createElement('br'), document.createTextNode(it.title ?? '')); marker.setPopup(new maplibregl.Popup({ offset: 24 }).setDOMContent(div)); }
      this.markers.push(marker);
    }
    const pos = this.position();
    if (pos) { const el = document.createElement('span'); el.className = 'material-symbols-outlined'; el.style.cssText = 'font-size:30px;color:#d32f2f'; el.textContent = 'sailing'; this.markers.push(new maplibregl.Marker({ element: el }).setLngLat([pos.lng, pos.lat]).setPopup(new maplibregl.Popup({ offset: 20 }).setDOMContent(textEl(fmtDateTime(pos.time)))).addTo(map)); }
    const all = [...o.legs.flatMap(l => l.coordinates.map(c => [c[0], c[1]] as [number, number])), ...o.items.map(i => [i.lng, i.lat] as [number, number])];
    // the tab body may still be collapsed: let the container get its size before fitting the bounds
    setTimeout(() => { map.resize(); if (all.length) map.fitBounds(bounds(all), { padding: 40, maxZoom: 12, duration: 0 }); }, 150);
  }
  private markers: maplibregl.Marker[] = [];
  private legPopup?: maplibregl.Popup;
  private showLegTooltip(e: MapLayerMouseEvent): void {
    const label = e.features?.[0]?.properties?.['label'];
    if (!label || !this.map) return;
    this.legPopup?.remove();
    this.legPopup = new maplibregl.Popup({ closeButton: false, closeOnClick: false }).setLngLat(e.lngLat).setDOMContent(textEl(label)).addTo(this.map);
  }

  highlight(i: number, zoom = true): void {
    const map = this.map; const o = this.overview(); if (!map || !o) return;
    o.legs.forEach((_, j) => map.setFeatureState({ source: 'legs', id: j }, { hl: j === i, dim: i >= 0 && j !== i }));
    if (zoom && i >= 0 && o.legs[i].coordinates.length) map.fitBounds(bounds(o.legs[i].coordinates.map(c => [c[0], c[1]] as [number, number])), { padding: 80, maxZoom: 13 });
  }
  go(i: number): void { this.router.navigate(['/trips', this.id(), TABS[i]], { replaceUrl: true }); if (i === 0) setTimeout(() => this.drawMap(), 100); }
  totalNm(): number { return this.legs().reduce((s, l) => s + (l.distanceNm ?? 0), 0); }

  adopt(t: Trip, s: NonNullable<ReturnType<TripPage['suggestions']>>[number]): void {
    this.dialog.open(EntryDialog, { data: { shipId: t.shipId, tripId: t.id, suggestion: s }, width: '800px' }).afterClosed().subscribe(ok => ok && this.reload());
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

  private saveLegs(legs: { fromHarbourId: string; toHarbourId: string; plannedDate: string | null }[]): void {
    this.api.put<Leg[]>(`/api/trips/${this.id()}/legs`, legs).subscribe(l => this.legs.set(l));
  }
  addLeg(harbourId: string): void {
    const legs = this.legs();
    const from = legs.length ? legs[legs.length - 1].toHarbourId : this.trip()?.plannedStartHarbourId ?? null;
    if (!from) { alert('Zuerst einen Starthafen im Törn festlegen oder zwei Häfen wählen.'); this.pendingFrom = harbourId; return; }
    this.saveLegs([...legs.map(l => ({ fromHarbourId: l.fromHarbourId, toHarbourId: l.toHarbourId, plannedDate: l.plannedDate })), { fromHarbourId: this.pendingFrom ?? from, toHarbourId: harbourId, plannedDate: null }]);
    this.pendingFrom = null;
  }
  private pendingFrom: string | null = null;
  removeLeg(i: number): void { const legs = this.legs().map(l => ({ fromHarbourId: l.fromHarbourId, toHarbourId: l.toHarbourId, plannedDate: l.plannedDate })); legs.splice(i, 1); for (let k = 1; k < legs.length; k++) legs[k].fromHarbourId = legs[k - 1].toHarbourId; this.saveLegs(legs); }
  moveLeg(i: number, d: number): void {
    const legs = this.legs().map(l => ({ fromHarbourId: l.fromHarbourId, toHarbourId: l.toHarbourId, plannedDate: l.plannedDate })); const j = i + d; if (j < 0 || j >= legs.length) return;
    [legs[i].toHarbourId, legs[j].toHarbourId] = [legs[j].toHarbourId, legs[i].toHarbourId]; for (let k = 1; k < legs.length; k++) legs[k].fromHarbourId = legs[k - 1].toHarbourId; this.saveLegs(legs);
  }
}
