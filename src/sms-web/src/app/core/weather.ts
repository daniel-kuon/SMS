import { Component, computed, inject, input, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { TranslatePipe } from '@ngx-translate/core';
import { Api } from './api';
import { formatPosition } from './seamark-icons';
import { fmtDate } from '../shared/shared';

/** Forecast as served by /api/weather (provider-neutral shape; the API hides Open-Meteo or whatever replaces it). */
export interface WeatherPoint { time: string; temperatureC: number | null; windKn: number | null; windDirDeg: number | null; gustKn: number | null; precipitationMm: number | null; precipitationChance: number | null; weatherCode: number | null; pressureHpa: number | null; cloudCover: number | null; waveHeightM: number | null; waveDirDeg: number | null; wavePeriodS: number | null; }
export interface WeatherDay { date: string; weatherCode: number | null; tempMinC: number | null; tempMaxC: number | null; windMaxKn: number | null; gustMaxKn: number | null; windDirDeg: number | null; precipitationMm: number | null; sunrise: string | null; sunset: string | null; }
export interface TideExtreme { time: string; levelM: number; high: boolean; }
export interface TideForecast { provider: string; stationId: string; stationName: string; stationLat: number; stationLng: number; distanceKm: number; datum: string; attribution: string; commercialUse: boolean; extremes: TideExtreme[]; }
export interface WeatherForecast { provider: string; lat: number; lng: number; timeZone: string; current: WeatherPoint; hourly: WeatherPoint[]; daily: WeatherDay[]; fetchedAt: string; }
/** Observation stored along a live track (/api/trips/{id}/weather). */
export interface WeatherObservation extends Omit<WeatherPoint, 'precipitationChance'> { id: number; lat: number; lng: number; }

/** WMO weather interpretation codes (as used by Open-Meteo) → material icon + i18n key. */
export function weatherCodeInfo(code: number | null | undefined): { icon: string; key: string } {
  if (code == null) return { icon: 'help', key: 'unknown' };
  if (code === 0) return { icon: 'wb_sunny', key: 'clear' };
  if (code <= 2) return { icon: 'partly_cloudy_day', key: 'partlyCloudy' };
  if (code === 3) return { icon: 'cloud', key: 'overcast' };
  if (code <= 48) return { icon: 'foggy', key: 'fog' };
  if (code <= 57) return { icon: 'grain', key: 'drizzle' };
  if (code <= 67) return { icon: 'rainy', key: 'rain' };
  if (code <= 77) return { icon: 'weather_snowy', key: 'snow' };
  if (code <= 82) return { icon: 'rainy', key: 'showers' };
  if (code <= 86) return { icon: 'weather_snowy', key: 'snowShowers' };
  return { icon: 'thunderstorm', key: 'thunderstorm' };
}

/** Beaufort number for a wind speed in knots. */
export function beaufort(kn: number | null | undefined): number | null {
  if (kn == null) return null;
  const limits = [1, 3, 6, 10, 16, 21, 27, 33, 40, 47, 55, 63];
  const i = limits.findIndex(l => kn < l);
  return i === -1 ? 12 : i;
}

/** Compass point (N, NNO, …) for a bearing in degrees, German abbreviations as on nautical charts. */
export function compass(deg: number | null | undefined): string {
  if (deg == null) return '–';
  const pts = ['N', 'NNO', 'NO', 'ONO', 'O', 'OSO', 'SO', 'SSO', 'S', 'SSW', 'SW', 'WSW', 'W', 'WNW', 'NW', 'NNW'];
  return pts[Math.round((((deg % 360) + 360) % 360) / 22.5) % 16];
}

export const fmtHour = (iso: string) => new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
export const fmtWeekday = (date: string) => new Date(date + 'T12:00:00').toLocaleDateString(undefined, { weekday: 'short', day: '2-digit', month: '2-digit' });

/** Current conditions, the next 24 hours and the 7-day outlook for one position. */
@Component({
  selector: 'app-weather-card',
  imports: [MatIconModule, DecimalPipe, TranslatePipe],
  template: `
    @if (forecast(); as f) {
      <div class="current">
        <mat-icon class="big">{{ info(f.current.weatherCode).icon }}</mat-icon>
        <div class="grow">
          <div class="temp">{{ f.current.temperatureC | number:'1.0-0' }} °C <span class="muted">{{ ('weather.code.' + info(f.current.weatherCode).key) | translate }}</span></div>
          <div class="wind"><mat-icon inline [style.transform]="arrow(f.current.windDirDeg)">navigation</mat-icon> {{ compass(f.current.windDirDeg) }} {{ f.current.windKn | number:'1.0-0' }} kn ({{ bft(f.current.windKn) }} Bft)
            @if (f.current.gustKn) { · {{ 'weather.gusts' | translate }} {{ f.current.gustKn | number:'1.0-0' }} kn }</div>
          <div class="muted small">
            @if (f.current.pressureHpa) { {{ f.current.pressureHpa | number:'1.0-0' }} hPa · }
            @if (f.current.waveHeightM != null) { {{ 'weather.waves' | translate }} {{ f.current.waveHeightM | number:'1.1-1' }} m {{ compass(f.current.waveDirDeg) }} @if (f.current.wavePeriodS) { / {{ f.current.wavePeriodS | number:'1.0-0' }} s } · }
            @if (f.current.precipitationMm) { {{ f.current.precipitationMm | number:'1.1-1' }} mm · }
            {{ 'weather.asOf' | translate }} {{ hour(f.current.time) }}
          </div>
        </div>
      </div>
      <div class="hours">
        @for (h of hours(); track h.time) {
          <div class="hour">
            <div class="muted small">{{ hour(h.time) }}</div>
            <mat-icon>{{ info(h.weatherCode).icon }}</mat-icon>
            <div>{{ h.temperatureC | number:'1.0-0' }}°</div>
            <div class="small"><mat-icon inline [style.transform]="arrow(h.windDirDeg)">navigation</mat-icon> {{ h.windKn | number:'1.0-0' }}</div>
            @if (h.waveHeightM != null) { <div class="muted small">{{ h.waveHeightM | number:'1.1-1' }} m</div> }
            @if (h.precipitationChance) { <div class="muted small">{{ h.precipitationChance }} %</div> }
          </div>
        }
      </div>
      <table class="days">
        @for (d of f.daily; track d.date) {
          <tr>
            <td>{{ weekday(d.date) }}</td>
            <td><mat-icon inline>{{ info(d.weatherCode).icon }}</mat-icon></td>
            <td>{{ d.tempMinC | number:'1.0-0' }} / {{ d.tempMaxC | number:'1.0-0' }} °C</td>
            <td><mat-icon inline [style.transform]="arrow(d.windDirDeg)">navigation</mat-icon> {{ compass(d.windDirDeg) }} {{ d.windMaxKn | number:'1.0-0' }} kn @if (d.gustMaxKn) { <span class="muted">({{ d.gustMaxKn | number:'1.0-0' }})</span> }</td>
            <td class="muted">@if (d.precipitationMm) { {{ d.precipitationMm | number:'1.1-1' }} mm }</td>
          </tr>
        }
      </table>
      <div class="muted small">{{ 'weather.position' | translate }}: {{ pos(f) }} · {{ 'weather.source' | translate }}: {{ f.provider }}</div>
      @if (tides(); as t) {
        <div class="tides">
          <div><mat-icon inline>waves</mat-icon> <b>{{ 'weather.tides' | translate }}</b> <span class="muted small">{{ t.stationName }} · {{ t.distanceKm | number:'1.0-0' }} km · {{ t.datum }}</span></div>
          <div class="tide-row">
            @for (e of nextTides(); track e.time) {
              <div class="tide"><div class="muted small">{{ weekday(e.time.slice(0, 10)) }}</div><div>{{ e.high ? 'HW' : 'NW' }} {{ hour(e.time) }}</div><div class="small">{{ e.levelM | number:'1.1-2' }} m</div></div>
            }
          </div>
          <div class="muted tiny">{{ t.attribution.split('. Source')[0] }}</div>
        </div>
      }
    } @else if (error()) { <p class="muted">{{ error() }}</p> } @else { <p class="muted">{{ 'common.loading' | translate }}</p> }`,
  styles: `
    .current { display: flex; gap: 10px; align-items: center; } .big { font-size: 48px; width: 48px; height: 48px; } .temp { font-size: 20px; } .grow { flex: 1; }
    .hours { display: flex; gap: 2px; overflow-x: auto; margin: 8px 0; } .hour { min-width: 46px; text-align: center; padding: 4px 2px; border-radius: 4px; background: #f4f7fa; }
    .days { width: 100%; border-collapse: collapse; } .days td { padding: 3px 4px; border-bottom: 1px solid #eee; white-space: nowrap; }
    .small { font-size: 12px; } .tiny { font-size: 10px; } mat-icon[inline] { vertical-align: middle; }
    .tides { margin-top: 8px; border-top: 1px solid #eee; padding-top: 6px; } .tide-row { display: flex; gap: 2px; overflow-x: auto; } .tide { min-width: 70px; text-align: center; padding: 4px 2px; border-radius: 4px; background: #eef4fa; }
  `,
})
export class WeatherCard {
  forecast = input<WeatherForecast | null>(null);
  error = input<string | null>(null);
  tides = input<TideForecast | null>(null);
  nextTides = computed(() => (this.tides()?.extremes ?? []).filter(e => new Date(e.time).getTime() > Date.now() - 3600_000).slice(0, 8));
  hours = computed(() => this.forecast()?.hourly.slice(0, 24) ?? []);
  info = weatherCodeInfo; compass = compass; bft = beaufort; hour = fmtHour; weekday = fmtWeekday;
  arrow = (deg: number | null) => deg == null ? '' : `rotate(${(deg + 180) % 360}deg)`; // wind blows *from* deg: arrow points where it goes
  pos = (f: WeatherForecast) => formatPosition(f.lat, f.lng);
}

/** Opened from the map context menu ("weather forecast here") with { lat, lng }. */
@Component({
  imports: [MatDialogModule, MatButtonModule, WeatherCard, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'weather.forecast' | translate }}</h2>
    <mat-dialog-content><app-weather-card [forecast]="forecast()" [error]="error()" [tides]="tides()" /></mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.close' | translate }}</button></mat-dialog-actions>`,
})
export class WeatherDialog {
  private api = inject(Api);
  readonly data = inject<{ lat: number; lng: number }>(MAT_DIALOG_DATA);
  forecast = signal<WeatherForecast | null>(null);
  error = signal<string | null>(null);
  tides = signal<TideForecast | null>(null);
  constructor() { loadForecast(this.api, '/api/weather', { lat: this.data.lat, lng: this.data.lng }, this.forecast, this.error); loadTides(this.api, '/api/weather/tides', { lat: this.data.lat, lng: this.data.lng }, this.tides); }
}

export function loadTides(api: Api, url: string, params: Record<string, number> | undefined, tides: { set(v: TideForecast | null): void }): void {
  tides.set(null);
  api.get<TideForecast | null>(url, params).subscribe({ next: t => tides.set(t ?? null), error: () => tides.set(null) });
}

export function loadForecast(api: Api, url: string, params: Record<string, number> | undefined, forecast: { set(v: WeatherForecast | null): void }, error: { set(v: string | null): void }): void {
  forecast.set(null); error.set(null);
  api.get<WeatherForecast>(url, params).subscribe({ next: f => forecast.set(f), error: () => error.set('Wetterdienst nicht erreichbar.') });
}

export const fmtObservation = (o: WeatherObservation) => `${fmtDate(o.time)} ${fmtHour(o.time)}: ${o.temperatureC?.toFixed(0) ?? '–'} °C, ${compass(o.windDirDeg)} ${o.windKn?.toFixed(0) ?? '–'} kn${o.waveHeightM != null ? `, ${o.waveHeightM.toFixed(1)} m` : ''}`;
