import { Component, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, AuthService, errorMessage } from '../../core/api';
import { drafts, isNetworkError, OfflineQueue } from '../../core/offline';
import { EntryCrew, LogbookDefaults, LogbookEntry, Paged, RouteWarning, routeWarningText, Ship } from '../../core/models';
import { TranslateService } from '@ngx-translate/core';

interface WeatherSummary { samples: number; windBft: number; windKn: number; windDirection: string | null; gustKn: number | null; weather: string; seaState: string; waveHeightM: number | null; temperatureC: number | null; }
import { confirm, fmtDateTime, HarbourSelect, VisibilityPicker } from '../../shared/shared';
import { RichEditor } from '../../shared/rich-editor';

const WIND = ['N', 'NNE', 'NE', 'ENE', 'E', 'ESE', 'SE', 'SSE', 'S', 'SSW', 'SW', 'WSW', 'W', 'WNW', 'NW', 'NNW'];

/** Legacy paged/sortable logbook table (04 §4.6) for a ship, optionally filtered by trip. */
@Component({
  selector: 'app-logbook-table',
  imports: [FormsModule, RouterLink, MatButtonModule, MatIconModule, MatTableModule, MatPaginatorModule, MatSortModule, MatFormFieldModule, MatSelectModule, TranslatePipe],
  template: `
    <div class="row toolbar">
      @if (canWrite()) { <button mat-flat-button (click)="edit()"><mat-icon>add</mat-icon> {{ 'logbook.newEntry' | translate }}</button> }
      @if (!tripId()) { <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'logbook.year' | translate }}</mat-label><mat-select [(ngModel)]="year" (selectionChange)="load()"><mat-option [value]="null">{{ 'logbook.all' | translate }}</mat-option>@for (y of years; track y) { <mat-option [value]="y">{{ y }}</mat-option> }</mat-select></mat-form-field> }
      <span class="grow"></span><span class="muted">{{ total() }} {{ 'ship.entries' | translate }}</span>
    </div>
    <div class="scroll">
    <table mat-table [dataSource]="entries()" matSort (matSortChange)="sort($event)" class="full-width">
      <ng-container matColumnDef="departedAt"><th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'logbook.departed' | translate }}</th><td mat-cell *matCellDef="let e"><a [routerLink]="['/logbook', e.id]">{{ fmt(e.departedAt) }}</a></td></ng-container>
      <ng-container matColumnDef="start"><th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'logbook.startHarbour' | translate }}</th><td mat-cell *matCellDef="let e"><a [routerLink]="['/harbours', e.startHarbourId]">{{ e.startHarbour }}</a></td></ng-container>
      <ng-container matColumnDef="end"><th mat-header-cell *matHeaderCellDef mat-sort-header>{{ 'logbook.endHarbour' | translate }}</th><td mat-cell *matCellDef="let e"><a [routerLink]="['/harbours', e.endHarbourId]">{{ e.endHarbour }}</a></td></ng-container>
      <ng-container matColumnDef="arrivedAt"><th mat-header-cell *matHeaderCellDef>{{ 'logbook.arrived' | translate }}</th><td mat-cell *matCellDef="let e">{{ fmt(e.arrivedAt) }}</td></ng-container>
      <ng-container matColumnDef="duration"><th mat-header-cell *matHeaderCellDef>{{ 'logbook.duration' | translate }}</th><td mat-cell *matCellDef="let e">{{ e.duration }}</td></ng-container>
      <ng-container matColumnDef="distance"><th mat-header-cell *matHeaderCellDef>sm</th><td mat-cell *matCellDef="let e">{{ e.sailedDistanceNm }}</td></ng-container>
      <ng-container matColumnDef="wind"><th mat-header-cell *matHeaderCellDef>{{ 'logbook.windSpeed' | translate }}</th><td mat-cell *matCellDef="let e">{{ e.windDirection }} {{ e.windSpeed }}</td></ng-container>
      <ng-container matColumnDef="engine"><th mat-header-cell *matHeaderCellDef>{{ 'ship.engineHours' | translate }}</th><td mat-cell *matCellDef="let e">{{ e.engineHoursStart }} – {{ e.engineHoursEnd }}</td></ng-container>
      <ng-container matColumnDef="crew"><th mat-header-cell *matHeaderCellDef>{{ 'logbook.crew' | translate }}</th><td mat-cell *matCellDef="let e">{{ e.crewList }}</td></ng-container>
      <ng-container matColumnDef="visibility"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let e"><span class="chip" [class]="'chip ' + e.visibility.toLowerCase()">{{ 'visibility.' + e.visibility | translate }}</span></td></ng-container>
      <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let e">@if (e.canEdit || canWrite()) { <button mat-icon-button (click)="edit(e)"><mat-icon>edit</mat-icon></button><button mat-icon-button (click)="remove(e)"><mat-icon>delete</mat-icon></button> }</td></ng-container>
      <tr mat-header-row *matHeaderRowDef="cols"></tr><tr mat-row *matRowDef="let e; columns: cols" [attr.data-sel]="'entry:' + e.id" [class.sel]="selectedId() === e.id" (click)="rowSelect.emit(e.id)"></tr>
    </table></div>
    <mat-paginator [length]="total()" [pageSize]="pageSize" [pageSizeOptions]="[10, 25, 50, 100]" (page)="page($event)" />`,
  styles: `.scroll { overflow-x: auto; } .full-width { width: 100%; } .toolbar { padding: 8px 0; } tr.sel { background: #fff3e0; }`,
})
export class LogbookTable {
  private api = inject(Api); private dialog = inject(MatDialog);
  shipId = input.required<string>(); tripId = input<string | null>(null); canWrite = input(false);
  /** Trip page: opens the entry editor next to the map instead of the plain dialog; selection is synced with the map. */
  editor = input<((e?: LogbookEntry) => Promise<boolean>) | null>(null);
  selectedId = input<string | null>(null); rowSelect = output<string>();
  entries = signal<LogbookEntry[]>([]); total = signal(0);
  cols = ['departedAt', 'start', 'end', 'arrivedAt', 'duration', 'distance', 'wind', 'engine', 'crew', 'visibility', 'actions'];
  year: number | null = null; years = Array.from({ length: 15 }, (_, i) => new Date().getFullYear() - i);
  pageIndex = 0; pageSize = 25; sortField = ''; fmt = fmtDateTime;
  private offline = inject(OfflineQueue);
  constructor() { effect(() => { this.shipId(); this.tripId(); this.offline.flushed(); this.load(); }); } // reloads after queued entries were sent
  load(): void {
    this.api.get<Paged<LogbookEntry>>(`/api/ships/${this.shipId()}/logbook`, { trip: this.tripId(), year: this.year, page: this.pageIndex + 1, pageSize: this.pageSize, sort: this.sortField })
      .subscribe(p => { this.entries.set(p.items); this.total.set(p.total); });
  }
  page(e: PageEvent): void { this.pageIndex = e.pageIndex; this.pageSize = e.pageSize; this.load(); }
  sort(s: Sort): void { this.sortField = s.direction ? s.active : ''; this.load(); }
  edit(e?: LogbookEntry): void {
    const editor = this.editor();
    if (editor) { void editor(e).then(ok => ok && this.load()); return; }
    this.dialog.open(EntryDialog, { data: { shipId: this.shipId(), tripId: this.tripId(), entry: e }, width: '800px' }).afterClosed().subscribe(ok => ok && this.load()); }
  async remove(e: LogbookEntry): Promise<void> { if (await confirm(this.dialog, 'Eintrag löschen?')) this.api.delete(`/api/logbook/${e.id}`).subscribe(() => this.load()); }
}

@Component({
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslatePipe, LogbookTable],
  template: `
    <div class="page">
      <div class="row"><h1 class="grow">{{ ship()?.name }} – {{ 'logbook.title' | translate }}</h1><a mat-stroked-button [routerLink]="['/ships', id()]"><mat-icon>directions_boat</mat-icon> {{ ship()?.name }}</a></div>
      @if (ship(); as s) { <app-logbook-table [shipId]="s.id" [canWrite]="!!s.myRole" /> }
    </div>`,
})
export class LogbookPage {
  private api = inject(Api); id = input.required<string>(); ship = signal<Ship | null>(null);
  ngOnInit(): void { this.api.get<Ship>(`/api/ships/${this.id()}`).subscribe(s => this.ship.set(s)); }
}

/** Entry dialog with the legacy fields (04 §4.6) plus weather/sea state/sails; defaults from the previous entry (05 §5.4). */
@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, TranslatePipe, HarbourSelect, VisibilityPicker, RichEditor],
  template: `
    <h2 mat-dialog-title>{{ data.entry ? ('common.edit' | translate) : ('logbook.newEntry' | translate) }}</h2>
    <mat-dialog-content class="form-grid" (input)="saveDraft()" (change)="saveDraft()">
      <p class="muted full draft-hint">{{ 'logbook.draftHint' | translate }}@if (restored()) { · {{ 'logbook.draftRestored' | translate }} <button mat-button (click)="discardDraft()">{{ 'logbook.discardDraft' | translate }}</button> }</p>
      <app-harbour-select [label]="'logbook.startHarbour' | translate" [(harbourId)]="m.startHarbourId" [harbourName]="startName()" [required]="true" (selected)="startName.set($event.name); loadRoute()" />
      <app-harbour-select [label]="'logbook.endHarbour' | translate" [(harbourId)]="m.endHarbourId" [harbourName]="endName()" [required]="true" (selected)="endName.set($event.name); loadRoute()" />
      @if (data.map) {
        <div class="full row">
          <button mat-stroked-button type="button" (click)="pickEnd()"><mat-icon>ads_click</mat-icon> {{ 'logbook.pickEnd' | translate }}</button>
          <button mat-stroked-button type="button" (click)="loadRoute()" [disabled]="!m.startHarbourId || !m.endHarbourId"><mat-icon>route</mat-icon> {{ 'logbook.loadRoute' | translate }}</button>
          <button mat-button type="button" (click)="resetRoute()"><mat-icon>undo</mat-icon> {{ 'logbook.resetRoute' | translate }}</button>
          <span class="muted">{{ routeHint() }}</span>
        </div>
        <p class="muted full route-help">{{ 'logbook.routeHelp' | translate }}</p>
      }
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.departed' | translate }}</mat-label><input matInput type="datetime-local" [(ngModel)]="m.departedAt" required /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.arrived' | translate }}</mat-label><input matInput type="datetime-local" [(ngModel)]="m.arrivedAt" required /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.engineStart' | translate }}</mat-label><input matInput type="number" step="0.1" [(ngModel)]="m.engineHoursStart" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.engineEnd' | translate }}</mat-label><input matInput type="number" step="0.1" [(ngModel)]="m.engineHoursEnd" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.logStart' | translate }}</mat-label><input matInput type="number" step="0.1" [(ngModel)]="m.logStart" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.logEnd' | translate }}</mat-label><input matInput type="number" step="0.1" [(ngModel)]="m.logEnd" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.windDirection' | translate }}</mat-label><mat-select [(ngModel)]="m.windDirection"><mat-option [value]="null">–</mat-option>@for (w of wind; track w) { <mat-option [value]="w">{{ w }}</mat-option> }</mat-select></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.windSpeed' | translate }}</mat-label><input matInput [(ngModel)]="m.windSpeed" placeholder="4 Bft" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.weather' | translate }}</mat-label><input matInput [(ngModel)]="m.weather" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.seaState' | translate }}</mat-label><input matInput [(ngModel)]="m.seaState" /></mat-form-field>
      @if (data.tripId) { <div class="full"><button mat-stroked-button type="button" (click)="weatherFromTrack()" [disabled]="!m.departedAt || !m.arrivedAt"><mat-icon>air</mat-icon> {{ 'logbook.weatherFromTrack' | translate }}</button> @if (weatherHint()) { <span class="muted"> {{ weatherHint() }}</span> }</div> }
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.distance' | translate }}</mat-label><input matInput type="number" step="0.1" [(ngModel)]="m.sailedDistanceNm" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.sails' | translate }}</mat-label><input matInput [(ngModel)]="m.sailsNotes" /></mat-form-field>
      <div class="full"><b>{{ 'logbook.crew' | translate }}:</b>
        @for (c of crew; track $index) { <mat-checkbox [(ngModel)]="c.selected">{{ c.name }}</mat-checkbox> }
        <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Gast hinzufügen</mat-label><input matInput [(ngModel)]="guest" (keydown.enter)="addGuest(); $event.preventDefault()" /></mat-form-field></div>
      <sms-rich-editor class="full" [label]="'logbook.notes' | translate" [value]="m.notes" (valueChange)="m.notes = $event; saveDraft()"
        [context]="{ shipId: data.shipId, tripId: data.tripId, logbookEntryId: data.entry?.id, visibility: m.visibility }" />
      <app-visibility [(value)]="m.visibility" />
      @if (error()) { <div class="error full">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
})
export class EntryDialog {
  data = inject<{ shipId: string; tripId: string | null; entry?: LogbookEntry; map?: EntryMapBridge; defaultStart?: { id: string; name: string } | null; suggestion?: { departedAt: string; arrivedAt: string; startHarbourId: string | null; startHarbourName: string | null; endHarbourId: string | null; endHarbourName: string | null; sailedDistanceNm: number } }>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api); private auth = inject(AuthService); private translate = inject(TranslateService);
  wind = WIND; error = signal(''); startName = signal<string | null>(this.data.entry?.startHarbour ?? null); endName = signal<string | null>(this.data.entry?.endHarbour ?? null);
  crew: (EntryCrew & { selected: boolean })[] = []; guest = '';
  private offline = inject(OfflineQueue); restored = signal(false);
  private draftScope = `logbook:${this.data.shipId}:${this.data.tripId ?? 'ship'}:${this.data.entry?.id ?? 'new'}`;
  private e = this.data.entry;
  m = { startHarbourId: this.e?.startHarbourId ?? null as string | null, endHarbourId: this.e?.endHarbourId ?? null as string | null,
    departedAt: local(this.e?.departedAt), arrivedAt: local(this.e?.arrivedAt), engineHoursStart: this.e?.engineHoursStart ?? null, engineHoursEnd: this.e?.engineHoursEnd ?? null,
    logStart: this.e?.logStart ?? null, logEnd: this.e?.logEnd ?? null, windSpeed: this.e?.windSpeed ?? '', windDirection: this.e?.windDirection ?? null, weather: this.e?.weather ?? '', seaState: this.e?.seaState ?? '',
    sailedDistanceNm: this.e?.sailedDistanceNm ?? null, sailsNotes: this.e?.sailsNotes ?? '', notes: this.e?.notes ?? '', visibility: this.e?.visibility ?? this.auth.me()?.defaultContentVisibility ?? 'Crew' as const };

  constructor() {
    const draft = drafts.load<EntryDraft>(this.draftScope);
    if (draft) { this.m = draft.m; this.crew = draft.crew; this.startName.set(draft.startName); this.endName.set(draft.endName); this.restored.set(true); this.initRoute(); return; }
    if (this.e) { this.crew = this.e.crew.map(c => ({ ...c, selected: true })); this.initRoute(); return; }
    this.api.get<LogbookDefaults>(`/api/ships/${this.data.shipId}/logbook/new-defaults`, { trip: this.data.tripId }).subscribe(d => {
      const start = d.startHarbourId ? { id: d.startHarbourId, name: d.startHarbourName ?? '' } : this.data.defaultStart ?? null; // previous entry's end, else the plan's current stop
      this.m.startHarbourId = start?.id ?? null; this.startName.set(start?.name ?? null); this.m.engineHoursStart = d.engineHoursStart; this.m.logStart = d.logStart;
      this.crew = d.crew.map(c => ({ ...c, selected: true }));
      const s = this.data.suggestion;
      if (s) { // prefilled from a detected harbour stop (spec 12 §12.2)
        this.m.departedAt = local(s.departedAt); this.m.arrivedAt = local(s.arrivedAt); this.m.sailedDistanceNm = s.sailedDistanceNm;
        if (s.startHarbourId) { this.m.startHarbourId = s.startHarbourId; this.startName.set(s.startHarbourName); }
        if (s.endHarbourId) { this.m.endHarbourId = s.endHarbourId; this.endName.set(s.endHarbourName); }
      }
      this.initRoute();
    });
  }

  // --- route on the trip map (docs/14): routed line between the harbours, adjusted on the map, stored as routeGeometry
  routeHint = signal(''); private routeReset = false;
  private initRoute(): void {
    const stored = this.e?.routeGeometry?.coordinates;
    if (stored?.length) this.data.map?.showRoute(stored); else this.loadRoute();
  }
  loadRoute(): void {
    const map = this.data.map; const from = this.m.startHarbourId, to = this.m.endHarbourId;
    if (!map || !from || !to) return;
    this.routeReset = false; this.routeHint.set('…');
    this.api.get<{ distanceNm: number; geometry: { coordinates: number[][] }; warnings?: RouteWarning[] }>('/api/routing/route', { fromHarbour: from, toHarbour: to }).subscribe({
      next: r => { map.showRoute(r.geometry.coordinates); this.routeHint.set(`${r.distanceNm} sm${routeWarningText(r.warnings)}`); this.m.sailedDistanceNm ??= r.distanceNm; },
      error: err => this.routeHint.set(errorMessage(err)),
    });
  }
  async pickEnd(): Promise<void> {
    const h = await this.data.map?.pickHarbour(this.m.startHarbourId);
    if (!h) return;
    this.m.endHarbourId = h.id; this.endName.set(h.name); this.saveDraft(); this.loadRoute();
  }
  resetRoute(): void { this.routeReset = true; this.data.map?.showRoute(null); this.routeHint.set(''); }
  private routeGeometry(): { type: 'LineString'; coordinates: number[][] } | null {
    if (!this.data.map) return null; // null keeps the stored route
    if (this.routeReset) return { type: 'LineString', coordinates: [] }; // empty clears it
    const c = this.data.map.route();
    return c && c.length >= 2 ? { type: 'LineString', coordinates: c } : null;
  }
  weatherHint = signal('');
  /** Wind, weather and sea state condensed from the weather recorded along the live track of this leg. */
  weatherFromTrack(): void {
    const from = new Date(this.m.departedAt).toISOString(), to = new Date(this.m.arrivedAt).toISOString();
    this.api.get<WeatherSummary | null>(`/api/trips/${this.data.tripId}/weather/summary`, { from, to }).subscribe({
      next: s => {
        if (!s) { this.weatherHint.set(this.translate.instant('logbook.noWeatherData')); return; }
        this.m.windDirection = s.windDirection; this.m.windSpeed = `${s.windBft} Bft`; this.m.weather = s.weather; this.m.seaState = s.seaState;
        this.weatherHint.set(this.translate.instant('logbook.weatherApplied', { n: s.samples })); this.saveDraft();
      },
      error: e => this.weatherHint.set(errorMessage(e)),
    });
  }
  addGuest(): void { if (this.guest.trim()) { this.crew.push({ crewMemberId: null, userId: null, name: this.guest.trim(), selected: true }); this.guest = ''; this.saveDraft(); } }
  saveDraft(): void { drafts.save(this.draftScope, { m: this.m, crew: this.crew, startName: this.startName(), endName: this.endName() }); }
  discardDraft(): void { drafts.clear(this.draftScope); this.ref.close(false); }
  save(): void {
    if (!this.m.startHarbourId || !this.m.endHarbourId || !this.m.departedAt || !this.m.arrivedAt) { this.error.set('Start- und Zielhafen, Ablegen und Ankunft angeben.'); return; }
    const body = { ...this.m, tripId: this.data.tripId, routeGeometry: this.routeGeometry(), departedAt: new Date(this.m.departedAt).toISOString(), arrivedAt: new Date(this.m.arrivedAt).toISOString(),
      crew: this.crew.filter(c => c.selected).map(c => ({ crewMemberId: c.crewMemberId, userId: c.userId, guestName: c.crewMemberId || c.userId ? null : c.name })) };
    const method = this.e ? 'PUT' : 'POST'; const url = this.e ? `/api/logbook/${this.e.id}` : `/api/ships/${this.data.shipId}/logbook`;
    const done = () => { drafts.clear(this.draftScope); this.ref.close(true); };
    const req = this.e ? this.api.put(url, body) : this.api.post(url, body);
    req.subscribe({
      next: done,
      error: async err => {
        // on board without signal: keep the entry and send it when the connection is back (spec 07 H2)
        if (isNetworkError(err)) { await this.offline.enqueueJson(method, url, body, `Logbucheintrag ${this.startName() ?? ''} → ${this.endName() ?? ''}`.trim()); done(); }
        else this.error.set(errorMessage(err));
      },
    });
  }
}

/** The trip page's map, handed to the entry dialog: harbour pick and the editable route line. */
export interface EntryMapBridge {
  pickHarbour(from: string | null): Promise<{ id: string; name: string } | null>;
  showRoute(coords: number[][] | null): void;
  route(): number[][] | null;
}

type EntryDraft = { m: EntryDialog['m']; crew: EntryDialog['crew']; startName: string | null; endName: string | null };

function local(iso: string | undefined): string {
  const d = iso ? new Date(iso) : new Date();
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}
