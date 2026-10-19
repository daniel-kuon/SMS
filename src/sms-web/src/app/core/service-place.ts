/** A place of a harbour service from GET /api/pois/near (details parsed from the OSM tags). */
export interface ServicePlace {
  id: number; name: string | null; kind: string; lat: number; lng: number; distanceM: number;
  openingHours: string | null; cuisine: string | null; website: string | null; phone: string | null; address: string | null;
}

const CUISINE_DE: Record<string, string> = {
  italian: 'Italienisch', german: 'Deutsch', regional: 'Regional', greek: 'Griechisch', chinese: 'Chinesisch', asian: 'Asiatisch', indian: 'Indisch', turkish: 'Türkisch', japanese: 'Japanisch',
  thai: 'Thailändisch', vietnamese: 'Vietnamesisch', french: 'Französisch', spanish: 'Spanisch', mediterranean: 'Mediterran', international: 'International', seafood: 'Meeresfrüchte', fish: 'Fisch',
  ice_cream: 'Eis', coffee_shop: 'Kaffee', steak_house: 'Steakhaus', burger: 'Burger', pizza: 'Pizza', kebab: 'Kebab', sandwich: 'Sandwiches', bakery: 'Bäckerei', danish: 'Dänisch', swedish: 'Schwedisch',
};

/** "ice_cream;pizza" -> "Eis, Pizza" (de) / "Ice cream, Pizza" (other languages); unknown values are shown as is, underscores replaced. */
export function cuisineText(v: string, lang: string): string {
  return v.split(';').map(c => c.trim()).filter(Boolean).map(c => {
    const k = c.toLowerCase();
    if (lang !== 'en' && CUISINE_DE[k]) return CUISINE_DE[k];
    const t = c.replace(/_/g, ' ');
    return t.charAt(0).toUpperCase() + t.slice(1);
  }).join(', ');
}

/** Google Maps walking route from the harbour to the place (opened in a new tab by the caller). */
export const googleRouteUrl = (from: { lat: number; lng: number }, to: { lat: number; lng: number }) =>
  `https://www.google.com/maps/dir/?api=1&origin=${from.lat},${from.lng}&destination=${to.lat},${to.lng}&travelmode=walking`;

export interface PlaceRow { label: string; value: string; href?: string; }
/** The available details as label/value rows (labels already translated); shared by the side-panel list and the map popups. */
export function placeRows(p: ServicePlace, lang: string, labels: { hours: string; cuisine: string; website: string; phone: string; address: string }): PlaceRow[] {
  const rows: PlaceRow[] = [];
  if (p.openingHours) rows.push({ label: labels.hours, value: p.openingHours });
  if (p.cuisine) rows.push({ label: labels.cuisine, value: cuisineText(p.cuisine, lang) });
  if (p.address) rows.push({ label: labels.address, value: p.address });
  // OSM tags are user data: links only for http(s) and plain phone numbers (a "javascript:" website tag must not become a link)
  const tel = p.phone?.replace(/[^\d+]/g, '').replace(/(?!^)\+/g, '');
  if (p.phone) rows.push({ label: labels.phone, value: p.phone, href: tel ? `tel:${tel}` : undefined });
  if (p.website) rows.push({ label: labels.website, value: p.website.replace(/^https?:\/\/(www\.)?/, '').replace(/\/$/, ''), href: safeHttpUrl(p.website) });
  return rows;
}

/** The URL when it is http(s) (a bare "example.org" gets https://), otherwise undefined. */
export function safeHttpUrl(u: string): string | undefined {
  try { const x = new URL(/^[a-z][a-z0-9+.-]*:/i.test(u) ? u : `https://${u}`); return x.protocol === 'http:' || x.protocol === 'https:' ? x.href : undefined; } catch { return undefined; }
}
