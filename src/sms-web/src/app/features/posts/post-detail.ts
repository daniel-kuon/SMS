import { Component, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, AuthService, errorMessage } from '../../core/api';
import { Media, Post, Trip } from '../../core/models';
import { fmtDate, fmtDateTime, Gallery, UploadZone, VisibilityPicker } from '../../shared/shared';
import { drafts } from '../../core/offline';
import { SocialBox } from '../social/social';
import { RichEditor } from '../../shared/rich-editor';

@Component({
  imports: [RouterLink, MatCardModule, MatButtonModule, TranslatePipe, Gallery, SocialBox],
  template: `
    @if (post(); as p) {
    <div class="page page-narrow">
      <h1>{{ p.title || ('post.by' | translate) + ' ' + p.author }}</h1>
      <div class="muted">{{ p.author }} · {{ fmtTime(p.publishedAt) }} · <a [routerLink]="['/trips', p.tripId, 'timeline']">{{ 'trip.timeline' | translate }}</a> · <span class="chip" [class]="'chip ' + p.visibility.toLowerCase()">{{ 'visibility.' + p.visibility | translate }}</span></div>
      <mat-card appearance="outlined"><mat-card-content><div class="rich" [innerHTML]="p.content"></div>@if (p.media.length) { <app-gallery [photos]="p.media" /> }</mat-card-content></mat-card>
      <app-social target="posts" [id]="p.id" />
    </div>}`,
})
export class PostDetailPage {
  private api = inject(Api); id = input.required<string>(); post = signal<Post | null>(null); fmtTime = fmtDateTime;
  ngOnInit(): void { this.api.get<Post>(`/api/posts/${this.id()}`).subscribe(p => this.post.set(p)); }
}

/** Blog post editor (spec 07 E1): rich text; photos uploaded in the upload zone are attached to the post as its gallery. */
@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslatePipe, VisibilityPicker, UploadZone, RichEditor],
  template: `
    <h2 mat-dialog-title>{{ 'post.new' | translate }}</h2>
    <mat-dialog-content class="form-grid">
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'post.title' | translate }}</mat-label><input matInput [(ngModel)]="m.title" /></mat-form-field>
      <div class="full"><sms-rich-editor [label]="'post.content' | translate" [value]="m.content" (valueChange)="m.content = $event; saveDraft()"
        [context]="{ tripId: data.trip.id, postId: data.postId, visibility: m.visibility }" /><div class="muted hint">{{ 'post.draftHint' | translate }}</div></div>
      <mat-form-field appearance="outline"><mat-label>{{ 'post.day' | translate }}</mat-label><input matInput type="date" [(ngModel)]="m.day" [min]="data.trip.startDate" [max]="data.trip.endDate" /></mat-form-field>
      <app-visibility [(value)]="m.visibility" />
      <div class="full"><app-upload [url]="'/api/trips/' + data.trip.id + '/media'" [fields]="{ visibility: m.visibility, day: m.day }" (uploaded)="attach($event)" />
        <div class="photo-grid">@for (p of photos(); track p.id) { <img [src]="p.thumbUrl" (click)="detach(p)" title="Entfernen" /> }</div></div>
      @if (error()) { <div class="error full">{{ error() }}</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (data.postId) { <button mat-button color="warn" (click)="remove()">{{ 'common.delete' | translate }}</button> }
      <button mat-button mat-dialog-close>{{ 'common.cancel' | translate }}</button><button mat-flat-button (click)="save()">{{ 'common.save' | translate }}</button></mat-dialog-actions>`,
})
export class PostEditorDialog {
  data = inject<{ trip: Trip; postId?: string }>(MAT_DIALOG_DATA); private ref = inject(MatDialogRef); private api = inject(Api); private auth = inject(AuthService);
  photos = signal<Media[]>([]); error = signal('');
  m = { title: '', content: '', day: clamp(new Date().toISOString().slice(0, 10), this.data.trip), visibility: this.auth.me()?.defaultContentVisibility ?? 'Crew' as const };
  private draftScope = `post:${this.data.trip.id}:${this.data.postId ?? 'new'}`;
  constructor() {
    const draft = drafts.load<typeof this.m & { photos?: Media[] }>(this.draftScope);
    if (draft) { this.m = { title: draft.title, content: draft.content, day: draft.day, visibility: draft.visibility }; this.photos.set(draft.photos ?? []); }
    else if (this.data.postId) this.api.get<Post>(`/api/posts/${this.data.postId}`).subscribe(p => { this.m = { title: p.title ?? '', content: p.content, day: p.day, visibility: p.visibility }; this.photos.set(p.media); });
  }
  saveDraft(): void { drafts.save(this.draftScope, { ...this.m, photos: this.photos() }); }
  attach(m: Media[]): void { this.photos.update(p => [...p, ...m]); this.saveDraft(); }
  detach(p: Media): void { this.photos.update(ps => ps.filter(x => x.id !== p.id)); }
  save(): void {
    const body = { ...this.m, mediaIds: this.photos().map(p => p.id) };
    const req = this.data.postId ? this.api.put(`/api/posts/${this.data.postId}`, body) : this.api.post(`/api/trips/${this.data.trip.id}/posts`, body);
    req.subscribe({ next: () => { drafts.clear(this.draftScope); this.ref.close(true); }, error: e => this.error.set(errorMessage(e)) });
  }
  remove(): void { this.api.delete(`/api/posts/${this.data.postId}`).subscribe(() => this.ref.close(true)); }
}

function clamp(day: string, t: Trip): string { return day < t.startDate ? t.startDate : day > t.endDate ? t.endDate : day; }
