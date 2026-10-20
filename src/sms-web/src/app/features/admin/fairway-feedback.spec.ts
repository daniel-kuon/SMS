import { Buoy, buoysToFc, exportFileName, FeedbackItem, isLineKind, itemsToFc, LAYERS, metresPerPixel, originalGeometry, parseLayers, passagesToFc, snapToBuoy } from './fairway-feedback';

describe('fairway feedback drawing helpers', () => {
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

describe('fairway feedback corrected objects', () => {
  const original = { type: 'LineString' as const, coordinates: [[11.9, 55], [11.915, 55.01], [11.93, 55]] };
  const corrected: FeedbackItem = { id: 'c', groupId: 'g', kind: 'CorrectedObject', objectRef: 'correction:x', snapshot: { note: 'Fahrwasser (5 Tonnenpaare)', geometry: original },
    geometry: { type: 'LineString', coordinates: [[11.9, 55], [11.93, 55]] }, note: null, expectation: null, createdAt: '' };

  it('shows the original under the correction and reads it from the snapshot', () => {
    expect(originalGeometry(corrected)).toEqual(original);
    expect(originalGeometry({ ...corrected, snapshot: { note: 'x' } })).toBeNull();
    const fc = itemsToFc([corrected]);
    expect(fc.features.map(f => [f.properties!['kind'], f.geometry.type])).toEqual([['Original', 'LineString'], ['CorrectedObject', 'LineString']]);
    expect(itemsToFc([{ ...corrected, kind: 'WrongObject' }]).features.length).toBe(1); // a wrong object is its own original
  });

  it('treats drawn and corrected lines as editable lines', () => {
    expect(isLineKind('MissingFairway')).toBe(true);
    expect(isLineKind('CorrectedObject')).toBe(true);
    expect(isLineKind('WrongObject')).toBe(false);
    expect(isLineKind(undefined)).toBe(false);
  });
});
