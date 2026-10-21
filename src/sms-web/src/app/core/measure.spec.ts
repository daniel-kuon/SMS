import { formatBearing, initialBearing, measureLegs } from './measure';

describe('measure', () => {
  it('bearings of the cardinal directions', () => {
    expect(initialBearing([10, 54], [10, 55])).toBeCloseTo(0, 6);
    expect(initialBearing([10, 54], [11, 54])).toBeCloseTo(89.6, 1);   // great circle: a little north of east
    expect(initialBearing([10, 55], [10, 54])).toBeCloseTo(180, 6);
    expect(initialBearing([11, 54], [10, 54])).toBeCloseTo(270.4, 1);
  });
  it('one minute of latitude is one nautical mile', () => {
    const r = measureLegs([[10, 54], [10, 54 + 1 / 60], [10, 54 + 3 / 60]]);
    expect(r.legs.length).toBe(2);
    expect(r.legs[0].nm).toBeCloseTo(1, 2);
    expect(r.legs[1].nm).toBeCloseTo(2, 2);
    expect(r.totalNm).toBeCloseTo(3, 2);
  });
  it('no legs for fewer than two points', () => {
    expect(measureLegs([[10, 54]])).toEqual({ legs: [], totalNm: 0 });
    expect(measureLegs([])).toEqual({ legs: [], totalNm: 0 });
  });
  it('formats three digits, 360 as 000', () => {
    expect(formatBearing(5.2)).toBe('005°');
    expect(formatBearing(359.6)).toBe('000°');
    expect(formatBearing(123.4)).toBe('123°');
  });
});
