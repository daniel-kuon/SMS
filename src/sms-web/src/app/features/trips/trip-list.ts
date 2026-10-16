import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { Api } from '../../core/api';
import { CrewStatus, TripSummary } from '../../core/models';
import { fmtDate } from '../../shared/shared';

@Component({
  imports: [RouterLink, MatListModule, MatButtonModule, TranslatePipe],
  template: `
    <div class="page page-narrow">
      <h1>{{ 'trip.myTrips' | translate }}</h1>
      @if (trips().length === 0) { <p class="muted">{{ 'trip.noTrips' | translate }}</p> }
      <mat-list>
        @for (t of trips(); track t.summary.id) {
          <a mat-list-item [routerLink]="['/trips', t.summary.id]">
            <span matListItemTitle>{{ t.summary.name }} <span class="muted">· {{ t.summary.shipName }}</span> @if (t.myStatus === 'Invited') { <span class="chip crew">{{ 'trip.pending' | translate }}</span> }</span>
            <span matListItemLine>{{ fmt(t.summary.startDate) }} – {{ fmt(t.summary.endDate) }} · {{ 'trip.status' + t.summary.status | translate }} · {{ t.summary.crewCount }} Crew</span>
          </a>
        }
      </mat-list>
    </div>`,
})
export class TripListPage {
  private api = inject(Api);
  trips = signal<{ summary: TripSummary; myStatus: CrewStatus }[]>([]);
  fmt = fmtDate;
  constructor() { this.api.get<{ summary: TripSummary; myStatus: CrewStatus }[]>('/api/trips').subscribe(t => this.trips.set(t)); }
}
