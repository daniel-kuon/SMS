import { bearingDeg, haversineM, TrackPath } from './track-playback';

describe('TrackPath', () => {
  // two legs: north 0.1° (≈ 11.1 km), then east
  const legs = [{ coordinates: [[10, 54], [10, 54.1]] }, { coordinates: [[10, 54.1], [10.2, 54.1]] }];
  const path = new TrackPath(legs);

  it('measures the total distance and exposes it in nautical miles', () => {
    expect(path.totalM).toBeCloseTo(haversineM(54, 10, 54.1, 10) + haversineM(54.1, 10, 54.1, 10.2), 3);
    expect(path.totalNm).toBeGreaterThan(10);
    expect(path.empty).toBe(false);
    expect(new TrackPath([]).empty).toBe(true);
    expect(new TrackPath([{ coordinates: [[1, 1]] }]).at(0.5)).toBeNull();
  });

  it('interpolates along the path and reports the current leg', () => {
    const start = path.at(0)!;
    expect([start.lng, start.lat, start.legIndex]).toEqual([10, 54, 0]);
    expect(start.bearingDeg).toBeCloseTo(0, 3); // heading north
    const quarter = path.at(0.25)!;
    expect(quarter.lng).toBe(10);
    expect(quarter.lat).toBeGreaterThan(54); expect(quarter.lat).toBeLessThan(54.1);
    expect(quarter.legIndex).toBe(0);
    const late = path.at(0.9)!;
    expect(late.lat).toBeCloseTo(54.1, 9);
    expect(late.lng).toBeGreaterThan(10); expect(late.legIndex).toBe(1);
    expect(late.bearingDeg).toBeCloseTo(90, 0);
    const end = path.at(1)!;
    expect([end.lng, end.lat]).toEqual([10.2, 54.1]);
    expect(end.distanceNm).toBeCloseTo(path.totalNm, 9);
    expect(path.at(5)!.lng).toBe(10.2); // clamped
  });

  it('is monotonic in distance', () => {
    let last = -1;
    for (let f = 0; f <= 1; f += 0.05) { const d = path.at(f)!.distanceNm; expect(d).toBeGreaterThanOrEqual(last); last = d; }
  });

  it('builds the travelled trail ending at the current position', () => {
    expect(path.trail(0)).toEqual([[10, 54], [10, 54]]);
    const half = path.trail(0.5);
    expect(half[0]).toEqual([10, 54]);
    expect(half.length).toBeGreaterThanOrEqual(3);
    const tip = half[half.length - 1]; const pos = path.at(0.5)!;
    expect(tip).toEqual([pos.lng, pos.lat]);
    expect(path.trail(1).length).toBe(5);
  });
});

describe('bearingDeg', () => {
  it('gives compass bearings', () => {
    expect(bearingDeg(54, 10, 55, 10)).toBeCloseTo(0, 5);
    expect(bearingDeg(54, 10, 54, 11)).toBeCloseTo(90, 0);
    expect(bearingDeg(54, 10, 53, 10)).toBeCloseTo(180, 5);
    expect(bearingDeg(54, 10, 54, 9)).toBeCloseTo(270, 0);
  });
});
