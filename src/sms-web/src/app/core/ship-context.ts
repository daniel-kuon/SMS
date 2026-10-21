import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Api, AuthService } from './api';
import { Paged, Ship } from './models';

/**
 * The ship the map works for, remembered per browser: routes avoid water shallower than its minimum depth (a band's middle below it) and warn
 * where a band's floor lies below its warning depth; the air draft closes low bridges; hazards deeper than the larger depth are hidden.
 */
export interface ShipContext { id: string | null; name: string; minDepthM: number | null; warnDepthM: number | null; airDraftM: number | null; }
const KEY = 'sms.shipContext';
/** A ship (or a stored context) with a draft only: minimum depth = draft + this, as the routing API does. */
const DRAFT_MARGIN_M = 0.5;

@Injectable({ providedIn: 'root' })
export class ShipContextService {
  private api = inject(Api); private auth = inject(AuthService);
  readonly ship = signal<ShipContext | null>(load());
  readonly myShips = signal<Ship[]>([]);
  readonly minDepthM = computed(() => this.ship()?.minDepthM ?? null);
  readonly warnDepthM = computed(() => this.ship()?.warnDepthM ?? null);
  readonly airDraftM = computed(() => this.ship()?.airDraftM ?? null);
  /** Hazards deeper than this are hidden: the warning depth, else the minimum depth. */
  readonly hazardDepthM = computed(() => this.warnDepthM() ?? this.minDepthM());

  constructor() {
    effect(() => { try { const s = this.ship(); if (s) localStorage.setItem(KEY, JSON.stringify(s)); else localStorage.removeItem(KEY); } catch { /* private mode */ } });
    effect(() => { if (this.auth.me()) this.api.get<Paged<Ship>>('/api/ships', { mine: true, pageSize: 100 }).subscribe({ next: p => this.myShips.set(p.items), error: () => this.myShips.set([]) }); else this.myShips.set([]); });
  }

  select(ship: Ship | null): void { this.ship.set(ship ? fromShip(ship) : null); }
  /** Manual values for guests or ships without data. */
  manual(minDepthM: number | null, warnDepthM: number | null, airDraftM: number | null): void {
    this.ship.set(minDepthM === null && warnDepthM === null && airDraftM === null ? null : { id: null, name: '', minDepthM, warnDepthM, airDraftM });
  }
  /** Query parameters the routing API understands (bridges without a charted clearance: the API's default, passed with a "bridgeUnknown" warning). */
  routeParams(): Record<string, number | string> { return routeParams(this.ship()); }
}

/** Routing API parameters for a context: minimum and warning depth, air draft ("Durchfahrtshöhe": without it the API assumes a
 *  typical yacht's mast and names every fixed bridge on the route in a warning). */
export function routeParams(s: ShipContext | null): Record<string, number> {
  const p: Record<string, number> = {};
  if (s?.minDepthM != null) p['minDepth'] = s.minDepthM;
  if (s?.warnDepthM != null) p['warnDepth'] = s.warnDepthM;
  if (s?.airDraftM != null) p['airDraft'] = s.airDraftM;
  return p;
}

/** The context for a ship: its own minimum depth, else draft + margin; warnings only with its own warning depth. */
export function fromShip(ship: Pick<Ship, 'id' | 'name' | 'draftM' | 'airDraftM' | 'minDepthM' | 'warnDepthM'>): ShipContext {
  return { id: ship.id, name: ship.name, minDepthM: ship.minDepthM ?? (ship.draftM != null ? round(ship.draftM + DRAFT_MARGIN_M) : null), warnDepthM: ship.warnDepthM ?? null, airDraftM: ship.airDraftM };
}

/** A stored context; one from before the two depth settings ({ draftM }) becomes minimum depth = draft + margin, no warnings. */
export function parseContext(raw: string | null): ShipContext | null {
  if (!raw) return null;
  const s = JSON.parse(raw) as Partial<ShipContext> & { draftM?: number | null };
  if (s.minDepthM === undefined) return { id: s.id ?? null, name: s.name ?? '', minDepthM: s.draftM != null ? round(s.draftM + DRAFT_MARGIN_M) : null, warnDepthM: null, airDraftM: s.airDraftM ?? null };
  return { id: s.id ?? null, name: s.name ?? '', minDepthM: s.minDepthM ?? null, warnDepthM: s.warnDepthM ?? null, airDraftM: s.airDraftM ?? null };
}

const round = (m: number) => Math.round(m * 100) / 100;

function load(): ShipContext | null {
  try { return parseContext(localStorage.getItem(KEY)); } catch { return null; }
}
