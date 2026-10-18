/** Pure helpers for the trip map (docs/14): plan steps with alternatives, the leg list sent to PUT /legs, route vertex editing. */

export type LineGeometry = { type: 'LineString'; coordinates: number[][] };
export interface MapPlannedLeg { id: string; order: number; alternative: number; fromHarbourId: string; toHarbourId: string; plannedDate: string | null; distanceNm: number | null; geometry: LineGeometry | null; }
export interface MapEntry { id: string; startHarbourId: string; endHarbourId: string; departedAt: string; arrivedAt: string; geometry: LineGeometry | null; }
export interface MapHarbour { id: string; name: string; lat: number; lng: number; }
export interface MapPhoto { id: string; thumbUrl: string; lat: number; lng: number; takenAt: string | null; logbookEntryId: string | null; }
/** GET /api/trips/{id}/map */
export interface TripMapData { plannedLegs: MapPlannedLeg[]; logbook: MapEntry[]; harbours: MapHarbour[]; photos: MapPhoto[]; }

/** Colour per alternative index: 0 blue, 1 orange, 2 green, 3 purple (then repeating). */
export const ALT_COLORS = ['#1e88e5', '#fb8c00', '#43a047', '#8e24aa'];
export const altColor = (alt: number) => ALT_COLORS[alt % ALT_COLORS.length];

/** A plan: the origin (planned start harbour or the from-harbour of the first step) and per step its candidate harbours (index = alternative). */
export interface Plan { origin: string | null; steps: string[][]; dates: Record<string, string | null>; }
export interface PlanLegInput { fromHarbourId: string; toHarbourId: string; plannedDate: string | null; order: number; alternative: number; }
type LegLike = { order: number; alternative?: number; fromHarbourId: string; toHarbourId: string; plannedDate: string | null };

/** Groups the stored legs into steps: legs of the same order form one step, its candidates are their distinct destinations by alternative. */
export function parsePlan(legs: LegLike[], plannedStart: string | null): Plan {
  const sorted = [...legs].sort((a, b) => a.order - b.order || (a.alternative ?? 0) - (b.alternative ?? 0));
  const steps: string[][] = []; const dates: Record<string, string | null> = {};
  let lastOrder: number | null = null;
  for (const l of sorted) {
    if (l.order !== lastOrder) { steps.push([]); lastOrder = l.order; }
    const step = steps[steps.length - 1];
    if (!step.includes(l.toHarbourId)) step.push(l.toHarbourId);
    dates[l.toHarbourId] ??= l.plannedDate;
  }
  return { origin: plannedStart ?? sorted[0]?.fromHarbourId ?? null, steps, dates };
}

/**
 * The leg list for PUT /legs. Step k has max(|prev|, |cur|) legs; leg i runs from prev[min(i, |prev|-1)] to cur[min(i, |cur|-1)]
 * with alternative i: one start fans out to the alternatives, alternatives merge into a single next stop, equal counts run in parallel.
 */
export function buildLegs(plan: Plan): PlanLegInput[] {
  if (!plan.origin) return [];
  const out: PlanLegInput[] = [];
  let prev = [plan.origin];
  plan.steps.filter(s => s.length).forEach((cur, order) => {
    for (let i = 0; i < Math.max(prev.length, cur.length); i++) {
      const to = cur[Math.min(i, cur.length - 1)];
      out.push({ fromHarbourId: prev[Math.min(i, prev.length - 1)], toHarbourId: to, plannedDate: plan.dates[to] ?? null, order, alternative: i });
    }
    prev = cur;
  });
  return out;
}

/** Removes candidate `alt` of step `step`; a step without candidates disappears. */
export function removeCandidate(plan: Plan, step: number, alt: number): Plan {
  const steps = plan.steps.map(s => [...s]);
  steps[step].splice(alt, 1);
  return { ...plan, steps: steps.filter(s => s.length) };
}

/** The harbours the next step departs from (the last step's candidates, or the origin). */
export const lastStop = (plan: Plan): string | null => plan.steps.at(-1)?.[0] ?? plan.origin;

// --- route editing (screen coordinates for hit tests, lng/lat for the line)

/** Index of the segment (i → i+1) closest to p; points are planar (e.g. map.project pixels). */
export function nearestSegment(points: [number, number][], p: [number, number]): number {
  let best = 0, bestD = Infinity;
  for (let i = 0; i < points.length - 1; i++) {
    const d = segDist2(points[i], points[i + 1], p);
    if (d < bestD) { bestD = d; best = i; }
  }
  return best;
}
function segDist2(a: [number, number], b: [number, number], p: [number, number]): number {
  const dx = b[0] - a[0], dy = b[1] - a[1], len = dx * dx + dy * dy;
  const t = len ? Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / len)) : 0;
  const x = a[0] + t * dx - p[0], y = a[1] + t * dy - p[1];
  return x * x + y * y;
}
/** New vertex c after segment start `seg`. */
export const insertVertex = (coords: number[][], seg: number, c: number[]): number[][] => [...coords.slice(0, seg + 1), c, ...coords.slice(seg + 1)];
/** Removes vertex i; a line keeps at least its two end points. */
export const removeVertex = (coords: number[][], i: number): number[][] => coords.length <= 2 ? coords : coords.filter((_, j) => j !== i);
export const moveVertex = (coords: number[][], i: number, c: number[]): number[][] => coords.map((x, j) => (j === i ? c : x));

/** Every position of the payload, for fitBounds. */
export function allPoints(d: TripMapData): [number, number][] {
  return [
    ...d.plannedLegs.flatMap(l => l.geometry?.coordinates ?? []), ...d.logbook.flatMap(e => e.geometry?.coordinates ?? []),
    ...d.harbours.map(h => [h.lng, h.lat]), ...d.photos.map(p => [p.lng, p.lat]),
  ].map(c => [c[0], c[1]] as [number, number]);
}
