import { formatOpeningHours, isOpenNow, openNowText } from './opening-hours';

describe('opening-hours', () => {
  it('formats the month/weekday/PH/multi-span example', () => {
    const raw = 'May-Sep PH,Mo-Su 12:00-14:30,17:00-22:00';
    expect(formatOpeningHours(raw, 'de')).toEqual(['Mai–Sep: täglich 12:00–14:30, 17:00–22:00, auch an Feiertagen']);
  });
  it('mentions public holidays when weekdays are limited', () => {
    expect(formatOpeningHours('Mo-Fr,PH 10:00-12:00', 'de')).toEqual(['Mo–Fr 10:00–12:00, auch an Feiertagen']);
    expect(formatOpeningHours('Mo-Fr,PH 10:00-12:00', 'en')).toEqual(['Mon–Fri 10:00–12:00, also on public holidays']);
    expect(formatOpeningHours('PH off', 'de')).toEqual(['Feiertage geschlossen']);
  });
  it('formats one line per rule', () => {
    expect(formatOpeningHours('Mo-Fr 08:00-18:00; Sa 08:00-13:00', 'de')).toEqual(['Mo–Fr 08:00–18:00', 'Sa 08:00–13:00']);
    expect(formatOpeningHours('Mo-Sa 09:00-19:00; Su off', 'de')).toEqual(['Mo–Sa 09:00–19:00', 'So geschlossen']);
    expect(formatOpeningHours('Mo,We,Fr 9:00-17:00', 'en')).toEqual(['Mon, Wed, Fri 09:00–17:00']);
    expect(formatOpeningHours('24/7', 'de')).toEqual(['Rund um die Uhr']);
    expect(formatOpeningHours('Mo-Su sunrise-sunset', 'de')).toEqual(['täglich Sonnenaufgang–Sonnenuntergang']);
    expect(formatOpeningHours('Dec-Feb off', 'en')).toEqual(['Dec–Feb: daily closed']);
  });
  it('returns null for unparseable values', () => {
    expect(formatOpeningHours('by appointment', 'de')).toBeNull();
    expect(formatOpeningHours('Mo-Fr 08:00-12:00, Sa 09:00-10:00', 'de')).toBeNull();
    expect(isOpenNow('by appointment')).toBeNull();
  });
  it('computes open/closed now from local time', () => {
    const at = (d: string, h: number, m = 0) => new Date(`${d}T${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:00`);
    const raw = 'May-Sep PH,Mo-Su 12:00-14:30,17:00-22:00';
    expect(isOpenNow(raw, at('2026-07-15', 13))).toBe(true);
    expect(isOpenNow(raw, at('2026-07-15', 15))).toBe(false);
    expect(isOpenNow(raw, at('2026-11-15', 13))).toBe(false);
    const shop = 'Mo-Sa 09:00-19:00; Su off';
    expect(isOpenNow(shop, at('2026-10-17', 10))).toBe(true);   // Saturday
    expect(isOpenNow(shop, at('2026-10-18', 10))).toBe(false);  // Sunday
    expect(isOpenNow('24/7', at('2026-10-18', 3))).toBe(true);
    expect(isOpenNow('Fr 20:00-02:00', at('2026-10-17', 1))).toBe(true);   // Saturday 01:00, from Friday
    expect(isOpenNow('Mo-Su sunrise-sunset', at('2026-10-17', 10))).toBeNull();
    expect(openNowText(true, 'de')).toBe('jetzt geöffnet');
  });
});
