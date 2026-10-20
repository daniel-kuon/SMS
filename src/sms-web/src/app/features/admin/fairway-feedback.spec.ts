import { Buoy, buoysToFc, dedupe, draftLine, exportFileName, FeedbackItem, itemsToFc, LAYERS, metresPerPixel, parseLayers, passagesToFc, snapToBuoy } from './fairway-feedback';

describe('fairway feedback drawing helpers', () => {
  it('drops the doubled vertex of a double-click and needs two distinct points', () => {
    expect(dedupe([[1, 2], [1, 2], [3, 4], [3, 4], [1, 2]])).toEqual([[1, 2], [3, 4], [1, 2]]);
    expect(draftLine([[1, 2], [1, 2]])).toBeNull();
    expect(draftLine([[1, 2], [3, 4], [3, 4]])).toEqual({ type: 'LineString', coordinates: [[1, 2], [3, 4]] });
  });

  it('snaps to the nearest buoy within the radius', () => {
    const buoy = (id: number, lat: number, lng: number): Buoy => ({ id, type: 'buoy_lateral', category: 'port', name: null, colour: 'red', side: 'Port', lat, lng, tags: null });
    const buoys = [buoy(1, 55.0005, 11.9), buoy(2, 55.0002, 11.9)];
    expect(snapToBuoy([11.9, 55], buoys, 50)).toEqual([11.9, 55.0002]); // 22 m: the nearer one
    expect(snapToBuoy([11.9, 55], buoys, 10)).toEqual([11.9, 55]);
  });

  it('converts pixels to metres like MapLibre', () => {
    expect(metresPerPixel(0, 0)).toBeCloseTo(78271.5, 0);
    expect(metresPerPixel(55, 12) / metresPerPixel(55, 13)).toBeCloseTo(2, 6);
  });
});

describe('fairway feedback data helpers', () => {
  it('builds the map sources', () => {
    const p = passagesToFc([{ id: 'a', source: 'Derived', note: null, active: true, geometry: { type: 'LineString', coordinates: [[1, 2], [3, 4]] }, createdAt: '', category: 'fairway' }]);
    expect(p.features[0].properties).toEqual({ id: 'a', category: 'fairway', label: '', active: true });
    const b = buoysToFc([{ id: 7, type: 'buoy_lateral', category: 'starboard', name: 'Ø 3', colour: 'green', side: 'Starboard', lat: 55, lng: 11.9, tags: {} }]);
    expect(b.features[0].geometry).toEqual({ type: 'Point', coordinates: [11.9, 55] });
    const item = (id: string, geometry: FeedbackItem['geometry']): FeedbackItem => ({ id, groupId: 'g', kind: 'Comment', objectRef: null, snapshot: null, geometry, note: 'x', expectation: null, createdAt: '' });
    expect(itemsToFc([item('1', null), item('2', { type: 'Point', coordinates: [1, 2] })]).features.map(f => f.properties!['id'])).toEqual(['2']);
  });

  it('names export files after the group and day', () => {
    expect(exportFileName('Bøgestrøm / Grønsund', new Date('2026-10-20T12:00:00Z'))).toBe('fahrwasser-feedback-bogestrom-gronsund-2026-10-20.json');
    expect(exportFileName('Ærø', new Date('2026-10-20T12:00:00Z'))).toBe('fahrwasser-feedback-aero-2026-10-20.json');
    expect(exportFileName('!!', new Date('2026-10-20T12:00:00Z'))).toBe('fahrwasser-feedback-gruppe-2026-10-20.json');
  });

  it('reads the stored layer filter and falls back to all layers', () => {
    expect(parseLayers(null)).toEqual(LAYERS);
    expect(parseLayers('{')).toEqual(LAYERS);
    expect(parseLayers('["buoys","nope","fairway"]')).toEqual(['fairway', 'buoys']);
  });
});
