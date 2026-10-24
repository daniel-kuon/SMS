import { centreOf, drawnGeometry, drawShape, Issue, issueFeatures, issueState } from './routing-issues';

describe('routing issues helpers', () => {
  const base: Issue = { key: 'water:1:2', type: 'UncertainWater', title: 'Unsichere Tiefe', detail: null, priority: 5, geometry: { type: 'Polygon', coordinates: [[[10, 54], [10.03, 54], [10.03, 54.03], [10, 54.03], [10, 54]]] }, refId: null, detected: true, decision: null };
  const decision = { id: 'd', status: 'Fixed' as const, correctionId: 'c', note: null, basisKind: 'depth', basisRefs: 'ft: 1', basedAt: '', checkedAt: null, outdatedAt: null, outdatedReason: null, createdAt: '' };

  it('derives the state from the decision', () => {
    expect(issueState(base)).toBe('open');
    expect(issueState({ ...base, decision })).toBe('fixed');
    expect(issueState({ ...base, decision: { ...decision, status: 'Ignored' } })).toBe('ignored');
    expect(issueState({ ...base, decision: { ...decision, outdatedAt: '2026-10-24' } })).toBe('outdated'); // outdated wins over fixed
  });

  it('draws areas for area actions and lines for passages only when enough points were clicked', () => {
    expect(drawShape('known')).toBe('area'); expect(drawShape('passage')).toBe('line'); expect(drawShape('clearance')).toBeNull();
    expect(drawnGeometry('avoid', [[10, 54], [10.1, 54]])).toBeNull();
    expect((drawnGeometry('avoid', [[10, 54], [10.1, 54], [10.1, 54.1]]) as { coordinates: number[][][] }).coordinates[0]).toEqual([[10, 54], [10.1, 54], [10.1, 54.1], [10, 54]]);
    expect(drawnGeometry('passage', [[10, 54], [10.1, 54]])!.type).toBe('LineString');
    expect(drawnGeometry('ignore', [[10, 54], [10.1, 54], [10.1, 54.1]])).toBeNull();
  });

  it('builds map features keyed by the issue key and centres on the geometry', () => {
    const fc = issueFeatures([base, { ...base, key: 'k2', decision }]);
    expect(fc.features.map(f => f.id)).toEqual(['water:1:2', 'k2']);
    expect(fc.features[1].properties).toEqual({ key: 'k2', type: 'UncertainWater', state: 'fixed' });
    const c = centreOf(base.geometry);
    expect(c.lat).toBeCloseTo(54.015); expect(c.lng).toBeCloseTo(10.015);
  });
});
