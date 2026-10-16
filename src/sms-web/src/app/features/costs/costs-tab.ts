import { Component, computed, inject, input, output, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, errorMessage } from '../../core/api';
import { Balances, Expense, SplitMode, Trip } from '../../core/models';
import { confirm, fmtDate, fmtMoney } from '../../shared/shared';

/** "Kosten" tab (spec 09 §9.5): summary, balances, suggested transfers, expenses, settlements, lock, export. */
@Component({
  selector: 'app-costs-tab',
  imports: [DecimalPipe, MatButtonModule, MatIconModule, MatTableModule, MatCardModule, TranslatePipe],
  template: `
    @if (balances(); as b) {
    <div class="costs">
      <div class="row toolbar">
        @if (!b.lockedAt) { <button mat-flat-button (click)="newExpense()"><mat-icon>add</mat-icon> {{ 'costs.newExpense' | translate }}</button> } @else { <span class="chip crew">{{ 'costs.locked' | translate }}</span> }
        @if (trip().me?.role === 'Skipper') { <button mat-stroked-button (click)="lock(!b.lockedAt)">{{ (b.lockedAt ? 'costs.unlock' : 'costs.lock') | translate }}</button> }
        <a mat-stroked-button [href]="'/api/trips/' + trip().id + '/costs/export'" target="_blank"><mat-icon>download</mat-icon> {{ 'costs.export' | translate }}</a>
      </div>
      <div class="cards">
        <mat-card appearance="outlined"><mat-card-content><span class="label">{{ 'costs.total' | translate }}</span><b>{{ money(b.total) }}</b></mat-card-content></mat-card>
        @if (mine(); as m) {
          <mat-card appearance="outlined"><mat-card-content><span class="label">{{ 'costs.myPaid' | translate }}</span><b>{{ money(m.paid) }}</b></mat-card-content></mat-card>
          <mat-card appearance="outlined"><mat-card-content><span class="label">{{ 'costs.myShare' | translate }}</span><b>{{ money(m.share) }}</b></mat-card-content></mat-card>
          <mat-card appearance="outlined" [class.pos]="m.balance > 0" [class.neg]="m.balance < 0"><mat-card-content><span class="label">{{ (m.balance >= 0 ? 'costs.youGet' : 'costs.youOwe') | translate }}</span><b>{{ money(abs(m.balance)) }}</b></mat-card-content></mat-card>
        }
      </div>

      <h3>{{ 'costs.balances' | translate }}</h3>
      <table mat-table [dataSource]="b.balances" class="full-width">
        <ng-container matColumnDef="name"><th mat-header-cell *matHeaderCellDef>{{ 'common.name' | translate }}</th><td mat-cell *matCellDef="let r">{{ r.name }}</td></ng-container>
        <ng-container matColumnDef="paid"><th mat-header-cell *matHeaderCellDef>{{ 'costs.paid' | translate }}</th><td mat-cell *matCellDef="let r">{{ money(r.paid) }}</td></ng-container>
        <ng-container matColumnDef="share"><th mat-header-cell *matHeaderCellDef>{{ 'costs.share' | translate }}</th><td mat-cell *matCellDef="let r">{{ money(r.share) }}</td></ng-container>
        <ng-container matColumnDef="balance"><th mat-header-cell *matHeaderCellDef>{{ 'costs.balance' | translate }}</th><td mat-cell *matCellDef="let r" [class.pos]="r.balance > 0" [class.neg]="r.balance < 0">{{ money(r.balance) }}</td></ng-container>
        <tr mat-header-row *matHeaderRowDef="['name','paid','share','balance']"></tr><tr mat-row *matRowDef="let r; columns: ['name','paid','share','balance']"></tr>
      </table>

      <h3>{{ 'costs.transfers' | translate }}</h3>
      @if (b.suggestedTransfers.length === 0) { <p class="muted">✓</p> }
      @for (t of b.suggestedTransfers; track $index) {
        <div class="row transfer"><span>{{ t.from }} → {{ t.to }}: <b>{{ money(t.amount) }}</b></span>
          @if (t.fromCrewMemberId === b.myCrewMemberId || trip().me?.role === 'Skipper') { <button mat-stroked-button (click)="report(t.toCrewMemberId, t.amount, t.fromCrewMemberId)">{{ 'costs.markPaid' | translate }}</button> }</div>
      }
      @if (b.settlements.length) { <h4>{{ 'costs.settlements' | translate }}</h4>
        @for (s of b.settlements; track s.id) { <div class="row transfer"><span>{{ fmt(s.paidAt) }}: {{ s.from }} → {{ s.to }} <b>{{ money(s.amount) }}</b> <span class="chip" [class.public]="s.status === 'Confirmed'">{{ 'costs.' + s.status.toLowerCase() | translate }}</span></span>
          @if (s.status === 'Reported' && (s.toCrewMemberId === b.myCrewMemberId || trip().me?.role === 'Skipper')) { <button mat-stroked-button (click)="confirmSettlement(s.id)">{{ 'costs.confirmReceipt' | translate }}</button> }
          @if (s.status === 'Reported' && s.fromCrewMemberId === b.myCrewMemberId) { <button mat-icon-button (click)="deleteSettlement(s.id)"><mat-icon>delete</mat-icon></button> }</div> } }

      <h3>{{ 'costs.expenses' | translate }}</h3>
      <div class="scroll"><table mat-table [dataSource]="expenses()" class="full-width">
        <ng-container matColumnDef="paidOn"><th mat-header-cell *matHeaderCellDef>{{ 'costs.paidOn' | translate }}</th><td mat-cell *matCellDef="let e">{{ fmt(e.paidOn) }}</td></ng-container>
        <ng-container matColumnDef="category"><th mat-header-cell *matHeaderCellDef>{{ 'costs.category' | translate }}</th><td mat-cell *matCellDef="let e">{{ e.category }} <span class="muted">{{ e.description }}</span></td></ng-container>
        <ng-container matColumnDef="paidBy"><th mat-header-cell *matHeaderCellDef>{{ 'costs.paidBy' | translate }}</th><td mat-cell *matCellDef="let e">{{ e.paidBy }}</td></ng-container>
        <ng-container matColumnDef="amount"><th mat-header-cell *matHeaderCellDef>{{ 'costs.amount' | translate }}</th><td mat-cell *matCellDef="let e">{{ money(e.amountInTripCurrency) }} @if (e.currency !== b.currency) { <span class="muted">({{ fmtM(e.amount, e.currency) }} @ {{ e.exchangeRate | number:'1.4-4' }}, {{ e.rateSource }} {{ fmt(e.rateDate) }})</span> }</td></ng-container>
        <ng-container matColumnDef="shares"><th mat-header-cell *matHeaderCellDef>{{ 'costs.splitAmong' | translate }}</th><td mat-cell *matCellDef="let e"><span class="muted">@for (s of e.shares; track s.crewMemberId) { {{ s.name }} {{ money(s.computedShare) }}; }</span></td></ng-container>
        <ng-container matColumnDef="actions"><th mat-header-cell *matHeaderCellDef></th><td mat-cell *matCellDef="let e">@if (!e.deleted && !b.lockedAt) { <button mat-icon-button (click)="edit(e)"><mat-icon>edit</mat-icon></button><button mat-icon-button (click)="remove(e)"><mat-icon>delete</mat-icon></button> } @if (e.deleted) { <span class="chip private">gelöscht</span> }</td></ng-container>
        <tr mat-header-row *matHeaderRowDef="cols"></tr><tr mat-row *matRowDef="let e; columns: cols" [class.deleted]="e.deleted"></tr>
      </table></div>
    </div>}`,
  styles: `
    .toolbar { padding: 12px 0; } .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 10px; margin-bottom: 12px; }
    .cards mat-card-content { display: flex; flex-direction: column; } .label { font-size: 12px; color: #666; } .cards b { font-size: 22px; }
    .pos { color: #1c5b2e; } .neg { color: #8a1c1c; } .transfer { padding: 6px 0; border-bottom: 1px solid #eee; } .deleted { opacity: .5; text-decoration: line-through; }
    .scroll { overflow-x: auto; } .full-width { width: 100%; }`,
})
export class CostsTab {
  private api = inject(Api); private dialog = inject(MatDialog);
  trip = input.required<Trip>(); changed = output<void>();
  balances = signal<Balances | null>(null); expenses = signal<Expense[]>([]); categories = signal<string[]>([]);
  cols = ['paidOn', 'category', 'paidBy', 'amount', 'shares', 'actions'];
  mine = computed(() => this.balances()?.balances.find(b => b.crewMemberId === this.balances()?.myCrewMemberId) ?? null);
  fmt = fmtDate; fmtM = fmtMoney; abs = Math.abs;
  money = (v: number) => fmtMoney(v, this.balances()?.currency ?? this.trip().currency);
  ngOnInit(): void { this.load(); }
  load(): void {
    this.api.get<Balances>(`/api/trips/${this.trip().id}/balances`).subscribe(b => this.balances.set(b));
    this.api.get<{ categories: string[]; items: Expense[] }>(`/api/trips/${this.trip().id}/expenses`).subscribe(r => { this.expenses.set(r.items); this.categories.set(r.categories); });
  }
  newExpense(): void { this.dialog.open(ExpenseDialog, { data: { trip: this.trip(), categories: this.categories() }, width: '700px' }).afterClosed().subscribe(ok => ok && this.load()); }
  edit(e: Expense): void { this.dialog.open(ExpenseDialog, { data: { trip: this.trip(), categories: this.categories(), expense: e }, width: '700px' }).afterClosed().subscribe(ok => ok && this.load()); }
  async remove(e: Expense): Promise<void> { if (await confirm(this.dialog, 'Ausgabe löschen?')) this.api.delete(`/api/expenses/${e.id}`).subscribe(() => this.load()); }
  report(to: string, amount: number, from: string): void { this.api.post(`/api/trips/${this.trip().id}/settlements`, { toCrewMemberId: to, amount, fromCrewMemberId: from }).subscribe(() => this.load()); }
  confirmSettlement(id: string): void { this.api.post(`/api/settlements/${id}/confirm`, {}).subscribe(() => this.load()); }
  deleteSettlement(id: string): void { this.api.delete(`/api/settlements/${id}`).subscribe(() => this.load()); }
  lock(lock: boolean): void { this.api.post(`/api/trips/${this.trip().id}/costs/${lock ? 'lock' : 'unlock'}`, {}).subscribe(() => { this.load(); this.changed.emit(); }); }
}

const CURRENCIES = ['EUR', 'DKK', 'SEK', 'NOK', 'GBP', 'PLN', 'CHF', 'USD', 'CZK', 'HUF', 'ISK', 'TRY', 'JPY'];

@Component({
  imports: [DecimalPipe, FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'costs.newExpense' | translate }}</h2>
    <mat-dialog-content class="form-grid">
      <mat-form-field appearance="outline"><mat-label>{{ 'costs.amount' | translate }}</mat-label><input matInput type="number" step="0.01" min="0" [(ngModel)]="m.amount" (ngModelChange)="preview()" required /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'costs.currency' | translate }}</mat-label><mat-select [(ngModel)]="m.currency" (selectionChange)="fetchRate()">@for (c of currencies; track c) { <mat-option [value]="c">{{ c }}</mat-option> }</mat-select></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'costs.paidOn' | translate }}</mat-label><input matInput type="date" [(ngModel)]="m.paidOn" (ngModelChange)="fetchRate()" required /></mat-form-field>
      @if (m.currency !== data.trip.currency) {
        <mat-form-field appearance="outline"><mat-label>{{ 'costs.rate' | translate }} (1 {{ m.currency }} = ? {{ data.trip.currency }})</mat-label><input matInput type="number" step="0.000001" [(ngModel)]="m.exchangeRate" (ngModelChange)="manual = true; preview()" />
          <mat-hint>@if (ecb(); as r) { {{ 'costs.rateHint' | translate }} {{ r.rateDate }}: 1 {{ data.trip.currency }} = {{ (1 / r.rate) | number:'1.2-4' }} {{ m.currency }} } @else { {{ 'costs.manualRate' | translate }} }</mat-hint></mat-form-field>
        <div class="muted">{{ 'costs.converted' | translate }} <b>{{ converted() | number:'1.2-2' }} {{ data.trip.currency }}</b></div>
      }
      <mat-form-field appearance="outline"><mat-label>{{ 'costs.category' | translate }}</mat-label><mat-select [(ngModel)]="m.category">@for (c of data.categories; track c) { <mat-option [value]="c">{{ c }}</mat-option> }</mat-select></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'costs.paidBy' | translate }}</mat-label><mat-select [(ngModel)]="m.paidByCrewMemberId" [disabled]="data.trip.me?.role !== 'Skipper'">@for (c of crew; track c.id) { <mat-option [value]="c.id">{{ c.name }}</mat-option> }</mat-select></mat-form-field>
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'common.description' | translate }}</mat-label><input matInput [(ngModel)]="m.description" /></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>{{ 'costs.splitMode' | translate }}</mat-label><mat-select [(ngModel)]="m.splitMode"><mat-option value="Equal">{{ 'costs.equal' | translate }}</mat-option><mat-option value="Weights">{{ 'costs.weights' | translate }}</mat-option><mat-option value="Exact">{{ 'costs.exact' | translate }}</mat-option></mat-select></mat-form-field>
      <div class="full"><b>{{ 'costs.splitAmong' | translate }}:</b>
        <div class="participants">@for (p of participants; track p.id) {
          <div class="row"><mat-checkbox [(ngModel)]="p.selected">{{ p.name }}</mat-checkbox>
            @if (m.splitMode === 'Weights') { <mat-form-field appearance="outline" subscriptSizing="dynamic" class="small"><mat-label>{{ 'costs.weight' | translate }}</mat-label><input matInput type="number" step="0.5" [(ngModel)]="p.weight" /></mat-form-field> }
            @if (m.splitMode === 'Exact') { <mat-form-field appearance="outline" subscriptSizing="dynamic" class="small"><mat-label>{{ data.trip.currency }}</mat-label><input matInput type="number" step="0.01" [(ngModel)]="p.exact" /></mat-form-field> }</div> }</div></div>
      @if (error()) { <div class="error full">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
  styles: `.small { width: 120px; } .participants .row { padding: 2px 0; }`,
})
export class ExpenseDialog {
  data = inject<{ trip: Trip; categories: string[]; expense?: Expense }>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api);
  currencies = CURRENCIES; error = signal(''); ecb = signal<{ rate: number; rateDate: string } | null>(null); manual = false;
  crew = this.data.trip.crew.filter(c => c.status === 'Accepted' || c.status === 'Left');
  private e = this.data.expense;
  m = { amount: this.e?.amount ?? null as number | null, currency: this.e?.currency ?? this.data.trip.currency, paidOn: this.e?.paidOn ?? new Date().toISOString().slice(0, 10), exchangeRate: this.e?.exchangeRate ?? null as number | null,
    category: this.e?.category ?? this.data.categories[0] ?? 'Sonstiges', description: this.e?.description ?? '', paidByCrewMemberId: this.e?.paidByCrewMemberId ?? this.data.trip.me?.id ?? '', splitMode: this.e?.splitMode ?? 'Equal' as SplitMode };
  participants = this.crew.map(c => { const s = this.e?.shares.find(x => x.crewMemberId === c.id); return { id: c.id, name: c.name, selected: this.e ? !!s : c.status === 'Accepted', weight: s?.weight ?? 1, exact: s?.exactAmount ?? null as number | null }; });
  constructor() { this.manual = this.e?.rateSource === 'Manual'; if (!this.manual) this.fetchRate(); }
  fetchRate(): void {
    if (this.m.currency === this.data.trip.currency) { this.ecb.set(null); return; }
    this.api.get<{ rate: number; rateDate: string }>('/api/exchange-rates', { from: this.m.currency, to: this.data.trip.currency, date: this.m.paidOn }).subscribe({
      next: r => { this.ecb.set(r); if (!this.manual) this.m.exchangeRate = r.rate; }, error: () => this.ecb.set(null) });
  }
  preview(): void { /* template recomputes */ }
  converted(): number { return (this.m.amount ?? 0) * (this.m.exchangeRate ?? 0); }
  save(): void {
    const body = { ...this.m, exchangeRate: this.manual ? this.m.exchangeRate : null, participants: this.participants.filter(p => p.selected).map(p => ({ crewMemberId: p.id, weight: this.m.splitMode === 'Weights' ? p.weight : null, exactAmount: this.m.splitMode === 'Exact' ? p.exact : null })) };
    const req = this.e ? this.api.put(`/api/expenses/${this.e.id}`, body) : this.api.post(`/api/trips/${this.data.trip.id}/expenses`, body);
    req.subscribe({ next: () => this.ref.close(true), error: err => this.error.set(errorMessage(err)) });
  }
}
