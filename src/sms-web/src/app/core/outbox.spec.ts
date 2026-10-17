import { flushOutbox, outbox, OutboxItem } from './offline';

const item = (label: string, userId = 'u1'): OutboxItem => ({ userId, method: 'POST', url: '/api/ships/s/logbook', body: { label }, label, createdAt: 1 });

describe('outbox', () => {
  afterEach(() => localStorage.clear());

  it('stores items in localStorage and survives corrupt data', () => {
    expect(outbox.count()).toBe(0);
    outbox.add(item('a')); outbox.add(item('b'));
    expect(outbox.list().map(i => i.label)).toEqual(['a', 'b']);
    expect(outbox.count()).toBe(2);
    localStorage.setItem(outbox.key, '{broken');
    expect(outbox.list()).toEqual([]);
    outbox.clear();
    expect(localStorage.getItem(outbox.key)).toBeNull();
  });

  it('flushes in order, keeps the rest after a network error', async () => {
    outbox.add(item('a')); outbox.add(item('b')); outbox.add(item('c'));
    const sent: string[] = [];
    const r = await flushOutbox('u1', async i => { if (i.label === 'b') throw { status: 0 }; sent.push(i.label); });
    expect(sent).toEqual(['a']);
    expect(r.sent).toBe(1); expect(r.rejected).toEqual([]);
    expect(outbox.list().map(i => i.label)).toEqual(['b', 'c']);
  });

  it('drops rejected items and those of other accounts', async () => {
    outbox.add(item('a')); outbox.add(item('other', 'u2')); outbox.add(item('bad'));
    const r = await flushOutbox('u1', async i => { if (i.label === 'bad') throw { status: 400 }; });
    expect(r.sent).toBe(1);
    expect(r.rejected.map(i => i.label)).toEqual(['bad']);
    expect(outbox.count()).toBe(0);
  });

  it('sends nothing without a signed-in user and keeps nothing', async () => {
    outbox.add(item('a'));
    const r = await flushOutbox(undefined, async () => { throw new Error('must not send'); });
    expect(r.sent).toBe(0);
    expect(outbox.count()).toBe(0); // a signed-out device keeps no foreign entries
  });
});
