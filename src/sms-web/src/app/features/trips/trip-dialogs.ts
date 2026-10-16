import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, errorMessage } from '../../core/api';
import { Crew, Trip, TripStatus } from '../../core/models';
import { HarbourSelect, VisibilityPicker } from '../../shared/shared';

const CURRENCIES = ['EUR', 'DKK', 'SEK', 'NOK', 'GBP', 'PLN', 'CHF', 'USD', 'HRK', 'TRY', 'CZK', 'ISK', 'JPY'];

@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe, VisibilityPicker, HarbourSelect],
  template: `
    <h2 mat-dialog-title>{{ data.trip?.name ?? ('trip.new' | translate) }}</h2>
    <mat-dialog-content class="form-grid">
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'common.name' | translate }}</mat-label><input matInput [(ngModel)]="m.name" required /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'trip.start' | translate }}</mat-label><input matInput type="date" [(ngModel)]="m.startDate" required /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'trip.end' | translate }}</mat-label><input matInput type="date" [(ngModel)]="m.endDate" required /></mat-form-field>
      <app-harbour-select [label]="'logbook.startHarbour' | translate" [(harbourId)]="m.plannedStartHarbourId" />
      <app-harbour-select [label]="'logbook.endHarbour' | translate" [(harbourId)]="m.plannedEndHarbourId" />
      <mat-form-field appearance="outline"><mat-label>{{ 'trip.status' | translate }}</mat-label><mat-select [(ngModel)]="m.status">
        @for (s of statuses; track s) { <mat-option [value]="s">{{ 'trip.status' + s | translate }}</mat-option> }</mat-select></mat-form-field>
      <app-visibility [(value)]="m.visibility" [options]="['Crew', 'Public']" />
      <mat-form-field appearance="outline"><mat-label>{{ 'trip.currency' | translate }}</mat-label><mat-select [(ngModel)]="m.currency">@for (c of currencies; track c) { <mat-option [value]="c">{{ c }}</mat-option> }</mat-select></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'trip.timeZone' | translate }}</mat-label><input matInput [(ngModel)]="m.timeZone" /></mat-form-field>
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'common.description' | translate }}</mat-label><textarea matInput rows="3" [(ngModel)]="m.description"></textarea></mat-form-field>
      @if (error()) { <div class="error full">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
})
export class TripEditDialog {
  data = inject<{ shipId: string; trip?: Trip }>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api);
  statuses: TripStatus[] = ['Planned', 'Active', 'Completed', 'Cancelled']; currencies = CURRENCIES;
  private t = this.data.trip;
  m = { name: this.t?.name ?? '', description: this.t?.description ?? '', startDate: this.t?.startDate ?? new Date().toISOString().slice(0, 10), endDate: this.t?.endDate ?? new Date(Date.now() + 7 * 864e5).toISOString().slice(0, 10),
    timeZone: this.t?.timeZone ?? 'Europe/Berlin', plannedStartHarbourId: this.t?.plannedStartHarbourId ?? null, plannedEndHarbourId: this.t?.plannedEndHarbourId ?? null,
    visibility: this.t?.visibility ?? 'Crew' as const, status: this.t?.status ?? 'Planned' as TripStatus, currency: this.t?.currency ?? 'EUR' };
  error = signal('');
  save(): void {
    const req = this.t ? this.api.put(`/api/trips/${this.t.id}`, this.m) : this.api.post<{ id: string }>(`/api/ships/${this.data.shipId}/trips`, this.m);
    req.subscribe({ next: r => this.ref.close((r as { id?: string })?.id ?? true), error: e => this.error.set(errorMessage(e)) });
  }
}

@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatCheckboxModule, MatAutocompleteModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ data.member ? data.member.name : ('trip.invite' | translate) }}</h2>
    <mat-dialog-content class="form-grid">
      @if (!data.member) {
        <mat-form-field appearance="outline" class="full"><mat-label>Benutzer suchen</mat-label>
          <input matInput [(ngModel)]="query" (ngModelChange)="search($event)" [matAutocomplete]="auto" [disabled]="guest" />
          <mat-autocomplete #auto="matAutocomplete" (optionSelected)="userId = $event.option.value.userId; query = $event.option.value.displayName">
            @for (u of hits(); track u.userId) { <mat-option [value]="u">{{ u.displayName }} <span class="muted">{{ u.email }}</span></mat-option> }
          </mat-autocomplete></mat-form-field>
        <mat-checkbox class="full" [(ngModel)]="guest">{{ 'trip.guest' | translate }}</mat-checkbox>
        @if (guest) { <mat-form-field appearance="outline" class="full"><mat-label>{{ 'trip.guestName' | translate }}</mat-label><input matInput [(ngModel)]="guestName" /></mat-form-field> }
      } @else if (data.member.isGuest) {
        <mat-form-field appearance="outline" class="full"><mat-label>{{ 'trip.guestName' | translate }}</mat-label><input matInput [(ngModel)]="guestName" /></mat-form-field>
        <mat-form-field appearance="outline" class="full"><mat-label>Mit Konto verknüpfen</mat-label>
          <input matInput [(ngModel)]="query" (ngModelChange)="search($event)" [matAutocomplete]="auto2" />
          <mat-autocomplete #auto2="matAutocomplete" (optionSelected)="userId = $event.option.value.userId; query = $event.option.value.displayName">
            @for (u of hits(); track u.userId) { <mat-option [value]="u">{{ u.displayName }}</mat-option> }
          </mat-autocomplete></mat-form-field>
      }
      <mat-form-field appearance="outline"><mat-label>{{ 'common.from' | translate }}</mat-label><input matInput type="date" [(ngModel)]="fromDate" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'common.to' | translate }}</mat-label><input matInput type="date" [(ngModel)]="toDate" /></mat-form-field>
      <mat-checkbox class="full" [(ngModel)]="canWriteLogbook">{{ 'trip.logbookPermission' | translate }}</mat-checkbox>
      @if (!data.member) { <button mat-button class="full" (click)="link()"><span>{{ 'trip.inviteLink' | translate }}</span></button> @if (inviteLink()) { <code class="full">{{ inviteLink() }}</code> } }
      @if (error()) { <div class="error full">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
})
export class CrewDialog {
  data = inject<{ tripId: string; member?: Crew }>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api);
  query = ''; userId = ''; guest = false; guestName = this.data.member?.name ?? ''; canWriteLogbook = this.data.member?.canWriteLogbook ?? false;
  fromDate = this.data.member?.fromDate ?? ''; toDate = this.data.member?.toDate ?? '';
  hits = signal<{ userId: string; displayName: string; email: string }[]>([]); inviteLink = signal(''); error = signal('');
  search(q: string): void { if (typeof q === 'string' && q.length >= 2) this.api.get<{ userId: string; displayName: string; email: string }[]>('/api/users', { q }).subscribe(h => this.hits.set(h)); }
  link(): void { this.api.post<{ link: string }>(`/api/trips/${this.data.tripId}/invitations`, {}).subscribe(r => this.inviteLink.set(r.link)); }
  save(): void {
    const body = { userId: this.guest ? null : this.userId || null, guestName: this.guest || this.data.member?.isGuest ? this.guestName : null, role: 'Crew', canWriteLogbook: this.canWriteLogbook, fromDate: this.fromDate || null, toDate: this.toDate || null };
    const req = this.data.member ? this.api.put(`/api/crew/${this.data.member.id}`, body) : this.api.post(`/api/trips/${this.data.tripId}/crew`, body);
    req.subscribe({ next: () => this.ref.close(true), error: e => this.error.set(errorMessage(e)) });
  }
}
