import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCardModule } from '@angular/material/card';
import { MatMenuModule } from '@angular/material/menu';
import { MatBadgeModule } from '@angular/material/badge';
import { MatListModule } from '@angular/material/list';
import { TranslatePipe } from '@ngx-translate/core';
import { Api, AuthService } from '../../core/api';
import { TripSummary } from '../../core/models';
import { fmtDate, fmtDateTime } from '../../shared/shared';

export type SocialTarget = 'posts' | 'media' | 'logbook' | 'trips' | 'reviews';
interface CommentDto { id: string; authorId: string; author: string; avatarUrl: string | null; text: string; createdAt: string; canDelete: boolean; }
interface SocialInfo { comments: CommentDto[]; likes: number; liked: boolean; }

/** Likes + comments for any visible item (spec 07 E4). */
@Component({
  selector: 'app-social',
  imports: [FormsModule, RouterLink, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, TranslatePipe],
  template: `
    @if (info(); as i) {
      <div class="row">
        <button mat-button (click)="toggleLike(i)" [disabled]="!auth.me()"><mat-icon>{{ i.liked ? 'favorite' : 'favorite_border' }}</mat-icon> {{ i.likes }}</button>
        <span class="muted">{{ i.comments.length }} {{ 'social.comments' | translate }}</span>
        @if (auth.me()) { <span class="grow"></span><button mat-button (click)="report()"><mat-icon>flag</mat-icon> {{ 'social.report' | translate }}</button> }
      </div>
      @for (c of i.comments; track c.id) {
        <div class="comment"><a [routerLink]="['/u', c.authorId]"><b>{{ c.author }}</b></a> <span class="muted">{{ fmt(c.createdAt) }}</span>
          @if (c.canDelete) { <button mat-icon-button (click)="remove(c)"><mat-icon>delete</mat-icon></button> }
          <div>{{ c.text }}</div></div>
      }
      @if (auth.me()) {
        <form class="row" (ngSubmit)="send()">
          <mat-form-field appearance="outline" class="grow" subscriptSizing="dynamic"><mat-label>{{ 'social.write' | translate }}</mat-label><input matInput name="text" [(ngModel)]="text" maxlength="4000" /></mat-form-field>
          <button mat-flat-button type="submit" [disabled]="!text.trim()">{{ 'social.send' | translate }}</button>
        </form>
      }
    }`,
  styles: `.comment { padding: 6px 0; border-top: 1px solid #eee; }`,
})
export class SocialBox {
  private api = inject(Api); readonly auth = inject(AuthService);
  target = input.required<SocialTarget>(); id = input.required<string>();
  info = signal<SocialInfo | null>(null); text = ''; fmt = fmtDateTime;
  constructor() { effect(() => { this.target(); this.id(); this.load(); }); }
  load(): void { this.api.get<SocialInfo>(`/api/${this.target()}/${this.id()}/social`).subscribe({ next: i => this.info.set(i), error: () => this.info.set(null) }); }
  toggleLike(i: SocialInfo): void { (i.liked ? this.api.delete(`/api/${this.target()}/${this.id()}/reactions`) : this.api.put(`/api/${this.target()}/${this.id()}/reactions`, {})).subscribe(() => this.load()); }
  send(): void { this.api.post(`/api/${this.target()}/${this.id()}/comments`, { text: this.text }).subscribe(() => { this.text = ''; this.load(); }); }
  remove(c: CommentDto): void { this.api.delete(`/api/comments/${c.id}`).subscribe(() => this.load()); }
  report(): void {
    const reason = prompt('Grund der Meldung:');
    if (reason) this.api.post('/api/reports', { targetType: { posts: 'Post', media: 'Media', logbook: 'LogbookEntry', trips: 'Trip', reviews: 'HarbourReview' }[this.target()], targetId: this.id(), reason }).subscribe(() => alert('Danke, die Meldung wurde übermittelt.'));
  }
}

/** Follow / unfollow button for users and ships. */
@Component({
  selector: 'app-follow',
  imports: [MatButtonModule, MatIconModule, TranslatePipe],
  template: `@if (auth.me() && auth.me()!.id !== id()) { <button mat-stroked-button (click)="toggle()"><mat-icon>{{ following() ? 'notifications_active' : 'notifications' }}</mat-icon> {{ (following() ? 'social.unfollow' : 'social.follow') | translate }}</button> }`,
})
export class FollowButton {
  private api = inject(Api); readonly auth = inject(AuthService);
  targetType = input.required<'User' | 'Ship'>(); id = input.required<string>();
  following = signal(false);
  constructor() { effect(() => { this.id(); if (this.auth.me()) this.api.get<{ targetType: string; targetId: string }[]>('/api/me/follows').subscribe(f => this.following.set(f.some(x => x.targetType === this.targetType() && x.targetId === this.id()))); }); }
  toggle(): void {
    (this.following() ? this.api.delete(`/api/follows/${this.targetType()}/${this.id()}`) : this.api.post('/api/follows', { targetType: this.targetType(), targetId: this.id() }))
      .subscribe(() => this.following.update(v => !v));
  }
}

interface FeedItem { kind: string; id: string; tripId: string; tripName: string; shipName: string; authorId: string; author: string; title: string | null; excerpt: string | null; thumbUrl: string | null; at: string; link: string; }

@Component({
  imports: [RouterLink, MatCardModule, MatListModule, TranslatePipe],
  template: `
    <div class="page page-narrow">
      <h1>{{ (mine() ? 'social.feed' : 'social.explore') | translate }}</h1>
      @if (!mine() && trips().length) { <h3>{{ 'nav.trips' | translate }}</h3><mat-list>@for (t of trips(); track t.id) { <a mat-list-item [routerLink]="['/trips', t.id]"><span matListItemTitle>{{ t.name }} · {{ t.shipName }}</span><span matListItemLine>{{ fmtD(t.startDate) }} – {{ fmtD(t.endDate) }}</span></a> }</mat-list> }
      @if (items().length === 0) { <p class="muted">{{ 'social.empty' | translate }}</p> }
      @for (i of items(); track i.id) {
        <mat-card appearance="outlined" class="item"><mat-card-content>
          <div class="row"><a [routerLink]="i.link"><b>{{ i.title || i.author }}</b></a><span class="chip">{{ i.kind }}</span><span class="grow"></span><span class="muted">{{ fmt(i.at) }}</span></div>
          <div class="muted"><a [routerLink]="['/u', i.authorId]">{{ i.author }}</a> · <a [routerLink]="['/trips', i.tripId]">{{ i.tripName }}</a> · {{ i.shipName }}</div>
          <div class="row">@if (i.thumbUrl) { <img [src]="i.thumbUrl" class="thumb" /> }<p>{{ i.excerpt }}</p></div>
        </mat-card-content></mat-card>
      }
    </div>`,
  styles: `.item { margin: 10px 0; } .thumb { width: 96px; height: 96px; object-fit: cover; border-radius: 6px; }`,
})
export class FeedPage {
  private api = inject(Api); private router = inject(Router);
  mine = signal(false); items = signal<FeedItem[]>([]); trips = signal<TripSummary[]>([]); fmt = fmtDateTime; fmtD = fmtDate;
  constructor() {
    this.mine.set(this.router.url.startsWith('/feed'));
    if (this.mine()) this.api.get<FeedItem[]>('/api/feed').subscribe(i => this.items.set(i));
    else this.api.get<{ items: FeedItem[]; trips: TripSummary[] }>('/api/explore').subscribe(r => { this.items.set(r.items); this.trips.set(r.trips); });
  }
}

interface NotificationDto { id: string; type: string; title: string; link: string | null; createdAt: string; readAt: string | null; }

/** Toolbar bell with unread count; polls every minute. */
@Component({
  selector: 'app-notifications',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatMenuModule, MatBadgeModule, TranslatePipe],
  template: `
    <button mat-icon-button [matMenuTriggerFor]="menu" (menuOpened)="load()"><mat-icon [matBadge]="unread() || null" matBadgeColor="warn" matBadgeSize="small">notifications</mat-icon></button>
    <mat-menu #menu="matMenu" class="notif-menu">
      @if (items().length === 0) { <div class="muted" style="padding: 8px 16px">{{ 'social.noNotifications' | translate }}</div> }
      @for (n of items(); track n.id) {
        <a mat-menu-item [routerLink]="n.link ?? '/'" (click)="read(n)" [class.unread]="!n.readAt"><span>{{ n.title }}</span><br><small class="muted">{{ fmt(n.createdAt) }}</small></a>
      }
      @if (unread()) { <button mat-menu-item (click)="readAll()"><mat-icon>done_all</mat-icon> {{ 'social.readAll' | translate }}</button> }
    </mat-menu>`,
  styles: `.unread { font-weight: 600; }`,
})
export class NotificationBell {
  private api = inject(Api); private auth = inject(AuthService);
  items = signal<NotificationDto[]>([]); unread = signal(0); fmt = fmtDateTime;
  private timer = setInterval(() => this.load(), 60000);
  constructor() { effect(() => { if (this.auth.me()) this.load(); }); }
  ngOnDestroy(): void { clearInterval(this.timer); }
  load(): void { if (!this.auth.me()) return; this.api.get<{ unread: number; items: NotificationDto[] }>('/api/notifications').subscribe(r => { this.items.set(r.items); this.unread.set(r.unread); }); }
  read(n: NotificationDto): void { if (!n.readAt) this.api.post(`/api/notifications/${n.id}/read`, {}).subscribe(() => this.load()); }
  readAll(): void { this.api.post('/api/notifications/read-all', {}).subscribe(() => this.load()); }
}
