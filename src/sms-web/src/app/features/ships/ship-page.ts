import { Component, inject, input, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatListModule } from '@angular/material/list';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, AuthService, errorMessage } from '../../core/api';
import { DeviceCreated, PrivacyZone, Ship, ShipStats, TrackingDevice, TripSummary } from '../../core/models';
import { confirm, fmtDate, fmtDateTime, UploadZone } from '../../shared/shared';
import { ShipEditDialog } from './ship-list';
import { TripEditDialog } from '../trips/trip-dialogs';
import { TaskList } from '../trips/task-list';

@Component({
  imports: [DecimalPipe, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatListModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe, UploadZone, TaskList],
  template: `
    @if (ship(); as s) {
    <div class="page">
      <div class="row"><h1 class="grow">{{ s.name }} <span class="chip" [class]="'chip ' + s.visibility.toLowerCase()">{{ 'visibility.' + s.visibility | translate }}</span></h1>
        @if (s.myRole === 'Owner') { <button mat-stroked-button (click)="edit(s)"><mat-icon>edit</mat-icon> {{ 'common.edit' | translate }}</button> }
        <a mat-stroked-button [routerLink]="['/ships', s.id, 'logbook']"><mat-icon>menu_book</mat-icon> {{ 'ship.logbook' | translate }}</a>
        @if (s.myRole) { <button mat-flat-button (click)="newTrip(s)"><mat-icon>add</mat-icon> {{ 'trip.new' | translate }}</button> }
      </div>
      <div class="grid">
        <mat-card appearance="outlined"><mat-card-content>
          @if (s.photoUrl) { <img class="photo" [src]="s.photoUrl" /> }
          @if (s.myRole === 'Owner') { <app-upload [url]="'/api/ships/' + s.id + '/photo'" (uploaded)="load()" /> }
          <dl class="dl">
            <dt>{{ 'ship.type' | translate }}</dt><dd>{{ s.type }}</dd>
            <dt>{{ 'ship.length' | translate }} / {{ 'ship.beam' | translate }} / {{ 'ship.draft' | translate }}</dt><dd>{{ s.lengthM }} / {{ s.beamM }} / {{ s.draftM }} m</dd>
            <dt>{{ 'ship.sailNumber' | translate }}</dt><dd>{{ s.sailNumber }}</dd>
            <dt>{{ 'ship.callSign' | translate }} / {{ 'ship.mmsi' | translate }}</dt><dd>{{ s.callSign }} {{ s.mmsi }}</dd>
            <dt>{{ 'ship.homeHarbour' | translate }}</dt><dd>@if (s.homeHarbourId) { <a [routerLink]="['/harbours', s.homeHarbourId]">{{ s.homeHarbourName }}</a> }</dd>
            <dt>{{ 'ship.cruisingSpeed' | translate }}</dt><dd>{{ s.cruisingSpeedKn }} kn</dd>
          </dl>
          @if (s.description) { <div class="rich" [innerHTML]="s.description"></div> }
        </mat-card-content></mat-card>

        <mat-card appearance="outlined"><mat-card-content>
          <h3>{{ 'ship.stats' | translate }}</h3>
          @if (stats(); as st) {
            <div class="stats"><div><b>{{ st.nauticalMiles | number:'1.0-1' }}</b><span>{{ 'ship.nm' | translate }}</span></div><div><b>{{ st.engineHours | number:'1.0-1' }}</b><span>{{ 'ship.engineHours' | translate }}</span></div>
              <div><b>{{ st.entries }}</b><span>{{ 'ship.entries' | translate }}</span></div><div><b>{{ st.harbours }}</b><span>{{ 'ship.harbours' | translate }}</span></div></div>
          }
          <h3>{{ 'ship.members' | translate }} @if (s.myRole === 'Owner') { <button mat-icon-button (click)="invite(s)"><mat-icon>person_add</mat-icon></button> }</h3>
          <mat-list>
            @for (m of s.members; track m.userId) {
              <mat-list-item><a matListItemTitle [routerLink]="['/u', m.userId]">{{ m.displayName }}</a><span matListItemLine>{{ 'ship.' + m.role.toLowerCase() | translate }}</span>
                @if (s.myRole === 'Owner') { <button matListItemMeta mat-icon-button (click)="removeMember(s, m.userId)"><mat-icon>delete</mat-icon></button> }</mat-list-item>
            }
          </mat-list>
        </mat-card-content></mat-card>

        <mat-card appearance="outlined" class="span2"><mat-card-content>
          <h3>{{ 'ship.trips' | translate }}</h3>
          @if (trips().length === 0) { <p class="muted">{{ 'trip.noTrips' | translate }}</p> }
          <mat-list>
            @for (t of trips(); track t.id) {
              <a mat-list-item [routerLink]="['/trips', t.id]"><span matListItemTitle>{{ t.name }}</span><span matListItemLine>{{ fmt(t.startDate) }} – {{ fmt(t.endDate) }} · {{ 'trip.status' + t.status | translate }} · {{ t.crewCount }} Crew</span></a>
            }
          </mat-list>
        </mat-card-content></mat-card>

        @if (s.myRole) { <mat-card appearance="outlined" class="span2"><mat-card-content><h3>{{ 'ship.tasks' | translate }}</h3><app-task-list [url]="'/api/ships/' + s.id + '/tasks'" /></mat-card-content></mat-card> }

        @if (s.myRole) { <mat-card appearance="outlined" class="span2"><mat-card-content>
          <h3>{{ 'tracking.devices' | translate }}</h3>
          <p class="muted">{{ 'tracking.hint' | translate }}</p>
          <mat-list>@for (d of devices(); track d.id) {
            <mat-list-item><span matListItemTitle>{{ d.name }} <span class="chip">{{ d.protocol }}</span> @if (!d.active) { <span class="chip private">revoked</span> }</span>
              <span matListItemLine>{{ d.owner }} @if (d.lastSeenAt) { · {{ 'tracking.lastSeen' | translate }} {{ fmtTime(d.lastSeenAt) }} }</span>
              @if (d.active) { <button matListItemMeta mat-icon-button (click)="revokeDevice(s, d)"><mat-icon>delete</mat-icon></button> }</mat-list-item> }</mat-list>
          <form class="row" (ngSubmit)="addDevice(s)">
            <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'common.name' | translate }}</mat-label><input matInput name="deviceName" [(ngModel)]="deviceName" /></mat-form-field>
            <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'tracking.protocol' | translate }}</mat-label><mat-select name="protocol" [(ngModel)]="deviceProtocol"><mat-option value="OsmAnd">OsmAnd / Traccar Client / GPSLogger</mat-option><mat-option value="OwnTracks">OwnTracks</mat-option><mat-option value="Json">JSON / Signal K</mat-option></mat-select></mat-form-field>
            <button mat-flat-button type="submit" [disabled]="!deviceName">{{ 'tracking.addDevice' | translate }}</button></form>
          @if (created(); as c) { <div class="token"><b>{{ 'tracking.token' | translate }}:</b> <code>{{ c.token }}</code><br><b>OsmAnd:</b> <code>{{ c.osmAndUrl }}</code><br><b>OwnTracks:</b> <code>{{ c.ownTracksUrl }}</code><br><b>JSON:</b> <code>{{ c.jsonUrl }}</code><p class="muted">{{ c.hint }}</p></div> }

          @if (s.myRole === 'Owner') {
            <h3>{{ 'tracking.privacyZones' | translate }}</h3>
            <mat-list>@for (z of zones(); track z.id) { <mat-list-item><span matListItemTitle>{{ z.name }}</span><span matListItemLine>{{ z.lat }}, {{ z.lng }} · {{ z.radiusM }} m</span><button matListItemMeta mat-icon-button (click)="removeZone(s, z)"><mat-icon>delete</mat-icon></button></mat-list-item> }</mat-list>
            <form class="row" (ngSubmit)="addZone(s)">
              <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'common.name' | translate }}</mat-label><input matInput name="zName" [(ngModel)]="zone.name" /></mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Lat</mat-label><input matInput type="number" step="0.0001" name="zLat" [(ngModel)]="zone.lat" /></mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Lng</mat-label><input matInput type="number" step="0.0001" name="zLng" [(ngModel)]="zone.lng" /></mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>m</mat-label><input matInput type="number" name="zR" [(ngModel)]="zone.radiusM" /></mat-form-field>
              <button mat-flat-button type="submit" [disabled]="!zone.name">{{ 'common.new' | translate }}</button></form>
          }
        </mat-card-content></mat-card> }
      </div>
    </div>}`,
  styles: `
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; } .span2 { grid-column: 1 / -1; }
    .photo { width: 100%; max-height: 320px; object-fit: cover; border-radius: 8px; }
    .dl { display: grid; grid-template-columns: auto 1fr; gap: 4px 12px; } dt { color: #666; } dd { margin: 0; }
    .token { background: #f3f3f3; padding: 8px; border-radius: 6px; font-size: 13px; word-break: break-all; }
    .stats { display: flex; gap: 20px; flex-wrap: wrap; } .stats div { display: flex; flex-direction: column; } .stats b { font-size: 22px; } .stats span { font-size: 12px; color: #666; }
    @media (max-width: 800px) { .grid { grid-template-columns: 1fr; } }`,
})
export class ShipPage {
  private api = inject(Api); private dialog = inject(MatDialog); readonly auth = inject(AuthService);
  id = input.required<string>();
  ship = signal<Ship | null>(null); stats = signal<ShipStats | null>(null); trips = signal<TripSummary[]>([]);
  fmt = fmtDate; fmtTime = fmtDateTime;
  devices = signal<TrackingDevice[]>([]); zones = signal<PrivacyZone[]>([]); created = signal<DeviceCreated | null>(null);
  deviceName = ''; deviceProtocol: 'OsmAnd' | 'OwnTracks' | 'Json' = 'OsmAnd'; zone = { name: '', lat: 54.4, lng: 10.2, radiusM: 500 };
  ngOnInit(): void { this.load(); }
  loadTracking(s: Ship): void {
    if (!s.myRole) return;
    this.api.get<TrackingDevice[]>(`/api/ships/${s.id}/tracking-devices`).subscribe(d => this.devices.set(d));
    this.api.get<PrivacyZone[]>(`/api/ships/${s.id}/privacy-zones`).subscribe(z => this.zones.set(z));
  }
  addDevice(s: Ship): void { this.api.post<DeviceCreated>(`/api/ships/${s.id}/tracking-devices`, { name: this.deviceName, protocol: this.deviceProtocol }).subscribe(c => { this.created.set(c); this.deviceName = ''; this.loadTracking(s); }); }
  async revokeDevice(s: Ship, d: TrackingDevice): Promise<void> { if (await confirm(this.dialog, `${d.name} widerrufen?`)) this.api.delete(`/api/ships/${s.id}/tracking-devices/${d.id}`).subscribe(() => this.loadTracking(s)); }
  addZone(s: Ship): void { this.api.post(`/api/ships/${s.id}/privacy-zones`, this.zone).subscribe(() => { this.zone.name = ''; this.loadTracking(s); }); }
  removeZone(s: Ship, z: PrivacyZone): void { this.api.delete(`/api/ships/${s.id}/privacy-zones/${z.id}`).subscribe(() => this.loadTracking(s)); }
  load(): void {
    this.api.get<Ship>(`/api/ships/${this.id()}`).subscribe(s => { this.ship.set(s); this.loadTracking(s); });
    this.api.get<ShipStats>(`/api/ships/${this.id()}/stats`).subscribe(s => this.stats.set(s));
    this.api.get<TripSummary[]>(`/api/ships/${this.id()}/trips`).subscribe(t => this.trips.set(t));
  }
  edit(s: Ship): void { this.dialog.open(ShipEditDialog, { data: s, width: '700px' }).afterClosed().subscribe(ok => ok && this.load()); }
  newTrip(s: Ship): void { this.dialog.open(TripEditDialog, { data: { shipId: s.id }, width: '700px' }).afterClosed().subscribe(ok => ok && this.load()); }
  invite(s: Ship): void { this.dialog.open(MemberDialog, { data: s }).afterClosed().subscribe(ok => ok && this.load()); }
  async removeMember(s: Ship, userId: string): Promise<void> {
    if (!(await confirm(this.dialog, 'Mitglied entfernen?'))) return;
    this.api.delete(`/api/ships/${s.id}/members/${userId}`).subscribe({ next: () => this.load(), error: e => alert(errorMessage(e)) });
  }
}

@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatAutocompleteModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'ship.invite' | translate }}</h2>
    <mat-dialog-content class="form-grid">
      <mat-form-field appearance="outline" class="full"><mat-label>Benutzer suchen</mat-label>
        <input matInput [(ngModel)]="query" (ngModelChange)="search($event)" [matAutocomplete]="auto" />
        <mat-autocomplete #auto="matAutocomplete" (optionSelected)="userId = $event.option.value.userId; query = $event.option.value.displayName">
          @for (u of hits(); track u.userId) { <mat-option [value]="u">{{ u.displayName }}</mat-option> }
        </mat-autocomplete></mat-form-field>
      <mat-form-field appearance="outline" class="full"><mat-label>oder E-Mail einladen</mat-label><input matInput type="email" [(ngModel)]="email" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Rolle</mat-label><mat-select [(ngModel)]="role"><mat-option value="Skipper">{{ 'ship.skipper' | translate }}</mat-option><mat-option value="Owner">{{ 'ship.owner' | translate }}</mat-option></mat-select></mat-form-field>
      @if (link()) { <p class="full">{{ 'trip.inviteLink' | translate }}: <code>{{ link() }}</code></p> }
      @if (error()) { <div class="error full">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button><button mat-flat-button (click)="save()">{{ 'ship.invite' | translate }}</button></mat-dialog-actions>`,
})
export class MemberDialog {
  data = inject<Ship>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api);
  query = ''; userId = ''; email = ''; role: 'Owner' | 'Skipper' = 'Skipper'; hits = signal<{ userId: string; displayName: string }[]>([]); link = signal(''); error = signal('');
  search(q: string): void { if (typeof q === 'string' && q.length >= 2) this.api.get<{ userId: string; displayName: string }[]>('/api/users', { q }).subscribe(h => this.hits.set(h)); }
  save(): void {
    if (this.userId) this.api.post(`/api/ships/${this.data.id}/members`, { userId: this.userId, role: this.role }).subscribe({ next: () => this.ref.close(true), error: e => this.error.set(errorMessage(e)) });
    else if (this.email) this.api.post<{ link: string }>(`/api/ships/${this.data.id}/invitations`, { email: this.email, role: this.role }).subscribe({ next: r => this.link.set(r.link), error: e => this.error.set(errorMessage(e)) });
  }
}
