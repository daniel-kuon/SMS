import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Api, AuthService } from './api';
import { Paged, Ship } from './models';

/** The ship the map works for: its draft hides harmless hazards and its draft/air draft constrain routes. Remembered per browser. */
export interface ShipContext { id: string | null; name: string; draftM: number | null; airDraftM: number | null; }
const KEY = 'sms.shipContext';

@Injectable({ providedIn: 'root' })
export class ShipContextService {
  private api = inject(Api); private auth = inject(AuthService);
  readonly ship = signal<ShipContext | null>(load());
  readonly myShips = signal<Ship[]>([]);
  readonly draftM = computed(() => this.ship()?.draftM ?? null);
  readonly airDraftM = computed(() => this.ship()?.airDraftM ?? null);

  constructor() {
    effect(() => { try { const s = this.ship(); if (s) localStorage.setItem(KEY, JSON.stringify(s)); else localStorage.removeItem(KEY); } catch { /* private mode */ } });
    effect(() => { if (this.auth.me()) this.api.get<Paged<Ship>>('/api/ships', { mine: true, pageSize: 100 }).subscribe({ next: p => this.myShips.set(p.items), error: () => this.myShips.set([]) }); else this.myShips.set([]); });
  }

  select(ship: Ship | null): void { this.ship.set(ship ? { id: ship.id, name: ship.name, draftM: ship.draftM, airDraftM: ship.airDraftM } : null); }
  /** Manual values for guests or ships without data. */
  manual(draftM: number | null, airDraftM: number | null): void { this.ship.set(draftM === null && airDraftM === null ? null : { id: null, name: '', draftM, airDraftM }); }
  /** Query parameters the routing API understands. */
  routeParams(): Record<string, number> { const p: Record<string, number> = {}; const s = this.ship(); if (s?.draftM != null) p['draft'] = s.draftM; if (s?.airDraftM != null) p['airDraft'] = s.airDraftM; return p; }
}

function load(): ShipContext | null {
  try { const raw = localStorage.getItem(KEY); return raw ? JSON.parse(raw) as ShipContext : null; } catch { return null; }
}
