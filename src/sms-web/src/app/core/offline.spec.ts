import { drafts, isNetworkError } from './offline';

describe('isNetworkError', () => {
  it('treats status 0 / missing status as connectivity problems, HTTP errors as server answers', () => {
    expect(isNetworkError({ status: 0 })).toBe(true);
    expect(isNetworkError(new Error('fetch failed'))).toBe(true);
    expect(isNetworkError({ status: 400 })).toBe(false);
    expect(isNetworkError({ status: 500 })).toBe(false);
  });
  it('is true while the browser is offline', () => {
    const spy = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    expect(isNetworkError({ status: 400 })).toBe(true);
    spy.mockRestore();
  });
});

describe('drafts', () => {
  afterEach(() => localStorage.clear());
  it('saves, loads and clears per scope', () => {
    drafts.save('post:t1', { title: 'Tag 1', content: '<p>Moin</p>' });
    expect(drafts.load<{ title: string }>('post:t1')?.title).toBe('Tag 1');
    expect(drafts.load('post:other')).toBeNull();
    expect(localStorage.getItem('draft:post:t1')).toContain('Moin');
    drafts.clear('post:t1');
    expect(drafts.load('post:t1')).toBeNull();
  });
  it('ignores corrupt JSON', () => {
    localStorage.setItem('draft:x', '{not json');
    expect(drafts.load('x')).toBeNull();
  });
});
