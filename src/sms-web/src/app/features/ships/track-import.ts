import { Component, inject, input, OnDestroy, OnInit, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, errorMessage } from '../../core/api';
import { fmtDate, fmtDateTime } from '../../shared/shared';

export interface TrackImport {
  id: string; fileName: string; status: 'Queued' | 'Running' | 'Done' | 'Failed'; progressPercent: number; parsed: number; added: number; duplicates: number; rejected: number;
  tracks: number; waypoints: number; routes: number; error: string | null; tripId: string | null; createdAt: string; finishedAt: string | null;
}
export interface Voyage { from: string; to: string; distanceNm: number; startHarbourName: string | null; endHarbourName: string | null; }

const n = (v: number) => v.toLocaleString('de-DE');
/** One line per import: counts so far, and what TimeZero exports contain but the portal does not import (events, planned routes). */
export function importSummary(i: TrackImport): string {
  if (i.status === 'Failed') return `${i.fileName}: Fehler – ${i.error ?? ''}`;
  if (i.status === 'Queued') return `${i.fileName}: wartet …`;
  const parts = [`${n(i.parsed)} Punkte gelesen`, `${n(i.added)} neu`];
  if (i.duplicates) parts.push(`${n(i.duplicates)} schon vorhanden`);
  if (i.rejected) parts.push(`${n(i.rejected)} verworfen (Sprünge)`);
  const skipped = [i.waypoints ? `${n(i.waypoints)} Ereignisse` : '', i.routes ? `${n(i.routes)} Routen` : ''].filter(x => x).join(' und ');
  if (skipped) parts.push(`${skipped} nicht importiert`);
  return `${i.fileName}${i.status === 'Running' ? ` (${i.progressPercent} %)` : ''}: ${parts.join(', ')}`;
}

/** Track file upload (GPX/KML/NMEA, also .zip/.gz/.7z archives such as TimeZero exports); the server imports in the background, this polls the progress. */
@Component({
  selector: 'app-track-import',
  imports: [MatProgressBarModule, TranslatePipe],
  template: `
    <div class="import"><label>{{ 'tracking.import' | translate }} <input type="file" accept=".gpx,.kml,.nmea,.txt,.log,.zip,.gz,.7z" multiple (change)="upload($event)" [disabled]="uploading()" /></label></div>
    @if (uploading()) { <mat-progress-bar mode="indeterminate" /> <span class="muted small">{{ 'tracking.uploading' | translate }}</span> }
    @if (error()) { <p class="error">{{ error() }}</p> }
    @for (i of imports(); track i.id) {
      <div class="item">
        @if (i.status === 'Running' || i.status === 'Queued') { <mat-progress-bar [mode]="i.status === 'Queued' ? 'query' : 'determinate'" [value]="i.progressPercent" /> }
        <span class="muted small" [class.error]="i.status === 'Failed'">{{ summary(i) }}</span>
      </div>
    }`,
  styles: `.item { margin: 4px 0; } .small { font-size: 12px; }`,
})
export class TrackImportCard implements OnInit, OnDestroy {
  private api = inject(Api);
  shipId = input.required<string>();
  tripId = input<string | null>(null);
  /** Show the ship's recent imports (ship page); the trip page shows only what was uploaded there. */
  showRecent = input(false);
  /** Emitted when an import of this card finished (trip/voyage data changed). */
  done = output<void>();
  imports = signal<TrackImport[]>([]); uploading = signal(false); error = signal('');
  summary = importSummary;
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void { if (this.showRecent()) this.api.get<TrackImport[]>(`/api/ships/${this.shipId()}/track-imports`).subscribe(l => { this.imports.set(l.slice(0, 5)); this.poll(); }); }
  ngOnDestroy(): void { clearInterval(this.timer); }

  upload(e: Event): void {
    const input = e.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (!files.length) return;
    this.uploading.set(true); this.error.set('');
    this.api.upload<TrackImport[]>(`/api/ships/${this.shipId()}/track-imports`, files, this.tripId() ? { tripId: this.tripId()! } : {}).subscribe({
      next: r => { this.uploading.set(false); input.value = ''; this.imports.set([...r, ...this.imports()]); this.poll(); },
      error: err => { this.uploading.set(false); this.error.set(errorMessage(err)); },
    });
  }

  private poll(): void {
    clearInterval(this.timer);
    if (!this.imports().some(i => i.status === 'Queued' || i.status === 'Running')) return;
    this.timer = setInterval(() => {
      const open = this.imports().filter(i => i.status === 'Queued' || i.status === 'Running');
      if (!open.length) { clearInterval(this.timer); return; }
      for (const i of open)
        this.api.get<TrackImport>(`/api/ships/${this.shipId()}/track-imports/${i.id}`).subscribe(u => {
          this.imports.update(l => l.map(x => x.id === u.id ? u : x));
          if (u.status === 'Done' || u.status === 'Failed') this.done.emit();
        });
    }, 2000);
  }
}

/** "Fahrten ohne Törn": ship-level track (imported or live, outside every trip) split into voyages; each can become a trip. */
@Component({
  selector: 'app-voyages',
  imports: [DecimalPipe, MatButtonModule, MatCheckboxModule, TranslatePipe],
  template: `
    <h3>{{ 'voyages.title' | translate }} @if (total()) { <span class="muted">({{ total() }})</span> }</h3>
    @if (total() === 0) { <p class="muted">{{ 'voyages.none' | translate }}</p> }
    @else {
      <p class="muted">{{ 'voyages.hint' | translate }}</p>
      @for (v of items(); track v.from) {
        <div class="row voyage">
          <mat-checkbox [checked]="selected().has(v.from)" (change)="toggle(v)" />
          <span class="grow">{{ fmt(v.from) }} – {{ fmtEnd(v) }}: {{ v.startHarbourName ?? '?' }} → {{ v.endHarbourName ?? '?' }} · {{ v.distanceNm | number:'1.0-1' }} sm</span>
          <button mat-stroked-button (click)="adopt([v])" [disabled]="busy()">{{ 'voyages.adopt' | translate }}</button>
        </div>
      }
      <div class="row">
        @if (selected().size > 1) { <button mat-flat-button (click)="adoptSelected()" [disabled]="busy()">{{ 'voyages.adoptSelected' | translate: { n: selected().size } }}</button> }
        @if (items().length < (total() ?? 0)) { <button mat-button (click)="more()">{{ 'voyages.more' | translate }}</button> }
      </div>
      @if (error()) { <p class="error">{{ error() }}</p> }
    }`,
  styles: `.voyage { align-items: center; gap: 6px; border-bottom: 1px solid #eee; padding: 2px 0; } .grow { flex: 1; }`,
})
export class VoyageList implements OnInit {
  private api = inject(Api); private router = inject(Router);
  shipId = input.required<string>();
  items = signal<Voyage[]>([]); total = signal<number | null>(null); selected = signal(new Set<string>()); busy = signal(false); error = signal('');

  ngOnInit(): void { this.load(); }
  load(): void { this.api.get<{ total: number; items: Voyage[] }>(`/api/ships/${this.shipId()}/voyages`, { take: 50 }).subscribe(r => { this.items.set(r.items); this.total.set(r.total); this.selected.set(new Set()); }); }
  more(): void { this.api.get<{ total: number; items: Voyage[] }>(`/api/ships/${this.shipId()}/voyages`, { skip: this.items().length, take: 50 }).subscribe(r => this.items.update(l => [...l, ...r.items])); }
  fmt(d: string): string { return fmtDateTime(d); }
  fmtEnd(v: Voyage): string { return fmtDate(v.to) === fmtDate(v.from) ? new Date(v.to).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' }) : fmtDateTime(v.to); }
  toggle(v: Voyage): void { this.selected.update(s => { const c = new Set(s); if (!c.delete(v.from)) c.add(v.from); return c; }); }
  adoptSelected(): void { this.adopt(this.items().filter(v => this.selected().has(v.from))); }
  /** Creates one trip spanning the given voyages and opens it (the trip page then suggests logbook entries from the track). */
  adopt(vs: Voyage[]): void {
    if (!vs.length) return;
    const from = vs.reduce((a, v) => v.from < a ? v.from : a, vs[0].from), to = vs.reduce((a, v) => v.to > a ? v.to : a, vs[0].to);
    const first = vs.find(v => v.from === from)!, last = vs.find(v => v.to === to)!;
    const name = first.startHarbourName && last.endHarbourName ? `${first.startHarbourName} → ${last.endHarbourName}` : null;
    this.busy.set(true);
    this.api.post<{ id: string }>(`/api/ships/${this.shipId()}/voyages`, { from, to, name }).subscribe({
      next: r => { this.busy.set(false); this.router.navigate(['/trips', r.id]); },
      error: e => { this.busy.set(false); this.error.set(errorMessage(e)); },
    });
  }
}
