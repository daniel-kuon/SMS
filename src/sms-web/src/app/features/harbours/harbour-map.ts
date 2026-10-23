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
import type { GeoJSONSource, MapLayerMouseEvent, MapMouseEvent } from 'maplibre-gl';
import { forkJoin } from 'rxjs';
import type { Point as GeoPoint } from 'geojson';
import { Api, AuthService, errorMessage } from '../../core/api';
import { Harbour, HarbourMarker, HarbourService, NearbyPort, needsAirDraftHint, RouteWarning, routeWarningText, Wifi } from '../../core/models';
import { Location } from '@angular/common';
import { bounds, createMap, formatView, maplibregl, MlMap, POI_ICON, poiAt, rowValueEl, ServiceSpot, setPoiFocus, setServiceActive, setServiceHighlight, setServicePath, syncViewWithUrl, textEl, urlViewOptions, viewFromUrl } from '../../core/map';
import { haversineM } from '../../core/track-playback';
import { formatServiceDistance } from '../../core/service-distance';
import { googleRouteUrl, PlaceRow, placeRows, ServicePlace } from '../../core/service-place';
import { appI18n } from '../../core/site';
import { TranslateService } from '@ngx-translate/core';
import { UploadZone } from '../../shared/shared';
import { loadForecast, loadTides, TideForecast, WeatherCard, WeatherDialog, WeatherForecast } from '../../core/weather';
import { ShipContextService } from '../../core/ship-context';
import { applyDepthShading, applyHazardFilter } from '../../core/map';
import { formatDecimal, formatDepthBand, formatNautical, insertVia, joinLegs, LatLng, nearestLeg, nearestOnPolyline, PointInfo, RouteLeg } from '../../core/map-point';
import { parseRouteLink, RouteLink, routeLinkParams, withParams } from '../../core/route-link';
import { LineEditor } from '../../core/line-editor';
import { Lines } from '../../core/line-network';
import { formatBearing, measureLegs } from '../../core/measure';
/** Show only the places up to this distance when there are any; otherwise the ones up to the 5 km that the API returns. */
const NEAR_RADIUS_M = 3000;

/** Service glyphs not covered by the POI icons (Material Symbols). */
const SERVICE_ICON: Record<string, string> = { power_supply: 'electrical_services', sanitary_dump_station: 'plumbing', waste_disposal: 'delete', internet_access: 'wifi', bus: 'directions_bus', tram: 'tram', train: 'train', ferry: 'directions_boat', chandlery: 'sailing', slipway: 'rowing' };
/** Marker elements sit inside the map container, so their mouse events reach the map's handlers too. */
const onMarker = (ev: Event) => !!(ev.target as HTMLElement | null)?.closest?.('.maplibregl-marker');
/** A small round map marker (clicked point, route start). */
function dotEl(colour: string): HTMLElement {
  const el = document.createElement('div');
  el.style.cssText = `width:12px;height:12px;border-radius:50%;background:${colour};border:2px solid #fff;box-shadow:0 0 3px rgba(0,0,0,.5)`;
  return el;
}
/** A via point of the route: a white dot ringed in the route's blue, with a small × to remove it. */
function viaEl(removable = false): HTMLElement {
  const el = document.createElement('div');
  el.className = 'route-via';
  el.style.cssText = 'width:14px;height:14px;border-radius:50%;background:#fff;border:3px solid #1e88e5;box-shadow:0 0 3px rgba(0,0,0,.4);cursor:grab;position:relative';
  if (removable) {
    el.title = 'Ziehen verschiebt, Rechtsklick oder × entfernt';
    const x = document.createElement('button');
    x.type = 'button'; x.textContent = '×'; x.setAttribute('aria-label', 'Wegpunkt entfernen');
    x.style.cssText = 'position:absolute;left:12px;top:-12px;width:16px;height:16px;padding:0;border:none;border-radius:50%;background:#455a64;color:#fff;font:12px/16px sans-serif;cursor:pointer';
    el.append(x);
  }
  return el;
}
/** The hover handle on the route: the line editor's hollow dot in the route's blue; mouse events pass through to the route below. */
function routeHandleEl(): HTMLElement {
  const el = document.createElement('div');
  el.style.cssText = 'width:10px;height:10px;border-radius:50%;background:rgba(255,255,255,.7);border:3px solid #1e88e5;box-shadow:0 0 3px rgba(0,0,0,.5);pointer-events:none';
  return el;
}
const ROUTE_HANDLE_PX = 8;
const straightNm =(a: { lat: number; lng: number }, b: { lat: number; lng: number }) => Math.round(haversineM(a.lat, a.lng, b.lat, b.lng) / 185.2) / 10;

@Component({
  imports: [FormsModule, MatButtonModule, MatIconModule, MatTableModule, MatTabsModule, TranslatePipe, UploadZone, MatListModule, RouterLink, WeatherCard, MatSelectModule, MatFormFieldModule, MatInputModule],
  template: `
    <div class="layout">
      <div class="map-wrap">
        <div #map class="map"></div>
        <button type="button" class="ruler" [class.active]="measuring()" (click)="toggleMeasure()" [title]="'measure.start' | translate" [attr.aria-label]="'measure.start' | translate" [attr.aria-pressed]="measuring()"><mat-icon>straighten</mat-icon></button>
        @if (measuring()) {
          <div class="measure" role="region" [attr.aria-label]="'measure.title' | translate">
            <div class="head"><b class="grow">{{ 'measure.title' | translate }}</b>
              <button mat-icon-button class="clear-measure" (click)="clearMeasure()" [disabled]="!measure().legs.length" [title]="'measure.clear' | translate" [attr.aria-label]="'measure.clear' | translate"><mat-icon>delete_sweep</mat-icon></button>
              <button mat-icon-button (click)="toggleMeasure()" [title]="'measure.end' | translate" [attr.aria-label]="'measure.end' | translate"><mat-icon>close</mat-icon></button></div>
            @if (measure().legs.length) {
              <table>
                <thead><tr><th>#</th><th>{{ 'measure.length' | translate }}</th><th>{{ 'measure.bearing' | translate }}</th></tr></thead>
                <tbody>@for (l of measure().legs; track $index) { <tr><td>{{ $index + 1 }}</td><td>{{ nm(l.nm) }}</td><td>{{ bearing(l.bearing) }}</td></tr> }</tbody>
                <tfoot><tr><td>{{ 'measure.total' | translate }}</td><td colspan="2">{{ nm(measure().totalNm) }}</td></tr></tfoot>
              </table>
            } @else { <p class="muted">{{ 'measure.hint' | translate }}</p> }
          </div>
        }
        @if (point(); as p) {
          <div class="point-bar" role="region" [attr.aria-label]="'mapPoint.title' | translate">
            <div class="pos"><b>{{ nautical(p) }}</b> <span class="muted">{{ decimal(p) }}</span>
              <button mat-icon-button class="copy" (click)="copyPosition(p)" [title]="'mapPoint.copy' | translate" [attr.aria-label]="'mapPoint.copy' | translate"><mat-icon>{{ copied() ? 'check' : 'content_copy' }}</mat-icon></button>
              <button mat-icon-button class="close" (click)="closePoint()" [attr.aria-label]="'common.close' | translate"><mat-icon>close</mat-icon></button></div>
            <div class="facts">
              <span class="depth"><mat-icon inline>waves</mat-icon> {{ depthText() }}</span>
              @if (dtmText(); as d) { <span class="dtm" [title]="'mapPoint.dtmHint' | translate"><mat-icon inline>grid_on</mat-icon> {{ d }}</span> }
              @if (bridgeText(); as b) { <span class="bridge"><mat-icon inline>bridge</mat-icon> {{ b }}</span> }
            </div>
            <div class="actions">
              @if (canRouteHere()) { <button mat-flat-button class="route-here" (click)="routeHere()"><mat-icon>route</mat-icon> {{ 'mapPoint.routeTo' | translate }}</button> }
              <button mat-stroked-button class="route-from" (click)="routeFromHere()"><mat-icon>trip_origin</mat-icon> {{ 'mapPoint.routeFrom' | translate }}</button>
              @if (start()) { <span class="muted start">{{ 'mapPoint.start' | translate: { name: startName() } }}
                @if (routeStart()) { <button mat-icon-button class="clear-start" (click)="clearStart()" [title]="'mapPoint.clearStart' | translate" [attr.aria-label]="'mapPoint.clearStart' | translate"><mat-icon>close</mat-icon></button> }</span> }
            </div>
          </div>
        }
        @if (courseRoute() || refRoute()) {
          <div class="route-box" [class.above-bar]="point()" role="region" [attr.aria-label]="'routeLink.title' | translate">
            @if (refRoute(); as r) {
              <div class="legend"><label><input type="checkbox" class="toggle-ref" [checked]="refVisible()" (change)="toggleRef()" /><span class="swatch saved"></span>{{ 'routeLink.saved' | translate: { nm: r.nm } }}</label>
                <button mat-icon-button class="remove-ref" (click)="removeRef()" [title]="'routeLink.remove' | translate" [attr.aria-label]="'routeLink.remove' | translate"><mat-icon>close</mat-icon></button></div>
              <div class="legend current"><span class="swatch"></span>{{ 'routeLink.current' | translate: { nm: courseRoute()?.distanceNm ?? (routing() ? '…' : '–') } }}</div>
            }
            @if (courseRoute()) { <button mat-stroked-button class="save-link" [disabled]="routing()" (click)="saveRouteLink()"><mat-icon>{{ linkCopied() ? 'check' : 'link' }}</mat-icon> {{ (linkCopied() ? 'routeLink.copied' : 'routeLink.save') | translate }}</button> }
          </div>
        }
      </div>
      <div class="ship-ctx">
        <mat-icon inline>sailing</mat-icon>
        @if (shipCtx.myShips().length) {
          <select [ngModel]="shipCtx.ship()?.id ?? ''" (ngModelChange)="pickShip($event)" [attr.aria-label]="'ship.forMap' | translate">
            <option value="">{{ 'ship.noShipContext' | translate }}</option>
            @for (s of shipCtx.myShips(); track s.id) { <option [value]="s.id">{{ s.name }}</option> }
          </select>
        }
        <label [title]="'ship.minDepthHint' | translate">{{ 'ship.minDepth' | translate }} <input type="number" step="0.1" min="0" [ngModel]="shipCtx.minDepthM()" (ngModelChange)="shipCtx.manual($event === '' ? null : +$event, shipCtx.warnDepthM(), shipCtx.airDraftM())" /></label>
        <label [title]="'ship.warnDepthHint' | translate">{{ 'ship.warnDepth' | translate }} <input type="number" step="0.1" min="0" [ngModel]="shipCtx.warnDepthM()" (ngModelChange)="shipCtx.manual(shipCtx.minDepthM(), $event === '' ? null : +$event, shipCtx.airDraftM())" /></label>
        <label [title]="'ship.airDraftHint' | translate">{{ 'ship.airDraft' | translate }} <input #airDraft type="number" step="0.1" min="0" [ngModel]="shipCtx.airDraftM()" (ngModelChange)="shipCtx.manual(shipCtx.minDepthM(), shipCtx.warnDepthM(), $event === '' ? null : +$event)" /></label>
        @if (shipCtx.hazardDepthM() !== null) { <span class="muted small">{{ 'ship.hazardsHidden' | translate }}</span> }
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
              <span class="chip svc" role="button" tabindex="0" [class.active]="activeService() === s.kind" (click)="toggleService(s)" (keydown.enter)="toggleService(s)" [title]="('harbour.svc.' + s.kind | translate) + (s.name ? ': ' + s.name : '')"><mat-icon inline>{{ serviceIcon(s.kind) }}</mat-icon> {{ distance(s.distanceM, s.walkM) }}</span>
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
    .map-wrap { flex: 1; position: relative; display: flex; min-width: 0; } .map { flex: 1; min-height: 60vh; }
    .point-bar { position: absolute; left: 8px; right: 8px; bottom: 28px; z-index: 3; max-width: 640px; margin: 0 auto; background: rgba(255,255,255,.97); border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,.3); padding: 4px 44px 8px 12px; font-size: 13px; display: flex; flex-direction: column; gap: 4px; }
    .point-bar .pos { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; } .point-bar .close { position: absolute; top: 0; right: 0; }
    .point-bar .facts, .point-bar .actions { display: flex; flex-wrap: wrap; gap: 4px 14px; align-items: center; } .point-bar mat-icon[inline] { vertical-align: middle; }
    .point-bar .start { display: inline-flex; align-items: center; }
    .route-box { position: absolute; left: 8px; bottom: 28px; z-index: 3; background: rgba(255,255,255,.97); border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,.3); padding: 4px 8px; font-size: 13px; display: flex; flex-direction: column; gap: 2px; align-items: flex-start; }
    .route-box .legend { display: flex; align-items: center; gap: 6px; min-height: 28px; } .route-box label { display: inline-flex; align-items: center; gap: 6px; cursor: pointer; } .route-box .current { padding-left: 19px; }
    .route-box .swatch { display: inline-block; width: 22px; border-top: 4px solid #1e88e5; } .route-box .swatch.saved { border-top: 3px dashed #757575; }
    .ruler { position: absolute; left: 10px; top: 84px; z-index: 2; width: 29px; height: 29px; padding: 0; border: none; border-radius: 4px; background: #fff; box-shadow: 0 0 0 2px rgba(0,0,0,.1); cursor: pointer; display: flex; align-items: center; justify-content: center; }
    .ruler mat-icon { font-size: 20px; width: 20px; height: 20px; } .ruler.active { background: #1e88e5; color: #fff; }
    .measure { position: absolute; left: 48px; top: 84px; z-index: 3; min-width: 200px; max-height: calc(100% - 200px); overflow-y: auto; background: rgba(255,255,255,.97); border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,.3); padding: 2px 4px 6px 10px; font-size: 13px; }
    .measure .head { display: flex; align-items: center; } .measure .grow { flex: 1; } .measure p { margin: 0 6px 4px 0; max-width: 220px; }
    .measure table { border-collapse: collapse; width: 100%; } .measure th { text-align: left; color: #666; font-weight: 500; } .measure th, .measure td { padding: 2px 8px 2px 0; } .measure td:not(:first-child) { font-variant-numeric: tabular-nums; }
    .measure tfoot td { border-top: 1px solid #ccc; font-weight: 600; }
    .route-box.above-bar { bottom: 140px; } /* the point bar spans the bottom */
    .panel { width: 0; overflow: hidden; transition: width .2s; background: white; box-shadow: -2px 0 8px rgba(0,0,0,.15); }
    .panel.open { width: 420px; max-width: 100vw; padding: 12px; overflow-y: auto; }
    .panel h2 { margin: 0; font-size: 20px; } .links { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; margin: 6px 0; } .links .pos { font-size: 12px; }
    .review { padding: 6px 0; border-bottom: 1px solid #eee; }
    .ship-ctx { position: absolute; left: 50px; top: 10px; right: 50px; width: fit-content; flex-wrap: wrap; background: rgba(255,255,255,.95); border-radius: 4px; box-shadow: 0 1px 4px rgba(0,0,0,.3); padding: 4px 8px; display: flex; gap: 8px; align-items: center; font-size: 12px; z-index: 2; }
    .ship-ctx input { width: 44px; } .ship-ctx select { max-width: 140px; } .ship-ctx .small { font-size: 11px; }
    .full-width { width: 100%; }
    .services { display: flex; gap: 6px; flex-wrap: wrap; margin: 6px 0; } .svc { display: inline-flex; align-items: center; gap: 2px; cursor: pointer; } .svc.active { outline: 2px solid #e65100; }
    .places-head { margin: 8px 0 4px; font-size: 14px; font-weight: 600; } .places-head mat-icon { vertical-align: middle; }
    .places { list-style: none; margin: 0 0 8px; padding: 0; max-height: 40vh; overflow-y: auto; font-size: 13px; } .places li { padding: 6px 8px; border-bottom: 1px solid #e0e0e0; cursor: pointer; } .places li:hover, .places li.sel { background: #fff3e0; } .places li.sel { border-left: 3px solid #d50000; } .places .row { white-space: pre-line; } .places .badge { font-weight: 600; white-space: nowrap; color: #c62828; } .places .badge.open { color: #2e7d32; } .places a mat-icon { font-size: 14px; vertical-align: middle; } .svc mat-icon { font-size: 16px; }
    @media (max-width: 800px) { .layout { flex-direction: column; height: auto; } .map-wrap { height: 60vh; flex: none; } .point-bar { left: 4px; right: 4px; bottom: 4px; } .panel.open { width: 100%; } }
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
  airDraftEl = viewChild<ElementRef<HTMLInputElement>>('airDraft');
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
  services = computed((): HarbourService[] => { const h = this.harbour(); return !h ? [] : h.services?.length ? h.services : Object.keys(h.facilities).filter(k => h.facilities[k]).map(kind => ({ kind, distanceM: 0, name: null, poiId: null })); });
  serviceIcon = (kind: string) => SERVICE_ICON[kind] ?? POI_ICON[kind] ?? 'check';
  private translate = inject(TranslateService);
  /** The walk when known ("350 m zu Fuß"), else the straight distance. */
  distance = (m: number, walkM?: number | null) => formatServiceDistance(walkM ?? m, appI18n.lang(), this.translate.instant('harbour.onSite') as string, walkM != null ? this.translate.instant('harbour.onFoot') as string : undefined);
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
      const within3 = r.filter(p => (p.walkM ?? p.distanceM) <= NEAR_RADIUS_M), shown = within3.length ? within3 : r;   // places within 3 km if there are any, else up to 5 km
      this.placesHead.set(this.translate.instant('harbour.placesHeading', { label, count: shown.length, km: within3.length ? NEAR_RADIUS_M / 1000 : 5 }) as string);
      const view = shown.map(p => ({ p, name: p.name ?? label, dist: this.distance(p.distanceM, p.walkM), rows: placeRows(p, lang, labels), route: googleRouteUrl(h, p) }));
      this.walkFrom = h; this.walkAsked.clear();
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
  /** Hover on a list item (null = leave): its icon grows and the line from the harbour is drawn – straight and dashed until the walking path (GET /api/pois/walk) is there; the picked one stays highlighted. */
  focusPlace(i: number | null): void {
    const map = this.map, k = i ?? this.selected();
    if (!map) return;
    setServiceActive(map, k);
    const p = k === null ? undefined : this.places()[k], from = this.walkFrom, kind = this.activeService();
    if (k === null || !p || !from || this.walkAsked.has(p.id)) return;
    this.walkAsked.add(p.id);
    this.api.get<{ walkM: number; coordinates: [number, number][] }>('/api/pois/walk', { lat: from.lat, lng: from.lng, poiId: p.id }).subscribe({
      next: w => { if (this.map && this.activeService() === kind && this.places()[k]?.id === p.id) setServicePath(this.map, k, w.coordinates); },   // shows up if k is still the active one
      error: () => undefined,   // 404: no path, the straight line stays
    });
  }
  /** The harbour the listed places were looked up from, and the places whose walking path was already asked for. */
  private walkFrom: HarbourMarker | null = null;
  private walkAsked = new Set<number>();
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
    effect(() => { const min = this.shipCtx.minDepthM(), warn = this.shipCtx.warnDepthM(), hazard = this.shipCtx.hazardDepthM(); if (this.map) { applyHazardFilter(this.map, hazard); applyDepthShading(this.map, min, warn); } });
    // changed ship settings: the shown route again with the same points (debounced while typing); a route link in the URL follows
    effect(onCleanup => {
      const params = this.shipCtx.routeParams();
      const t = setTimeout(() => untracked(() => this.settingsChanged(params)), 500);
      onCleanup(() => clearTimeout(t));
    });
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
    map.addSource('route-ref', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    map.addLayer({ id: 'route-ref-line', type: 'line', source: 'route-ref', paint: { 'line-color': '#757575', 'line-width': 3, 'line-dasharray': [2, 1.5] } });
    map.addSource('route', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    map.addLayer({ id: 'route-line', type: 'line', source: 'route', filter: ['!=', ['get', 'onLand'], true], paint: { 'line-color': '#1e88e5', 'line-width': 4 } });
    map.addLayer({ id: 'route-on-land', type: 'line', source: 'route', filter: ['==', ['get', 'onLand'], true], paint: { 'line-color': '#1e88e5', 'line-width': 1.5, 'line-dasharray': [1, 2] } });
    // an invisible wide line over the route (8 px either side, at least 5 px past the drawn line): hover handle and grab area for dragging a via point out of it
    map.addLayer({ id: 'route-hit', type: 'line', source: 'route', filter: ['all', ['==', ['geometry-type'], 'LineString'], ['!=', ['get', 'onLand'], true]], paint: { 'line-color': '#000000', 'line-width': 2 * ROUTE_HANDLE_PX, 'line-opacity': 0.01 } });
    map.addLayer({ id: 'route-line-dashed', type: 'line', source: 'route', filter: ['==', ['get', 'dashed'], true], paint: { 'line-color': '#ffffff', 'line-width': 2, 'line-dasharray': [2, 2] } });
    map.addLayer({ id: 'route-check-depth', type: 'circle', source: 'route', filter: ['==', ['get', 'checkDepth'], true], paint: { 'circle-radius': 5, 'circle-color': '#9c97d6', 'circle-stroke-color': '#34357a', 'circle-stroke-width': 2 } });
    map.addLayer({ id: 'route-label', type: 'symbol', source: 'route', layout: { 'symbol-placement': 'line-center', 'text-field': ['get', 'label'], 'text-size': 12, 'text-font': ['Noto Sans Regular'] }, paint: { 'text-color': '#0d47a1', 'text-halo-color': '#ffffff', 'text-halo-width': 2 } });

    map.on('moveend', () => this.refresh());
    map.on('click', 'harbour-points', e => { if (this.ruler) return; const id = e.features?.[0]?.properties?.['id']; if (id) this.select(id); });
    map.on('click', e => {
      if (onMarker(e.originalEvent)) return;   // a via/start marker, not the map
      if (this.ruler) { if (!this.ruler.tryInsert(e.point)) this.ruler.append([e.lngLat.lng, e.lngLat.lat]); return; }   // measuring: no point bar, no routing
      if (map.queryRenderedFeatures(e.point, { layers: ['harbour-points', 'harbour-clusters'] }).length === 0 && !poiAt(map, e.point)) this.clickPoint(e.lngLat.lat, e.lngLat.lng);
    });
    map.on('mouseenter', 'route-hit', () => { if (this.editable()) map.getCanvas().style.cursor = 'grab'; });
    map.on('mouseleave', 'route-hit', () => { if (!this.dragging) map.getCanvas().style.cursor = ''; });
    map.on('mousedown', 'route-hit', e => this.dragRoute(e));
    map.on('mousemove', e => this.hoverRoute(e));
    map.on('mouseout', () => this.routeHandle?.remove());
    map.on('click', 'harbour-clusters', e => { if (this.ruler) return; const f = e.features?.[0]; if (f) map.easeTo({ center: (f.geometry as GeoPoint).coordinates as [number, number], zoom: map.getZoom() + 2 }); });
    map.on('mouseenter', 'harbour-points', e => { map.getCanvas().style.cursor = 'pointer'; const id = e.features?.[0]?.properties?.['id']; if (id) this.hoverHarbour(id); });
    map.on('mouseleave', 'harbour-points', () => { map.getCanvas().style.cursor = ''; this.hideRoute(); });
    map.on('contextmenu', e => this.contextMenu(e));
    applyHazardFilter(map, this.shipCtx.hazardDepthM()); applyDepthShading(map, this.shipCtx.minDepthM(), this.shipCtx.warnDepthM());
    this.refresh();
    const h = this.harbour();
    if (this.link) this.reveal = null;   // a route link shows the route, not the harbour
    if (h) { setPoiFocus(map, h.id ? h : null); this.revealHarbour(h); }
    if (this.link) { this.openLink(this.link); this.link = null; }
  }

  ngOnDestroy(): void { this.endMeasure(); this.unsyncView?.(); this.map?.remove(); }

  // --- measuring: a ruler line (the passage line editor: click adds, drag moves, × deletes, click on the line inserts) with its legs listed ---
  private ruler?: LineEditor;
  measuring = signal(false);
  private measureLines = signal<Lines>([[]]);
  measure = computed(() => measureLegs(this.measureLines()[0] ?? []));
  nm = (v: number) => `${v.toLocaleString(appI18n.lang(), { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${this.translate.instant('measure.unit')}`;
  bearing = formatBearing;
  private onMeasureKey = (e: KeyboardEvent) => {
    const tag = (e.target as HTMLElement | null)?.tagName;
    if (e.key === 'Escape' && tag !== 'INPUT' && tag !== 'TEXTAREA' && tag !== 'SELECT') this.endMeasure();
  };
  toggleMeasure(): void {
    const map = this.map; if (!map) return;
    if (this.ruler) { this.endMeasure(); return; }
    this.routeHandle?.remove();
    this.ruler = new LineEditor(map, { colour: '#d81b60', noBranches: true, onChange: l => this.measureLines.set(l) });
    this.measuring.set(true);
    map.doubleClickZoom.disable(); map.getCanvas().style.setProperty('cursor', 'crosshair');
    document.addEventListener('keydown', this.onMeasureKey);
  }
  clearMeasure(): void { this.ruler?.set([[]]); this.measureLines.set([[]]); }
  private endMeasure(): void {
    if (!this.ruler) return;
    this.ruler.destroy(); this.ruler = undefined;
    this.measuring.set(false); this.measureLines.set([[]]);
    this.map?.doubleClickZoom.enable(); this.map?.getCanvas().style.removeProperty('cursor');
    document.removeEventListener('keydown', this.onMeasureKey);
  }

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
      if (this.keepLinkedRoute) this.keepLinkedRoute = false;   // a route link on a harbour page: the harbour's first load keeps the linked route
      else { this.pinned = false; this.hideRoute(true); }
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

  // --- the clicked point (bottom bar) and the routed course start → vias → end ---
  /** The last clicked map point: coordinates, charted depth and bridges nearby in the bottom bar. */
  point = signal<LatLng | null>(null);
  /** Its point info: undefined while loading, null when the API failed. */
  info = signal<PointInfo | null | undefined>(undefined);
  /** A start picked with "Route von hier"; without one the selected harbour is the start. */
  routeStart = signal<LatLng | null>(null);
  start = computed((): LatLng | null => this.routeStart() ?? this.harbour());
  startName = computed(() => { const s = this.routeStart(), h = this.harbour(); return s ? formatNautical(s.lat, s.lng, appI18n.lang()) : h?.name ?? ''; });
  canRouteHere = computed(() => { const s = this.start(), p = this.point(); return !!s && !!p && (s.lat !== p.lat || s.lng !== p.lng); });
  copied = signal(false);
  nautical = (p: LatLng) => formatNautical(p.lat, p.lng, appI18n.lang());
  decimal = (p: LatLng) => formatDecimal(p.lat, p.lng);
  depthText = computed(() => {
    const i = this.info(); if (i === undefined) return '…'; if (i === null) return '–';
    return i.depth ? formatDepthBand(i.depth.minM, i.depth.maxM, appI18n.lang()) : this.translate.instant('mapPoint.noDepth') as string;
  });
  /** The EMODnet DTM cell under the point: "EMODnet: 3,4 m (gemessen)". */
  dtmText = computed(() => {
    const d = this.info()?.dtm; if (!d) return null;
    return `EMODnet: ${d.depthM.toLocaleString(appI18n.lang(), { minimumFractionDigits: 1, maximumFractionDigits: 1 })} m (${this.translate.instant(d.interpolated ? 'mapPoint.interpolated' : 'mapPoint.measured')})`;
  });
  /** The nearest bridge within 300 m: "Brücke: 12 m Durchfahrtshöhe · 80 m entfernt". */
  bridgeText = computed(() => {
    const b = this.info()?.bridges[0]; if (!b) return null;
    const kind = b.kind === 'OpeningBridge' ? 'mapPoint.openingBridge' : 'mapPoint.bridge';
    const clearance = b.clearanceM != null ? this.translate.instant('mapPoint.clearance', { m: b.clearanceM.toLocaleString(appI18n.lang(), { maximumFractionDigits: 1 }) }) : this.translate.instant('mapPoint.clearanceUnknown');
    return `${this.translate.instant(kind)}: ${clearance} · ${this.translate.instant('mapPoint.away', { m: Math.round(b.distanceM) })}`;
  });
  private pointMarker?: maplibregl.Marker;
  private startMarker?: maplibregl.Marker;

  /** Click on open water/land: the bottom bar for that point (routing waits for "Route hierher"). */
  private clickPoint(lat: number, lng: number): void {
    const map = this.map; if (!map) return;
    const p = { lat, lng };
    this.point.set(p); this.info.set(undefined); this.copied.set(false);
    (this.pointMarker ??= new maplibregl.Marker({ element: dotEl('#e65100') })).setLngLat([lng, lat]).addTo(map);
    this.api.get<PointInfo>('/api/routing/point-info', { lat, lng }).subscribe({
      next: i => { if (this.point() === p) this.info.set(i); },
      error: () => { if (this.point() === p) this.info.set(null); },
    });
  }
  closePoint(): void { this.point.set(null); this.pointMarker?.remove(); }
  copyPosition(p: LatLng): void { navigator.clipboard?.writeText(`${this.nautical(p)} (${this.decimal(p)})`).then(() => this.copied.set(true), () => undefined); }
  routeFromHere(): void {
    const p = this.point(), map = this.map; if (!p || !map) return;
    this.routeStart.set(p);
    (this.startMarker ??= new maplibregl.Marker({ element: dotEl('#2e7d32') })).setLngLat([p.lng, p.lat]).addTo(map);
  }
  clearStart(): void { this.routeStart.set(null); this.startMarker?.remove(); }

  /** The pinned, editable route: start → vias → end, routed leg by leg (legs: each leg's line, for finding the dragged one). */
  private course: { from: LatLng; to: LatLng; vias: LatLng[]; legs: number[][][] } | null = null;
  routing = signal(false);
  private routeSeq = 0;
  /** The calculation of an opened link: its result (with the saved route) is fitted into view once. */
  private fitSeq = -1;
  private viaMarkers: maplibregl.Marker[] = [];
  dragging = false;
  editable = () => !!this.course?.legs.length && this.pinned && !this.routing() && !this.ruler;
  /** The small handle on the route where the mouse is near it (within ROUTE_HANDLE_PX): where a drag pulls out a via point. Click-through. */
  private routeHandle?: maplibregl.Marker;
  private hoverRoute(e: MapMouseEvent): void {
    const map = this.map, coords = this.courseRoute()?.coordinates;
    const near = !!map && !!coords && this.editable() && !this.dragging && !onMarker(e.originalEvent)
      && map.queryRenderedFeatures(e.point, { layers: ['route-hit'] }).length > 0;
    const r = near ? nearestOnPolyline(coords!.map(c => { const q = map!.project(c); return [q.x, q.y] as [number, number]; }), [e.point.x, e.point.y]) : null;
    if (!r || r.dist > ROUTE_HANDLE_PX) { this.routeHandle?.remove(); return; }
    (this.routeHandle ??= new maplibregl.Marker({ element: routeHandleEl() })).setLngLat(map!.unproject(r.point)).addTo(map!);
  }

  /** "Route hierher": a pinned route from the start to the clicked point, the distance in a popup there. */
  routeHere(): void { const s = this.start(), p = this.point(); if (s && p) this.startCourse({ lat: s.lat, lng: s.lng }, p); }

  private startCourse(s: LatLng, p: LatLng, vias: LatLng[] = []): void {
    const map = this.map; if (!map) return;
    this.hideRoute(true);
    this.course = { from: s, to: p, vias, legs: [] };
    this.draw([[s.lng, s.lat], [p.lng, p.lat]], straightNm(s, p), true);
    this.pinned = true;
    this.pointPopup = new maplibregl.Popup({ closeButton: true, closeOnClick: false, offset: 8 }).setLngLat([p.lng, p.lat]).setDOMContent(textEl(`${straightNm(s, p)} sm (Luftlinie) …`)).addTo(map);
    this.pointPopup.on('close', () => { if (this.pinned) this.hideRoute(true); });
    this.syncVias(); this.recalc();
  }

  // --- route links: the route's inputs and its line (ref) in the URL; a link with ref shows that route next to the newly calculated one ---
  /** The route of the URL the page opened with, until it is shown. */
  private link: RouteLink | null = parseRouteLink(this.location.path().split('?')[1] ?? '');
  private keepLinkedRoute = !!this.link;
  /** The current course's routed line, for saving it in a link. */
  courseRoute = signal<{ coordinates: [number, number][]; distanceNm: number } | null>(null);
  /** The route saved in the opened link (grey dashed). */
  refRoute = signal<{ coordinates: [number, number][]; nm: number } | null>(null);
  refVisible = signal(true);
  linkCopied = signal(false);

  /** Opens a route link: its settings become the ship context, the saved route is drawn and the route is calculated anew from its inputs. */
  private openLink(link: RouteLink): void {
    const map = this.map; if (!map) return;
    const ctx = this.shipCtx;
    if (ctx.minDepthM() !== link.minDepth || ctx.warnDepthM() !== link.warnDepth || ctx.airDraftM() !== link.airDraft) ctx.manual(link.minDepth, link.warnDepth, link.airDraft);
    if (link.ref) {
      this.refRoute.set(link.ref); this.refVisible.set(true);
      (map.getSource('route-ref') as GeoJSONSource).setData({ type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: link.ref.coordinates } });
      map.setLayoutProperty('route-ref-line', 'visibility', 'visible');
    }
    this.routeStart.set(link.from);
    (this.startMarker ??= new maplibregl.Marker({ element: dotEl('#2e7d32') })).setLngLat([link.from.lng, link.from.lat]).addTo(map);
    this.startCourse(link.from, link.to, link.vias);
    // the whole route once, even when the link has a view (c): now the saved route and the route points, again when the first calculation is in
    map.fitBounds(bounds([...(link.ref?.coordinates ?? []), ...[link.from, ...link.vias, link.to].map(p => [p.lng, p.lat] as [number, number])]), { padding: 60, animate: false });
    this.fitSeq = this.routeSeq;
  }

  /** "Route im Link speichern": the route's inputs and line into the URL (the view stays), the URL to the clipboard. */
  saveRouteLink(): void {
    const c = this.course, r = this.courseRoute(); if (!c || !r) return;
    const [path, query = ''] = this.location.path().split('?');
    this.location.replaceState(path, withParams(query, routeLinkParams(c.from, c.to, c.vias, this.shipCtx.routeParams(), r.coordinates, r.distanceNm)), this.location.getState());
    navigator.clipboard?.writeText(window.location.href).then(() => this.linkCopied.set(true), () => undefined);
    setTimeout(() => this.linkCopied.set(false), 2500);
  }
  toggleRef(): void { const v = !this.refVisible(); this.refVisible.set(v); this.map?.setLayoutProperty('route-ref-line', 'visibility', v ? 'visible' : 'none'); }
  removeRef(): void {
    this.refRoute.set(null);
    (this.map?.getSource('route-ref') as GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection', features: [] });
    const [path, query = ''] = this.location.path().split('?');
    this.location.replaceState(path, withParams(query, { ref: null }), this.location.getState());
  }

  /** The ship settings the shown route was calculated with (JSON of the routing parameters). */
  private routedWith = '';
  private pinnedPort?: NearbyPort;
  private settingsChanged(params: Record<string, number | string>): void {
    const key = JSON.stringify(params);
    if (key === this.routedWith || !this.pinned) return;
    if (this.course) this.recalc(); else if (this.pinnedPort) this.pinRoute(this.pinnedPort);
    const [path, query = ''] = this.location.path().split('?');
    if (!new URLSearchParams(query).has('from')) return;
    const s = (k: string) => params[k] == null ? null : String(params[k]);
    this.location.replaceState(path, withParams(query, { minDepth: s('minDepth'), warnDepth: s('warnDepth'), airDraft: s('airDraft') }), this.location.getState());
  }

  /** Routes every leg of the course (the route API per leg, in parallel), joins them and draws the result; the old line stays faded meanwhile. */
  private recalc(): void {
    const c = this.course, map = this.map; if (!c || !map) return;
    const pts = [c.from, ...c.vias, c.to], seq = ++this.routeSeq, popup = this.pointPopup;
    this.routedWith = JSON.stringify(this.shipCtx.routeParams());
    const busy = (b: boolean) => { this.routing.set(b); if (map.getLayer('route-line')) map.setPaintProperty('route-line', 'line-opacity', b ? 0.45 : 1); const el = popup?.getElement(); if (el) el.style.opacity = b ? '0.6' : ''; };
    busy(true);
    const straight = straightNm(c.from, c.to);
    forkJoin(pts.slice(1).map((to, i) => this.api.get<RouteLeg>('/api/routing/route', { from: `${pts[i].lat},${pts[i].lng}`, to: `${to.lat},${to.lng}`, ...this.shipCtx.routeParams() }))).subscribe({
      next: legs => {
        if (seq !== this.routeSeq || this.course !== c) return;
        busy(false);
        const r = joinLegs(legs);
        c.legs = legs.map(l => l.geometry.coordinates);
        this.courseRoute.set({ coordinates: r.coordinates, distanceNm: r.distanceNm });
        this.pinned = false; this.draw(r.coordinates, r.distanceNm, r.straight, !!r.warnings.length, r.warnings); this.pinned = true;
        if (seq === this.fitSeq) { this.fitSeq = -1; map.fitBounds(bounds([...r.coordinates, ...(this.refRoute()?.coordinates ?? [])]), { padding: 60, animate: false }); }
        popup?.setDOMContent(this.withAirDraftHint(textEl(r.straight ? `${r.distanceNm} sm (Luftlinie, keine Seeroute)` : `${r.distanceNm} sm · Luftlinie ${straight} sm${routeWarningText(r.warnings)}`), r.warnings));
      },
      // 422 "no sea route" (a point too far from navigable water or not connected): no line across land, the reason in the popup
      error: err => {
        if (seq !== this.routeSeq || this.course !== c) return;
        busy(false);
        c.legs = []; this.courseRoute.set(null);
        (map.getSource('route') as GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection', features: [] });
        popup?.setDOMContent(textEl(`${errorMessage(err)} (Luftlinie ${straight} sm)`));
      },
    });
  }

  /** Bridge warnings on a route without an air draft: a button under them that focuses the air draft field (entering the mast height
   *  drops the warnings for bridges it clears). */
  private withAirDraftHint(el: HTMLElement, warnings: RouteWarning[]): HTMLElement {
    if (!needsAirDraftHint(warnings, this.shipCtx.airDraftM())) return el;
    const hint = document.createElement('button');
    hint.type = 'button'; hint.className = 'air-draft-hint'; hint.textContent = this.translate.instant('ship.airDraftForBridges') as string;
    hint.style.cssText = 'display:block;margin-top:4px;padding:0;border:0;background:none;color:var(--mat-sys-primary,#1565c0);text-decoration:underline;cursor:pointer;font:inherit;text-align:left';
    hint.onclick = () => { const input = this.airDraftEl()?.nativeElement; input?.scrollIntoView({ block: 'nearest' }); input?.focus(); };
    const box = document.createElement('div'); box.append(el, hint);
    return box;
  }

  /** Mouse down on the route line: dragging pulls out a new via point between the two route points of that leg, routed again on release. */
  private dragRoute(e: MapLayerMouseEvent): void {
    const c = this.course, map = this.map;
    if (!c || !map || !this.editable() || e.originalEvent.button !== 0 || onMarker(e.originalEvent)) return;   // a via marker drags itself
    e.preventDefault();   // no map pan for this gesture
    const leg = nearestLeg(c.legs, e.lngLat.lng, e.lngLat.lat), canvas = map.getCanvas(), rect = canvas.getBoundingClientRect();
    const x0 = e.originalEvent.clientX, y0 = e.originalEvent.clientY;
    let at = e.lngLat, ghost: maplibregl.Marker | undefined;
    this.routeHandle?.remove();
    this.dragging = true; canvas.style.cursor = 'grabbing';
    const move = (ev: MouseEvent) => {
      if (!ghost && Math.hypot(ev.clientX - x0, ev.clientY - y0) < 4) return;   // a click, not a drag
      at = map.unproject([ev.clientX - rect.left, ev.clientY - rect.top]);
      (ghost ??= new maplibregl.Marker({ element: viaEl() }).setLngLat(at).addTo(map)).setLngLat(at);
    };
    const up = () => {
      document.removeEventListener('mousemove', move); document.removeEventListener('mouseup', up);
      this.dragging = false; canvas.style.cursor = '';
      ghost?.remove();
      if (!ghost || this.course !== c) return;
      c.vias = insertVia(c.vias, leg, { lat: at.lat, lng: at.lng });
      this.syncVias(); this.recalc();
    };
    document.addEventListener('mousemove', move); document.addEventListener('mouseup', up);
  }

  /** One draggable marker per via point (drag = move, right-click or × = remove), rebuilt after every change of the list. */
  private syncVias(): void {
    for (const m of this.viaMarkers) m.remove();
    this.viaMarkers = [];
    const c = this.course, map = this.map; if (!c || !map) return;
    this.viaMarkers = c.vias.map((v, i) => {
      const el = viaEl(true);
      const remove = (ev: Event) => { ev.preventDefault(); ev.stopPropagation(); if (this.course !== c) return; c.vias = c.vias.filter((_, k) => k !== i); this.syncVias(); this.recalc(); };
      el.querySelector('button')?.addEventListener('click', remove);
      el.addEventListener('contextmenu', remove);
      const m = new maplibregl.Marker({ element: el, draggable: true }).setLngLat([v.lng, v.lat]).addTo(map);
      m.on('dragend', () => { if (this.course !== c) return; const p = m.getLngLat(); c.vias = c.vias.map((x, k) => k === i ? { lat: p.lat, lng: p.lng } : x); this.recalc(); });
      return m;
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
  /** warned: the route uses an assumed passage (label "⚠"); a start/end on land gets a thin dotted line to the water the route starts/ends at. */
  private draw(coords: [number, number][], nm: number, dashed: boolean, warned = false, warnings?: RouteWarning[]): void {
    const map = this.map; if (!map) return;
    const onLand = (warnings ?? []).filter(w => (w.kind === 'startOnLand' || w.kind === 'endOnLand') && w.snappedLat != null && w.snappedLng != null)
      .map(w => ({ type: 'Feature' as const, properties: { onLand: true }, geometry: { type: 'LineString' as const, coordinates: [[w.snappedLng!, w.snappedLat!], [w.lng, w.lat]] } }));
    // "Tiefe prüfen": a lavender dot where the route crosses water charted below the warning depth (the note is in the popup)
    const checkDepth = (warnings ?? []).filter(w => w.kind === 'checkDepth')
      .map(w => ({ type: 'Feature' as const, properties: { checkDepth: true }, geometry: { type: 'Point' as const, coordinates: [w.lng, w.lat] } }));
    (map.getSource('route') as GeoJSONSource).setData({ type: 'FeatureCollection', features: [{ type: 'Feature', properties: { label: `${nm} sm${warned ? ' ⚠' : ''}`, dashed }, geometry: { type: 'LineString', coordinates: coords } }, ...onLand, ...checkDepth] });
    // the map stays where it is: hovering/pinning only draws (the view moves only when another harbour is selected)
  }
  hideRoute(force = false): void {
    if (this.pinned && !force) return;
    if (force) {
      const p = this.pointPopup; this.pointPopup = undefined; p?.remove();
      this.course = null; this.pinnedPort = undefined; this.routeSeq++; this.syncVias(); this.routeHandle?.remove(); this.routing.set(false); this.pinned = false; this.courseRoute.set(null);
      if (this.map?.getLayer('route-line')) this.map.setPaintProperty('route-line', 'line-opacity', 1);
    }
    this.hovered = null;
    (this.map?.getSource('route') as GeoJSONSource | undefined)?.setData({ type: 'FeatureCollection', features: [] });
  }
  pinRoute(p: NearbyPort): void { this.pinnedPort = p; this.routedWith = JSON.stringify(this.shipCtx.routeParams()); this.pinned = false; this.hovered = p.harbourId; this.showRoute(p); this.pinned = true; }

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
