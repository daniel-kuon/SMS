import { formatPosition, seamarkTypeLabel } from './seamark-icons';
import { hazardFilter } from './chart-symbols';

describe('sea-mark helpers', () => {
  it('formats positions in degrees and decimal minutes', () => {
    expect(formatPosition(54.372956, 11.005651)).toBe("54°22.377' N 011°00.339' E");
    expect(formatPosition(-33.85, -151.2)).toBe("33°51.000' S 151°12.000' W");
  });
  it('labels OSM sea-mark types', () => {
    expect(seamarkTypeLabel('buoy_lateral', 'de')).toBe('Seitentonne');
    expect(seamarkTypeLabel('buoy_lateral', 'en')).toBe('Lateral buoy');
    expect(seamarkTypeLabel('something_new', 'de')).toBe('something new');
  });
  it('hides only charted hazards deeper than draft plus margin', () => {
    expect(hazardFilter(null)).toEqual(['==', ['geometry-type'], 'Point']);
    const f = hazardFilter(1.8) as unknown[];
    expect(f[0]).toBe('all');
    expect(JSON.stringify(f)).toContain('2.3'); // 1.8 m draft + 0.5 m margin
  });
});
