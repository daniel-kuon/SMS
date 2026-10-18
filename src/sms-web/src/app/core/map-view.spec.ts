import { vi } from 'vitest';
import type { Map as MlMap } from 'maplibre-gl';
import { formatView, parseView, syncViewWithUrl, UrlHistory, viewFromUrl } from './map';

describe('map view in the URL', () => {
  it('parses and formats ?c=lat,lng,zoom', () => {
    expect(parseView('54.39812,10.43401,13.5')).toEqual({ lat: 54.39812, lng: 10.43401, zoom: 13.5 });
    expect(formatView({ lat: 54.398123456, lng: 10.4, zoom: 13.456 })).toBe('54.39812,10.40000,13.46');
    expect(parseView(formatView({ lat: -33.9, lng: 151.2, zoom: 9 }))).toEqual({ lat: -33.9, lng: 151.2, zoom: 9 });
    for (const bad of [null, '', '54,10', '54,10,', 'a,b,c', '91,10,5', '54,181,5', '54,10,30']) expect(parseView(bad)).toBeNull();
    expect(viewFromUrl('/harbours/x?foo=1&c=54.4,10.5,12')).toEqual({ lat: 54.4, lng: 10.5, zoom: 12 });
    expect(viewFromUrl('/harbours/x')).toBeNull();
  });

  it('replaces while moving, pushes a new entry once the map settled elsewhere, and jumps back on popstate', () => {
    vi.useFakeTimers();
    let center = { lat: 54, lng: 10 }, zoom = 10;
    const handlers: Record<string, () => void> = {};
    const map = {
      getCenter: () => center, getZoom: () => zoom,
      project: ([lng, lat]: [number, number]) => ({ x: lng * 1000, y: lat * 1000, dist(o: { x: number; y: number }) { return Math.hypot(this.x - o.x, this.y - o.y); } }),
      on: (ev: string, fn: () => void) => { handlers[ev] = fn; }, off: vi.fn(),
      jumpTo: vi.fn((o: { center: [number, number]; zoom: number }) => { center = { lng: o.center[0], lat: o.center[1] }; zoom = o.zoom; }),
    } as unknown as MlMap;
    const entries = ['/harbours/a?c=54.00000,10.00000,10'];
    let pos = 0, popstate: (e: { url?: string }) => void = () => {};
    const history: UrlHistory = {
      path: () => entries[pos], getState: () => null,
      replaceState: (p, q) => { entries[pos] = q ? `${p}?${q}` : p; },
      go: (p, q) => { entries.splice(++pos); entries.push(q ? `${p}?${q}` : p); },
      subscribe: fn => { popstate = fn; return { unsubscribe: vi.fn() }; },
    };
    const stop = syncViewWithUrl(map, history);

    center = { lat: 54.0001, lng: 10 }; handlers['moveend'](); vi.advanceTimersByTime(1000); // tiny nudge: replace only
    expect(entries).toEqual(['/harbours/a?c=54.00010,10.00000,10']);

    center = { lat: 54.5, lng: 10.5 }; zoom = 12; handlers['moveend']();
    expect(entries[0]).toBe('/harbours/a?c=54.50000,10.50000,12'); // reload keeps the view right away
    vi.advanceTimersByTime(1000);
    expect(entries).toEqual(['/harbours/a?c=54.00010,10.00000,10', '/harbours/a?c=54.50000,10.50000,12']);

    pos = 0; popstate({ url: entries[0] }); // Back
    expect(map.jumpTo).toHaveBeenLastCalledWith({ center: [10, 54.0001], zoom: 10 });
    handlers['moveend'](); vi.advanceTimersByTime(1000); // the jump itself pushes nothing
    expect(entries.length).toBe(2);

    stop();
    vi.useRealTimers();
  });
});
