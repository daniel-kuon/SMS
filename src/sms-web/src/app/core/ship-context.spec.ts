import { fromShip, parseContext } from './ship-context';
import { routeWarningText } from './models';
import { depthFillColour, depthHatchFilter } from './map';

describe('ship depth settings', () => {
  it('migrates a stored draft-only context to minimum depth = draft + 0.5 m without warnings', () => {
    expect(parseContext(JSON.stringify({ id: 'a', name: 'Hulle', draftM: 1.8, airDraftM: 14 }))).toEqual({ id: 'a', name: 'Hulle', minDepthM: 2.3, warnDepthM: null, airDraftM: 14 });
    expect(parseContext(JSON.stringify({ id: null, name: '', draftM: null, airDraftM: 12 }))?.minDepthM).toBeNull();
    expect(parseContext(JSON.stringify({ id: null, name: '', minDepthM: 2, warnDepthM: 2.8, airDraftM: null }))).toEqual({ id: null, name: '', minDepthM: 2, warnDepthM: 2.8, airDraftM: null });
    expect(parseContext(null)).toBeNull();
  });
  it('takes the ship\'s own minimum depth as given, else draft + 0.5 m', () => {
    const ship = { id: 's', name: 'S', draftM: 1.8, airDraftM: null, minDepthM: null, warnDepthM: null };
    expect(fromShip(ship).minDepthM).toBe(2.3);
    expect(fromShip({ ...ship, minDepthM: 2, warnDepthM: 2.6 })).toEqual({ id: 's', name: 'S', minDepthM: 2, warnDepthM: 2.6, airDraftM: null });
  });
  it('lists the same route warning once with a count', () => {
    const w = (note: string) => ({ kind: 'checkDepth', lat: 54, lng: 10, note });
    expect(routeWarningText([w('Tiefe prüfen'), w('Tiefe prüfen'), w('Durchfahrt angenommen')])).toBe(' · ⚠ Tiefe prüfen (2×); Durchfahrt angenommen');
    expect(routeWarningText([])).toBe('');
  });
  it('shades too shallow by the band middle and "check depth" by the band floor', () => {
    const fill = JSON.stringify(depthFillColour(2, 2.8));
    expect(fill).toContain('["<",["/",["+",["get","min_depth"],["get","max_depth"]],2],2],"#34357a"');
    expect(fill).toContain('["<",["get","min_depth"],2.8],"#9c97d6"');
    expect(JSON.stringify(depthFillColour(2))).not.toContain('2.8');
    expect(JSON.stringify(depthHatchFilter(2))).toContain('["<",["/",["+",["get","min_depth"],["get","max_depth"]],2],2]');
    expect(JSON.stringify(depthFillColour(null, null))).not.toContain('#34357a');
  });
});
