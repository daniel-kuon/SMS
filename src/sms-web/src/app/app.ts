import { Component, inject, signal } from '@angular/core';
import { UpperCasePipe } from '@angular/common';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Api, AuthService } from './core/api';
import { ContentPage } from './core/models';
import { NotificationBell } from './features/social/social';
import { OfflineQueue } from './core/offline';
import { SiteService } from './core/site';

@Component({
  selector: 'app-root',
  host: { '(document:click)': 'routeRichLink($event)' },
  imports: [UpperCasePipe, RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule, TranslatePipe, NotificationBell],
  template: `
    <mat-toolbar color="primary" class="nav">
      @if (siteService.shipSite; as s) {
        <a routerLink="/" class="brand"><mat-icon>sailing</mat-icon><span>{{ s.ship.name }}</span></a>
        @for (p of s.pages; track p.id) { <a mat-button [routerLink]="'/pages/' + p.path" routerLinkActive="active">{{ p.title }}</a> }
        <a mat-button [routerLink]="['/ships', s.ship.id, 'logbook']" routerLinkActive="active"><mat-icon>menu_book</mat-icon> {{ 'ship.logbook' | translate }}</a>
      } @else {
      <a routerLink="/" class="brand"><mat-icon>sailing</mat-icon><span>{{ 'app.title' | translate }}</span></a>
      <a mat-button routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }"><mat-icon>map</mat-icon> {{ 'nav.harbours' | translate }}</a>
      <a mat-button routerLink="/explore" routerLinkActive="active"><mat-icon>explore</mat-icon> {{ 'nav.explore' | translate }}</a>
      }
      @if (auth.me() && !siteService.shipSite) {
        <a mat-button routerLink="/feed" routerLinkActive="active"><mat-icon>dynamic_feed</mat-icon> {{ 'nav.feed' | translate }}</a>
        <a mat-button routerLink="/ships" routerLinkActive="active"><mat-icon>directions_boat</mat-icon> {{ 'nav.ships' | translate }}</a>
        <a mat-button routerLink="/trips" routerLinkActive="active"><mat-icon>route</mat-icon> {{ 'nav.trips' | translate }}</a>
      }
      <span class="spacer"></span>
      @if (!offline.online()) { <span class="chip private" title="offline"><mat-icon inline>cloud_off</mat-icon> offline</span> }
      @if (offline.pending()) { <button mat-button (click)="offline.flush()" title="Ausstehende Uploads"><mat-icon>cloud_upload</mat-icon> {{ offline.pending() }}</button> }
      @if (auth.me()) { <app-notifications /> }
      <button mat-button [matMenuTriggerFor]="langMenu"><mat-icon>language</mat-icon> {{ translate.getCurrentLang() | uppercase }}</button>
      <mat-menu #langMenu="matMenu">
        <button mat-menu-item (click)="setLang('de')">Deutsch</button>
        <button mat-menu-item (click)="setLang('en')">English</button>
      </mat-menu>
      @if (auth.me(); as me) {
        <button mat-button [matMenuTriggerFor]="userMenu"><mat-icon>account_circle</mat-icon> {{ me.displayName }}</button>
        <mat-menu #userMenu="matMenu">
          <a mat-menu-item [routerLink]="['/u', me.id]">{{ 'nav.profile' | translate }}</a>
          <a mat-menu-item routerLink="/settings">{{ 'nav.settings' | translate }}</a>
          @if (me.isAdmin) { <a mat-menu-item routerLink="/admin">{{ 'nav.admin' | translate }}</a> }
          <button mat-menu-item (click)="auth.logout()">{{ 'nav.logout' | translate }}</button>
        </mat-menu>
      } @else if (auth.me() === null) {
        <a mat-button routerLink="/auth/login">{{ 'nav.login' | translate }}</a>
        <a mat-stroked-button routerLink="/auth/register">{{ 'nav.register' | translate }}</a>
      }
    </mat-toolbar>
    <main class="content"><router-outlet /></main>
    <footer class="footer">
      @for (p of pages(); track p.slug) { <a [routerLink]="['/p', p.slug]">{{ p.title }}</a> }
      @if (siteService.shipSite) { <a routerLink="/explore" class="muted">{{ 'site.poweredBy' | translate }}</a> }
      <span class="attribution">© OpenStreetMap-Mitwirkende · OpenSeaMap</span>
    </footer>
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-height: 100vh; }
    .nav { gap: 4px; position: sticky; top: 0; z-index: 1000; }
    .brand { display: flex; align-items: center; gap: 6px; color: inherit; text-decoration: none; font-weight: 600; margin-right: 12px; }
    .spacer { flex: 1; }
    .active { background: rgba(255,255,255,.15); }
    .content { flex: 1; display: flex; flex-direction: column; }
    .footer { display: flex; gap: 16px; padding: 8px 16px; font-size: 12px; background: #f3f3f3; align-items: center; flex-wrap: wrap; }
    .footer a { color: #555; }
    .attribution { margin-left: auto; color: #777; }
    @media (max-width: 700px) { .nav a span, .nav .mat-mdc-button .mdc-button__label { display: none; } }
  `,
})
export class App {
  readonly auth = inject(AuthService);
  readonly translate = inject(TranslateService);
  readonly offline = inject(OfflineQueue);
  readonly siteService = inject(SiteService);
  private api = inject(Api);
  readonly pages = signal<Pick<ContentPage, 'slug' | 'title'>[]>([]);

  constructor() {
    this.loadPages();
    this.translate.onLangChange.subscribe(() => this.loadPages());
  }

  private router = inject(Router);
  /** Internal anchors inside rendered rich text ([innerHTML] in .rich) navigate in-app instead of reloading the page. */
  routeRichLink(e: MouseEvent): void {
    const href = richLinkTarget(e);
    if (href) { e.preventDefault(); void this.router.navigateByUrl(href); }
  }

  setLang(lang: string): void {
    localStorage.setItem('lang', lang);
    this.translate.use(lang);
  }

  private loadPages(): void {
    this.api.get<(Pick<ContentPage, 'slug' | 'title' | 'showInFooter'> & { children: unknown[] })[]>('/api/pages', { lang: this.translate.getCurrentLang() ?? 'de' })
      .subscribe(p => this.pages.set(p.filter(x => x.showInFooter)));
  }
}

/** App route of a plain left click on a same-origin anchor in rendered rich text (not in an editor, not /api), else null. */
export function richLinkTarget(e: MouseEvent): string | null {
  if (e.defaultPrevented || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return null;
  const a = (e.target as Element | null)?.closest?.('.rich a[href]') as HTMLAnchorElement | null;
  if (!a || (a.target && a.target !== '_self') || a.closest('[contenteditable="true"]')) return null;
  const href = a.getAttribute('href') ?? '';
  return href.startsWith('/') && !href.startsWith('//') && !href.startsWith('/api/') ? href : null;
}
