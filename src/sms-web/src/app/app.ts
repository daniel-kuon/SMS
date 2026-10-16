import { Component, inject, signal } from '@angular/core';
import { UpperCasePipe } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Api, AuthService } from './core/api';
import { ContentPage } from './core/models';

@Component({
  selector: 'app-root',
  imports: [UpperCasePipe, RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule, TranslatePipe],
  template: `
    <mat-toolbar color="primary" class="nav">
      <a routerLink="/" class="brand"><mat-icon>sailing</mat-icon><span>{{ 'app.title' | translate }}</span></a>
      <a mat-button routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }"><mat-icon>map</mat-icon> {{ 'nav.harbours' | translate }}</a>
      @if (auth.me()) {
        <a mat-button routerLink="/ships" routerLinkActive="active"><mat-icon>directions_boat</mat-icon> {{ 'nav.ships' | translate }}</a>
        <a mat-button routerLink="/trips" routerLinkActive="active"><mat-icon>route</mat-icon> {{ 'nav.trips' | translate }}</a>
      }
      <span class="spacer"></span>
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
  private api = inject(Api);
  readonly pages = signal<Pick<ContentPage, 'slug' | 'title'>[]>([]);

  constructor() {
    this.loadPages();
    this.translate.onLangChange.subscribe(() => this.loadPages());
  }

  setLang(lang: string): void {
    localStorage.setItem('lang', lang);
    this.translate.use(lang);
  }

  private loadPages(): void {
    this.api.get<Pick<ContentPage, 'slug' | 'title' | 'showInFooter'>[]>('/api/pages', { lang: this.translate.getCurrentLang() ?? 'de' })
      .subscribe(p => this.pages.set(p.filter(x => x.showInFooter)));
  }
}
