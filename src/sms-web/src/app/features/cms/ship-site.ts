import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, AuthService, errorMessage } from '../../core/api';
import { ShipPageNode, ShipSite, SiteService } from '../../core/site';
import { confirm, fmtDate, fmtDateTime } from '../../shared/shared';
import { RichEditor } from '../../shared/rich-editor';

/** Landing page of a ship website (custom domain or /ships/:id/site preview): ship card, nav pages, latest logbook. */
@Component({
  selector: 'app-ship-site-home',
  imports: [RouterLink, MatCardModule, MatListModule, TranslatePipe],
  template: `
    @if (site(); as s) {
    <div class="page page-narrow">
      <div class="hero">
        @if (s.ship.photoUrl) { <img [src]="s.ship.photoUrl" class="hero-img" /> }
        <h1>{{ s.ship.name }}</h1>
        <div class="muted">{{ s.ship.type }} @if (s.ship.homeHarbour) { · {{ s.ship.homeHarbour }} } @if (s.ship.lengthM) { · {{ s.ship.lengthM }} m }</div>
        @if (s.ship.description) { <div class="rich" [innerHTML]="s.ship.description"></div> }
      </div>
      @if (s.pages.length) { <nav class="pages">@for (p of s.pages; track p.id) { <a mat-button [routerLink]="base() + '/pages/' + p.path">{{ p.title }}</a> }</nav> }
      <h3>{{ 'site.latestLogbook' | translate }}</h3>
      @if (s.latestLogbook.length === 0) { <p class="muted">{{ 'common.none' | translate }}</p> }
      <mat-list>@for (e of s.latestLogbook; track e.id) {
        <a mat-list-item [routerLink]="['/logbook', e.id]"><span matListItemTitle>{{ e.startHarbour }} → {{ e.endHarbour }}</span><span matListItemLine>{{ fmtTime(e.departedAt) }} · {{ e.duration }} h @if (e.sailedDistanceNm) { · {{ e.sailedDistanceNm }} sm } @if (e.windSpeed) { · {{ e.windDirection }} {{ e.windSpeed }} }</span></a> }</mat-list>
      @if (s.trips.length) { <h3>{{ 'nav.trips' | translate }}</h3>
        <mat-list>@for (t of s.trips; track t.id) { <a mat-list-item [routerLink]="['/trips', t.id]"><span matListItemTitle>{{ t.name }}</span><span matListItemLine>{{ fmt(t.startDate) }} – {{ fmt(t.endDate) }}</span></a> }</mat-list> }
    </div>}`,
  styles: `.hero-img { width: 100%; max-height: 360px; object-fit: cover; border-radius: 8px; } .pages { display: flex; flex-wrap: wrap; gap: 4px; margin: 12px 0; }`,
})
export class ShipSiteHome {
  private api = inject(Api); private siteService = inject(SiteService);
  shipId = input<string>();
  site = signal<ShipSite | null>(null);
  base = computed(() => this.shipId() ? `/ships/${this.shipId()}/site` : '');
  fmt = fmtDate; fmtTime = fmtDateTime;
  constructor() {
    effect(() => {
      const id = this.shipId();
      if (id) this.api.get<ShipSite>(`/api/ships/${id}/site-preview`).subscribe(s => this.site.set(s));
      else this.site.set(this.siteService.shipSite);
    });
  }
}

/** A single ship page addressed by its slug path. */
@Component({
  selector: 'app-ship-site-page',
  imports: [RouterLink, MatCardModule, MatButtonModule, TranslatePipe],
  template: `
    @if (page(); as p) {
    <div class="page page-narrow">
      <h1>{{ p.title }}</h1>
      <mat-card appearance="outlined"><mat-card-content><div class="rich" [innerHTML]="p.content"></div></mat-card-content></mat-card>
      @if (p.children.length) { <h3>{{ 'site.subPages' | translate }}</h3><div class="row">@for (c of p.children; track c.id) { <a mat-stroked-button [routerLink]="base() + '/pages/' + c.path">{{ c.title }}</a> }</div> }
    </div>} @else if (missing()) { <div class="page"><p class="muted">404</p></div> }`,
})
export class ShipSitePage {
  private api = inject(Api); private siteService = inject(SiteService);
  shipId = input<string>(); path = input.required<string>(); p2 = input<string>(); p3 = input<string>();
  page = signal<ShipPageNode | null>(null); missing = signal(false);
  base = computed(() => this.shipId() ? `/ships/${this.shipId()}/site` : '');
  constructor() {
    effect(() => {
      const id = this.shipId() ?? this.siteService.shipSite?.ship.id;
      const path = [this.path(), this.p2(), this.p3()].filter(Boolean).join('/');
      if (!id) return;
      this.api.get<ShipPageNode>(`/api/ships/${id}/pages/${path}`).subscribe({ next: p => { this.page.set(p); this.missing.set(false); }, error: () => { this.page.set(null); this.missing.set(true); } });
    });
  }
}

/** Owner editor: page tree + domains ("Website" section on the ship page). */
@Component({
  selector: 'app-ship-site-editor',
  imports: [NgTemplateOutlet, FormsModule, RouterLink, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatCheckboxModule, TranslatePipe],
  template: `
    <div class="row"><h3 class="grow">{{ 'site.website' | translate }}</h3>
      <a mat-stroked-button [routerLink]="['/ships', shipId(), 'site']"><mat-icon>visibility</mat-icon> {{ 'site.preview' | translate }}</a>
      <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon> {{ 'site.newPage' | translate }}</button></div>
    @if (pages().length === 0) { <p class="muted">{{ 'site.noPages' | translate }}</p> }
    <ul class="tree">@for (p of pages(); track p.id) { <ng-container *ngTemplateOutlet="node; context: { $implicit: p }" /> }</ul>
    <ng-template #node let-p>
      <li><span [class.unpublished]="!p.published">{{ p.title }}</span> <code class="muted">/{{ p.path }}</code> @if (!p.showInNav) { <span class="chip">hidden</span> }
        <button mat-icon-button (click)="edit(p)"><mat-icon>edit</mat-icon></button><button mat-icon-button (click)="edit(null, p.id)" title="Unterseite"><mat-icon>subdirectory_arrow_right</mat-icon></button><button mat-icon-button (click)="remove(p)"><mat-icon>delete</mat-icon></button>
        @if (p.children.length) { <ul>@for (c of p.children; track c.id) { <ng-container *ngTemplateOutlet="node; context: { $implicit: c }" /> }</ul> }</li>
    </ng-template>

    <h3>{{ 'site.domains' | translate }}</h3>
    <p class="muted">{{ 'site.domainsHint' | translate }}</p>
    <ul>@for (d of domains(); track d.id) { <li><code>{{ d.host }}</code> @if (d.isPrimary) { <span class="chip public">primary</span> } <button mat-icon-button (click)="removeDomain(d)"><mat-icon>delete</mat-icon></button></li> }</ul>
    <form class="row" (ngSubmit)="addDomain()">
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="grow"><mat-label>{{ 'site.domain' | translate }}</mat-label><input matInput name="host" [(ngModel)]="host" placeholder="kairos.example.com" /></mat-form-field>
      <button mat-flat-button type="submit" [disabled]="!host">{{ 'common.new' | translate }}</button></form>
    @if (error()) { <div class="error">{{ error() }}</div> }`,
  styles: `.tree, .tree ul { list-style: none; padding-left: 0; } .tree ul { padding-left: 24px; } .unpublished { opacity: .5; }`,
})
export class ShipSiteEditor {
  private api = inject(Api); private dialog = inject(MatDialog);
  shipId = input.required<string>();
  pages = signal<ShipPageNode[]>([]); domains = signal<{ id: string; host: string; isPrimary: boolean }[]>([]); host = ''; error = signal('');
  constructor() { effect(() => { this.shipId(); this.load(); }); }
  load(): void { this.api.get<{ pages: ShipPageNode[]; domains: { id: string; host: string; isPrimary: boolean }[] }>(`/api/ships/${this.shipId()}/site`).subscribe(s => { this.pages.set(s.pages); this.domains.set(s.domains); }); }
  edit(page: ShipPageNode | null, parentId: string | null = null): void {
    this.dialog.open(ShipPageDialog, { data: { shipId: this.shipId(), page, parentId: page?.parentId ?? parentId, all: flatten(this.pages()) }, width: '800px' }).afterClosed().subscribe(ok => ok && this.load());
  }
  async remove(p: ShipPageNode): Promise<void> { if (await confirm(this.dialog, `„${p.title}“ löschen? Unterseiten rücken eine Ebene nach oben.`)) this.api.delete(`/api/ship-pages/${p.id}`).subscribe(() => this.load()); }
  addDomain(): void { this.error.set(''); this.api.post(`/api/ships/${this.shipId()}/domains`, { host: this.host }).subscribe({ next: () => { this.host = ''; this.load(); }, error: e => this.error.set(errorMessage(e)) }); }
  removeDomain(d: { id: string }): void { this.api.delete(`/api/ships/${this.shipId()}/domains/${d.id}`).subscribe(() => this.load()); }
}

export function flatten(nodes: ShipPageNode[], depth = 0): (ShipPageNode & { depth: number })[] {
  return nodes.flatMap(n => [{ ...n, depth }, ...flatten(n.children, depth + 1)]);
}

@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, TranslatePipe, RichEditor],
  template: `
    <h2 mat-dialog-title>{{ data.page ? data.page.title : ('site.newPage' | translate) }}</h2>
    <mat-dialog-content class="form-grid">
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'post.title' | translate }}</mat-label><input matInput [(ngModel)]="m.title" required /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Slug</mat-label><input matInput [(ngModel)]="m.slug" placeholder="automatisch" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'site.parent' | translate }}</mat-label><mat-select [(ngModel)]="m.parentId"><mat-option [value]="null">–</mat-option>@for (p of data.all; track p.id) { @if (p.id !== data.page?.id) { <mat-option [value]="p.id">{{ '— '.repeat(p.depth) }}{{ p.title }}</mat-option> } }</mat-select></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'site.order' | translate }}</mat-label><input matInput type="number" [(ngModel)]="m.order" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'profile.language' | translate }}</mat-label><mat-select [(ngModel)]="m.language"><mat-option value="de">de</mat-option><mat-option value="en">en</mat-option></mat-select></mat-form-field>
      <mat-checkbox [(ngModel)]="m.published">{{ 'site.published' | translate }}</mat-checkbox>
      <mat-checkbox [(ngModel)]="m.showInNav">{{ 'site.showInNav' | translate }}</mat-checkbox>
      <sms-rich-editor class="full" [label]="'post.content' | translate" [(value)]="m.content" [context]="{ shipId: data.shipId, visibility: 'Public' }" />
      @if (error()) { <div class="error full">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
})
export class ShipPageDialog {
  data = inject<{ shipId: string; page: ShipPageNode | null; parentId: string | null; all: (ShipPageNode & { depth: number })[] }>(MAT_DIALOG_DATA);
  private ref = inject(MatDialogRef); private api = inject(Api);
  m = { title: this.data.page?.title ?? '', slug: this.data.page?.slug ?? '', content: this.data.page?.content ?? '', parentId: this.data.parentId, order: this.data.page?.order ?? 0, published: this.data.page?.published ?? true, showInNav: this.data.page?.showInNav ?? true, language: this.data.page?.language ?? 'de' };
  error = signal('');
  save(): void {
    const body = { ...this.m, slug: this.m.slug || null };
    const req = this.data.page ? this.api.put(`/api/ship-pages/${this.data.page.id}`, body) : this.api.post(`/api/ships/${this.data.shipId}/pages`, body);
    req.subscribe({ next: () => this.ref.close(true), error: e => this.error.set(errorMessage(e)) });
  }
}
