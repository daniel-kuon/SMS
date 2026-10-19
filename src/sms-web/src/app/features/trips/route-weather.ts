import { Component, effect, inject, input, OnDestroy, signal, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, errorMessage } from '../../core/api';
import { maplibregl, MlMap } from '../../core/map';
import { beaufort, compass } from '../../core/weather';
import { fmtDateTime } from '../../shared/shared';

/** POST /api/weather/route */
export interface RouteWeatherPoint { no: number; lat: number; lng: number; distanceNm: number; headingDeg: number; eta: string; tacking: boolean; forecast: boolean; windKn: number | null; windDirDeg: number | null; gustKn: number | null; waveHeightM: number | null; waveDirDeg: number | null; }
export interface RouteWeatherResult { departure: string; arrival: string; durationHours: number; distanceNm: number; provider: string; points: RouteWeatherPoint[]; }

/** "2026-06-01T09:00" for a datetime-local input: the planned day at 09:00, else the next full hour. */
export function defaultDeparture(date: string | null, now = new Date()): string {
  if (date) return `${date.slice(0, 10)}T09:00`;
  const d = new Date(now); d.setMinutes(60, 0, 0);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:00`;
}
export const fmtDuration = (h: number) => { const m = Math.round(h * 60); return `${Math.floor(m / 60)}:${String(m % 60).padStart(2, "0")} h`; };

/** Weather along a routed line (docs/13): departure + speed → points every 5 nm with ETA and the forecast then; wind arrows on the map. */
@Component({
  selector: 'app-route-weather',
  imports: [DecimalPipe, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="rw">
      <b><mat-icon inline>air</mat-icon> {{ 'weather.route.title' | translate }}</b>
      <div class="form">
        <label>{{ 'weather.route.departure' | translate }} <input type="datetime-local" [value]="departure()" (change)="departure.set($any($event.target).value)" /></label>
        <label>{{ 'weather.route.speed' | translate }} <input type="number" min="0.5" max="40" step="0.5" [value]="speed()" (change)="speed.set(+$any($event.target).value)" /></label>
        <button mat-stroked-button (click)="estimate()" [disabled]="busy() || !departure() || !(speed() > 0)">{{ 'weather.route.estimate' | translate }}</button>
      </div>
      @if (error()) { <p class="muted">{{ error() }}</p> }
      @if (result(); as r) {
        <p><b>{{ 'weather.route.duration' | translate }}: {{ dur(r.durationHours) }}</b> · {{ 'weather.route.arrival' | translate }}: <b>{{ fmt(r.arrival) }}</b> · {{ r.distanceNm | number:'1.0-1' }} sm</p>
        <table>
          <tr><th>{{ 'weather.route.point' | translate }}</th><th>ETA</th><th>{{ 'weather.route.wind' | translate }}</th><th>{{ 'weather.gusts' | translate }}</th><th>{{ 'weather.waves' | translate }}</th></tr>
          @for (p of r.points; track p.no) {
            <tr [class.tack]="p.tacking" [title]="p.tacking ? ('weather.route.tacking' | translate) : ''">
              <td>{{ p.no }}</td><td>{{ fmt(p.eta) }}</td>
              @if (p.forecast) {
                <td><mat-icon inline [style.transform]="arrow(p.windDirDeg)">navigation</mat-icon> {{ compass(p.windDirDeg) }} {{ p.windKn | number:'1.0-0' }} kn ({{ bft(p.windKn) }} Bft)</td>
                <td>@if (p.gustKn != null) { {{ p.gustKn | number:'1.0-0' }} kn }</td>
                <td>@if (p.waveHeightM != null) { {{ p.waveHeightM | number:'1.1-1' }} m }</td>
              } @else { <td colspan="3" class="muted">{{ 'weather.route.noForecast' | translate }}</td> }
            </tr>
          }
        </table>
        <p class="muted small">{{ 'weather.route.hint' | translate }} {{ 'weather.source' | translate }}: {{ r.provider }}</p>
      }
    </div>`,
  styles: `
    .rw { margin: 10px 0; padding: 8px; border-radius: 6px; background: #f4f7fa; font-size: 13px; }
    .form { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin: 6px 0; } .form input[type=number] { width: 60px; }
    table { width: 100%; border-collapse: collapse; } td, th { padding: 2px 4px; border-bottom: 1px solid #e3e8ee; text-align: left; white-space: nowrap; }
    tr.tack td:first-child { box-shadow: inset 3px 0 #fb8c00; } .small { font-size: 11px; } mat-icon[inline] { vertical-align: middle; }`,
})
export class RouteWeather implements OnDestroy {
  private api = inject(Api);
  /** GeoJSON positions [lng, lat] of the routed leg. */
  coordinates = input.required<number[][]>();
  /** Planned day (yyyy-MM-dd…) for the default departure. */
  date = input<string | null>(null);
  map = input<MlMap | null>(null);
  departure = signal(''); speed = signal(5);
  result = signal<RouteWeatherResult | null>(null); error = signal<string | null>(null); busy = signal(false);
  private markers: maplibregl.Marker[] = [];
  fmt = fmtDateTime; compass = compass; bft = beaufort; dur = fmtDuration;
  arrow = (deg: number | null) => deg == null ? '' : `rotate(${(deg + 180) % 360}deg)`; // wind *from* deg: the arrow points where it blows

  constructor() {
    // another leg: fresh default departure, old result and arrows gone
    effect(() => { this.coordinates(); const d = this.date(); untracked(() => { this.departure.set(defaultDeparture(d)); this.show(null); }); });
  }
  ngOnDestroy(): void { this.show(null); }

  estimate(): void {
    this.busy.set(true); this.error.set(null);
    const body = { coordinates: this.coordinates(), departure: new Date(this.departure()).toISOString(), speedKn: this.speed() };
    this.api.post<RouteWeatherResult>('/api/weather/route', body).subscribe({
      next: r => { this.busy.set(false); this.show(r); },
      error: e => { this.busy.set(false); this.show(null); this.error.set(errorMessage(e)); },
    });
  }

  private show(r: RouteWeatherResult | null): void {
    this.result.set(r);
    for (const m of this.markers) m.remove(); this.markers = [];
    const map = this.map(); if (!r || !map) return;
    for (const p of r.points) {
      const el = document.createElement('div');
      el.style.cssText = `display:flex;align-items:center;gap:2px;padding:1px 5px 1px 2px;border-radius:10px;font:11px sans-serif;white-space:nowrap;background:rgba(255,255,255,.92);box-shadow:0 1px 3px rgba(0,0,0,.35);border:1.5px solid ${p.tacking ? '#fb8c00' : '#1b4d7a'}`;
      const icon = document.createElement('span'); icon.className = 'material-symbols-outlined'; icon.textContent = p.forecast ? 'navigation' : 'help';
      icon.style.cssText = `font-size:16px;color:#1b4d7a;${p.forecast && p.windDirDeg != null ? `transform:${this.arrow(p.windDirDeg)}` : ''}`;
      el.append(icon, p.forecast ? `${p.windKn?.toFixed(0) ?? '–'} kn · ${beaufort(p.windKn) ?? '–'} Bft` : '–');
      el.title = `${p.no}: ${fmtDateTime(p.eta)}`;
      this.markers.push(new maplibregl.Marker({ element: el }).setLngLat([p.lng, p.lat]).addTo(map));
    }
  }
}
