import { RouteWarning } from './models';

/** A clicked map point, a route start or a via point. */
export interface LatLng { lat: number; lng: number; }

/** The point-info API's answer: the charted depth band there (null = no depth data) and the bridges within 300 m, nearest first. */
export interface PointInfo { depth: { minM: number; maxM: number } | null; bridges: { kind: string; clearanceM: number | null; distanceM: number }[]; }

/** One leg of GET /api/routing/route. */
export interface RouteLeg { distanceNm: number; straightLineNm: number; geometry: { coordinates: number[][] }; method: string; warnings?: RouteWarning[]; }

/** "54° 24,123' N 010° 13,456' E" (en: decimal point); minutes rounded to 0.001', a 59.9996' carries into the degree. */
export function formatNautical(lat: number, lng: number, lang = 'de'): string {
  const f = (v: number, pos: string, neg: string, degWidth: number) => {
    const t = Math.round(Math.abs(v) * 60000), deg = Math.floor(t / 60000), min = ((t % 60000) / 1000).toFixed(3).padStart(6, '0');
    return `${String(deg).padStart(degWidth, '0')}° ${lang === 'en' ? min : min.replace('.', ',')}' ${v >= 0 ? pos : neg}`;
  };
  return `${f(lat, 'N', 'S', 2)} ${f(lng, 'E', 'W', 3)}`;
}

/** "54.40205, 10.22427": the form map and GPS apps take. */
export const formatDecimal = (lat: number, lng: number) => `${lat.toFixed(5)}, ${lng.toFixed(5)}`;

/** "1,8–3,7 m" for a charted depth band. */
export function formatDepthBand(minM: number, maxM: number, lang = 'de'): string {
  const n = (m: number) => m.toLocaleString(lang === 'en' ? 'en' : 'de', { maximumFractionDigits: 1 });
  return `${n(minM)}–${n(maxM)} m`;
}

/** The leg (index into legs) whose line passes nearest to lng/lat: the leg a drag on the route line started from. */
export function nearestLeg(legs: number[][][], lng: number, lat: number): number {
  const k = Math.cos(lat * Math.PI / 180);   // equirectangular: east-west degrees shrink with latitude
  let best = 0, bestD = Infinity;
  legs.forEach((leg, i) => {
    for (let j = 0; j < leg.length; j++) {
      const [ax, ay] = leg[j], [bx, by] = leg[Math.min(j + 1, leg.length - 1)];
      const dx = (bx - ax) * k, dy = by - ay, px = (lng - ax) * k, py = lat - ay, len = dx * dx + dy * dy;
      const t = len ? Math.max(0, Math.min(1, (px * dx + py * dy) / len)) : 0, ex = px - t * dx, ey = py - t * dy, d = ex * ex + ey * ey;
      if (d < bestD) { bestD = d; best = i; }
    }
  });
  return best;
}

/** Leg i runs from route point i to i + 1 (points = start, vias…, end): a via dragged off it goes between those two, at vias[i]. */
export const insertVia = <T>(vias: T[], leg: number, p: T): T[] => [...vias.slice(0, leg), p, ...vias.slice(leg)];

/** The legs start → via → … → end as one route: lines joined, distances summed, warnings merged (a via on land is no start/end on land). */
export function joinLegs(legs: RouteLeg[]): { coordinates: [number, number][]; distanceNm: number; straight: boolean; warnings: RouteWarning[] } {
  const coordinates: [number, number][] = [];
  for (const l of legs) for (const c of l.geometry.coordinates) {
    const last = coordinates[coordinates.length - 1];
    if (!last || last[0] !== c[0] || last[1] !== c[1]) coordinates.push([c[0], c[1]]);
  }
  const warnings = legs.flatMap((l, i) => (l.warnings ?? []).filter(w => !(w.kind === 'startOnLand' && i > 0) && !(w.kind === 'endOnLand' && i < legs.length - 1)));
  return { coordinates, distanceNm: Math.round(legs.reduce((s, l) => s + l.distanceNm, 0) * 10) / 10, straight: legs.some(l => l.method === 'straight-line'), warnings };
}

/** Nearest point on a polyline in plane coordinates (screen pixels): the segment (index of its first vertex), the point and the distance. */
export function nearestOnPolyline(pts: [number, number][], p: [number, number]): { index: number; point: [number, number]; dist: number } | null {
  let best: { index: number; point: [number, number]; dist: number } | null = null;
  for (let i = 0; i + 1 < pts.length; i++) {
    const [ax, ay] = pts[i], [bx, by] = pts[i + 1], dx = bx - ax, dy = by - ay, len = dx * dx + dy * dy;
    const t = len ? Math.max(0, Math.min(1, ((p[0] - ax) * dx + (p[1] - ay) * dy) / len)) : 0;
    const point: [number, number] = [ax + t * dx, ay + t * dy], dist = Math.hypot(p[0] - point[0], p[1] - point[1]);
    if (!best || dist < best.dist) best = { index: i, point, dist };
  }
  return best;
}
/** The list without element i (a deleted vertex). */
export const removeAt = <T>(list: T[], i: number): T[] => list.filter((_, k) => k !== i);
/** The list with element i replaced (a moved vertex). */
export const replaceAt = <T>(list: T[], i: number, v: T): T[] => list.map((x, k) => k === i ? v : x);
