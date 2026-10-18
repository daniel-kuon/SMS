import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, errorMessage } from '../../core/api';
import { ContentPage } from '../../core/models';
import { tenantPreview } from '../../core/site';
import { RouterLink } from '@angular/router';

interface RoutingStatus { method: string; graphVersion: number; builtAt: string | null; region: string | null; nodes: number; edges: number; waterways: number; building: boolean; progress: { phase: string; message: string; at: string }[]; }

@Component({
  imports: [DatePipe, FormsModule, MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatProgressSpinnerModule, TranslatePipe, RouterLink],
  template: `
    <div class="page page-narrow">
      <h1>{{ 'admin.title' | translate }}</h1>
      <mat-card appearance="outlined"><mat-card-content>
        <h3>{{ 'admin.importOsm' | translate }}</h3>
        <div class="row"><mat-form-field appearance="outline" class="grow" subscriptSizing="dynamic"><mat-label>{{ 'admin.bbox' | translate }}</mat-label><input matInput [(ngModel)]="bbox" /></mat-form-field>
          <button mat-flat-button (click)="importOsm()" [disabled]="busy()">{{ 'admin.import' | translate }}</button></div>
        @if (result()) { <p>{{ result() }}</p> }
      </mat-card-content></mat-card>
      <mat-card appearance="outlined"><mat-card-content>
        <h3>{{ 'admin.pages' | translate }}</h3>
        <div class="row"><mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Slug</mat-label><input matInput [(ngModel)]="page.slug" (change)="loadPage()" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Sprache</mat-label><mat-select [(ngModel)]="page.language" (selectionChange)="loadPage()"><mat-option value="de">de</mat-option><mat-option value="en">en</mat-option></mat-select></mat-form-field>
          <mat-checkbox [(ngModel)]="page.showInFooter">Footer</mat-checkbox></div>
        <div class="row"><mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'site.parent' | translate }}</mat-label><mat-select [(ngModel)]="page.parentId"><mat-option [value]="null">–</mat-option>@for (p of allPages(); track p.id) { @if (p.id !== page.id) { <mat-option [value]="p.id">{{ p.path }}</mat-option> } }</mat-select></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'site.order' | translate }}</mat-label><input matInput type="number" [(ngModel)]="page.order" /></mat-form-field>
          <mat-checkbox [(ngModel)]="page.showInNav">{{ 'site.showInNav' | translate }}</mat-checkbox></div>
        <mat-form-field appearance="outline" class="full-width"><mat-label>Titel</mat-label><input matInput [(ngModel)]="page.title" /></mat-form-field>
        <mat-form-field appearance="outline" class="full-width"><mat-label>Inhalt (HTML)</mat-label><textarea matInput rows="10" [(ngModel)]="page.content"></textarea></mat-form-field>
        <button mat-flat-button (click)="savePage()">{{ 'common.save' | translate }}</button>
      </mat-card-content></mat-card>
      <mat-card appearance="outlined"><mat-card-content>
        <h3>{{ 'admin.seamarks' | translate }}</h3>
        <p class="muted">{{ 'admin.seamarksHint' | translate }} @if (seamarks(); as s) { · {{ s.count }} {{ 'admin.seamarksCount' | translate }} }</p>
        <div class="row"><mat-form-field appearance="outline" class="grow" subscriptSizing="dynamic"><mat-label>{{ 'admin.bbox' | translate }}</mat-label><input matInput [(ngModel)]="seamarkBbox" /></mat-form-field>
          <button mat-flat-button (click)="importSeamarks()" [disabled]="busy()">{{ 'admin.import' | translate }}</button>
          <button mat-stroked-button (click)="deriveFairways()" [disabled]="busy()" [title]="'admin.fairwaysHint' | translate">{{ 'admin.fairways' | translate }}</button></div>
      </mat-card-content></mat-card>
      <mat-card appearance="outlined"><mat-card-content>
        <h3>{{ 'admin.tenantPreview' | translate }}</h3>
        <p class="muted">{{ 'admin.tenantPreviewHint' | translate }}</p>
        <div class="row"><mat-form-field appearance="outline" class="grow" subscriptSizing="dynamic"><mat-label>Host</mat-label><input matInput [(ngModel)]="tenantHost" placeholder="kairos.example.com" /></mat-form-field>
          <button mat-flat-button (click)="setTenant()">OK</button><button mat-button (click)="tenantHost = ''; setTenant()">{{ 'admin.tenantReset' | translate }}</button></div>
      </mat-card-content></mat-card>
      <mat-card appearance="outlined"><mat-card-content>
        <h3>{{ 'admin.routing' | translate }}</h3>
        @if (routing(); as r) {
          <p>{{ 'admin.routingStatus' | translate }}: <b>{{ r.method }}</b> · Version {{ r.graphVersion }} · {{ r.nodes }} {{ 'admin.nodes' | translate }} / {{ r.edges }} {{ 'admin.edges' | translate }} @if (r.builtAt) { · {{ 'admin.builtAt' | translate }} {{ r.builtAt | date:'dd.MM.yyyy HH:mm' }} } @if (r.region) { · Region {{ r.region }} }</p>
          <p class="muted">{{ 'admin.routingHint' | translate }}</p>
          <div class="row"><button mat-flat-button (click)="rebuild()" [disabled]="r.building">{{ (r.building ? 'admin.routingBuilding' : 'admin.routingRebuild') | translate }}</button>@if (r.building) { <mat-spinner diameter="20" /> }</div>
          <h4>{{ 'admin.waterways' | translate }} <span class="muted">({{ r.waterways }})</span></h4>
          <p class="muted">{{ 'admin.waterwaysHint' | translate }}</p>
          <div class="row"><mat-form-field appearance="outline" class="grow" subscriptSizing="dynamic"><mat-label>{{ 'admin.bbox' | translate }}</mat-label><input matInput [(ngModel)]="waterwayBbox" /></mat-form-field>
            <button mat-stroked-button (click)="importWaterways()" [disabled]="busy()">{{ 'admin.import' | translate }}</button>
            <label class="muted">{{ 'admin.orFile' | translate }} <input type="file" accept=".json" (change)="importWaterwaysFile($event)" /></label></div>
          <h4>{{ 'admin.pbf' | translate }}</h4>
          <p class="muted">{{ 'admin.pbfHint' | translate }}</p>
          <div class="row"><mat-form-field appearance="outline" class="grow" subscriptSizing="dynamic"><mat-label>URL / Pfad (.osm.pbf)</mat-label><input matInput [(ngModel)]="pbf.url" /></mat-form-field>
            <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'admin.bbox' | translate }}</mat-label><input matInput [(ngModel)]="pbf.bbox" placeholder="optional" /></mat-form-field></div>
          <div class="row"><mat-checkbox [(ngModel)]="pbf.seamarks">{{ 'admin.seamarks' | translate }}</mat-checkbox><mat-checkbox [(ngModel)]="pbf.harbours">{{ 'admin.importOsm' | translate }}</mat-checkbox><mat-checkbox [(ngModel)]="pbf.waterways">{{ 'admin.waterways' | translate }}</mat-checkbox><mat-checkbox [(ngModel)]="pbf.pois">{{ 'admin.pois' | translate }}</mat-checkbox>
            <button mat-flat-button (click)="startPbf()" [disabled]="pbfStatus()?.running || !pbf.url">{{ (pbfStatus()?.running ? 'admin.routingBuilding' : 'admin.import') | translate }}</button></div>
          @if (pbfStatus(); as ps) { <ul class="progress">@for (p of ps.progress.slice(-6); track p.at) { <li><span class="muted">{{ p.at | date:'HH:mm:ss' }}</span> [{{ p.phase }}] {{ p.message }}</li> }</ul> }
          <h4>{{ 'admin.corrections' | translate }}</h4>
          <p class="muted">{{ 'admin.correctionsHint' | translate }}</p>
          <a mat-stroked-button routerLink="/admin/corrections">{{ 'admin.editCorrections' | translate }}</a>
          <ul class="progress">@for (p of r.progress; track p.at) { <li><span class="muted">{{ p.at | date:'HH:mm:ss' }} (+{{ elapsed(r.progress[0].at, p.at) }})</span> [{{ p.phase }}] {{ p.message }}</li> }</ul>
        }
      </mat-card-content></mat-card>
      <mat-card appearance="outlined"><mat-card-content>
        <h3>{{ 'admin.reports' | translate }}</h3>
        @for (r of reports(); track r.id) {
          <div class="row report"><span><b>{{ r.targetType }}</b> {{ r.targetId }} · {{ r.reporter }} · {{ r.reason }} · <i>{{ r.status }}</i></span>
            @if (r.status === 'Open') { <button mat-stroked-button (click)="resolve(r, true)">{{ 'admin.hide' | translate }}</button><button mat-button (click)="resolve(r, false)">{{ 'admin.dismiss' | translate }}</button> }</div>
        } @empty { <p class="muted">–</p> }
      </mat-card-content></mat-card>
      <mat-card appearance="outlined"><mat-card-content>
        <h3>{{ 'admin.signoz' | translate }}</h3>
        <p>SigNoz UI: <a href="http://localhost:3302" target="_blank">http://localhost:3302</a> · Login: admin&#64;localhost.dev / SailingPortal2026!</p>
      </mat-card-content></mat-card>
    </div>`,
  styles: `.full-width { width: 100%; } mat-card { margin-bottom: 12px; } .progress { font-size: 12px; list-style: none; padding: 0; } .report { padding: 4px 0; border-bottom: 1px solid #eee; font-size: 13px; }`,
})
export class AdminPage {
  private api = inject(Api);
  bbox = '54.2,9.8,54.7,10.9'; busy = signal(false); result = signal('');
  page: ContentPage & { id?: string; parentId: string | null; order: number; showInNav: boolean } = { slug: 'impressum', title: '', content: '', language: 'de', showInFooter: true, parentId: null, order: 0, showInNav: false };
  allPages = signal<{ id: string; path: string }[]>([]);
  seamarks = signal<{ count: number } | null>(null); seamarkBbox = '54.2,9.8,54.7,10.9'; tenantHost = tenantPreview.get() ?? '';
  loadAllPages(): void {
    this.api.get<{ id: string; path: string; children: unknown[] }[]>('/api/pages', { lang: this.page.language }).subscribe(tree => {
      const flat = (nodes: { id: string; path: string; children: unknown[] }[]): { id: string; path: string }[] => nodes.flatMap(n => [{ id: n.id, path: n.path }, ...flat(n.children as typeof nodes)]);
      this.allPages.set(flat(tree));
    });
  }
  importSeamarks(): void {
    this.busy.set(true);
    this.api.post<{ created: number; updated: number; skipped: number }>(`/api/admin/seamarks/import-osm?bbox=${encodeURIComponent(this.seamarkBbox)}`, {}).subscribe({
      next: r => { this.busy.set(false); this.result.set(`Seezeichen: ${r.created} neu, ${r.updated} aktualisiert, ${r.skipped} übersprungen`); this.loadSeamarks(); }, error: e => { this.busy.set(false); this.result.set(errorMessage(e)); } });
  }
  loadSeamarks(): void { this.api.get<{ count: number }>('/api/seamarks/status').subscribe(s => this.seamarks.set(s)); }
  setTenant(): void { tenantPreview.set(this.tenantHost.trim() || null); location.href = '/'; }
  routing = signal<RoutingStatus | null>(null);
  private timer?: ReturnType<typeof setInterval>;
  constructor() { this.loadPage(); this.loadAllPages(); this.loadSeamarks(); this.loadRouting(); this.loadReports(); this.loadPbf(); this.timer = setInterval(() => { if (this.pbfStatus()?.running) { this.loadPbf(); this.loadSeamarks(); } if (this.routing()?.building) this.loadRouting(); }, 5000); }
  ngOnDestroy(): void { clearInterval(this.timer); }
  reports = signal<{ id: string; targetType: string; targetId: string; reporter: string; reason: string; status: string }[]>([]);
  loadReports(): void { this.api.get<typeof this.reports extends () => infer T ? T : never>('/api/admin/reports').subscribe(r => this.reports.set(r)); }
  resolve(r: { id: string }, hide: boolean): void { this.api.post(`/api/admin/reports/${r.id}/resolve`, { hide, resolution: hide ? 'hidden' : 'dismissed' }).subscribe(() => this.loadReports()); }
  elapsed(from: string, to: string): string { const s = Math.max(0, Math.round((Date.parse(to) - Date.parse(from)) / 1000)); return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`; }
  loadRouting(): void { this.api.get<RoutingStatus>('/api/routing/status').subscribe(r => this.routing.set(r)); }
  rebuild(): void { this.api.post('/api/admin/routing/rebuild', {}).subscribe({ next: () => setTimeout(() => this.loadRouting(), 500), error: e => this.result.set(errorMessage(e)) }); }
  waterwayBbox = '54.2,9.1,54.5,10.2';
  pbf = { url: 'https://download.geofabrik.de/europe/germany/schleswig-holstein-latest.osm.pbf', bbox: '', seamarks: true, harbours: true, waterways: true, pois: true };
  pbfStatus = signal<{ running: boolean; progress: { phase: string; message: string; at: string }[] } | null>(null);
  loadPbf(): void { this.api.get<NonNullable<ReturnType<AdminPage['pbfStatus']>>>('/api/admin/routing/import-pbf/status').subscribe(s => this.pbfStatus.set(s)); }
  startPbf(): void {
    this.api.post('/api/admin/routing/import-pbf', { ...this.pbf, bbox: this.pbf.bbox || null }).subscribe({ next: () => setTimeout(() => this.loadPbf(), 500), error: e => this.result.set(errorMessage(e)) });
  }
  deriveFairways(): void {
    this.busy.set(true);
    this.api.post<{ marks: number; created: number; removed: number }>(`/api/admin/routing/derive-fairways?bbox=${encodeURIComponent(this.seamarkBbox)}`, {}).subscribe({
      next: r => { this.busy.set(false); this.result.set(`${r.created} Fahrwasser aus ${r.marks} Seitentonnen abgeleitet (${r.removed} ersetzt) – Routing-Graph neu bauen, damit sie wirken.`); this.loadRouting(); },
      error: e => { this.busy.set(false); this.result.set(errorMessage(e)); } });
  }
  importWaterways(): void {
    this.busy.set(true);
    this.api.post<{ created: number; removed: number; bridges: number; locks: number }>(`/api/admin/routing/import-waterways?bbox=${encodeURIComponent(this.waterwayBbox)}`, {}).subscribe({
      next: r => { this.busy.set(false); this.result.set(`${r.created} Wasserstraßen, ${r.bridges} Brücken, ${r.locks} Schleusen importiert (${r.removed} ersetzt) – Routing-Graph neu bauen, damit sie wirken.`); this.loadRouting(); },
      error: e => { this.busy.set(false); this.result.set(errorMessage(e)); } });
  }
  importWaterwaysFile(e: Event): void {
    const file = (e.target as HTMLInputElement).files?.[0]; if (!file) return;
    this.api.upload<{ created: number; removed: number; bridges: number; locks: number }>('/api/admin/routing/import-waterways-file', [file]).subscribe({
      next: r => { this.result.set(`${r.created} Wasserstraßen, ${r.bridges} Brücken, ${r.locks} Schleusen importiert (${r.removed} ersetzt) – Routing-Graph neu bauen, damit sie wirken.`); this.loadRouting(); }, error: err => this.result.set(errorMessage(err)) });
  }
  importOsm(): void {
    this.busy.set(true); this.result.set('');
    this.api.post<{ created: number; updated: number; skipped: number }>(`/api/admin/harbours/import-osm?bbox=${encodeURIComponent(this.bbox)}`, {}).subscribe({
      next: r => { this.busy.set(false); this.result.set(`${r.created} neu, ${r.updated} aktualisiert, ${r.skipped} übersprungen`); }, error: e => { this.busy.set(false); this.result.set(errorMessage(e)); } });
  }
  loadPage(): void { this.api.get<typeof this.page>(`/api/pages/${this.page.slug}`, { lang: this.page.language }).subscribe({ next: p => this.page = { ...this.page, ...p, slug: this.page.slug, language: this.page.language }, error: () => { this.page.title = ''; this.page.content = ''; } }); this.loadAllPages(); }
  savePage(): void { this.api.put(`/api/pages/${this.page.slug}`, this.page).subscribe(() => this.result.set('Seite gespeichert.')); }
}
