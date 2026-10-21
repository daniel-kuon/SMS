import { haversineM } from './track-playback';

/** Initial great-circle bearing from a to b ([lng, lat]) in true degrees, 0 ≤ b < 360. */
export function initialBearing(a: [number, number], b: [number, number]): number {
  const rad = Math.PI / 180, φ1 = a[1] * rad, φ2 = b[1] * rad, Δλ = (b[0] - a[0]) * rad;
  const y = Math.sin(Δλ) * Math.cos(φ2), x = Math.cos(φ1) * Math.sin(φ2) - Math.sin(φ1) * Math.cos(φ2) * Math.cos(Δλ);
  return (Math.atan2(y, x) / rad + 360) % 360;
}

/** The legs of a measured line ([lng, lat] points): length in nm (haversine) and initial bearing each, and the total. */
export function measureLegs(points: [number, number][]): { legs: { nm: number; bearing: number }[]; totalNm: number } {
  const legs = points.slice(1).map((b, i) => ({ nm: haversineM(points[i][1], points[i][0], b[1], b[0]) / 1852, bearing: initialBearing(points[i], b) }));
  return { legs, totalNm: legs.reduce((s, l) => s + l.nm, 0) };
}

/** "045°": whole degrees, three digits, 359.6 rounds to 000. */
export const formatBearing = (deg: number) => `${String(Math.round(deg) % 360).padStart(3, '0')}°`;
