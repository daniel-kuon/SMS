import { importSummary, TrackImport } from './track-import';

describe('importSummary', () => {
  const base: TrackImport = { id: '1', fileName: 'test.7z', status: 'Running', progressPercent: 42, parsed: 1200000, added: 1100000, duplicates: 100000, rejected: 0,
    tracks: 900, waypoints: 603, routes: 64, error: null, tripId: null, createdAt: '2026-10-30T10:00:00Z', finishedAt: null };

  it('shows progress, counts and what is not imported', () => {
    const s = importSummary(base);
    expect(s).toContain('test.7z (42 %)');
    expect(s).toContain('1.200.000 Punkte gelesen');
    expect(s).toContain('100.000 schon vorhanden');
    expect(s).toContain('603 Ereignisse und 64 Routen nicht importiert');
    expect(s).not.toContain('verworfen');
  });

  it('reports queued and failed imports', () => {
    expect(importSummary({ ...base, status: 'Queued' })).toBe('test.7z: wartet …');
    expect(importSummary({ ...base, status: 'Failed', error: 'kaputt' })).toBe('test.7z: Fehler – kaputt');
    expect(importSummary({ ...base, status: 'Done', waypoints: 0, routes: 0 })).not.toContain('%');
  });
});
