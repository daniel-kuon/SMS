import { Component, inject, input, model, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { debounceTime, distinctUntilChanged, Subject, switchMap } from 'rxjs';
import PhotoSwipeLightbox from 'photoswipe/lightbox';
import PhotoSwipe from 'photoswipe';
import { Api, errorMessage, Visibility } from '../core/api';
import { HarbourMarker, Media } from '../core/models';

export const VISIBILITIES: Visibility[] = ['Private', 'Crew', 'Public'];

@Component({
  selector: 'app-confirm',
  imports: [MatDialogModule, MatButtonModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    @if (data.message) { <mat-dialog-content>{{ data.message }}</mat-dialog-content> }
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button>
      <button mat-flat-button color="warn" [mat-dialog-close]="true">{{ data.ok ?? ('common.delete' | translate) }}</button>
    </mat-dialog-actions>`,
})
export class ConfirmDialog { data = inject<{ title: string; message?: string; ok?: string }>(MAT_DIALOG_DATA); }

export async function confirm(dialog: MatDialog, title: string, message?: string, ok?: string): Promise<boolean> {
  const ref = dialog.open(ConfirmDialog, { data: { title, message, ok } });
  return !!(await ref.afterClosed().toPromise());
}

/** Visibility select (Privat / Crew / Öffentlich). */
@Component({
  selector: 'app-visibility',
  imports: [MatFormFieldModule, MatSelectModule, FormsModule, TranslatePipe],
  template: `
    <mat-form-field appearance="outline" class="full-width">
      <mat-label>{{ 'common.visibility' | translate }}</mat-label>
      <mat-select [ngModel]="value()" (ngModelChange)="value.set($event)">
        @for (v of options(); track v) { <mat-option [value]="v">{{ 'visibility.' + v | translate }}</mat-option> }
      </mat-select>
    </mat-form-field>`,
})
export class VisibilityPicker {
  value = model<Visibility>('Crew');
  options = input<Visibility[]>(VISIBILITIES);
}

/** Harbour autocomplete bound to the catalogue search. */
@Component({
  selector: 'app-harbour-select',
  imports: [MatFormFieldModule, MatInputModule, MatAutocompleteModule, FormsModule],
  template: `
    <mat-form-field appearance="outline" class="full-width">
      <mat-label>{{ label() }}</mat-label>
      <input matInput [ngModel]="text()" (ngModelChange)="onType($event)" [matAutocomplete]="auto" [required]="required()" />
      <mat-autocomplete #auto="matAutocomplete" [displayWith]="display" (optionSelected)="select($event.option.value)">
        @for (h of hits(); track h.id) { <mat-option [value]="h">{{ h.name }} <span class="muted">{{ h.country }}</span></mat-option> }
      </mat-autocomplete>
    </mat-form-field>`,
})
export class HarbourSelect {
  private api = inject(Api);
  label = input('Hafen');
  required = input(false);
  harbourId = model<string | null>(null);
  harbourName = input<string | null>(null);
  selected = output<HarbourMarker>();
  text = signal('');
  hits = signal<HarbourMarker[]>([]);
  private query$ = new Subject<string>();

  constructor() {
    this.query$.pipe(debounceTime(200), distinctUntilChanged(), switchMap(q => this.api.get<HarbourMarker[]>('/api/harbours', { q, limit: 15 }))).subscribe(h => this.hits.set(h));
  }
  ngOnInit(): void { if (this.harbourName()) this.text.set(this.harbourName()!); }
  ngOnChanges(): void { if (this.harbourName() && !this.text()) this.text.set(this.harbourName()!); }
  display = (h: HarbourMarker | string | null) => (typeof h === 'string' ? h : h?.name ?? '');
  onType(v: string | HarbourMarker): void {
    if (typeof v !== 'string') return;
    this.text.set(v);
    if (v.length >= 2) this.query$.next(v); else this.hits.set([]);
    this.harbourId.set(null);
  }
  select(h: HarbourMarker): void { this.text.set(h.name); this.harbourId.set(h.id); this.selected.emit(h); }
}

/** Photo grid with a PhotoSwipe lightbox. */
@Component({
  selector: 'app-gallery',
  template: `
    <div class="photo-grid" [attr.id]="id">
      @for (m of photos(); track m.id) {
        <a [href]="m.previewUrl" [attr.data-pswp-width]="m.width ?? 1600" [attr.data-pswp-height]="m.height ?? 1200" target="_blank" rel="noreferrer">
          <img [src]="m.thumbUrl" [alt]="m.caption ?? ''" [title]="m.caption ?? ''" loading="lazy" />
        </a>
      }
    </div>`,
})
export class Gallery {
  photos = input.required<Media[]>();
  id = `g${Math.random().toString(36).slice(2)}`;
  private lightbox?: PhotoSwipeLightbox;
  ngAfterViewInit(): void {
    this.lightbox = new PhotoSwipeLightbox({ gallery: `#${this.id}`, children: 'a', pswpModule: PhotoSwipe });
    this.lightbox.on('uiRegister', () => this.lightbox!.pswp?.ui?.registerElement({
      name: 'caption', order: 9, isButton: false, appendTo: 'root',
      onInit: (el, pswp) => pswp.on('change', () => { el.innerHTML = (pswp.currSlide?.data.element as HTMLElement | undefined)?.querySelector('img')?.title ?? ''; }),
    }));
    this.lightbox.init();
  }
  ngOnDestroy(): void { this.lightbox?.destroy(); }
}

/** Drag-and-drop multi-file upload (spec 05 §5.5 / 07 E3). */
@Component({
  selector: 'app-upload',
  imports: [MatIconModule, MatProgressBarModule, TranslatePipe],
  template: `
    <div class="drop" [class.over]="over()" (dragover)="$event.preventDefault(); over.set(true)" (dragleave)="over.set(false)" (drop)="onDrop($event)" (click)="file.click()">
      <mat-icon>add_photo_alternate</mat-icon>
      <span>{{ 'media.drop' | translate }}</span>
      <input #file type="file" accept="image/*" multiple hidden (change)="onPick($event)" />
    </div>
    @if (busy()) { <mat-progress-bar mode="indeterminate" /> <span class="muted">{{ 'media.uploading' | translate }}</span> }
    @if (error()) { <div class="error">{{ error() }}</div> }`,
  styles: `
    .drop { border: 2px dashed #9ab; border-radius: 8px; padding: 16px; display: flex; gap: 8px; align-items: center; justify-content: center; cursor: pointer; color: #567; }
    .drop.over { background: #e8f1fb; border-color: #39f; }`,
})
export class UploadZone {
  private api = inject(Api);
  url = input.required<string>();
  fields = input<Record<string, string>>({});
  uploaded = output<Media[]>();
  over = signal(false);
  busy = signal(false);
  error = signal('');

  onDrop(e: DragEvent): void { e.preventDefault(); this.over.set(false); this.send(Array.from(e.dataTransfer?.files ?? [])); }
  onPick(e: Event): void { this.send(Array.from((e.target as HTMLInputElement).files ?? [])); (e.target as HTMLInputElement).value = ''; }
  private send(files: File[]): void {
    const images = files.filter(f => f.type.startsWith('image/'));
    if (images.length === 0) return;
    this.busy.set(true); this.error.set('');
    this.api.upload<Media[]>(this.url(), images, this.fields()).subscribe({
      next: m => { this.busy.set(false); this.uploaded.emit(m); },
      error: err => { this.busy.set(false); this.error.set(errorMessage(err)); },
    });
  }
}

export function fmtDate(d: string | null | undefined, opts: Intl.DateTimeFormatOptions = { dateStyle: 'medium' }): string {
  if (!d) return '';
  return new Intl.DateTimeFormat(localStorage.getItem('lang') === 'en' ? 'en-GB' : 'de-DE', opts).format(new Date(d));
}
export function fmtDateTime(d: string | null | undefined): string { return fmtDate(d, { dateStyle: 'short', timeStyle: 'short' }); }
export function fmtMoney(v: number | null | undefined, currency: string): string {
  if (v === null || v === undefined) return '';
  return new Intl.NumberFormat(localStorage.getItem('lang') === 'en' ? 'en-GB' : 'de-DE', { style: 'currency', currency }).format(v);
}
