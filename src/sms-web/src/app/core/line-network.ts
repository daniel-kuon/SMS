import type { LineString, MultiLineString } from 'geojson';

/**
 * A drawn line with branches: lines[0] is the main line, every further line starts at a vertex of another one (a junction, the exact same
 * coordinate). Junctions are identified by equal coordinates, so moving or deleting one vertex keeps all branches meeting there connected.
 */
export type Pos = [number, number];
export type Lines = Pos[][];

export const samePos = (a: Pos, b: Pos) => a[0] === b[0] && a[1] === b[1];
/** Every vertex equal to `from` moves to `to` (a dragged junction drags all its branches along). */
export const moveShared = (lines: Lines, from: Pos, to: Pos): Lines => lines.map(l => l.map(p => samePos(p, from) ? to : p));
/** Every vertex equal to `at` is removed (a deleted junction leaves no dangling copy). */
export const removeShared = (lines: Lines, at: Pos): Lines => lines.map(l => l.filter(p => !samePos(p, at)));
/** Vertex inserted into one branch before `index`. */
export const insertInto = (lines: Lines, branch: number, index: number, p: Pos): Lines =>
  lines.map((l, i) => i === branch ? [...l.slice(0, index), p, ...l.slice(index)] : l);
/** A new branch from the junction `node` to `p` (the node's coordinate taken exactly, so the branch joins the line there). */
export const startBranch = (lines: Lines, node: Pos, p: Pos): Lines => [...lines.filter(l => l.length > 0), [node, p]];
/** The distinct vertices with the number of times they occur (more than once: a junction). */
export function uniqueVertices(lines: Lines): { p: Pos; count: number }[] {
  const seen = new Map<string, { p: Pos; count: number }>();
  for (const l of lines) for (const p of l) { const k = `${p[0]},${p[1]}`; const v = seen.get(k); if (v) v.count++; else seen.set(k, { p, count: 1 }); }
  return [...seen.values()];
}
/** Index of the vertex nearest to `at` within maxDist (plane coordinates, e.g. screen pixels), or -1. */
export function nearestVertex(points: Pos[], at: Pos, maxDist: number): number {
  let best = -1, bestD = maxDist;
  points.forEach((p, i) => { const d = Math.hypot(p[0] - at[0], p[1] - at[1]); if (d <= bestD) { best = i; bestD = d; } });
  return best;
}
/** Branches ready to save: consecutive duplicates removed, branches with fewer than two points dropped. */
export const cleanLines = (lines: Lines): Lines =>
  lines.map(l => l.filter((p, i) => i === 0 || !samePos(p, l[i - 1]))).filter(l => l.length >= 2);
/** One branch: a LineString, several: a MultiLineString, none: null. */
export function linesToGeometry(lines: Lines): LineString | MultiLineString | null {
  const c = cleanLines(lines);
  return c.length === 0 ? null : c.length === 1 ? { type: 'LineString', coordinates: c[0] } : { type: 'MultiLineString', coordinates: c };
}
/** Branches of a (Multi)LineString, 2D copies (the object they came from stays untouched). */
export function geometryToLines(g: { type: string; coordinates?: unknown } | null | undefined): Lines {
  const copy = (l: number[][]) => l.map(c => [c[0], c[1]] as Pos);
  if (g?.type === 'LineString') return [copy(g.coordinates as number[][])];
  if (g?.type === 'MultiLineString') return (g.coordinates as number[][][]).map(copy);
  return [[]];
}
export const vertexCount = (lines: Lines) => lines.reduce((n, l) => n + l.length, 0);
