import { Component, effect, inject, input, signal } from '@angular/core';
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
import { EntryCrew, LogbookDefaults, LogbookEntry, Paged, Ship } from '../../core/models';
import { confirm, fmtDateTime, HarbourSelect, VisibilityPicker } from '../../shared/shared';

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
      <tr mat-header-row *matHeaderRowDef="cols"></tr><tr mat-row *matRowDef="let e; columns: cols"></tr>
    </table></div>
    <mat-paginator [length]="total()" [pageSize]="pageSize" [pageSizeOptions]="[10, 25, 50, 100]" (page)="page($event)" />`,
  styles: `.scroll { overflow-x: auto; } .full-width { width: 100%; } .toolbar { padding: 8px 0; }`,
})
export class LogbookTable {
  private api = inject(Api); private dialog = inject(MatDialog);
  shipId = input.required<string>(); tripId = input<string | null>(null); canWrite = input(false);
  entries = signal<LogbookEntry[]>([]); total = signal(0);
  cols = ['departedAt', 'start', 'end', 'arrivedAt', 'duration', 'distance', 'wind', 'engine', 'crew', 'visibility', 'actions'];
  year: number | null = null; years = Array.from({ length: 15 }, (_, i) => new Date().getFullYear() - i);
  pageIndex = 0; pageSize = 25; sortField = ''; fmt = fmtDateTime;
  constructor() { effect(() => { this.shipId(); this.tripId(); this.load(); }); }
  load(): void {
    this.api.get<Paged<LogbookEntry>>(`/api/ships/${this.shipId()}/logbook`, { trip: this.tripId(), year: this.year, page: this.pageIndex + 1, pageSize: this.pageSize, sort: this.sortField })
      .subscribe(p => { this.entries.set(p.items); this.total.set(p.total); });
  }
  page(e: PageEvent): void { this.pageIndex = e.pageIndex; this.pageSize = e.pageSize; this.load(); }
  sort(s: Sort): void { this.sortField = s.direction ? s.active : ''; this.load(); }
  edit(e?: LogbookEntry): void { this.dialog.open(EntryDialog, { data: { shipId: this.shipId(), tripId: this.tripId(), entry: e }, width: '800px' }).afterClosed().subscribe(ok => ok && this.load()); }
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
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, TranslatePipe, HarbourSelect, VisibilityPicker],
  template: `
    <h2 mat-dialog-title>{{ data.entry ? ('common.edit' | translate) : ('logbook.newEntry' | translate) }}</h2>
    <mat-dialog-content class="form-grid">
      <app-harbour-select [label]="'logbook.startHarbour' | translate" [(harbourId)]="m.startHarbourId" [harbourName]="startName()" [required]="true" />
      <app-harbour-select [label]="'logbook.endHarbour' | translate" [(harbourId)]="m.endHarbourId" [harbourName]="data.entry?.endHarbour ?? null" [required]="true" />
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
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.distance' | translate }}</mat-label><input matInput type="number" step="0.1" [(ngModel)]="m.sailedDistanceNm" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'logbook.sails' | translate }}</mat-label><input matInput [(ngModel)]="m.sailsNotes" /></mat-form-field>
      <div class="full"><b>{{ 'logbook.crew' | translate }}:</b>
        @for (c of crew; track $index) { <mat-checkbox [(ngModel)]="c.selected">{{ c.name }}</mat-checkbox> }
        <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Gast hinzufügen</mat-label><input matInput [(ngModel)]="guest" (keydown.enter)="addGuest(); $event.preventDefault()" /></mat-form-field></div>
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'logbook.notes' | translate }}</mat-label><textarea matInput rows="4" [(ngModel)]="m.notes"></textarea></mat-form-field>
      <app-visibility [(value)]="m.visibility" />
      @if (error()) { <div class="error full">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
})
export class EntryDialog {
  data = inject<{ shipId: string; tripId: string | null; entry?: LogbookEntry }>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api); private auth = inject(AuthService);
  wind = WIND; error = signal(''); startName = signal<string | null>(this.data.entry?.startHarbour ?? null);
  crew: (EntryCrew & { selected: boolean })[] = []; guest = '';
  private e = this.data.entry;
  m = { startHarbourId: this.e?.startHarbourId ?? null as string | null, endHarbourId: this.e?.endHarbourId ?? null as string | null,
    departedAt: local(this.e?.departedAt), arrivedAt: local(this.e?.arrivedAt), engineHoursStart: this.e?.engineHoursStart ?? null, engineHoursEnd: this.e?.engineHoursEnd ?? null,
    logStart: this.e?.logStart ?? null, logEnd: this.e?.logEnd ?? null, windSpeed: this.e?.windSpeed ?? '', windDirection: this.e?.windDirection ?? null, weather: this.e?.weather ?? '', seaState: this.e?.seaState ?? '',
    sailedDistanceNm: this.e?.sailedDistanceNm ?? null, sailsNotes: this.e?.sailsNotes ?? '', notes: this.e?.notes ?? '', visibility: this.e?.visibility ?? this.auth.me()?.defaultContentVisibility ?? 'Crew' as const };

  constructor() {
    if (this.e) { this.crew = this.e.crew.map(c => ({ ...c, selected: true })); return; }
    this.api.get<LogbookDefaults>(`/api/ships/${this.data.shipId}/logbook/new-defaults`, { trip: this.data.tripId }).subscribe(d => {
      this.m.startHarbourId = d.startHarbourId; this.startName.set(d.startHarbourName); this.m.engineHoursStart = d.engineHoursStart; this.m.logStart = d.logStart;
      this.crew = d.crew.map(c => ({ ...c, selected: true }));
    });
  }
  addGuest(): void { if (this.guest.trim()) { this.crew.push({ crewMemberId: null, userId: null, name: this.guest.trim(), selected: true }); this.guest = ''; } }
  save(): void {
    const body = { ...this.m, tripId: this.data.tripId, departedAt: new Date(this.m.departedAt).toISOString(), arrivedAt: new Date(this.m.arrivedAt).toISOString(),
      crew: this.crew.filter(c => c.selected).map(c => ({ crewMemberId: c.crewMemberId, userId: c.userId, guestName: c.crewMemberId || c.userId ? null : c.name })) };
    const req = this.e ? this.api.put(`/api/logbook/${this.e.id}`, body) : this.api.post(`/api/ships/${this.data.shipId}/logbook`, body);
    req.subscribe({ next: () => this.ref.close(true), error: err => this.error.set(errorMessage(err)) });
  }
}

function local(iso: string | undefined): string {
  const d = iso ? new Date(iso) : new Date();
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}
