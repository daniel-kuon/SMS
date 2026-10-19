import { cuisineText, googleRouteUrl, placeRows, ServicePlace } from './service-place';

describe('service-place', () => {
  it('translates common cuisines, others as is without underscores', () => {
    expect(cuisineText('italian;ice_cream', 'de')).toBe('Italienisch, Eis');
    expect(cuisineText('ice_cream;wok_bar', 'en')).toBe('Ice cream, Wok bar');
    expect(cuisineText('wok_bar', 'de')).toBe('Wok bar');
  });
  it('builds the walking route link', () => {
    expect(googleRouteUrl({ lat: 54.1, lng: 10.2 }, { lat: 54.2, lng: 10.3 })).toBe('https://www.google.com/maps/dir/?api=1&origin=54.1,10.2&destination=54.2,10.3&travelmode=walking');
  });
  it('lists only the available details', () => {
    const p: ServicePlace = { id: 1, name: null, kind: 'restaurant', lat: 0, lng: 0, distanceM: 10, openingHours: 'Mo-Su 10:00-22:00', cuisine: null, website: 'https://www.example.com/', phone: '+49 431 123', address: null };
    const l = { hours: 'H', cuisine: 'C', website: 'W', phone: 'P', address: 'A' };
    expect(placeRows(p, 'de', l)).toEqual([{ label: 'H', value: 'Mo-Su 10:00-22:00' }, { label: 'P', value: '+49 431 123', href: 'tel:+49431123' }, { label: 'W', value: 'example.com', href: 'https://www.example.com/' }]);
  });
});

describe('safeHttpUrl', () => {
  it('keeps http(s), adds https to bare hosts, drops other schemes', async () => {
    const { safeHttpUrl } = await import('./service-place');
    expect(safeHttpUrl('https://a.de/x')).toBe('https://a.de/x');
    expect(safeHttpUrl('www.a.de')).toBe('https://www.a.de/');
    expect(safeHttpUrl('javascript:alert(1)')).toBeUndefined();
    expect(safeHttpUrl('data:text/html,x')).toBeUndefined();
  });
});

