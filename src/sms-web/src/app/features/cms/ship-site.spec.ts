import { ShipPageNode } from '../../core/site';
import { flatten } from './ship-site';

const node = (id: string, children: ShipPageNode[] = []): ShipPageNode =>
  ({ id, shipId: 's', parentId: null, slug: id, path: id, title: id, content: '', order: 0, published: true, showInNav: true, language: 'de', updatedAt: '', children });

describe('flatten', () => {
  it('walks the page tree depth-first and records the depth', () => {
    const tree = [node('a', [node('a1', [node('a1x')]), node('a2')]), node('b')];
    expect(flatten(tree).map(n => `${n.depth}:${n.id}`)).toEqual(['0:a', '1:a1', '2:a1x', '1:a2', '0:b']);
  });
  it('returns an empty list for no pages', () => {
    expect(flatten([])).toEqual([]);
  });
});
