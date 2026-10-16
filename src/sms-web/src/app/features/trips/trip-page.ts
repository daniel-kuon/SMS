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
import * as L from 'leaflet';
import { baseLayers, markerCluster } from '../../core/leaflet-setup';
import { Api, AuthService, errorMessage } from '../../core/api';
import { Day, Leg, Media, Trip } from '../../core/models';
import { confirm, fmtDate, fmtDateTime, Gallery, HarbourSelect, textNode, UploadZone } from '../../shared/shared';
import { CrewDialog, TripEditDialog } from './trip-dialogs';
import { TaskList } from './task-list';
import { LogbookTable } from '../logbook/logbook-page';
import { PostEditorDialog } from '../posts/post-detail';
import { CostsTab } from '../costs/costs-tab';
import { FormsModule } from '@angular/forms';

const TABS = ['overview', 'timeline', 'logbook', 'photos', 'costs', 'tasks', 'crew', 'planner'];
const DAY_COLORS = ['#e53935', '#8e24aa', '#3949ab', '#039be5', '#00897b', '#7cb342', '#fdd835', '#fb8c00', '#6d4c41', '#546e7a'];

@Component({
  imports: [DecimalPipe, RouterLink, FormsModule, MatButtonModule, MatIconModule, MatTabsModule, MatListModule, MatCardModule, TranslatePipe, Gallery, UploadZone, TaskList, LogbookTable, CostsTab, HarbourSelect],
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
                <mat-list>@for (l of o.legs; track l.id; let i = $index) {
                  <a mat-list-item [routerLink]="['/logbook', l.id]" (mouseenter)="highlight(i)" (mouseleave)="highlight(-1)"><span matListItemIcon class="dot" [style.background]="color(i)"></span><span matListItemTitle>{{ l.from }} → {{ l.to }}</span><span matListItemLine>{{ fmt(l.day) }} · {{ l.distanceNm | number:'1.0-1' }} sm</span></a> }</mat-list>
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
                @if (p.media.length) { <app-gallery [photos]="p.media" /> }</mat-card-content></mat-card> }
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
    .dot { width: 14px; height: 14px; border-radius: 50%; display: inline-block; }
    .day { margin: 12px 0; } .day h3 { border-bottom: 2px solid #e3eef9; padding-bottom: 4px; } .entry, .post { margin: 8px 0; }
    @media (max-width: 800px) { .overview { flex-direction: column; } .side { max-width: none; } }`,
})
export class TripPage {
  private api = inject(Api); private dialog = inject(MatDialog); private router = inject(Router); readonly auth = inject(AuthService);
  id = input.required<string>(); tab = input<string>();
  mapEl = viewChild<ElementRef<HTMLDivElement>>('map');
  trip = signal<Trip | null>(null); days = signal<Day[]>([]); legs = signal<Leg[]>([]); allPhotos = signal<Media[]>([]);
  overview = signal<{ stats: { distanceNm: number; underwayHours: number; engineHours: number; harbours: number; nights: number; photos: number; posts: number; costsTotal: number | null }; legs: { id: string; day: string; from: string; to: string; coordinates: number[][]; distanceNm: number }[]; stops: { name: string; lat: number; lng: number; nights: number }[]; items: { type: string; id: string; lat: number; lng: number; thumbUrl: string | null; title: string | null }[]; attribution: string } | null>(null);
  tabIndex = signal(0);
  fmt = fmtDate; fmtTime = fmtDateTime;
  private map?: L.Map; private legLayers: L.Polyline[] = [];
  color = (i: number) => DAY_COLORS[i % DAY_COLORS.length];

  constructor() { effect(() => { this.id(); this.reload(); }); effect(() => { const i = TABS.indexOf(this.tab() ?? 'overview'); this.tabIndex.set(i < 0 ? 0 : i); }); }

  reload(): void {
    const id = this.id();
    this.api.get<Trip>(`/api/trips/${id}`).subscribe(t => this.trip.set(t));
    this.api.get<Day[]>(`/api/trips/${id}/timeline`).subscribe(d => { this.days.set(d); this.allPhotos.set(d.flatMap(x => [...x.photos, ...x.posts.flatMap(p => p.media)])); });
    this.api.get<Leg[]>(`/api/trips/${id}/legs`).subscribe(l => this.legs.set(l));
    this.api.get<NonNullable<ReturnType<TripPage['overview']>>>(`/api/trips/${id}/overview`).subscribe(o => { this.overview.set(o); setTimeout(() => this.drawMap(), 50); });
  }

  private drawMap(): void {
    const el = this.mapEl()?.nativeElement; const o = this.overview();
    if (!el || !o) return;
    if (!this.map) {
      this.map = L.map(el).setView([54.4, 10.5], 8);
      baseLayers(this.map);
    }
    const map = this.map;
    map.eachLayer(l => { if (!(l instanceof L.TileLayer)) map.removeLayer(l); });
    this.legLayers = o.legs.map((l, i) => L.polyline(l.coordinates.map(c => [c[1], c[0]] as [number, number]), { color: this.color(i), weight: 4, dashArray: '8 6' }).bindTooltip(textNode(`${l.from} → ${l.to}: ${l.distanceNm} sm`)).addTo(map));
    for (const s of o.stops) L.marker([s.lat, s.lng]).bindTooltip(textNode(`${s.name} (${s.nights}×)`), { permanent: true, direction: 'right', className: 'harbour-label' }).addTo(map);
    const cluster = markerCluster({ maxClusterRadius: 40 });
    for (const it of o.items) {
      const img = document.createElement('img'); img.src = it.thumbUrl ?? ''; img.className = 'photo-marker'; img.width = 40; img.height = 40;
      const icon = it.type === 'photo' && it.thumbUrl ? L.divIcon({ html: img, iconSize: [40, 40], className: '' })
        : L.divIcon({ html: '<span class="material-symbols-outlined" style="font-size:28px;color:#1b4d7a">chat_bubble</span>', iconSize: [28, 28], className: '' });
      const m = L.marker([it.lat, it.lng], { icon });
      if (it.type === 'post') m.on('click', () => this.router.navigate(['/posts', it.id]));
      else { const div = document.createElement('div'); const big = document.createElement('img'); big.src = it.thumbUrl ?? ''; big.style.maxWidth = '200px'; div.append(big, document.createElement('br'), document.createTextNode(it.title ?? '')); m.bindPopup(div); }
      cluster.addLayer(m);
    }
    map.addLayer(cluster);
    const all = [...o.legs.flatMap(l => l.coordinates.map(c => [c[1], c[0]] as [number, number])), ...o.items.map(i => [i.lat, i.lng] as [number, number])];
    // the tab body may still be collapsed: let the container get its size before fitting the bounds
    setTimeout(() => { map.invalidateSize(); if (all.length) map.fitBounds(L.latLngBounds(all).pad(0.3), { maxZoom: 12 }); }, 150);
  }

  highlight(i: number): void { this.legLayers.forEach((l, j) => l.setStyle({ weight: j === i ? 8 : 4, opacity: i < 0 || j === i ? 1 : 0.4 })); if (i >= 0) this.map?.fitBounds(this.legLayers[i].getBounds().pad(0.5)); }
  go(i: number): void { this.router.navigate(['/trips', this.id(), TABS[i]], { replaceUrl: true }); if (i === 0) setTimeout(() => this.drawMap(), 100); }
  totalNm(): number { return this.legs().reduce((s, l) => s + (l.distanceNm ?? 0), 0); }

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
