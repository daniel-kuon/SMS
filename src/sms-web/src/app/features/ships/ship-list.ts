import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, errorMessage } from '../../core/api';
import { Paged, Ship } from '../../core/models';
import { HarbourSelect, VisibilityPicker } from '../../shared/shared';
import { RichEditor } from '../../shared/rich-editor';

@Component({
  imports: [RouterLink, MatButtonModule, MatCardModule, MatIconModule, TranslatePipe],
  template: `
    <div class="page">
      <div class="row"><h1 class="grow">{{ 'ship.myShips' | translate }}</h1><button mat-flat-button (click)="create()"><mat-icon>add</mat-icon> {{ 'ship.new' | translate }}</button></div>
      @if (ships().length === 0) { <p class="muted">{{ 'ship.noShips' | translate }}</p> }
      <div class="cards">
        @for (s of ships(); track s.id) {
          <mat-card appearance="outlined" [routerLink]="['/ships', s.id]" class="card">
            @if (s.photoUrl) { <img mat-card-image [src]="s.photoUrl" /> } @else { <div class="placeholder"><mat-icon>sailing</mat-icon></div> }
            <mat-card-content><h3>{{ s.name }}</h3><div class="muted">{{ s.type }} @if (s.homeHarbourName) { · {{ s.homeHarbourName }} }</div>
              <span class="chip" [class]="'chip ' + s.visibility.toLowerCase()">{{ 'visibility.' + s.visibility | translate }}</span> <span class="chip">{{ s.myRole }}</span></mat-card-content>
          </mat-card>
        }
      </div>
    </div>`,
  styles: `.cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(260px, 1fr)); gap: 12px; } .card { cursor: pointer; } .placeholder { height: 140px; display: grid; place-items: center; background: #e3eef9; color: #1b4d7a; } .placeholder mat-icon { font-size: 48px; width: 48px; height: 48px; }`,
})
export class ShipListPage {
  private api = inject(Api); private dialog = inject(MatDialog);
  ships = signal<Ship[]>([]);
  constructor() { this.load(); }
  load(): void { this.api.get<Paged<Ship>>('/api/ships', { mine: true, pageSize: 100 }).subscribe(p => this.ships.set(p.items)); }
  create(): void { this.dialog.open(ShipEditDialog, { data: null, width: '700px' }).afterClosed().subscribe(ok => ok && this.load()); }
}

@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslatePipe, VisibilityPicker, HarbourSelect, RichEditor],
  template: `
    <h2 mat-dialog-title>{{ data?.name ?? ('ship.new' | translate) }}</h2>
    <mat-dialog-content class="form-grid">
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'common.name' | translate }}</mat-label><input matInput [(ngModel)]="m.name" required /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'ship.type' | translate }}</mat-label><input matInput [(ngModel)]="m.type" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'ship.cruisingSpeed' | translate }}</mat-label><input matInput type="number" step="0.5" [(ngModel)]="m.cruisingSpeedKn" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'ship.length' | translate }}</mat-label><input matInput type="number" step="0.01" [(ngModel)]="m.lengthM" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'ship.beam' | translate }}</mat-label><input matInput type="number" step="0.01" [(ngModel)]="m.beamM" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'ship.draft' | translate }}</mat-label><input matInput type="number" step="0.01" [(ngModel)]="m.draftM" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'ship.airDraft' | translate }}</mat-label><input matInput type="number" step="0.01" [(ngModel)]="m.airDraftM" /></mat-form-field>
      <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic"><mat-label>{{ 'ship.minDepth' | translate }}</mat-label><input matInput type="number" step="0.1" min="0" [(ngModel)]="m.minDepthM" /><mat-hint>{{ 'ship.minDepthHint' | translate }}</mat-hint></mat-form-field>
      <mat-form-field appearance="outline" class="full" subscriptSizing="dynamic"><mat-label>{{ 'ship.warnDepth' | translate }}</mat-label><input matInput type="number" step="0.1" min="0" [(ngModel)]="m.warnDepthM" /><mat-hint>{{ 'ship.warnDepthHint' | translate }}</mat-hint></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'ship.sailNumber' | translate }}</mat-label><input matInput [(ngModel)]="m.sailNumber" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'ship.callSign' | translate }}</mat-label><input matInput [(ngModel)]="m.callSign" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'ship.mmsi' | translate }}</mat-label><input matInput [(ngModel)]="m.mmsi" /></mat-form-field>
      <app-harbour-select [label]="'ship.homeHarbour' | translate" [(harbourId)]="m.homeHarbourId" [harbourName]="data?.homeHarbourName ?? null" />
      <app-visibility [(value)]="m.visibility" [options]="['Private', 'Public']" />
      <sms-rich-editor class="full" [label]="'common.description' | translate" [(value)]="m.description" [context]="{ shipId: data?.id, visibility: 'Public' }" />
      @if (error()) { <div class="error full">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
})
export class ShipEditDialog {
  data = inject<Ship | null>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api);
  m = { name: this.data?.name ?? '', type: this.data?.type ?? '', lengthM: this.data?.lengthM ?? null, beamM: this.data?.beamM ?? null, draftM: this.data?.draftM ?? null, airDraftM: this.data?.airDraftM ?? null, minDepthM: this.data?.minDepthM ?? null, warnDepthM: this.data?.warnDepthM ?? null,
    sailNumber: this.data?.sailNumber ?? '', callSign: this.data?.callSign ?? '', mmsi: this.data?.mmsi ?? '', homeHarbourId: this.data?.homeHarbourId ?? null, cruisingSpeedKn: this.data?.cruisingSpeedKn ?? 5,
    description: this.data?.description ?? '', visibility: this.data?.visibility ?? 'Public' as const };
  error = signal('');
  save(): void {
    const req = this.data ? this.api.put(`/api/ships/${this.data.id}`, this.m) : this.api.post('/api/ships', this.m);
    req.subscribe({ next: () => this.ref.close(true), error: e => this.error.set(errorMessage(e)) });
  }
}
