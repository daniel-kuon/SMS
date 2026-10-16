import { Component, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, AuthService, errorMessage } from '../../core/api';

/** Accept page for ship and trip invitation links. */
@Component({
  imports: [RouterLink, MatCardModule, MatButtonModule, TranslatePipe],
  template: `
    <div class="page page-narrow"><mat-card appearance="outlined"><mat-card-content>
      @if (info(); as i) {
        <h2>{{ i.name }} @if (i.shipName) { <span class="muted">· {{ i.shipName }}</span> }</h2>
        <p>{{ i.role }} · {{ i.status }}</p>
        @if (auth.me()) { <button mat-flat-button (click)="accept()">{{ 'trip.accept' | translate }}</button> }
        @else { <a mat-flat-button [routerLink]="['/auth/login']" [queryParams]="{ returnUrl: '/invitations/' + kind() + '/' + token() }">{{ 'nav.login' | translate }}</a> <a mat-button routerLink="/auth/register">{{ 'nav.register' | translate }}</a> }
      }
      @if (error()) { <p class="error">{{ error() }}</p> }
    </mat-card-content></mat-card></div>`,
})
export class InvitationPage {
  private api = inject(Api); private router = inject(Router); readonly auth = inject(AuthService);
  kind = input.required<string>(); token = input.required<string>();
  info = signal<{ name: string; shipName?: string; role: string; status: string } | null>(null); error = signal('');
  ngOnInit(): void { this.api.get<{ name: string; shipName?: string; role: string; status: string }>(`/api/${this.kind()}-invitations/${this.token()}`).subscribe({ next: i => this.info.set(i), error: e => this.error.set(errorMessage(e)) }); }
  accept(): void {
    this.api.post<{ shipId?: string; tripId?: string }>(`/api/${this.kind()}-invitations/${this.token()}/accept`, {}).subscribe({
      next: r => this.router.navigate(r.tripId ? ['/trips', r.tripId] : ['/ships', r.shipId]), error: e => this.error.set(errorMessage(e)) });
  }
}
