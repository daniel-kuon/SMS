import { Component, computed, effect, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';
import * as L from 'leaflet';
import { baseLayers, markerCluster } from '../../core/leaflet-setup';
import { Api, AuthService, errorMessage } from '../../core/api';
import { Harbour, HarbourMarker, NearbyPort, Wifi } from '../../core/models';
import { textNode, UploadZone } from '../../shared/shared';

const Legacy = { lat: 54.40774166820069, lng: 10.523529052734373, zoom: 9 };

@Component({
  imports: [FormsModule, MatButtonModule, MatIconModule, MatTableModule, MatTabsModule, TranslatePipe, UploadZone],
  template: `
    <div class="layout">
      <div #map class="map"></div>
      <aside class="panel" [class.open]="harbour()">
        @if (harbour(); as h) {
          <div class="row"><h2 class="grow">{{ h.name }}</h2>
            @if (auth.me()) { <button mat-icon-button (click)="edit(h)"><mat-icon>edit</mat-icon></button> }
            <button mat-icon-button (click)="close()"><mat-icon>close</mat-icon></button></div>
          <div class="muted">{{ h.country }} · {{ h.category }} @if (h.averageRating) { · ★ {{ h.averageRating }} }</div>
          @if (h.website) { <div><a [href]="h.website" target="_blank" rel="noreferrer">{{ h.website }}</a></div> }
          @if (h.phone) { <div>{{ 'harbour.phone' | translate }}: {{ h.phone }}</div> }
          @if (h.vhfChannel) { <div>{{ 'harbour.vhf' | translate }}: {{ h.vhfChannel }}</div> }
          @if (facilities(); as f) { <div class="chips">@for (k of f; track k) { <span class="chip">{{ k }}</span> }</div> }
          @if (h.photoUrls.length) { <div class="photo-grid">@for (p of h.photoUrls; track p) { <img [src]="p" /> }</div> }
          @if (auth.me()) { <app-upload [url]="'/api/harbours/' + h.id + '/photos'" (uploaded)="load(h.id)" /> }
          @if (h.description) { <div class="rich" [innerHTML]="h.description"></div> }

          <mat-tab-group>
            <mat-tab [label]="'harbour.nearby' | translate">
              <p class="disclaimer">{{ 'harbour.disclaimer' | translate }}</p>
              <table mat-table [dataSource]="nearby()" class="full-width">
                <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>{{ 'common.name' | translate }}</th><td mat-cell *matCellDef="let p"><a href="" (click)="$event.preventDefault(); select(p.harbourId)">{{ p.name }}</a></td></ng-container>
                <ng-container matColumnDef="distanceNm"><th mat-header-cell *matHeaderCellDef>{{ 'harbour.distance' | translate }}</th><td mat-cell *matCellDef="let p">{{ p.distanceNm }} sm</td></ng-container>
                <ng-container matColumnDef="etaHours"><th mat-header-cell *matHeaderCellDef>{{ 'harbour.eta' | translate }}</th><td mat-cell *matCellDef="let p">{{ p.etaHours }} h</td></ng-container>
                <ng-container matColumnDef="route"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let p"><button mat-button (click)="pinRoute(p)">{{ 'harbour.showRoute' | translate }}</button></td></ng-container>
                <tr mat-header-row *matHeaderRowDef="['name','distanceNm','etaHours','route']"></tr>
                <tr mat-row *matRowDef="let p; columns: ['name','distanceNm','etaHours','route']" (mouseenter)="showRoute(p)" (mouseleave)="hideRoute()"></tr>
              </table>
            </mat-tab>
            <mat-tab [label]="'harbour.wifi' | translate">
              @if (!auth.me()) { <p class="muted">{{ 'harbour.wifiLoginHint' | translate }}</p> }
              <table mat-table [dataSource]="h.wifis" class="full-width">
                <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>{{ 'common.name' | translate }}</th><td mat-cell *matCellDef="let w">{{ w.name }}</td></ng-container>
                <ng-container matColumnDef="password"><th mat-header-cell *matHeaderCellDef>{{ 'harbour.wifiPassword' | translate }}</th><td mat-cell *matCellDef="let w">{{ w.password ?? '•••' }}</td></ng-container>
                <ng-container matColumnDef="speed"><th mat-header-cell *matHeaderCellDef>{{ 'harbour.speed' | translate }}</th><td mat-cell *matCellDef="let w">{{ '▂▄▆█'.slice(0, w.speed + 1) }} @if (w.free) { · {{ 'harbour.free' | translate }} }</td></ng-container>
                <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let w">@if (auth.me()) { <button mat-icon-button (click)="editWifi(h, w)"><mat-icon>edit</mat-icon></button> }</td></ng-container>
                <tr mat-header-row *matHeaderRowDef="['name','password','speed','actions']"></tr>
                <tr mat-row *matRowDef="let w; columns: ['name','password','speed','actions']"></tr>
              </table>
              @if (auth.me()) { <button mat-button (click)="editWifi(h)"><mat-icon>add</mat-icon> WLAN</button> }
            </mat-tab>
            <mat-tab [label]="'harbour.reviews' | translate">
              @for (r of h.reviews; track r.id) { <div class="review"><b>{{ r.author }}</b> {{ '★'.repeat(r.rating) }} <span class="muted">{{ r.visitedOn }}</span><div>{{ r.text }}</div></div> }
              @if (auth.me()) { <button mat-button (click)="review(h)"><mat-icon>star</mat-icon> {{ 'harbour.addReview' | translate }}</button> }
            </mat-tab>
          </mat-tab-group>
        }
      </aside>
    </div>`,
  styles: `
    .layout { flex: 1; display: flex; position: relative; min-height: calc(100vh - 64px - 40px); }
    .map { flex: 1; min-height: 60vh; }
    .panel { width: 0; overflow: hidden; transition: width .2s; background: white; box-shadow: -2px 0 8px rgba(0,0,0,.15); }
    .panel.open { width: 420px; max-width: 100vw; padding: 12px; overflow-y: auto; }
    .panel h2 { margin: 0; font-size: 20px; }
    .review { padding: 6px 0; border-bottom: 1px solid #eee; }
    .full-width { width: 100%; }
    @media (max-width: 800px) { .layout { flex-direction: column; } .panel.open { width: 100%; } }
  `,
})
export class HarbourMapPage {
  readonly auth = inject(AuthService);
  private api = inject(Api);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  id = input<string>();
  mapEl = viewChild.required<ElementRef<HTMLDivElement>>('map');
  harbour = signal<Harbour | null>(null);
  nearby = signal<NearbyPort[]>([]);
  facilities = computed(() => { const f = this.harbour()?.facilities; return f ? Object.keys(f).filter(k => f[k]) : null; });
  private map!: L.Map;
  private cluster = markerCluster({ disableClusteringAtZoom: 11, maxClusterRadius: 40 });
  private markers = new Map<string, L.Marker>();
  private route?: L.Polyline;
  private pinned = false;
  private prevBounds?: L.LatLngBounds;

  constructor() { effect(() => { const id = this.id(); if (id) this.load(id); else { this.harbour.set(null); } }); }

  ngAfterViewInit(): void {
    this.map = L.map(this.mapEl().nativeElement).setView([Legacy.lat, Legacy.lng], Legacy.zoom);
    baseLayers(this.map);
    this.map.addLayer(this.cluster);
    this.map.on('moveend', () => this.refresh());
    this.map.on('contextmenu', e => this.contextMenu(e));
    this.refresh();
    setTimeout(() => this.map.invalidateSize(), 100);
  }

  refresh(): void {
    const b = this.map.getBounds();
    this.api.get<HarbourMarker[]>('/api/harbours', { bbox: `${b.getWest()},${b.getSouth()},${b.getEast()},${b.getNorth()}` }).subscribe(hs => {
      for (const h of hs) {
        if (this.markers.has(h.id)) continue;
        const m = L.marker([h.lat, h.lng], { title: h.name }).bindTooltip(textNode(h.name), { permanent: true, direction: 'right', className: 'harbour-label', offset: [10, -8] });
        m.on('click', () => this.select(h.id));
        m.on('mouseover', () => this.hoverHarbour(h.id));
        m.on('mouseout', () => this.hideRoute());
        this.markers.set(h.id, m);
        this.cluster.addLayer(m);
      }
    });
  }

  select(id: string): void { this.router.navigate(['/harbours', id]); }

  load(id: string): void {
    this.api.get<Harbour>(`/api/harbours/${id}`).subscribe(h => {
      this.harbour.set(h);
      this.pinned = false; this.hideRoute(true);
      if (this.map) this.map.panTo([h.lat, h.lng]);
      this.api.get<{ ports: NearbyPort[] }>('/api/routing/nearby-ports', { harbour: id, maxNm: 60, limit: 30 }).subscribe(r => this.nearby.set(r.ports));
    });
  }

  close(): void { this.hideRoute(true); this.router.navigate(['/']); }

  private hoverHarbour(id: string): void {
    const p = this.nearby().find(n => n.harbourId === id);
    if (p) this.showRoute(p);
  }

  /** Legacy behaviour (05 §5.2): hover draws the route with a "<n> sm" label and fits the map if needed. */
  showRoute(p: NearbyPort): void {
    const h = this.harbour();
    if (!h || this.pinned) return;
    this.route?.remove();
    this.route = L.polyline([[h.lat, h.lng], [p.lat, p.lng]], { color: '#1e88e5', weight: 4, dashArray: '8 6' })
      .bindTooltip(textNode(`${p.distanceNm} sm`), { permanent: true, direction: 'center', className: 'harbour-label' }).addTo(this.map);
    const bounds = this.route.getBounds();
    if (!this.map.getBounds().contains(bounds)) { this.prevBounds ??= this.map.getBounds(); this.map.fitBounds(bounds.pad(0.3)); }
  }
  hideRoute(force = false): void {
    if (this.pinned && !force) return;
    this.route?.remove(); this.route = undefined;
    if (this.prevBounds) { const b = this.prevBounds; this.prevBounds = undefined; setTimeout(() => this.map.fitBounds(b), 100); }
  }
  pinRoute(p: NearbyPort): void { this.pinned = false; this.showRoute(p); this.pinned = true; this.prevBounds = undefined; }

  private contextMenu(e: L.LeafletMouseEvent): void {
    const items: [string, () => void][] = [['harbour.nearbyHere', () => this.nearbyFrom(e.latlng)]];
    if (this.auth.me()) items.push(['harbour.newHarbour', () => this.edit(null, e.latlng)]);
    const popup = L.popup().setLatLng(e.latlng).setContent(() => {
      const div = document.createElement('div');
      for (const [key, fn] of items) {
        const b = document.createElement('button'); b.className = 'mdc-button'; b.style.display = 'block'; b.textContent = key === 'harbour.nearbyHere' ? 'Nächste Häfen von hier' : 'Neuer Hafen';
        b.onclick = () => { this.map.closePopup(); fn(); }; div.appendChild(b);
      }
      return div;
    }).openOn(this.map);
    void popup;
  }

  private nearbyFrom(ll: L.LatLng): void {
    this.api.get<{ ports: NearbyPort[] }>('/api/routing/nearby-ports', { lat: ll.lat, lng: ll.lng, maxNm: 60, limit: 30 }).subscribe(r => {
      this.harbour.set({ id: '', name: `${ll.lat.toFixed(3)}, ${ll.lng.toFixed(3)}`, lat: ll.lat, lng: ll.lng, category: null, country: null, website: null, description: null, phone: null, vhfChannel: null, facilities: {}, source: 'User', osmType: null, osmId: null, wifis: [], reviews: [], averageRating: null, photoUrls: [] });
      this.nearby.set(r.ports);
    });
  }

  edit(h: Harbour | null, at?: L.LatLng): void {
    this.dialog.open(HarbourEditDialog, { data: h ?? { lat: at?.lat, lng: at?.lng }, width: '600px' }).afterClosed().subscribe((id?: string) => {
      if (!id) return;
      this.markers.get(id)?.remove(); this.markers.delete(id);
      this.refresh(); this.select(id); if (this.harbour()?.id === id) this.load(id);
    });
  }
  editWifi(h: Harbour, w?: Wifi): void {
    this.dialog.open(WifiDialog, { data: { harbourId: h.id, wifi: w } }).afterClosed().subscribe(changed => { if (changed) this.load(h.id); });
  }
  review(h: Harbour): void {
    this.dialog.open(ReviewDialog, { data: { harbourId: h.id } }).afterClosed().subscribe(changed => { if (changed) this.load(h.id); });
  }
}

@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatCheckboxModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ data.id ? data.name : ('harbour.newHarbour' | translate) }}</h2>
    <mat-dialog-content class="form-grid">
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'common.name' | translate }}</mat-label><input matInput [(ngModel)]="m.name" required /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Lat</mat-label><input matInput type="number" step="0.00001" [(ngModel)]="m.lat" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Lng</mat-label><input matInput type="number" step="0.00001" [(ngModel)]="m.lng" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Land</mat-label><input matInput [(ngModel)]="m.country" maxlength="2" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Kategorie</mat-label><input matInput [(ngModel)]="m.category" /></mat-form-field>
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'harbour.website' | translate }}</mat-label><input matInput [(ngModel)]="m.website" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'harbour.phone' | translate }}</mat-label><input matInput [(ngModel)]="m.phone" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'harbour.vhf' | translate }}</mat-label><input matInput [(ngModel)]="m.vhfChannel" /></mat-form-field>
      <div class="full chips">@for (f of facilityKeys; track f) { <mat-checkbox [(ngModel)]="m.facilities[f]">{{ f }}</mat-checkbox> }</div>
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'common.description' | translate }} (HTML)</mat-label><textarea matInput rows="5" [(ngModel)]="m.description"></textarea></mat-form-field>
      @if (error()) { <div class="error full">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
})
export class HarbourEditDialog {
  data = inject<Partial<Harbour>>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api);
  facilityKeys = ['fuel', 'drinking_water', 'power_supply', 'toilets', 'shower', 'slipway', 'internet_access', 'restaurant', 'supermarket'];
  m = { name: this.data.name ?? '', lat: this.data.lat ?? 54.4, lng: this.data.lng ?? 10.5, country: this.data.country ?? '', category: this.data.category ?? 'marina', website: this.data.website ?? '', phone: this.data.phone ?? '', vhfChannel: this.data.vhfChannel ?? '', description: this.data.description ?? '', facilities: { ...(this.data.facilities ?? {}) } as Record<string, boolean> };
  error = signal('');
  save(): void {
    const req = this.data.id ? this.api.put<{ id?: string }>(`/api/harbours/${this.data.id}`, this.m) : this.api.post<{ id?: string }>('/api/harbours', this.m);
    req.subscribe({ next: r => this.ref.close(r?.id ?? this.data.id), error: e => this.error.set(errorMessage(e)) });
  }
}

@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatCheckboxModule, MatSelectModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>WLAN</h2>
    <mat-dialog-content class="form-grid">
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'common.name' | translate }}</mat-label><input matInput [(ngModel)]="m.name" required /></mat-form-field>
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'harbour.wifiPassword' | translate }}</mat-label><input matInput [(ngModel)]="m.password" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'harbour.speed' | translate }}</mat-label><mat-select [(ngModel)]="m.speed">@for (s of [0,1,2,3]; track s) { <mat-option [value]="s">{{ '▂▄▆█'.slice(0, s + 1) }}</mat-option> }</mat-select></mat-form-field>
      <mat-checkbox [(ngModel)]="m.free">{{ 'harbour.free' | translate }}</mat-checkbox>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (data.wifi) { <button mat-button color="warn" (click)="remove()">{{ 'common.delete' | translate }}</button> }
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
})
export class WifiDialog {
  data = inject<{ harbourId: string; wifi?: Wifi }>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api);
  m = { name: this.data.wifi?.name ?? '', password: this.data.wifi?.password ?? '', speed: this.data.wifi?.speed ?? 2, free: this.data.wifi?.free ?? true };
  save(): void { (this.data.wifi ? this.api.put(`/api/wifis/${this.data.wifi.id}`, this.m) : this.api.post(`/api/harbours/${this.data.harbourId}/wifis`, this.m)).subscribe(() => this.ref.close(true)); }
  remove(): void { this.api.delete(`/api/wifis/${this.data.wifi!.id}`).subscribe(() => this.ref.close(true)); }
}

@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'harbour.addReview' | translate }}</h2>
    <mat-dialog-content class="form-grid">
      <mat-form-field appearance="outline"><mat-label>★</mat-label><mat-select [(ngModel)]="m.rating">@for (s of [1,2,3,4,5]; track s) { <mat-option [value]="s">{{ '★'.repeat(s) }}</mat-option> }</mat-select></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'common.date' | translate }}</mat-label><input matInput type="date" [(ngModel)]="m.visitedOn" /></mat-form-field>
      <mat-form-field appearance="outline" class="full"><mat-label>Text</mat-label><textarea matInput rows="4" [(ngModel)]="m.text"></textarea></mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
})
export class ReviewDialog {
  data = inject<{ harbourId: string }>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api);
  m = { rating: 4, text: '', visitedOn: '' };
  save(): void { this.api.post(`/api/harbours/${this.data.harbourId}/reviews`, { ...this.m, visitedOn: this.m.visitedOn || null }).subscribe(() => this.ref.close(true)); }
}
