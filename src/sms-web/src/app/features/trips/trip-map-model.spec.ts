import { allPoints, buildLegs, insertVertex, lastStop, moveVertex, nearestSegment, parsePlan, removeCandidate, removeVertex } from './trip-map-model';

describe('trip plan steps', () => {
  // Kiel → Wendtorf → {Bagenkop | Marstal} → Svendborg (docs/14)
  const legs = [
    { order: 0, alternative: 0, fromHarbourId: 'kiel', toHarbourId: 'wendtorf', plannedDate: '2026-07-01' },
    { order: 1, alternative: 1, fromHarbourId: 'wendtorf', toHarbourId: 'marstal', plannedDate: null },
    { order: 1, alternative: 0, fromHarbourId: 'wendtorf', toHarbourId: 'bagenkop', plannedDate: null },
    { order: 2, alternative: 0, fromHarbourId: 'bagenkop', toHarbourId: 'svendborg', plannedDate: null },
    { order: 2, alternative: 1, fromHarbourId: 'marstal', toHarbourId: 'svendborg', plannedDate: null },
  ];

  it('groups legs into steps with candidates by alternative', () => {
    const plan = parsePlan(legs, null);
    expect(plan.origin).toBe('kiel');
    expect(plan.steps).toEqual([['wendtorf'], ['bagenkop', 'marstal'], ['svendborg']]);
    expect(plan.dates['wendtorf']).toBe('2026-07-01');
    expect(parsePlan(legs, 'laboe').origin).toBe('laboe');
    expect(parsePlan([], null)).toEqual({ origin: null, steps: [], dates: {} });
  });

  it('builds the leg list: fan out to alternatives, merge back, one leg per alternative', () => {
    const out = buildLegs(parsePlan(legs, null));
    expect(out.map(l => `${l.order}/${l.alternative} ${l.fromHarbourId}>${l.toHarbourId}`)).toEqual([
      '0/0 kiel>wendtorf', '1/0 wendtorf>bagenkop', '1/1 wendtorf>marstal', '2/0 bagenkop>svendborg', '2/1 marstal>svendborg',
    ]);
    expect(out[0].plannedDate).toBe('2026-07-01');
    expect(buildLegs({ origin: null, steps: [['a']], dates: {} })).toEqual([]);
  });

  it('runs equal alternative counts in parallel and pairs the rest with the last candidate', () => {
    const out = buildLegs({ origin: 'o', steps: [['a', 'b'], ['c', 'd', 'e']], dates: {} });
    expect(out.filter(l => l.order === 1).map(l => `${l.fromHarbourId}>${l.toHarbourId}`)).toEqual(['a>c', 'b>d', 'b>e']);
  });

  it('removes candidates and empty steps, renumbering orders', () => {
    const plan = parsePlan(legs, null);
    const noMarstal = removeCandidate(plan, 1, 1);
    expect(noMarstal.steps).toEqual([['wendtorf'], ['bagenkop'], ['svendborg']]);
    const noStep = removeCandidate(plan, 0, 0);
    expect(noStep.steps.length).toBe(2);
    expect(buildLegs(noStep)[0]).toEqual(expect.objectContaining({ order: 0, fromHarbourId: 'kiel', toHarbourId: 'bagenkop' }));
    expect(lastStop(plan)).toBe('svendborg');
    expect(lastStop({ origin: 'o', steps: [], dates: {} })).toBe('o');
  });
});

describe('route vertex editing', () => {
  const line = [[0, 0], [10, 0], [10, 10]];

  it('finds the nearest segment', () => {
    expect(nearestSegment([[0, 0], [10, 0], [10, 10]], [5, 1])).toBe(0);
    expect(nearestSegment([[0, 0], [10, 0], [10, 10]], [9, 6])).toBe(1);
  });

  it('inserts, moves and removes vertices', () => {
    expect(insertVertex(line, 0, [5, 0])).toEqual([[0, 0], [5, 0], [10, 0], [10, 10]]);
    expect(insertVertex(line, 1, [10, 5])).toEqual([[0, 0], [10, 0], [10, 5], [10, 10]]);
    expect(moveVertex(line, 1, [9, 1])).toEqual([[0, 0], [9, 1], [10, 10]]);
    expect(removeVertex(line, 1)).toEqual([[0, 0], [10, 10]]);
    expect(removeVertex([[0, 0], [1, 1]], 0)).toEqual([[0, 0], [1, 1]]); // never shorter than two points
  });

  it('collects every position of the map payload', () => {
    const pts = allPoints({ plannedLegs: [{ id: 'l', order: 0, alternative: 0, fromHarbourId: 'a', toHarbourId: 'b', plannedDate: null, distanceNm: 1, geometry: { type: 'LineString', coordinates: [[1, 2], [3, 4]] } }],
      logbook: [{ id: 'e', startHarbourId: 'a', endHarbourId: 'b', departedAt: '', arrivedAt: '', geometry: null }], harbours: [{ id: 'a', name: 'A', lat: 5, lng: 6 }], photos: [{ id: 'p', thumbUrl: '', lat: 7, lng: 8, takenAt: null, logbookEntryId: null }] });
    expect(pts).toEqual([[1, 2], [3, 4], [6, 5], [8, 7]]);
  });
});

