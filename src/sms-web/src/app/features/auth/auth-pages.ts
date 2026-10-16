import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Api, AuthService, errorMessage } from '../../core/api';

const imports = [FormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, TranslatePipe];

@Component({
  imports,
  template: `
    <div class="page page-narrow"><mat-card appearance="outlined"><mat-card-content>
      <h2>{{ 'auth.login' | translate }}</h2>
      <form (ngSubmit)="submit()" class="form-grid">
        <mat-form-field appearance="outline" class="full"><mat-label>{{ 'auth.email' | translate }}</mat-label><input matInput type="email" name="email" [(ngModel)]="email" required autocomplete="email" /></mat-form-field>
        <mat-form-field appearance="outline" class="full"><mat-label>{{ 'auth.password' | translate }}</mat-label><input matInput type="password" name="password" [(ngModel)]="password" required autocomplete="current-password" /></mat-form-field>
        @if (error()) { <div class="error full">{{ error() }} @if (notConfirmed()) { <button type="button" mat-button (click)="resend()">{{ 'auth.resend' | translate }}</button> }</div> }
        <div class="row full"><button mat-flat-button type="submit" [disabled]="busy()">{{ 'auth.login' | translate }}</button>
          <a mat-button routerLink="/auth/forgot-password">{{ 'auth.forgot' | translate }}</a>
          <span class="grow"></span><a routerLink="/auth/register">{{ 'auth.noAccount' | translate }}</a></div>
      </form>
    </mat-card-content></mat-card></div>`,
})
export class LoginPage {
  private auth = inject(AuthService); private router = inject(Router); private route = inject(ActivatedRoute); private api = inject(Api);
  email = ''; password = ''; error = signal(''); busy = signal(false); notConfirmed = signal(false);
  async submit(): Promise<void> {
    this.busy.set(true); this.error.set(''); this.notConfirmed.set(false);
    try { await this.auth.login(this.email, this.password); await this.router.navigateByUrl(this.route.snapshot.queryParamMap.get('returnUrl') ?? '/ships'); }
    catch (e) { this.error.set(errorMessage(e)); this.notConfirmed.set((e as { status?: number }).status === 403); }
    finally { this.busy.set(false); }
  }
  async resend(): Promise<void> { await firstValueFrom(this.api.post('/api/auth/resend-confirmation', { email: this.email })); this.error.set('Mail verschickt.'); }
}

@Component({
  imports,
  template: `
    <div class="page page-narrow"><mat-card appearance="outlined"><mat-card-content>
      <h2>{{ 'auth.register' | translate }}</h2>
      @if (done()) { <p>{{ 'auth.registered' | translate }}</p><a mat-button routerLink="/auth/login">{{ 'auth.login' | translate }}</a> }
      @else {
      <form (ngSubmit)="submit()" class="form-grid">
        <mat-form-field appearance="outline" class="full"><mat-label>{{ 'auth.displayName' | translate }}</mat-label><input matInput name="displayName" [(ngModel)]="displayName" required /></mat-form-field>
        <mat-form-field appearance="outline" class="full"><mat-label>{{ 'auth.email' | translate }}</mat-label><input matInput type="email" name="email" [(ngModel)]="email" required autocomplete="email" /></mat-form-field>
        <mat-form-field appearance="outline" class="full"><mat-label>{{ 'auth.password' | translate }}</mat-label><input matInput type="password" name="password" [(ngModel)]="password" required minlength="8" autocomplete="new-password" /></mat-form-field>
        @if (error()) { <div class="error full">{{ error() }}</div> }
        <div class="row full"><button mat-flat-button type="submit" [disabled]="busy()">{{ 'auth.register' | translate }}</button><span class="grow"></span><a routerLink="/auth/login">{{ 'auth.haveAccount' | translate }}</a></div>
      </form>}
    </mat-card-content></mat-card></div>`,
})
export class RegisterPage {
  private api = inject(Api);
  displayName = ''; email = ''; password = ''; error = signal(''); busy = signal(false); done = signal(false);
  async submit(): Promise<void> {
    this.busy.set(true); this.error.set('');
    try { await firstValueFrom(this.api.post('/api/auth/register', { email: this.email, password: this.password, displayName: this.displayName, language: localStorage.getItem('lang') ?? 'de' })); this.done.set(true); }
    catch (e) { this.error.set(errorMessage(e)); } finally { this.busy.set(false); }
  }
}

@Component({
  imports,
  template: `<div class="page page-narrow"><mat-card appearance="outlined"><mat-card-content>
    <p>{{ (ok() === null ? 'common.loading' : ok() ? 'auth.confirmed' : 'auth.confirmFailed') | translate }}</p>
    <a mat-flat-button routerLink="/auth/login">{{ 'auth.login' | translate }}</a></mat-card-content></mat-card></div>`,
})
export class ConfirmPage {
  ok = signal<boolean | null>(null);
  constructor(route: ActivatedRoute, api: Api) {
    const q = route.snapshot.queryParamMap;
    api.post('/api/auth/confirm-email', { userId: q.get('userId'), code: q.get('code') }).subscribe({ next: () => this.ok.set(true), error: () => this.ok.set(false) });
  }
}

@Component({
  imports,
  template: `<div class="page page-narrow"><mat-card appearance="outlined"><mat-card-content>
    <h2>{{ 'auth.forgot' | translate }}</h2>
    @if (sent()) { <p>{{ 'auth.resetSent' | translate }}</p> } @else {
    <form (ngSubmit)="submit()" class="form-grid">
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'auth.email' | translate }}</mat-label><input matInput type="email" name="email" [(ngModel)]="email" required /></mat-form-field>
      <button mat-flat-button type="submit">OK</button></form>}
    </mat-card-content></mat-card></div>`,
})
export class ForgotPage {
  private api = inject(Api); email = ''; sent = signal(false);
  submit(): void { this.api.post('/api/auth/forgot-password', { email: this.email }).subscribe(() => this.sent.set(true)); }
}

@Component({
  imports,
  template: `<div class="page page-narrow"><mat-card appearance="outlined"><mat-card-content>
    <h2>{{ 'auth.newPassword' | translate }}</h2>
    @if (done()) { <p>{{ 'auth.resetDone' | translate }}</p><a mat-flat-button routerLink="/auth/login">{{ 'auth.login' | translate }}</a> } @else {
    <form (ngSubmit)="submit()" class="form-grid">
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'auth.newPassword' | translate }}</mat-label><input matInput type="password" name="password" [(ngModel)]="password" required minlength="8" /></mat-form-field>
      @if (error()) { <div class="error full">{{ error() }}</div> }
      <button mat-flat-button type="submit">OK</button></form>}
    </mat-card-content></mat-card></div>`,
})
export class ResetPage {
  private api = inject(Api); private route = inject(ActivatedRoute);
  password = ''; error = signal(''); done = signal(false);
  submit(): void {
    const q = this.route.snapshot.queryParamMap;
    this.api.post('/api/auth/reset-password', { userId: q.get('userId'), code: q.get('code'), password: this.password }).subscribe({ next: () => this.done.set(true), error: e => this.error.set(errorMessage(e)) });
  }
}
