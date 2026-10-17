import { Correction, draftGeometry, lengthNm, toFeatureCollection } from './routing-corrections';

describe('routing corrections helpers', () => {
  const line: Correction = { id: 'a', kind: 'Passage', source: 'Derived', geometry: { type: 'LineString', coordinates: [[10, 54], [10, 54.1]] }, note: 'Kanal', active: true, createdAt: '' };
  const block: Correction = { id: 'b', kind: 'Block', source: 'Manual', geometry: { type: 'Polygon', coordinates: [[[10, 54], [10.1, 54], [10.1, 54.1], [10, 54]]] }, note: null, active: false, createdAt: '' };

  it('measures passage length in nautical miles', () => {
    expect(lengthNm(line.geometry)).toBeCloseTo(6.0, 0); // 0.1° of latitude ≈ 6 nm
    expect(lengthNm(block.geometry)).toBe(0);
  });

  it('turns corrections into a feature collection with display properties', () => {
    const fc = toFeatureCollection([line, block]);
    expect(fc.features.length).toBe(2);
    expect(fc.features[0].properties).toEqual({ id: 'a', kind: 'Passage', source: 'Derived', active: true, note: 'Kanal' });
    expect(fc.features[1].properties!['note']).toBe('');
    expect(fc.features[1].geometry.type).toBe('Polygon');
  });

  it('builds draft geometries only when enough points were clicked', () => {
    expect(draftGeometry('Passage', [[10, 54]])).toBeNull();
    expect(draftGeometry('Passage', [[10, 54], [10.1, 54]])!.type).toBe('LineString');
    expect(draftGeometry('Block', [[10, 54], [10.1, 54]])).toBeNull();
    const poly = draftGeometry('Block', [[10, 54], [10.1, 54], [10.1, 54.1]]);
    expect(poly!.type).toBe('Polygon');
    expect((poly as { coordinates: number[][][] }).coordinates[0]).toEqual([[10, 54], [10.1, 54], [10.1, 54.1], [10, 54]]); // ring closed
  });
});
