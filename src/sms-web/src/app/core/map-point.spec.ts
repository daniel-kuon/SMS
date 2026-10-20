import { formatDecimal, formatDepthBand, formatNautical, insertVia, joinLegs, nearestLeg, nearestOnPolyline, removeAt, replaceAt, RouteLeg } from './map-point';

describe('map point', () => {
  it('formats positions nautically with a decimal comma and decimally', () => {
    expect(formatNautical(54.40205, 10.22427)).toBe("54° 24,123' N 010° 13,456' E");
    expect(formatNautical(54.40205, 10.22427, 'en')).toBe("54° 24.123' N 010° 13.456' E");
    expect(formatNautical(-0.5, -9.99999999)).toBe("00° 30,000' S 010° 00,000' W");   // 59.9999' rounds into the degree
    expect(formatDecimal(54.402051, 10.2)).toBe('54.40205, 10.20000');
  });
  it('formats a depth band', () => {
    expect(formatDepthBand(1.83, 3.66)).toBe('1,8–3,7 m');
    expect(formatDepthBand(1.83, 3.66, 'en')).toBe('1.8–3.7 m');
  });
  it('puts a via dragged off a leg between that leg\'s two route points', () => {
    // start (0,0) → A (1,0) → B (2,0) → end (3,0): legs 0..2
    const legs = [[[0, 0], [1, 0]], [[1, 0], [2, 0]], [[2, 0], [3, 0]]];
    expect(nearestLeg(legs, 1.5, 0.01)).toBe(1);
    expect(nearestLeg(legs, 0.2, -0.01)).toBe(0);
    expect(nearestLeg(legs, 3.4, 0)).toBe(2);
    expect(insertVia(['A', 'B'], nearestLeg(legs, 1.5, 0.01), 'X')).toEqual(['A', 'X', 'B']);
    expect(insertVia(['A', 'B'], 0, 'X')).toEqual(['X', 'A', 'B']);
    expect(insertVia(['A', 'B'], 2, 'X')).toEqual(['A', 'B', 'X']);
    expect(insertVia([], 0, 'X')).toEqual(['X']);
  });
  it('joins legs: one line, summed distance, merged warnings without a via\'s start/end on land', () => {
    const w = (kind: string) => ({ kind, lat: 0, lng: 0, note: kind });
    const leg = (coords: number[][], nm: number, warnings: ReturnType<typeof w>[], method = 'graph'): RouteLeg => ({ distanceNm: nm, straightLineNm: nm, geometry: { coordinates: coords }, method, warnings });
    const r = joinLegs([leg([[0, 0], [1, 0]], 1.04, [w('startOnLand'), w('endOnLand')]), leg([[1, 0], [2, 0]], 2.03, [w('startOnLand'), w('bridge'), w('endOnLand')])]);
    expect(r.coordinates).toEqual([[0, 0], [1, 0], [2, 0]]);
    expect(r.distanceNm).toBe(3.1);
    expect(r.warnings.map(x => x.kind)).toEqual(['startOnLand', 'bridge', 'endOnLand']);
    expect(r.straight).toBe(false);
    expect(joinLegs([leg([[0, 0], [1, 0]], 1, [], 'straight-line')]).straight).toBe(true);
  });

  it('finds the nearest point on a polyline and edits vertices', () => {
    const line: [number, number][] = [[0, 0], [10, 0], [10, 10]];
    expect(nearestOnPolyline(line, [4, 3])).toEqual({ index: 0, point: [4, 0], dist: 3 });
    expect(nearestOnPolyline(line, [12, 6])).toEqual({ index: 1, point: [10, 6], dist: 2 });
    expect(nearestOnPolyline(line, [-5, 0])!.point).toEqual([0, 0]);   // clamped to the end
    expect(nearestOnPolyline([[1, 1]], [0, 0])).toBeNull();
    // a vertex inserted on segment 1 goes between vertex 1 and 2 (insertVia at index + 1)
    expect(insertVia(line, 1 + 1, [10, 6] as [number, number])).toEqual([[0, 0], [10, 0], [10, 6], [10, 10]]);
    expect(removeAt(line, 1)).toEqual([[0, 0], [10, 10]]);
    expect(replaceAt(line, 2, [9, 9] as [number, number])).toEqual([[0, 0], [10, 0], [9, 9]]);
    expect(line).toEqual([[0, 0], [10, 0], [10, 10]]);   // untouched
  });
});
