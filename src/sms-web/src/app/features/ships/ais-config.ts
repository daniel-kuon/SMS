import { Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, errorMessage } from '../../core/api';
import { fmtDateTime } from '../../shared/shared';

export interface AisConfig { provider: string; configured: boolean; mmsi: string | null; pollIntervalMinutes: number; enabled: boolean; lastPolledAt: string | null; callsThisMonth: number; lastError: string | null; providers: string[]; }

/** Spec 12 §12.1 C: owner-side AIS provider settings; the API key is write-only. */
@Component({
  selector: 'app-ais-config',
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslatePipe],
  template: `
    <h3>{{ 'ais.title' | translate }}</h3>
    <p class="muted">{{ 'ais.hint' | translate }}</p>
    @if (cfg(); as c) {
      <form class="form-grid" (ngSubmit)="save()">
        <mat-form-field appearance="outline"><mat-label>{{ 'ais.provider' | translate }}</mat-label><mat-select name="provider" [(ngModel)]="m.provider">@for (p of c.providers; track p) { <mat-option [value]="p">{{ p }}</mat-option> }</mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>{{ 'ais.apiKey' | translate }}</mat-label><input matInput name="apiKey" type="password" autocomplete="off" [(ngModel)]="m.apiKey" [placeholder]="c.configured ? ('ais.keyStored' | translate) : ''" /></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>MMSI</mat-label><input matInput name="mmsi" [(ngModel)]="m.mmsi" /></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>{{ 'ais.interval' | translate }}</mat-label><input matInput name="interval" type="number" min="1" max="1440" [(ngModel)]="m.pollIntervalMinutes" /></mat-form-field>
        <mat-checkbox name="enabled" [(ngModel)]="m.enabled">{{ 'ais.enabled' | translate }}</mat-checkbox>
        <div class="row full">
          <button mat-flat-button type="submit">{{ 'common.save' | translate }}</button>
          <button mat-stroked-button type="button" (click)="test()" [disabled]="!c.configured || busy()">{{ 'ais.test' | translate }}</button>
          <button mat-stroked-button type="button" (click)="poll()" [disabled]="!c.configured || !c.enabled || busy()">{{ 'ais.pollNow' | translate }}</button>
        </div>
      </form>
      <p class="muted">
        @if (c.configured) { {{ 'ais.keyStored' | translate }} · } {{ 'ais.calls' | translate }}: {{ c.callsThisMonth }}
        @if (c.lastPolledAt) { · {{ 'ais.lastPoll' | translate }} {{ fmtTime(c.lastPolledAt) }} }
        @if (c.lastError) { · <span class="error">{{ c.lastError }}</span> }
      </p>
      @if (result()) { <p>{{ result() }}</p> }
    }`,
})
export class AisConfigCard implements OnInit {
  private api = inject(Api);
  shipId = input.required<string>();
  cfg = signal<AisConfig | null>(null); result = signal(''); busy = signal(false);
  m = { provider: '', apiKey: '', mmsi: '', pollIntervalMinutes: 30, enabled: false };
  fmtTime = fmtDateTime;

  ngOnInit(): void { this.load(); }
  load(): void {
    this.api.get<AisConfig>(`/api/ships/${this.shipId()}/ais-config`).subscribe(c => { this.cfg.set(c); this.m = { provider: c.provider, apiKey: '', mmsi: c.mmsi ?? '', pollIntervalMinutes: c.pollIntervalMinutes, enabled: c.enabled }; });
  }
  save(): void {
    this.api.put<AisConfig>(`/api/ships/${this.shipId()}/ais-config`, { ...this.m, apiKey: this.m.apiKey || null, mmsi: this.m.mmsi || null }).subscribe({
      next: () => { this.result.set(''); this.load(); }, error: e => this.result.set(errorMessage(e)) });
  }
  test(): void {
    this.busy.set(true);
    this.api.post<{ count: number; latest: { time: string; lat: number; lng: number; sogKn: number | null } | null }>(`/api/ships/${this.shipId()}/ais-config/test`, {}).subscribe({
      next: r => { this.busy.set(false); this.result.set(r.latest ? `${r.count} Positionen – zuletzt ${this.fmtTime(r.latest.time)} bei ${r.latest.lat.toFixed(4)}, ${r.latest.lng.toFixed(4)}${r.latest.sogKn != null ? ` · ${r.latest.sogKn.toFixed(1)} kn` : ''}` : 'Verbindung ok, aber keine Position empfangen (sendet das Schiff gerade AIS?).'); },
      error: e => { this.busy.set(false); this.result.set(errorMessage(e)); } });
  }
  poll(): void {
    this.busy.set(true);
    this.api.post<{ added: number }>(`/api/ships/${this.shipId()}/ais-config/poll`, {}).subscribe({
      next: r => { this.busy.set(false); this.result.set(`${r.added} Positionen übernommen.`); this.load(); }, error: e => { this.busy.set(false); this.result.set(errorMessage(e)); } });
  }
}
