import { Correction, CorrectionFilter, draftGeometry, filterExpression, geometryCoords, KINDS, lengthNm, matchesFilter, parseFilter, toFeatureCollection } from './routing-corrections';

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

describe('routing corrections filter', () => {
  const c = (kind: Correction['kind'], source: Correction['source']): Correction => ({ id: kind + source, kind, source, geometry: { type: 'Point', coordinates: [10, 54] }, note: null, active: true, createdAt: '' });

  it('parses the stored filter and falls back to everything', () => {
    expect(parseFilter(null)).toEqual({ kinds: KINDS, sources: ['Manual', 'Derived'] });
    expect(parseFilter('nonsense')).toEqual({ kinds: KINDS, sources: ['Manual', 'Derived'] });
    expect(parseFilter('{"kinds":["Lock","Foo"],"sources":["Manual"]}')).toEqual({ kinds: ['Lock'], sources: ['Manual'] });
  });

  it('matches kind and source, also as a map expression', () => {
    const f: CorrectionFilter = { kinds: ['Block'], sources: ['Manual'] };
    expect(matchesFilter(c('Block', 'Manual'), f)).toBe(true);
    expect(matchesFilter(c('Block', 'Derived'), f)).toBe(false);
    expect(matchesFilter(c('Lock', 'Manual'), f)).toBe(false);
    expect(filterExpression(f)).toEqual(['all', ['in', ['get', 'kind'], ['literal', ['Block']]], ['in', ['get', 'source'], ['literal', ['Manual']]]]);
  });

  it('flattens geometry coordinates', () => {
    expect(geometryCoords({ type: 'Point', coordinates: [1, 2] })).toEqual([[1, 2]]);
    expect(geometryCoords({ type: 'Polygon', coordinates: [[[1, 2], [3, 4], [1, 2]]] }).length).toBe(3);
    expect(geometryCoords({ type: 'MultiPolygon', coordinates: [[[[1, 2], [3, 4]]], [[[5, 6]]]] })).toEqual([[1, 2], [3, 4], [5, 6]]);
  });
});
