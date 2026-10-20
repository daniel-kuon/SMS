import { LatLng, nearestOnPolyline } from './map-point';
import { haversineM } from './track-playback';

/**
 * A route in the URL, for "this route is wrong" links: the inputs (from, to, via, the routing settings) and `ref`, the route the
 * server returned then, so the page can show it next to the route the current server calculates.
 * ?from=54.4,10.2&to=54.5,10.6&via=54.45,10.3;54.47,10.4&minDepth=2.1&unknownBridges=pass&ref=<polyline>~20.4
 */

/** Google's polyline algorithm (precision 5), but with the base64url alphabet instead of the characters 63–126, so it needs no escaping in a URL. */
const ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_';

/** [lng, lat] pairs (GeoJSON order) as a URL-safe polyline. */
export function encodePolyline(coords: [number, number][]): string {
  let out = '', pLat = 0, pLng = 0;
  const enc = (d: number) => { let v = d < 0 ? ~(d << 1) : d << 1; while (v >= 0x20) { out += ALPHABET[(v & 0x1f) | 0x20]; v >>= 5; } out += ALPHABET[v]; };
  for (const [lng, lat] of coords) { const la = Math.round(lat * 1e5), ln = Math.round(lng * 1e5); enc(la - pLat); enc(ln - pLng); pLat = la; pLng = ln; }
  return out;
}

/** The [lng, lat] pairs of a polyline; null when it is malformed or leaves the globe. */
export function decodePolyline(s: string): [number, number][] | null {
  const out: [number, number][] = [];
  let i = 0, lat = 0, lng = 0;
  const next = (): number | null => {
    let shift = 0, v = 0, b: number;
    do {
      if (i >= s.length || shift > 30) return null;
      b = ALPHABET.indexOf(s[i++]); if (b < 0) return null;
      v |= (b & 0x1f) << shift; shift += 5;
    } while (b >= 0x20);
    return v & 1 ? ~(v >> 1) : v >> 1;
  };
  while (i < s.length) {
    const dLat = next(), dLng = next();
    if (dLat === null || dLng === null) return null;
    lat += dLat; lng += dLng;
    if (Math.abs(lat) > 9e6 || Math.abs(lng) > 18e6) return null;
    out.push([lng / 1e5, lat / 1e5]);
  }
  return out;
}

/** Douglas-Peucker in degrees (tol: the largest allowed deviation). */
export function simplifyLine(pts: [number, number][], tol: number): [number, number][] {
  if (pts.length < 3 || tol <= 0) return pts;
  const keep = new Uint8Array(pts.length); keep[0] = keep[pts.length - 1] = 1;
  const stack: [number, number][] = [[0, pts.length - 1]];
  while (stack.length) {
    const [a, b] = stack.pop()!;
    let idx = -1, max = tol;
    for (let i = a + 1; i < b; i++) { const d = nearestOnPolyline([pts[a], pts[b]], pts[i])!.dist; if (d > max) { max = d; idx = i; } }
    if (idx >= 0) { keep[idx] = 1; stack.push([a, idx], [idx, b]); }
  }
  return pts.filter((_, i) => keep[i]);
}

/** `ref` for a route: the polyline, simplified until it is at most maxLen characters long, and "~<nm>". */
export function formatRef(coords: [number, number][], nm: number, maxLen = 5500): string {
  let poly = encodePolyline(coords);
  for (let tol = 1e-5; poly.length > maxLen; tol *= 2) poly = encodePolyline(simplifyLine(coords, tol));
  return `${poly}~${nm}`;
}

/** The saved route of a `ref`; without a distance its length along the line. */
export function parseRef(ref: string): { coordinates: [number, number][]; nm: number } | null {
  const [poly, nmText] = ref.split('~'), coordinates = decodePolyline(poly ?? '');
  if (!coordinates || coordinates.length < 2) return null;
  const nm = Number(nmText);
  if (nmText && Number.isFinite(nm)) return { coordinates, nm };
  const m = coordinates.slice(1).reduce((s, c, i) => s + haversineM(coordinates[i][1], coordinates[i][0], c[1], c[0]), 0);
  return { coordinates, nm: Math.round(m / 185.2) / 10 };
}

export interface RouteLink {
  from: LatLng; to: LatLng; vias: LatLng[];
  minDepth: number | null; warnDepth: number | null; airDraft: number | null; unknownBridges: 'pass' | 'avoid' | null;
  ref: { coordinates: [number, number][]; nm: number } | null;
}

const latLng = (s: string | null): LatLng | null => {
  const [lat, lng] = (s ?? '').split(',').map(v => (v.trim() === '' ? NaN : Number(v)));
  return Number.isFinite(lat) && Number.isFinite(lng) && Math.abs(lat) <= 90 && Math.abs(lng) <= 180 ? { lat, lng } : null;
};
const num = (s: string | null) => { const n = s == null || s.trim() === '' ? NaN : Number(s); return Number.isFinite(n) && n >= 0 ? n : null; };
const fmt = (p: LatLng) => `${p.lat.toFixed(5)},${p.lng.toFixed(5)}`;

/** The route of a query string ("?from=…&to=…"), or null without a valid from and to. Invalid vias, settings or ref are dropped. */
export function parseRouteLink(query: string): RouteLink | null {
  const q = new URLSearchParams(query), from = latLng(q.get('from')), to = latLng(q.get('to'));
  if (!from || !to) return null;
  const b = q.get('unknownBridges'), ref = q.get('ref');
  return {
    from, to, vias: (q.get('via') ?? '').split(';').map(latLng).filter((p): p is LatLng => !!p),
    minDepth: num(q.get('minDepth')), warnDepth: num(q.get('warnDepth')), airDraft: num(q.get('airDraft')),
    unknownBridges: b === 'pass' || b === 'avoid' ? b : null,
    ref: ref ? parseRef(ref) : null,
  };
}

/** The query parameters of a route link (null: remove the parameter); settings: the routing API parameters (ShipContextService.routeParams()). */
export function routeLinkParams(from: LatLng, to: LatLng, vias: LatLng[], settings: Record<string, number | string>, coords: [number, number][], nm: number): Record<string, string | null> {
  const s = (k: string) => settings[k] == null ? null : String(settings[k]);
  return {
    from: fmt(from), to: fmt(to), via: vias.length ? vias.map(fmt).join(';') : null,
    minDepth: s('minDepth'), warnDepth: s('warnDepth'), airDraft: s('airDraft'), unknownBridges: s('unknownBridges'),
    ref: formatRef(coords, nm),
  };
}

/** A query string with these parameters set or (null) removed, the others kept. */
export function withParams(query: string, set: Record<string, string | null>): string {
  const p = new URLSearchParams(query);
  for (const [k, v] of Object.entries(set)) if (v === null) p.delete(k); else p.set(k, v);
  return readableQuery(p);
}

/** URLSearchParams.toString() without escaping , ; ~ (all fine in a query): links stay readable. */
export const readableQuery = (p: URLSearchParams) => p.toString().replace(/%(2C|3B|7E)/g, m => decodeURIComponent(m));
