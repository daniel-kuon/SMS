import { cleanLines, geometryToLines, insertInto, Lines, linesToGeometry, moveShared, nearestVertex, Pos, removeShared, startBranch, uniqueVertices, vertexCount } from './line-network';

describe('line network (branches with shared junctions)', () => {
  const j: Pos = [10, 0];
  // main line through the junction j, a branch from j northwards
  const net: Lines = [[[0, 0], j, [20, 0]], [j, [10, 10]]];

  it('starts a branch exactly at the attach node, also on an empty draft', () => {
    expect(startBranch([[[0, 0], j]], j, [10, 5])).toEqual([[[0, 0], j], [j, [10, 5]]]);
    expect(startBranch([[]], j, [10, 5])).toEqual([[j, [10, 5]]]); // a branch off another line on the map
  });

  it('moves a junction for every branch and leaves the input untouched', () => {
    const moved = moveShared(net, j, [11, 1]);
    expect(moved).toEqual([[[0, 0], [11, 1], [20, 0]], [[11, 1], [10, 10]]]);
    expect(net[1][0]).toEqual(j);
    expect(moveShared(net, [0, 0], [-1, 0])[1]).toEqual(net[1]); // other vertices: only their branch
  });

  it('deletes a junction from every branch', () => {
    expect(removeShared(net, j)).toEqual([[[0, 0], [20, 0]], [[10, 10]]]);
  });

  it('inserts into one branch', () => {
    expect(insertInto(net, 1, 1, [10, 5])).toEqual([net[0], [j, [10, 5], [10, 10]]]);
  });

  it('lists a junction once with its count', () => {
    const v = uniqueVertices(net);
    expect(v.length).toBe(4);
    expect(v.find(x => x.p[0] === 10 && x.p[1] === 0)!.count).toBe(2);
    expect(vertexCount(net)).toBe(5);
  });

  it('finds the nearest vertex within a radius', () => {
    expect(nearestVertex([[0, 0], [10, 0], [12, 0]], [11.5, 1], 5)).toBe(2);
    expect(nearestVertex([[0, 0]], [9, 9], 5)).toBe(-1);
  });

  it('saves one branch as LineString, several as MultiLineString, and drops stubs', () => {
    expect(cleanLines([[[0, 0], [0, 0], [1, 1]], [[5, 5]], []])).toEqual([[[0, 0], [1, 1]]]);
    expect(linesToGeometry([[[0, 0], [1, 1]], [[1, 1]]])).toEqual({ type: 'LineString', coordinates: [[0, 0], [1, 1]] });
    expect(linesToGeometry(net)!.type).toBe('MultiLineString');
    expect(linesToGeometry([[]])).toBeNull();
  });

  it('reads (Multi)LineStrings as detached 2D copies', () => {
    const g = { type: 'LineString', coordinates: [[1, 2, 3], [4, 5, 6]] };
    const l = geometryToLines(g);
    expect(l).toEqual([[[1, 2], [4, 5]]]);
    l[0][0][0] = 99;
    expect(g.coordinates[0][0]).toBe(1);
    expect(geometryToLines({ type: 'MultiLineString', coordinates: [[[0, 0], [1, 1]], [[1, 1], [2, 2]]] }).length).toBe(2);
    expect(geometryToLines(null)).toEqual([[]]);
    expect(geometryToLines({ type: 'Point', coordinates: [0, 0] })).toEqual([[]]);
  });
});
