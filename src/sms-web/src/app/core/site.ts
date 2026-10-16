import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LogbookEntry, TripSummary } from './models';

export interface ShipPageNode { id: string; shipId: string; parentId: string | null; slug: string; path: string; title: string; content: string; order: number; published: boolean; showInNav: boolean; language: string; updatedAt: string; children: ShipPageNode[]; }
export interface ShipSite {
  mode: 'ship';
  ship: { id: string; name: string; type: string | null; description: string | null; lengthM: number | null; beamM: number | null; draftM: number | null; sailNumber: string | null; homeHarbour: string | null; homeHarbourId: string | null; photoUrl: string | null; canEdit: boolean };
  pages: ShipPageNode[];
  latestLogbook: LogbookEntry[];
  trips: TripSummary[];
}
export type Site = { mode: 'portal'; host: string | null } | ShipSite;

/**
 * Tenant resolution: /api/site decides from the request host whether this origin serves the whole portal or
 * one ship's website (custom domain). In development a ship domain can be simulated with localStorage.tenantHost.
 */
@Injectable({ providedIn: 'root' })
export class SiteService {
  private http = inject(HttpClient);
  readonly site = signal<Site>({ mode: 'portal', host: null });
  get shipSite(): ShipSite | null { const s = this.site(); return s.mode === 'ship' ? s : null; }

  async load(): Promise<Site> {
    try {
      const site = await firstValueFrom(this.http.get<Site>('/api/site'));
      this.site.set(site);
      return site;
    } catch {
      return this.site();
    }
  }
}

/** Dev helper: which host the API should pretend to serve (sent as X-Tenant-Host, honoured in Development only). */
export const tenantPreview = {
  get(): string | null { try { return localStorage.getItem('tenantHost'); } catch { return null; } },
  set(host: string | null): void { try { host ? localStorage.setItem('tenantHost', host) : localStorage.removeItem('tenantHost'); } catch { /* ignore */ } },
};
