import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatListModule } from '@angular/material/list';
import { MatDialog } from '@angular/material/dialog';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Api, AuthService, errorMessage } from '../../core/api';
import { TripSummary } from '../../core/models';
import { confirm, fmtDate, HarbourSelect, UploadZone, VisibilityPicker } from '../../shared/shared';
import { FollowButton } from '../social/social';

interface PublicProfile { userId: string; displayName: string; bio: string | null; experience: string | null; homeHarbour: string | null; avatarUrl: string | null; trips: TripSummary[]; }

@Component({
  imports: [RouterLink, MatCardModule, MatListModule, TranslatePipe, FollowButton],
  template: `
    @if (profile(); as p) {
    <div class="page page-narrow">
      <div class="row">@if (p.avatarUrl) { <img class="avatar" [src]="p.avatarUrl" /> }<h1 class="grow">{{ p.displayName }}</h1><app-follow targetType="User" [id]="p.userId" /></div>
      @if (p.homeHarbour) { <div class="muted">{{ 'ship.homeHarbour' | translate }}: {{ p.homeHarbour }}</div> }
      @if (p.bio) { <div class="rich" [innerHTML]="p.bio"></div> }
      @if (p.experience) { <p><b>{{ 'profile.experience' | translate }}:</b> {{ p.experience }}</p> }
      <h3>{{ 'nav.trips' | translate }}</h3>
      <mat-list>@for (t of p.trips; track t.id) { <a mat-list-item [routerLink]="['/trips', t.id]"><span matListItemTitle>{{ t.name }} · {{ t.shipName }}</span><span matListItemLine>{{ fmt(t.startDate) }} – {{ fmt(t.endDate) }}</span></a> }</mat-list>
    </div>}`,
  styles: `.avatar { width: 64px; height: 64px; border-radius: 50%; object-fit: cover; }`,
})
export class PublicProfilePage {
  private api = inject(Api); id = input.required<string>(); profile = signal<PublicProfile | null>(null); fmt = fmtDate;
  ngOnInit(): void { this.api.get<PublicProfile>(`/api/users/${this.id()}`).subscribe(p => this.profile.set(p)); }
}

@Component({
  imports: [FormsModule, MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, TranslatePipe, HarbourSelect, VisibilityPicker, UploadZone],
  template: `
    <div class="page page-narrow"><mat-card appearance="outlined"><mat-card-content>
      <h2>{{ 'profile.settings' | translate }}</h2>
      <div class="form-grid">
        <mat-form-field appearance="outline" class="full"><mat-label>{{ 'auth.displayName' | translate }}</mat-label><input matInput [(ngModel)]="m.displayName" required /></mat-form-field>
        <mat-form-field appearance="outline" class="full"><mat-label>{{ 'profile.bio' | translate }}</mat-label><textarea matInput rows="3" [(ngModel)]="m.bio"></textarea></mat-form-field>
        <mat-form-field appearance="outline" class="full"><mat-label>{{ 'profile.experience' | translate }}</mat-label><input matInput [(ngModel)]="m.experience" /></mat-form-field>
        <app-harbour-select [label]="'ship.homeHarbour' | translate" [(harbourId)]="m.homeHarbourId" />
        <mat-form-field appearance="outline"><mat-label>{{ 'profile.visibility' | translate }}</mat-label><mat-select [(ngModel)]="m.profileVisibility"><mat-option value="Public">{{ 'profile.public' | translate }}</mat-option><mat-option value="Registered">{{ 'profile.registered' | translate }}</mat-option></mat-select></mat-form-field>
        <app-visibility [(value)]="m.defaultContentVisibility" />
        <mat-form-field appearance="outline"><mat-label>{{ 'profile.language' | translate }}</mat-label><mat-select [(ngModel)]="m.language"><mat-option value="de">Deutsch</mat-option><mat-option value="en">English</mat-option></mat-select></mat-form-field>
        <mat-checkbox class="full" [(ngModel)]="m.emailNotifications">{{ 'profile.notifications' | translate }}</mat-checkbox>
        <div class="full"><b>{{ 'profile.avatar' | translate }}</b><app-upload url="/api/me/avatar" (uploaded)="auth.load()" /></div>
        @if (error()) { <div class="error full">{{ error() }}</div> }
        <div class="row full"><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button><span class="grow"></span>
          <a mat-stroked-button href="/api/me/export" target="_blank">{{ 'profile.export' | translate }}</a><button mat-stroked-button color="warn" (click)="remove()">{{ 'profile.deleteAccount' | translate }}</button></div>
      </div>
      <h3>{{ 'profile.calendar' | translate }}</h3>
      @if (feeds(); as f) { <p><code>{{ f.user }}</code></p> @for (s of f.ships; track s.shipId) { <p>{{ s.name }}: <code>{{ s.url }}</code></p> } }
    </mat-card-content></mat-card></div>`,
})
export class SettingsPage {
  private api = inject(Api); readonly auth = inject(AuthService); private dialog = inject(MatDialog); private translate = inject(TranslateService);
  error = signal(''); feeds = signal<{ user: string; ships: { shipId: string; name: string; url: string }[] } | null>(null);
  m = { displayName: this.auth.me()?.displayName ?? '', bio: '', homeHarbourId: null as string | null, experience: '', profileVisibility: 'Public', defaultContentVisibility: this.auth.me()?.defaultContentVisibility ?? 'Crew' as const, language: this.auth.me()?.language ?? 'de', emailNotifications: true };
  constructor() {
    this.api.get<PublicProfile>(`/api/users/${this.auth.me()!.id}`).subscribe(p => { this.m.bio = p.bio ?? ''; this.m.experience = p.experience ?? ''; });
    this.api.get<{ user: string; ships: { shipId: string; name: string; url: string }[] }>('/api/calendar/my-feeds').subscribe(f => this.feeds.set(f));
  }
  save(): void { this.api.put('/api/me/profile', this.m).subscribe({ next: () => { this.auth.load(); localStorage.setItem('lang', this.m.language); this.translate.use(this.m.language); }, error: e => this.error.set(errorMessage(e)) }); }
  async remove(): Promise<void> {
    if (!(await confirm(this.dialog, this.translate.instant('profile.deleteConfirm')))) return;
    this.api.delete('/api/me').subscribe({ next: () => this.auth.logout(), error: e => this.error.set(errorMessage(e)) });
  }
}
