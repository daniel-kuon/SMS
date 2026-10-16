import { Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { Api } from '../../core/api';
import { Day, LogbookEntry } from '../../core/models';
import { fmtDate, fmtDateTime, Gallery } from '../../shared/shared';
import { SocialBox } from '../social/social';

/** Logbook entry detail: all fields plus the posts/photos of the day(s) it covers (spec 07 E2). */
@Component({
  imports: [RouterLink, MatCardModule, MatButtonModule, TranslatePipe, Gallery, SocialBox],
  template: `
    @if (entry(); as e) {
    <div class="page page-narrow">
      <h1>{{ e.startHarbour }} → {{ e.endHarbour }}</h1>
      <div class="muted">{{ fmtTime(e.departedAt) }} – {{ fmtTime(e.arrivedAt) }} · {{ e.duration }} h @if (e.tripId) { · <a [routerLink]="['/trips', e.tripId, 'logbook']">{{ 'trip.logbook' | translate }}</a> } · <a [routerLink]="['/ships', e.shipId, 'logbook']">{{ 'ship.logbook' | translate }}</a></div>
      <mat-card appearance="outlined"><mat-card-content>
        <dl class="dl">
          <dt>{{ 'logbook.crew' | translate }}</dt><dd>{{ e.crewList }}</dd>
          <dt>{{ 'logbook.distance' | translate }}</dt><dd>{{ e.sailedDistanceNm }} sm (Log {{ e.logStart }} – {{ e.logEnd }})</dd>
          <dt>{{ 'ship.engineHours' | translate }}</dt><dd>{{ e.engineHoursStart }} – {{ e.engineHoursEnd }}</dd>
          <dt>{{ 'logbook.windSpeed' | translate }}</dt><dd>{{ e.windDirection }} {{ e.windSpeed }}</dd>
          <dt>{{ 'logbook.weather' | translate }} / {{ 'logbook.seaState' | translate }}</dt><dd>{{ e.weather }} {{ e.seaState }}</dd>
          <dt>{{ 'logbook.sails' | translate }}</dt><dd>{{ e.sailsNotes }}</dd>
          <dt>{{ 'common.visibility' | translate }}</dt><dd><span class="chip" [class]="'chip ' + e.visibility.toLowerCase()">{{ 'visibility.' + e.visibility | translate }}</span></dd>
        </dl>
        @if (e.notes) { <div class="rich" [innerHTML]="e.notes"></div> }
      </mat-card-content></mat-card>
      <app-social target="logbook" [id]="e.id" />
      @for (d of days(); track d.date) {
        <h3>{{ fmt(d.date, { weekday: 'long', day: 'numeric', month: 'long' }) }}</h3>
        @for (p of d.posts; track p.id) { <mat-card appearance="outlined"><mat-card-content><b><a [routerLink]="['/posts', p.id]">{{ p.title || p.author }}</a></b> <span class="muted">{{ p.author }}</span><div class="rich" [innerHTML]="p.content"></div>@if (p.media.length) { <app-gallery [photos]="p.media" /> }</mat-card-content></mat-card> }
        @if (d.photos.length) { <app-gallery [photos]="d.photos" /> }
      }
    </div>}`,
  styles: `.dl { display: grid; grid-template-columns: auto 1fr; gap: 4px 12px; } dt { color: #666; } dd { margin: 0; }`,
})
export class EntryDetailPage {
  private api = inject(Api); id = input.required<string>();
  entry = signal<LogbookEntry | null>(null); days = signal<Day[]>([]); fmt = fmtDate; fmtTime = fmtDateTime;
  ngOnInit(): void { this.api.get<{ entry: LogbookEntry; days: Day[] }>(`/api/logbook/${this.id()}`).subscribe(r => { this.entry.set(r.entry); this.days.set(r.days); }); }
}
