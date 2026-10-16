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
import type { GeoJSONSource, MapMouseEvent } from 'maplibre-gl';
import type { Point as GeoPoint } from 'geojson';
import { Api, AuthService, errorMessage } from '../../core/api';
import { Harbour, HarbourMarker, NearbyPort, Wifi } from '../../core/models';
import { bounds, createMap, maplibregl, MlMap } from '../../core/map';
import { UploadZone } from '../../shared/shared';

@Component({
  imports: [FormsModule, MatButtonModule, MatIconModule, MatTableModule, MatTabsModule, TranslatePipe, UploadZone],
  template: `
    <div class="layout">
      <div #map class="map"></div>
      <aside class="panel" [class.open]="harbour()">
        @if (harbour(); as h) {
          <div class="row"><h2 class="grow">{{ h.name }}</h2>
            @if (auth.me() && h.id) { <button mat-icon-button (click)="edit(h)"><mat-icon>edit</mat-icon></button> }
            <button mat-icon-button (click)="close()"><mat-icon>close</mat-icon></button></div>
          <div class="muted">{{ h.country }} · {{ h.category }} @if (h.averageRating) { · ★ {{ h.averageRating }} }</div>
          @if (h.website) { <div><a [href]="h.website" target="_blank" rel="noreferrer">{{ h.website }}</a></div> }
          @if (h.phone) { <div>{{ 'harbour.phone' | translate }}: {{ h.phone }}</div> }
          @if (h.vhfChannel) { <div>{{ 'harbour.vhf' | translate }}: {{ h.vhfChannel }}</div> }
          @if (facilities(); as f) { <div class="chips">@for (k of f; track k) { <span class="chip">{{ k }}</span> }</div> }
          @if (h.photoUrls.length) { <div class="photo-grid">@for (p of h.photoUrls; track p) { <img [src]="p" /> }</div> }
          @if (auth.me() && h.id) { <app-upload [url]="'/api/harbours/' + h.id + '/photos'" (uploaded)="load(h.id)" /> }
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
                <tr mat-row *matRowDef="let p; columns: ['name','distanceNm','etaHours','route']" (mouseenter)="hovered = p.harbourId; showRoute(p)" (mouseleave)="hovered = null; hideRoute()"></tr>
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
              @if (auth.me() && h.id) { <button mat-button (click)="editWifi(h)"><mat-icon>add</mat-icon> WLAN</button> }
            </mat-tab>
            <mat-tab [label]="'harbour.reviews' | translate">
              @for (r of h.reviews; track r.id) { <div class="review"><b>{{ r.author }}</b> {{ '★'.repeat(r.rating) }} <span class="muted">{{ r.visitedOn }}</span><div>{{ r.text }}</div></div> }
              @if (auth.me() && h.id) { <button mat-button (click)="review(h)"><mat-icon>star</mat-icon> {{ 'harbour.addReview' | translate }}</button> }
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
  hovered: string | null = null;
  private map?: MlMap;
  private harbours = new Map<string, HarbourMarker>();
  private pinned = false;
  private prevBounds?: maplibregl.LngLatBounds;

  constructor() { effect(() => { const id = this.id(); if (id) this.load(id); else this.harbour.set(null); }); }

  async ngAfterViewInit(): Promise<void> {
    const map = await createMap(this.mapEl().nativeElement);
    this.map = map;
    map.addSource('harbours', { type: 'geojson', data: { type: 'FeatureCollection', features: [] }, cluster: true, clusterRadius: 40, clusterMaxZoom: 10 });
    map.addLayer({ id: 'harbour-clusters', type: 'circle', source: 'harbours', filter: ['has', 'point_count'], paint: { 'circle-color': '#1b4d7a', 'circle-radius': ['step', ['get', 'point_count'], 14, 10, 18, 50, 24], 'circle-opacity': 0.85 } });
    map.addLayer({ id: 'harbour-cluster-count', type: 'symbol', source: 'harbours', filter: ['has', 'point_count'], layout: { 'text-field': ['get', 'point_count_abbreviated'], 'text-size': 12, 'text-font': ['Noto Sans Regular'] }, paint: { 'text-color': '#ffffff' } });
    map.addLayer({ id: 'harbour-points', type: 'circle', source: 'harbours', filter: ['!', ['has', 'point_count']], paint: { 'circle-color': ['case', ['boolean', ['get', 'selected'], false], '#e65100', '#1976d2'], 'circle-radius': 7, 'circle-stroke-color': '#ffffff', 'circle-stroke-width': 2 } });
    map.addLayer({ id: 'harbour-labels', type: 'symbol', source: 'harbours', filter: ['!', ['has', 'point_count']], layout: { 'text-field': ['get', 'name'], 'text-size': 11, 'text-anchor': 'left', 'text-offset': [0.9, 0], 'text-font': ['Noto Sans Regular'], 'text-optional': true }, paint: { 'text-color': '#1b4d7a', 'text-halo-color': 'rgba(255,255,255,.9)', 'text-halo-width': 1.5 } });
    map.addSource('route', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    map.addLayer({ id: 'route-line', type: 'line', source: 'route', paint: { 'line-color': '#1e88e5', 'line-width': 4 } });
    map.addLayer({ id: 'route-line-dashed', type: 'line', source: 'route', filter: ['==', ['get', 'dashed'], true], paint: { 'line-color': '#ffffff', 'line-width': 2, 'line-dasharray': [2, 2] } });
    map.addLayer({ id: 'route-label', type: 'symbol', source: 'route', layout: { 'symbol-placement': 'line-center', 'text-field': ['get', 'label'], 'text-size': 12, 'text-font': ['Noto Sans Regular'] }, paint: { 'text-color': '#0d47a1', 'text-halo-color': '#ffffff', 'text-halo-width': 2 } });

    map.on('moveend', () => this.refresh());
    map.on('click', 'harbour-points', e => { const id = e.features?.[0]?.properties?.['id']; if (id) this.select(id); });
    map.on('click', 'harbour-clusters', e => { const f = e.features?.[0]; if (f) map.easeTo({ center: (f.geometry as GeoPoint).coordinates as [number, number], zoom: map.getZoom() + 2 }); });
    map.on('mouseenter', 'harbour-points', e => { map.getCanvas().style.cursor = 'pointer'; const id = e.features?.[0]?.properties?.['id']; if (id) this.hoverHarbour(id); });
    map.on('mouseleave', 'harbour-points', () => { map.getCanvas().style.cursor = ''; this.hideRoute(); });
    map.on('contextmenu', e => this.contextMenu(e));
    this.refresh();
    const h = this.harbour();
    if (h) map.easeTo({ center: [h.lng, h.lat], zoom: Math.max(map.getZoom(), 11) });
  }

  ngOnDestroy(): void { this.map?.remove(); }

  refresh(): void {
    const map = this.map; if (!map) return;
    const b = map.getBounds();
    this.api.get<HarbourMarker[]>('/api/harbours', { bbox: `${b.getWest()},${b.getSouth()},${b.getEast()},${b.getNorth()}` }).subscribe(hs => {
      for (const h of hs) this.harbours.set(h.id, h);
      this.setHarbourData();
    });
  }

  private setHarbourData(): void {
    const src = this.map?.getSource('harbours') as GeoJSONSource | undefined;
    src?.setData({ type: 'FeatureCollection', features: [...this.harbours.values()].map(h => ({ type: 'Feature', id: h.id, properties: { id: h.id, name: h.name, selected: h.id === this.harbour()?.id }, geometry: { type: 'Point', coordinates: [h.lng, h.lat] } })) });
  }

  select(id: string): void { this.router.navigate(['/harbours', id]); }

  load(id: string): void {
    this.api.get<Harbour>(`/api/harbours/${id}`).subscribe(h => {
      this.harbour.set(h);
      this.pinned = false; this.hideRoute(true);
      if (this.map) { this.map.easeTo({ center: [h.lng, h.lat] }); this.setHarbourData(); }
      this.api.get<{ ports: NearbyPort[] }>('/api/routing/nearby-ports', { harbour: id, maxNm: 60, limit: 30 }).subscribe(r => this.nearby.set(r.ports));
    });
  }

  close(): void { this.hideRoute(true); this.router.navigate(['/']); }

  private hoverHarbour(id: string): void {
    const p = this.nearby().find(n => n.harbourId === id);
    if (p) { this.hovered = id; this.showRoute(p); }
  }

  /** Legacy behaviour (05 §5.2): hover draws the route with a "<n> sm" label and fits the map if needed. */
  showRoute(p: NearbyPort): void {
    const h = this.harbour();
    if (!h || this.pinned) return;
    this.draw([[h.lng, h.lat], [p.lng, p.lat]], p.distanceNm, p.method === 'straight-line');
    if (p.method !== 'straight-line') {
      const params = h.id ? { fromHarbour: h.id, toHarbour: p.harbourId } : { from: `${h.lat},${h.lng}`, to: `${p.lat},${p.lng}` };
      this.api.get<{ distanceNm: number; geometry: { coordinates: number[][] }; method: string }>('/api/routing/route', params).subscribe(r => {
        if (this.hovered === p.harbourId) this.draw(r.geometry.coordinates.map(c => [c[0], c[1]] as [number, number]), r.distanceNm, r.method === 'straight-line');
      });
    }
  }
  private draw(coords: [number, number][], nm: number, dashed: boolean): void {
    const map = this.map; if (!map) return;
    (map.getSource('route') as GeoJSONSource).setData({ type: 'FeatureCollection', features: [{ type: 'Feature', properties: { label: `${nm} sm`, dashed }, geometry: { type: 'LineString', coordinates: coords } }] });
    const b = bounds(coords);
    const view = map.getBounds();
    if (!view.contains(b.getNorthEast()) || !view.contains(b.getSouthWest())) { this.prevBounds ??= view; map.fitBounds(b, { padding: 60, maxZoom: 12 }); }
  }
  hideRoute(force = false): void {
    if (this.pinned && !force) return;
    this.hovered = null;
    (this.map?.getSource('route') as GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection', features: [] });
    if (this.prevBounds) { const b = this.prevBounds; this.prevBounds = undefined; setTimeout(() => this.map?.fitBounds(b, { duration: 300 }), 100); }
  }
  pinRoute(p: NearbyPort): void { this.pinned = false; this.hovered = p.harbourId; this.showRoute(p); this.pinned = true; this.prevBounds = undefined; }

  private contextMenu(e: MapMouseEvent): void {
    const map = this.map; if (!map) return;
    const div = document.createElement('div');
    const popup = new maplibregl.Popup({ closeButton: false }).setLngLat(e.lngLat);
    const mk = (label: string, fn: () => void) => { const b = document.createElement('button'); b.className = 'mdc-button'; b.style.display = 'block'; b.textContent = label; b.onclick = () => { popup.remove(); fn(); }; div.appendChild(b); };
    mk('Nächste Häfen von hier', () => this.nearbyFrom(e.lngLat.lat, e.lngLat.lng));
    if (this.auth.me()) mk('Neuer Hafen', () => this.edit(null, e.lngLat.lat, e.lngLat.lng));
    popup.setDOMContent(div).addTo(map);
  }

  private nearbyFrom(lat: number, lng: number): void {
    this.api.get<{ ports: NearbyPort[] }>('/api/routing/nearby-ports', { lat, lng, maxNm: 60, limit: 30 }).subscribe(r => {
      this.harbour.set({ id: '', name: `${lat.toFixed(3)}, ${lng.toFixed(3)}`, lat, lng, category: null, country: null, website: null, description: null, phone: null, vhfChannel: null, facilities: {}, source: 'User', osmType: null, osmId: null, wifis: [], reviews: [], averageRating: null, photoUrls: [] });
      this.nearby.set(r.ports);
    });
  }

  edit(h: Harbour | null, lat?: number, lng?: number): void {
    this.dialog.open(HarbourEditDialog, { data: h ?? { lat, lng }, width: '600px' }).afterClosed().subscribe((id?: string) => {
      if (!id) return;
      this.harbours.delete(id);
      this.refresh(); this.select(id); if (this.harbour()?.id === id) this.load(id);
    });
  }
  editWifi(h: Harbour, w?: Wifi): void { this.dialog.open(WifiDialog, { data: { harbourId: h.id, wifi: w } }).afterClosed().subscribe(changed => { if (changed) this.load(h.id); }); }
  review(h: Harbour): void { this.dialog.open(ReviewDialog, { data: { harbourId: h.id } }).afterClosed().subscribe(changed => { if (changed) this.load(h.id); }); }
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
