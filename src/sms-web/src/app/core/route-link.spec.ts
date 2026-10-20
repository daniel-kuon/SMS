import { decodePolyline, encodePolyline, formatRef, parseRef, parseRouteLink, routeLinkParams, simplifyLine, withParams } from './route-link';

describe('route link', () => {
  const ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_';
  const google: [number, number][] = [[-120.2, 38.5], [-120.95, 40.7], [-126.453, 43.252]];

  it('is Google\'s polyline algorithm with a URL-safe alphabet', () => {
    const classic = '_p~iF~ps|U_ulLnnqC_mqNvxq`@'.split('').map(c => ALPHABET[c.charCodeAt(0) - 63]).join('');
    expect(encodePolyline(google)).toBe(classic);
    expect(classic).toMatch(/^[A-Za-z0-9_-]+$/);
  });
  it('round-trips, also negative coordinates and the poles/antimeridian', () => {
    const pts: [number, number][] = [[10.22427, 54.40205], [-9.99999, -0.5], [180, 90], [-180, -90], [0, 0]];
    expect(decodePolyline(encodePolyline(pts))).toEqual(pts);
    expect(decodePolyline(encodePolyline(google))).toEqual(google);
  });
  it('handles empty and malformed input', () => {
    expect(encodePolyline([])).toBe('');
    expect(decodePolyline('')).toEqual([]);
    expect(decodePolyline('_')).toBeNull();   // a continuation chunk without its end
    expect(decodePolyline('a!b')).toBeNull();
    expect(parseRef('')).toBeNull();
  });
  it('simplifies a long route until the ref fits and appends the distance', () => {
    const line: [number, number][] = Array.from({ length: 3000 }, (_, i) => [+(10 + i * 0.001).toFixed(5), +(54 + Math.sin(i / 50) * 0.05).toFixed(5)]);
    expect(encodePolyline(line).length).toBeGreaterThan(5500);
    const ref = formatRef(line, 20.4);
    expect(ref.length).toBeLessThanOrEqual(5500 + 5);
    const back = parseRef(ref)!;
    expect(back.nm).toBe(20.4);
    expect(back.coordinates[0]).toEqual(line[0]);
    expect(back.coordinates[back.coordinates.length - 1]).toEqual(line[line.length - 1]);
    expect(simplifyLine([[0, 0], [1, 0.000001], [2, 0]], 1e-5)).toEqual([[0, 0], [2, 0]]);
  });
  it('measures a ref without a distance', () => {
    expect(parseRef(encodePolyline([[10, 54], [10, 55]]))!.nm).toBe(60);
  });
  it('writes and reads the route inputs and keeps other parameters', () => {
    const q = withParams('c=54.4,10.5,9&ref=old', routeLinkParams({ lat: 54.4, lng: 10.2 }, { lat: -54.5, lng: -10.6 }, [{ lat: 54.45, lng: 10.3 }, { lat: 54.47, lng: 10.4 }],
      { minDepth: 2.1, airDraft: 14, unknownBridges: 'avoid' }, [[10.2, 54.4], [-10.6, -54.5]], 3.2));
    expect(q).toContain('c=54.4,10.5,9');
    expect(q).toContain('via=54.45000,10.30000;54.47000,10.40000');
    expect(q).not.toContain('warnDepth');
    const link = parseRouteLink(q)!;
    expect(link.from).toEqual({ lat: 54.4, lng: 10.2 });
    expect(link.to).toEqual({ lat: -54.5, lng: -10.6 });
    expect(link.vias.length).toBe(2);
    expect([link.minDepth, link.warnDepth, link.airDraft, link.unknownBridges]).toEqual([2.1, null, 14, 'avoid']);
    expect(link.ref).toEqual({ coordinates: [[10.2, 54.4], [-10.6, -54.5]], nm: 3.2 });
    expect(parseRouteLink('from=54.4,10.2')).toBeNull();
    expect(parseRouteLink('from=54.4,10.2&to=99,10&ref=x')).toBeNull();
  });
});
