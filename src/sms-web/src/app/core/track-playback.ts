/**
 * Spec 12 §12.3 track playback: the trip's legs (recorded or calculated) concatenated into one path that is
 * walked by distance. No per-point timestamps are needed, so the same code serves GPS tracks and straight legs.
 */
export interface PathPosition { lng: number; lat: number; legIndex: number; bearingDeg: number; distanceNm: number; }

const NM = 1852;

export function haversineM(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const r = 6_371_008.8; const rad = Math.PI / 180;
  const dLat = (lat2 - lat1) * rad; const dLng = (lng2 - lng1) * rad;
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(lat1 * rad) * Math.cos(lat2 * rad) * Math.sin(dLng / 2) ** 2;
  return 2 * r * Math.asin(Math.sqrt(a));
}

export function bearingDeg(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const rad = Math.PI / 180;
  const y = Math.sin((lng2 - lng1) * rad) * Math.cos(lat2 * rad);
  const x = Math.cos(lat1 * rad) * Math.sin(lat2 * rad) - Math.sin(lat1 * rad) * Math.cos(lat2 * rad) * Math.cos((lng2 - lng1) * rad);
  return (Math.atan2(y, x) / rad + 360) % 360;
}

export class TrackPath {
  /** Vertices as [lng, lat, cumulative metres, leg index]. */
  private readonly pts: [number, number, number, number][] = [];
  readonly totalM: number;

  constructor(legs: { coordinates: number[][] }[]) {
    let cum = 0;
    legs.forEach((leg, li) => {
      for (const c of leg.coordinates) {
        const last = this.pts[this.pts.length - 1];
        if (last) cum += haversineM(last[1], last[0], c[1], c[0]);
        this.pts.push([c[0], c[1], cum, li]);
      }
    });
    this.totalM = cum;
  }

  get empty(): boolean { return this.pts.length < 2 || this.totalM === 0; }
  get totalNm(): number { return this.totalM / NM; }

  /** Position after the given fraction (0…1) of the total distance. */
  at(fraction: number): PathPosition | null {
    if (this.empty) return null;
    const target = Math.min(1, Math.max(0, fraction)) * this.totalM;
    let i = 1;
    while (i < this.pts.length - 1 && this.pts[i][2] < target) i++;
    const a = this.pts[i - 1]; const b = this.pts[i];
    const span = b[2] - a[2];
    const t = span > 0 ? (target - a[2]) / span : 0;
    // leg index of the segment being travelled: the vertex we are heading to, except on the very first vertex
    const legIndex = target <= a[2] ? a[3] : b[3];
    return { lng: a[0] + (b[0] - a[0]) * t, lat: a[1] + (b[1] - a[1]) * t, legIndex, bearingDeg: bearingDeg(a[1], a[0], b[1], b[0]), distanceNm: target / NM };
  }

  /** The travelled part of the path up to the fraction, as a GeoJSON LineString coordinate list. */
  trail(fraction: number): number[][] {
    const p = this.at(fraction);
    if (!p) return [];
    const target = p.distanceNm * NM;
    const out: number[][] = [];
    for (const v of this.pts) { if (v[2] > target) break; out.push([v[0], v[1]]); }
    out.push([p.lng, p.lat]);
    return out;
  }
}
