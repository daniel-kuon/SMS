import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, errorMessage } from '../../core/api';
import { ContentPage } from '../../core/models';

@Component({
  imports: [FormsModule, MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, TranslatePipe],
  template: `
    <div class="page page-narrow">
      <h1>{{ 'admin.title' | translate }}</h1>
      <mat-card appearance="outlined"><mat-card-content>
        <h3>{{ 'admin.importOsm' | translate }}</h3>
        <div class="row"><mat-form-field appearance="outline" class="grow" subscriptSizing="dynamic"><mat-label>{{ 'admin.bbox' | translate }}</mat-label><input matInput [(ngModel)]="bbox" /></mat-form-field>
          <button mat-flat-button (click)="importOsm()" [disabled]="busy()">{{ 'admin.import' | translate }}</button></div>
        @if (result()) { <p>{{ result() }}</p> }
      </mat-card-content></mat-card>
      <mat-card appearance="outlined"><mat-card-content>
        <h3>{{ 'admin.pages' | translate }}</h3>
        <div class="row"><mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Slug</mat-label><input matInput [(ngModel)]="page.slug" (change)="loadPage()" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>Sprache</mat-label><mat-select [(ngModel)]="page.language" (selectionChange)="loadPage()"><mat-option value="de">de</mat-option><mat-option value="en">en</mat-option></mat-select></mat-form-field>
          <mat-checkbox [(ngModel)]="page.showInFooter">Footer</mat-checkbox></div>
        <mat-form-field appearance="outline" class="full-width"><mat-label>Titel</mat-label><input matInput [(ngModel)]="page.title" /></mat-form-field>
        <mat-form-field appearance="outline" class="full-width"><mat-label>Inhalt (HTML)</mat-label><textarea matInput rows="10" [(ngModel)]="page.content"></textarea></mat-form-field>
        <button mat-flat-button (click)="savePage()">{{ 'common.save' | translate }}</button>
      </mat-card-content></mat-card>
      <mat-card appearance="outlined"><mat-card-content>
        <h3>{{ 'admin.signoz' | translate }}</h3>
        <p>SigNoz UI: <a href="http://localhost:3302" target="_blank">http://localhost:3302</a> · Login: admin&#64;localhost.dev / SailingPortal2026!</p>
      </mat-card-content></mat-card>
    </div>`,
  styles: `.full-width { width: 100%; } mat-card { margin-bottom: 12px; }`,
})
export class AdminPage {
  private api = inject(Api);
  bbox = '54.2,9.8,54.7,10.9'; busy = signal(false); result = signal('');
  page: ContentPage = { slug: 'impressum', title: '', content: '', language: 'de', showInFooter: true };
  constructor() { this.loadPage(); }
  importOsm(): void {
    this.busy.set(true); this.result.set('');
    this.api.post<{ created: number; updated: number; skipped: number }>(`/api/admin/harbours/import-osm?bbox=${encodeURIComponent(this.bbox)}`, {}).subscribe({
      next: r => { this.busy.set(false); this.result.set(`${r.created} neu, ${r.updated} aktualisiert, ${r.skipped} übersprungen`); }, error: e => { this.busy.set(false); this.result.set(errorMessage(e)); } });
  }
  loadPage(): void { this.api.get<ContentPage>(`/api/pages/${this.page.slug}`, { lang: this.page.language }).subscribe({ next: p => this.page = { ...p, slug: this.page.slug, language: this.page.language }, error: () => { this.page.title = ''; this.page.content = ''; } }); }
  savePage(): void { this.api.put(`/api/pages/${this.page.slug}`, this.page).subscribe(() => this.result.set('Seite gespeichert.')); }
}
