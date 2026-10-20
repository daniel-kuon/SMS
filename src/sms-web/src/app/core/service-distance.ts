/**
 * Distance of a harbour service: 0 = on site, below 1 km in metres rounded to 10, from 1 km in km with one decimal (locale: "1,2 km" / "1.2 km").
 * With onFoot (the distance is walked over the path network, not straight) the word follows: "350 m zu Fuß".
 */
export function formatServiceDistance(m: number, lang: string, onSite: string, onFoot?: string): string {
  if (!m) return onSite;
  const rounded = Math.max(10, Math.round(m / 10) * 10);
  const d = rounded < 1000 ? `${rounded} m` : `${new Intl.NumberFormat(lang === 'en' ? 'en-GB' : 'de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(m / 1000)} km`;
  return onFoot ? `${d} ${onFoot}` : d;
}
