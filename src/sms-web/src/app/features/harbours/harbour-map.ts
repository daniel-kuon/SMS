import { Component, computed, effect, ElementRef, inject, input, signal, untracked, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { formatPosition } from '../../core/seamark-icons';
import { fmtDate } from '../../shared/shared';
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
import { Harbour, HarbourMarker, HarbourService, NearbyPort, RouteWarning, routeWarningText, Wifi } from '../../core/models';
import { Location } from '@angular/common';
import { createMap, formatView, maplibregl, MlMap, POI_ICON, poiAt, rowValueEl, ServiceSpot, setPoiFocus, setServiceActive, setServiceHighlight, syncViewWithUrl, textEl, urlViewOptions, viewFromUrl } from '../../core/map';
import { haversineM } from '../../core/track-playback';
import { formatServiceDistance } from '../../core/service-distance';
import { googleRouteUrl, PlaceRow, placeRows, ServicePlace } from '../../core/service-place';
import { appI18n } from '../../core/site';
import { TranslateService } from '@ngx-translate/core';
import { UploadZone } from '../../shared/shared';
import { loadForecast, loadTides, TideForecast, WeatherCard, WeatherDialog, WeatherForecast } from '../../core/weather';
import { ShipContextService } from '../../core/ship-context';
import { applyDepthShading, applyHazardFilter } from '../../core/map';
/** Show only the places up to this distance when there are any; otherwise the ones up to the 5 km that the API returns. */
const NEAR_RADIUS_M = 3000;

/** Service glyphs not covered by the POI icons (Material Symbols). */
const SERVICE_ICON: Record<string, string> = { power_supply: 'electrical_services', sanitary_dump_station: 'plumbing', waste_disposal: 'delete', internet_access: 'wifi', bus: 'directions_bus', tram: 'tram', train: 'train', ferry: 'directions_boat', chandlery: 'sailing', slipway: 'rowing' };
const straightNm =(a: { lat: number; lng: number }, b: { lat: number; lng: number }) => Math.round(haversineM(a.lat, a.lng, b.lat, b.lng) / 185.2) / 10;

@Component({
  imports: [FormsModule, MatButtonModule, MatIconModule, MatTableModule, MatTabsModule, TranslatePipe, UploadZone, MatListModule, RouterLink, WeatherCard, MatSelectModule, MatFormFieldModule, MatInputModule],
  template: `
    <div class="layout">
      <div #map class="map"></div>
      <div class="ship-ctx">
        <mat-icon inline>sailing</mat-icon>
        @if (shipCtx.myShips().length) {
          <select [ngModel]="shipCtx.ship()?.id ?? ''" (ngModelChange)="pickShip($event)" [attr.aria-label]="'ship.forMap' | translate">
            <option value="">{{ 'ship.noShipContext' | translate }}</option>
            @for (s of shipCtx.myShips(); track s.id) { <option [value]="s.id">{{ s.name }}</option> }
          </select>
        }
        <label>{{ 'ship.draft' | translate }} <input type="number" step="0.1" min="0" [ngModel]="shipCtx.draftM()" (ngModelChange)="shipCtx.manual($event === '' ? null : +$event, shipCtx.airDraftM())" /></label>
        <label>{{ 'ship.airDraft' | translate }} <input type="number" step="0.1" min="0" [ngModel]="shipCtx.airDraftM()" (ngModelChange)="shipCtx.manual(shipCtx.draftM(), $event === '' ? null : +$event)" /></label>
        @if (shipCtx.draftM() !== null) { <span class="muted small">{{ 'ship.hazardsHidden' | translate }}</span> }
      </div>
      <aside class="panel" [class.open]="harbour()">
        @if (harbour(); as h) {
          <div class="row"><h2 class="grow">{{ h.name }}</h2>
            @if (auth.me() && h.id) { <button mat-icon-button (click)="edit(h)"><mat-icon>edit</mat-icon></button> }
            <button mat-icon-button (click)="close()"><mat-icon>close</mat-icon></button></div>
          <div class="muted">{{ h.country }} · {{ h.category }} @if (h.averageRating) { · ★ {{ h.averageRating }} }</div>
          <div class="links">
            @if (h.website) { <a mat-stroked-button [href]="h.website" target="_blank" rel="noreferrer"><mat-icon>language</mat-icon> {{ 'harbour.website' | translate }}</a> }
            <a mat-stroked-button [href]="skipperGuideUrl(h)" target="_blank" rel="noreferrer"><mat-icon>menu_book</mat-icon> SkipperGuide</a>
            @if (h.osmId) { <a mat-stroked-button [href]="'https://www.openstreetmap.org/' + h.osmType + '/' + h.osmId" target="_blank" rel="noreferrer"><mat-icon>public</mat-icon> OSM</a> }
            <span class="muted pos">{{ position(h) }}</span>
          </div>
          @if (h.phone) { <div>{{ 'harbour.phone' | translate }}: {{ h.phone }}</div> }
          @if (h.vhfChannel) { <div>{{ 'harbour.vhf' | translate }}: {{ h.vhfChannel }}</div> }
          @if (services().length) {
            <div class="services" [attr.aria-label]="'harbour.services' | translate">@for (s of services(); track s.kind) {
              <span class="chip svc" role="button" tabindex="0" [class.active]="activeService() === s.kind" (click)="toggleService(s)" (keydown.enter)="toggleService(s)" [title]="('harbour.svc.' + s.kind | translate) + (s.name ? ': ' + s.name : '')"><mat-icon inline>{{ serviceIcon(s.kind) }}</mat-icon> {{ distance(s.distanceM) }}</span>
            }</div>
            @if (places().length) {
              <h4 class="places-head"><mat-icon inline>{{ serviceIcon(activeService() ?? '') }}</mat-icon> {{ placesHead() }}</h4>
              <ul class="places">@for (p of places(); track p.id; let i = $index) {
                <li [id]="'svc-place-' + i" [class.sel]="selected() === i" (mouseenter)="focusPlace(i)" (mouseleave)="focusPlace(null)" (click)="selectPlace(i)">
                  <div><b>{{ p.name }}</b> <span class="muted">· {{ p.dist }}</span></div>
                  @for (r of p.rows; track r.label) { <div class="muted row">{{ r.label }}: @if (r.href) { <a [href]="r.href" target="_blank" rel="noopener" (click)="$event.stopPropagation()">{{ r.value }}</a> } @else { {{ r.value }} } @if (r.badge; as b) { <span class="badge" [class.open]="b.open">{{ b.text }}</span> }</div> }
                  <a [href]="p.route" target="_blank" rel="noopener" (click)="$event.stopPropagation()"><mat-icon inline>directions_walk</mat-icon> {{ 'harbour.googleRoute' | translate }}</a>
                </li> }</ul>
            } @else if (placesEmpty()) { <p class="muted">{{ 'harbour.noPlaces' | translate }}</p> }
          }
          @if (h.photoUrls.length) { <div class="photo-grid">@for (p of h.photoUrls; track p) { <img [src]="p" /> }</div> }
          @if (auth.me() && h.id) { <app-upload [url]="'/api/harbours/' + h.id + '/photos'" (uploaded)="load(h.id)" /> }
          @if (h.description) { <div class="rich" [innerHTML]="h.description"></div> }

          <mat-tab-group>
            <mat-tab [label]="'harbour.nearby' | translate">
              <p class="disclaimer">{{ 'harbour.disclaimer' | translate }}</p>
              <table mat-table [dataSource]="nearby()" class="full-width">
                <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>{{ 'common.name' | translate }}</th><td mat-cell *matCellDef="let p"><a href="" (click)="$event.preventDefault(); select(p.harbourId, true)">{{ p.name }}</a></td></ng-container>
                <ng-container matColumnDef="distanceNm"><th mat-header-cell *matHeaderCellDef>{{ 'harbour.distance' | translate }}</th><td mat-cell *matCellDef="let p">{{ p.distanceNm }} sm</td></ng-container>
                <ng-container matColumnDef="etaHours"><th mat-header-cell *matHeaderCellDef>{{ 'harbour.eta' | translate }}</th><td mat-cell *matCellDef="let p">{{ p.etaHours }} h</td></ng-container>
                <ng-container matColumnDef="route"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let p"><button mat-button (click)="pinRoute(p)">{{ 'harbour.showRoute' | translate }}</button></td></ng-container>
                <tr mat-header-row *matHeaderRowDef="['name','distanceNm','etaHours','route']"></tr>
                <tr mat-row *matRowDef="let p; columns: ['name','distanceNm','etaHours','route']" (mouseenter)="hovered = p.harbourId; showRoute(p)" (mouseleave)="hovered = null; hideRoute()"></tr>
              </table>
            </mat-tab>
            <mat-tab [label]="'weather.title' | translate">
              <app-weather-card [forecast]="weather()" [error]="weatherError()" [tides]="tides()" />
            </mat-tab>
            <mat-tab [label]="'harbour.ships' | translate">
              @if (visits().length === 0) { <p class="muted">{{ 'harbour.noVisits' | translate }}</p> }
              <mat-list>@for (v of visits(); track v.shipId) {
                <a mat-list-item [routerLink]="['/ships', v.shipId]"><mat-icon matListItemIcon>sailing</mat-icon><span matListItemTitle>{{ v.name }}</span>
                  <span matListItemLine>{{ v.visits }}× · {{ 'harbour.lastVisit' | translate }} {{ fmt(v.lastVisit) }} @if (v.lastTripName) { · {{ v.lastTripName }} }</span></a> }</mat-list>
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
    .layout { flex: 1; display: flex; position: relative; height: calc(100dvh - 64px - 40px); min-height: 420px; } /* fixed height: a tall panel scrolls instead of stretching the map past the viewport (fitBounds then placed places off-screen) */
    .map { flex: 1; min-height: 60vh; }
    .panel { width: 0; overflow: hidden; transition: width .2s; background: white; box-shadow: -2px 0 8px rgba(0,0,0,.15); }
    .panel.open { width: 420px; max-width: 100vw; padding: 12px; overflow-y: auto; }
    .panel h2 { margin: 0; font-size: 20px; } .links { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; margin: 6px 0; } .links .pos { font-size: 12px; }
    .review { padding: 6px 0; border-bottom: 1px solid #eee; }
    .ship-ctx { position: absolute; left: 50px; top: 10px; background: rgba(255,255,255,.95); border-radius: 4px; box-shadow: 0 1px 4px rgba(0,0,0,.3); padding: 4px 8px; display: flex; gap: 8px; align-items: center; font-size: 12px; z-index: 2; }
    .ship-ctx input { width: 44px; } .ship-ctx select { max-width: 140px; } .ship-ctx .small { font-size: 11px; }
    .full-width { width: 100%; }
    .services { display: flex; gap: 6px; flex-wrap: wrap; margin: 6px 0; } .svc { display: inline-flex; align-items: center; gap: 2px; cursor: pointer; } .svc.active { outline: 2px solid #e65100; }
    .places-head { margin: 8px 0 4px; font-size: 14px; font-weight: 600; } .places-head mat-icon { vertical-align: middle; }
    .places { list-style: none; margin: 0 0 8px; padding: 0; max-height: 40vh; overflow-y: auto; font-size: 13px; } .places li { padding: 6px 8px; border-bottom: 1px solid #e0e0e0; cursor: pointer; } .places li:hover, .places li.sel { background: #fff3e0; } .places li.sel { border-left: 3px solid #d50000; } .places .row { white-space: pre-line; } .places .badge { font-weight: 600; white-space: nowrap; color: #c62828; } .places .badge.open { color: #2e7d32; } .places a mat-icon { font-size: 14px; vertical-align: middle; } .svc mat-icon { font-size: 16px; }
    @media (max-width: 800px) { .layout { flex-direction: column; height: auto; } .map { height: 60vh; flex: none; } .panel.open { width: 100%; } }
  `,
})
export class HarbourMapPage {
  readonly auth = inject(AuthService);
  readonly shipCtx = inject(ShipContextService);
  private api = inject(Api);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  id = input<string>();
  mapEl = viewChild.required<ElementRef<HTMLDivElement>>('map');
  harbour = signal<Harbour | null>(null);
  nearby = signal<NearbyPort[]>([]);
  visits = signal<{ shipId: string; name: string; visits: number; lastVisit: string; lastTripId: string | null; lastTripName: string | null }[]>([]);
  weather = signal<WeatherForecast | null>(null);
  weatherError = signal<string | null>(null);
  tides = signal<TideForecast | null>(null);
  fmt = fmtDate;
  position = (h: HarbourMarker) => formatPosition(h.lat, h.lng);
  skipperGuideUrl = (h: HarbourMarker) => `https://www.skipperguide.de/index.php?search=${encodeURIComponent(h.name)}`;
  /** Nearest services (computed server-side); until they are computed the harbour's own facility flags, on site. */
  services = computed(() => { const h = this.harbour(); return !h ? [] : h.services?.length ? h.services : Object.keys(h.facilities).filter(k => h.facilities[k]).map(kind => ({ kind, distanceM: 0, name: null, poiId: null })); });
  serviceIcon = (kind: string) => SERVICE_ICON[kind] ?? POI_ICON[kind] ?? 'check';
  private translate = inject(TranslateService);
  distance = (m: number) => formatServiceDistance(m, appI18n.lang(), this.translate.instant('harbour.onSite') as string);
  activeService = signal<string | null>(null);
  /** The places of the active service, nearest first, as the side-panel list shows them. */
  places = signal<{ id: number; name: string; dist: string; rows: PlaceRow[]; route: string }[]>([]);
  placesEmpty = signal(false);
  /** "Restaurants – 7 within 3 km": the selected service, how many places are shown and the radius that applied. */
  placesHead = signal('');
  selected = signal<number | null>(null);
  /** Highlights where the service is around the harbour (the nearest 50 POIs of that kind within 5 km as bigger POI icons, other POIs dimmed, map fitted to all of them) and lists them; again = off. A facility flag without a POI is the harbour itself. */
  toggleService(s: HarbourService): void {
    const h = this.harbour(), map = this.map;
    if (!h || !map) return;
    if (this.activeService() === s.kind) { this.clearService(); return; }
    this.clearService();
    this.activeService.set(s.kind);
    const label = this.translate.instant('harbour.svc.' + s.kind) as string;
    if (!s.poiId) { setServiceHighlight(map, [{ lat: h.lat, lng: h.lng, icon: `poi|amenity|${s.kind}` }], h); return; }
    this.api.get<ServicePlace[]>('/api/pois/near', { lat: h.lat, lng: h.lng, kind: s.kind, radiusM: 5000 }).subscribe(r => {
      if (this.activeService() !== s.kind || this.harbour() !== h || !this.map) return;
      const lang = appI18n.lang(), t = (k: string) => this.translate.instant('harbour.' + k) as string;
      const labels = { hours: t('openingHours'), cuisine: t('cuisine'), website: t('website'), phone: t('phone'), address: t('address') };
      const within3 = r.filter(p => p.distanceM <= NEAR_RADIUS_M), shown = within3.length ? within3 : r;   // places within 3 km if there are any, else up to 5 km
      this.placesHead.set(this.translate.instant('harbour.placesHeading', { label, count: shown.length, km: within3.length ? NEAR_RADIUS_M / 1000 : 5 }) as string);
      const view = shown.map(p => ({ p, name: p.name ?? label, dist: this.distance(p.distanceM), rows: placeRows(p, lang, labels), route: googleRouteUrl(h, p) }));
      this.places.set(view.map(v => ({ id: v.p.id, name: v.name, dist: v.dist, rows: v.rows, route: v.route })));
      this.placesEmpty.set(!shown.length);
      setServiceHighlight(this.map, view.map((v): ServiceSpot => ({
        lat: v.p.lat, lng: v.p.lng, icon: `poi|${v.p.poiClass ?? 'amenity'}|${v.p.poiKind ?? s.kind}`,
        popup: () => {
          const d = document.createElement('div'); d.className = 'poi-popup';
          d.append(textEl(v.name, 'b'), textEl(' · ' + v.dist, 'span'));
          for (const row of v.rows) {
            const line = document.createElement('div'); line.append(textEl(row.label + ': '));
            if (row.href) { const a = document.createElement('a'); a.href = row.href; a.target = '_blank'; a.rel = 'noopener'; a.textContent = row.value; line.append(a); } else line.append(rowValueEl(row));
            d.append(line);
          }
          const a = document.createElement('a'); a.href = v.route; a.target = '_blank'; a.rel = 'noopener'; a.textContent = this.translate.instant('harbour.googleRoute') as string;
          d.append(a);
          return d;
        },
      })), h, i => this.pickPlace(i));
    });
  }
  /** Hover on a list item (null = leave): its icon grows and the line from the harbour is drawn; the picked one stays highlighted. */
  focusPlace(i: number | null): void { if (this.map) setServiceActive(this.map, i ?? this.selected()); }
  selectPlace(i: number): void { this.selected.set(i); this.focusPlace(i); }
  /** An icon was clicked on the map: highlight and scroll the list item into view. */
  private pickPlace(i: number): void {
    this.selectPlace(i);
    setTimeout(() => document.getElementById('svc-place-' + i)?.scrollIntoView({ block: 'nearest', behavior: 'smooth' }));
  }
  private clearService(): void { this.activeService.set(null); this.places.set([]); this.placesEmpty.set(false); this.selected.set(null); if (this.map) setServiceHighlight(this.map, [], null); }
  hovered: string | null = null;
  private map?: MlMap;
  private harbours = new Map<string, HarbourMarker>();
  private pinned = false;
  private location = inject(Location);
  private unsyncView?: () => void;
  /** How the map follows the next loaded harbour: a deep link without a view centres it, a pick from a list only when it is off-screen; a click on the map never moves it. */
  private reveal: 'centre' | 'offscreen' | null = viewFromUrl(this.location.path()) ? null : 'centre';

  constructor() {
    effect(() => { const id = this.id(); if (id) this.load(id); else this.harbour.set(null); });
    effect(() => { const h = this.harbour(); if (this.map) setPoiFocus(this.map, h?.id ? h : null); untracked(() => { if (this.activeService()) this.clearService(); }); });
    effect(() => { const draft = this.shipCtx.draftM(); if (this.map) { applyHazardFilter(this.map, draft); applyDepthShading(this.map, draft); } });
  }
  pickShip(id: string): void { this.shipCtx.select(this.shipCtx.myShips().find(s => s.id === id) ?? null); }

  async ngAfterViewInit(): Promise<void> {
    const map = await createMap(this.mapEl().nativeElement, urlViewOptions(this.location.path()));
    this.map = map;
    this.unsyncView = syncViewWithUrl(map, this.location);
    // the side panel opening/closing resizes the map: keep the top-left corner where it was instead of re-centring (the map stays still)
    let size = { w: map.getContainer().clientWidth, h: map.getContainer().clientHeight };
    map.on('resize', () => {
      const w = map.getContainer().clientWidth, h = map.getContainer().clientHeight;
      if (w !== size.w || h !== size.h) map.panBy([(w - size.w) / 2, (h - size.h) / 2], { animate: false });
      size = { w, h };
    });
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
    map.on('click', e => { if (map.queryRenderedFeatures(e.point, { layers: ['harbour-points', 'harbour-clusters'] }).length === 0 && !poiAt(map, e.point)) this.clickPoint(e.lngLat.lat, e.lngLat.lng); });
    map.on('click', 'harbour-clusters', e => { const f = e.features?.[0]; if (f) map.easeTo({ center: (f.geometry as GeoPoint).coordinates as [number, number], zoom: map.getZoom() + 2 }); });
    map.on('mouseenter', 'harbour-points', e => { map.getCanvas().style.cursor = 'pointer'; const id = e.features?.[0]?.properties?.['id']; if (id) this.hoverHarbour(id); });
    map.on('mouseleave', 'harbour-points', () => { map.getCanvas().style.cursor = ''; this.hideRoute(); });
    map.on('contextmenu', e => this.contextMenu(e));
    applyHazardFilter(map, this.shipCtx.draftM()); applyDepthShading(map, this.shipCtx.draftM());
    this.refresh();
    const h = this.harbour();
    if (h) { setPoiFocus(map, h.id ? h : null); this.revealHarbour(h); }
  }

  ngOnDestroy(): void { this.unsyncView?.(); this.map?.remove(); }

  private revealHarbour(h: Harbour): void {
    const map = this.map, how = this.reveal;
    if (!map || !how) return;
    this.reveal = null;
    if (how === 'centre') map.easeTo({ center: [h.lng, h.lat], zoom: Math.max(map.getZoom(), 11) });
    else if (!map.getBounds().contains([h.lng, h.lat])) map.easeTo({ center: [h.lng, h.lat] });
  }

  /** The current view as query params, so router navigations keep it in the URL. */
  private viewParams(): { c?: string } {
    const m = this.map; if (!m) return {};
    const c = m.getCenter(); return { c: formatView({ lat: c.lat, lng: c.lng, zoom: m.getZoom() }) };
  }

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

  /** fromList: picked in a list (nearby ports), so the map moves to it if it is off-screen; a click on the map never moves it. */
  select(id: string, fromList = false): void { this.reveal = fromList ? 'offscreen' : null; this.router.navigate(['/harbours', id], { queryParams: this.viewParams() }); }

  load(id: string): void {
    this.api.get<Harbour>(`/api/harbours/${id}`).subscribe(h => {
      this.harbour.set(h);
      this.pinned = false; this.hideRoute(true);
      if (this.map) { this.revealHarbour(h); this.setHarbourData(); }
      this.api.get<{ ports: NearbyPort[] }>('/api/routing/nearby-ports', { harbour: id, maxNm: 60, limit: 30 }).subscribe(r => this.nearby.set(r.ports));
      this.api.get<ReturnType<HarbourMapPage['visits']>>(`/api/harbours/${id}/visits`).subscribe(v => this.visits.set(v));
      loadForecast(this.api, `/api/harbours/${id}/weather`, undefined, this.weather, this.weatherError);
      loadTides(this.api, `/api/harbours/${id}/tides`, undefined, this.tides);
    });
  }

  close(): void { this.hideRoute(true); this.router.navigate(['/'], { queryParams: this.viewParams() }); }

  private hoverHarbour(id: string): void {
    const h = this.harbour(); const m = this.harbours.get(id);
    if (!h || !m || id === h.id) return;
    const p = this.nearby().find(n => n.harbourId === id)
      ?? { harbourId: id, name: m.name, lat: m.lat, lng: m.lng, distanceNm: straightNm(h, m), straightLineNm: straightNm(h, m), etaHours: 0, method: 'straight-line' };
    this.hovered = id; this.showRoute(p);
  }

  /** Click on open water/land while a harbour is selected: pinned route from the harbour to that point with its distance. */
  private clickPoint(lat: number, lng: number): void {
    const h = this.harbour(); const map = this.map;
    if (!h || !map) return;
    const to = { lat, lng };
    this.pinned = false; this.hovered = null;
    this.draw([[h.lng, h.lat], [lng, lat]], straightNm(h, to), true);
    this.pinned = true;
    this.pointPopup?.remove();
    this.pointPopup = new maplibregl.Popup({ closeButton: true, closeOnClick: false }).setLngLat([lng, lat]).setDOMContent(textEl(`${straightNm(h, to)} sm (Luftlinie) …`)).addTo(map);
    this.pointPopup.on('close', () => { if (this.pinned) this.hideRoute(true); });
    this.api.get<{ distanceNm: number; straightLineNm: number; geometry: { coordinates: number[][] }; method: string; warnings?: RouteWarning[] }>('/api/routing/route', { from: `${h.lat},${h.lng}`, to: `${lat},${lng}`, ...this.shipCtx.routeParams() }).subscribe({
      next: r => {
        if (!this.pointPopup?.isOpen()) return;
        this.pinned = false; this.draw(r.geometry.coordinates.map(c => [c[0], c[1]] as [number, number]), r.distanceNm, r.method === 'straight-line', !!r.warnings?.length); this.pinned = true;
        this.pointPopup.setDOMContent(textEl(r.method === 'straight-line' ? `${r.distanceNm} sm (Luftlinie, keine Seeroute)` : `${r.distanceNm} sm · Luftlinie ${r.straightLineNm} sm${routeWarningText(r.warnings)}`));
      },
      error: () => this.pointPopup?.setDOMContent(textEl(`${straightNm(h, to)} sm (Luftlinie)`)),
    });
  }
  private pointPopup?: maplibregl.Popup;

  /** Legacy behaviour (05 §5.2): hover draws the route with a "<n> sm" label and fits the map if needed. */
  showRoute(p: NearbyPort): void {
    const h = this.harbour();
    if (!h || this.pinned) return;
    this.draw([[h.lng, h.lat], [p.lng, p.lat]], p.distanceNm, p.method === 'straight-line');
    if (p.method !== 'straight-line') {
      const params = { ...(h.id ? { fromHarbour: h.id, toHarbour: p.harbourId } : { from: `${h.lat},${h.lng}`, to: `${p.lat},${p.lng}` }), ...this.shipCtx.routeParams() };
      this.api.get<{ distanceNm: number; geometry: { coordinates: number[][] }; method: string; warnings?: RouteWarning[] }>('/api/routing/route', params).subscribe(r => {
        if (this.hovered === p.harbourId) this.draw(r.geometry.coordinates.map(c => [c[0], c[1]] as [number, number]), r.distanceNm, r.method === 'straight-line', !!r.warnings?.length);
      });
    }
  }
  /** warned: the route uses an assumed passage (label "⚠"). */
  private draw(coords: [number, number][], nm: number, dashed: boolean, warned = false): void {
    const map = this.map; if (!map) return;
    (map.getSource('route') as GeoJSONSource).setData({ type: 'FeatureCollection', features: [{ type: 'Feature', properties: { label: `${nm} sm${warned ? ' ⚠' : ''}`, dashed }, geometry: { type: 'LineString', coordinates: coords } }] });
    // the map stays where it is: hovering/pinning only draws (the view moves only when another harbour is selected)
  }
  hideRoute(force = false): void {
    if (this.pinned && !force) return;
    if (force) { const p = this.pointPopup; this.pointPopup = undefined; p?.remove(); }
    this.hovered = null;
    (this.map?.getSource('route') as GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection', features: [] });
  }
  pinRoute(p: NearbyPort): void { this.pinned = false; this.hovered = p.harbourId; this.showRoute(p); this.pinned = true; }

  private contextMenu(e: MapMouseEvent): void {
    const map = this.map; if (!map) return;
    const div = document.createElement('div');
    const popup = new maplibregl.Popup({ closeButton: false }).setLngLat(e.lngLat);
    const mk = (label: string, fn: () => void) => { const b = document.createElement('button'); b.className = 'mdc-button'; b.style.display = 'block'; b.textContent = label; b.onclick = () => { popup.remove(); fn(); }; div.appendChild(b); };
    mk('Nächste Häfen von hier', () => this.nearbyFrom(e.lngLat.lat, e.lngLat.lng));
    mk('Wettervorhersage hier', () => this.dialog.open(WeatherDialog, { data: { lat: e.lngLat.lat, lng: e.lngLat.lng }, width: '560px' }));
    if (this.auth.me()) mk('Neuer Hafen', () => this.edit(null, e.lngLat.lat, e.lngLat.lng));
    popup.setDOMContent(div).addTo(map);
  }

  private nearbyFrom(lat: number, lng: number): void {
    this.api.get<{ ports: NearbyPort[] }>('/api/routing/nearby-ports', { lat, lng, maxNm: 60, limit: 30 }).subscribe(r => {
      this.harbour.set({ id: '', name: `${lat.toFixed(3)}, ${lng.toFixed(3)}`, lat, lng, category: null, country: null, website: null, description: null, phone: null, vhfChannel: null, facilities: {}, source: 'User', osmType: null, osmId: null, wifis: [], reviews: [], averageRating: null, photoUrls: [], services: [] });
      this.nearby.set(r.ports);
      loadForecast(this.api, '/api/weather', { lat, lng }, this.weather, this.weatherError);
      loadTides(this.api, '/api/weather/tides', { lat, lng }, this.tides);
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
