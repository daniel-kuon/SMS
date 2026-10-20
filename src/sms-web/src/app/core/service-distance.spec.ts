import { formatServiceDistance } from './service-distance';

describe('formatServiceDistance', () => {
  it('on site for 0 m', () => {
    expect(formatServiceDistance(0, 'de', 'vor Ort')).toBe('vor Ort');
    expect(formatServiceDistance(0, 'en', 'on site')).toBe('on site');
  });
  it('rounds to 10 m below 1 km', () => {
    expect(formatServiceDistance(347, 'de', '')).toBe('350 m');
    expect(formatServiceDistance(4, 'de', '')).toBe('10 m');
    expect(formatServiceDistance(994, 'de', '')).toBe('990 m');
  });
  it('km with one decimal from 1 km, by locale', () => {
    expect(formatServiceDistance(1230, 'de', '')).toBe('1,2 km');
    expect(formatServiceDistance(1230, 'en', '')).toBe('1.2 km');
    expect(formatServiceDistance(996, 'en', '')).toBe('1.0 km');
    expect(formatServiceDistance(1500, 'de', '')).toBe('1,5 km');
  });
});

describe('formatServiceDistance on foot', () => {
  it('appends the walking word to a walked distance', () => {
    expect(formatServiceDistance(347, 'de', 'vor Ort', 'zu Fuß')).toBe('350 m zu Fuß');
    expect(formatServiceDistance(1230, 'en', 'on site', 'on foot')).toBe('1.2 km on foot');
  });
});
