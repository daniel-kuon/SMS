import { fmtDate, fmtDateTime, fmtMoney, textNode, VISIBILITIES } from './shared';

describe('formatting helpers', () => {
  afterEach(() => localStorage.clear());

  it('formats dates in the stored language', () => {
    expect(fmtDate(null)).toBe('');
    expect(fmtDate('2026-10-12T08:00:00Z')).toContain('2026');
    localStorage.setItem('lang', 'en');
    expect(fmtDate('2026-10-12T08:00:00Z')).toMatch(/Oct/);
    expect(fmtDateTime('2026-10-12T08:00:00Z')).toMatch(/\d{2}/);
  });

  it('formats money with the trip currency', () => {
    expect(fmtMoney(null, 'EUR')).toBe('');
    expect(fmtMoney(12.5, 'EUR').replace(/ /g, ' ')).toMatch(/12,50 €/);
    localStorage.setItem('lang', 'en');
    expect(fmtMoney(1234.5, 'USD')).toMatch(/\$1,234\.50/);
  });

  it('never parses user text as HTML in map labels', () => {
    const el = textNode('<img src=x onerror=alert(1)>');
    expect(el.querySelector('img')).toBeNull();
    expect(el.textContent).toContain('<img');
  });

  it('lists the three visibility levels in ascending order', () => {
    expect(VISIBILITIES).toEqual(['Private', 'Crew', 'Public']);
  });
});
