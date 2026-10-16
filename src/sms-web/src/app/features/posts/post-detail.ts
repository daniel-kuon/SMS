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
import { SocialBox } from '../social/social';

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

/** Blog post editor (spec 07 E1): plain text with line breaks → paragraphs; photos uploaded here are attached to the post. */
@Component({
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslatePipe, VisibilityPicker, UploadZone],
  template: `
    <h2 mat-dialog-title>{{ 'post.new' | translate }}</h2>
    <mat-dialog-content class="form-grid">
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'post.title' | translate }}</mat-label><input matInput [(ngModel)]="m.title" /></mat-form-field>
      <mat-form-field appearance="outline" class="full"><mat-label>{{ 'post.content' | translate }}</mat-label><textarea matInput rows="8" [(ngModel)]="m.content"></textarea></mat-form-field>
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
  constructor() {
    if (this.data.postId) this.api.get<Post>(`/api/posts/${this.data.postId}`).subscribe(p => { this.m = { title: p.title ?? '', content: htmlToText(p.content), day: p.day, visibility: p.visibility }; this.photos.set(p.media); });
  }
  attach(m: Media[]): void { this.photos.update(p => [...p, ...m]); }
  detach(p: Media): void { this.photos.update(ps => ps.filter(x => x.id !== p.id)); }
  save(): void {
    const body = { ...this.m, content: textToHtml(this.m.content), mediaIds: this.photos().map(p => p.id) };
    const req = this.data.postId ? this.api.put(`/api/posts/${this.data.postId}`, body) : this.api.post(`/api/trips/${this.data.trip.id}/posts`, body);
    req.subscribe({ next: () => this.ref.close(true), error: e => this.error.set(errorMessage(e)) });
  }
  remove(): void { this.api.delete(`/api/posts/${this.data.postId}`).subscribe(() => this.ref.close(true)); }
}

function clamp(day: string, t: Trip): string { return day < t.startDate ? t.startDate : day > t.endDate ? t.endDate : day; }
function textToHtml(text: string): string {
  const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  return text.split(/\n{2,}/).map(p => `<p>${esc(p.trim()).replace(/\n/g, '<br>')}</p>`).join('');
}
function htmlToText(html: string): string {
  const div = document.createElement('div'); div.innerHTML = html.replace(/<br\s*\/?>/gi, '\n').replace(/<\/p>/gi, '\n\n');
  return div.textContent?.trim() ?? '';
}
