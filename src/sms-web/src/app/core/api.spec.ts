import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { apiInterceptor, AuthService, errorMessage, Me } from './api';
import { tenantPreview } from './site';

const me: Me = { id: 'u1', email: 'a@b.c', emailConfirmed: true, displayName: 'A', language: 'de', isAdmin: false, defaultContentVisibility: 'Crew', avatarUrl: null };

describe('apiInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  let auth: AuthService;
  const snack = { open: vi.fn() };

  beforeEach(() => {
    tenantPreview.set(null);
    snack.open.mockReset();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([apiInterceptor])), provideHttpClientTesting(), { provide: MatSnackBar, useValue: snack }],
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });
  afterEach(() => { ctrl.verify(); tenantPreview.set(null); });

  it('adds the CSRF marker header to API requests only', () => {
    http.get('/api/ping').subscribe();
    http.get('https://tiles.openfreemap.org/styles/liberty').subscribe();
    expect(ctrl.expectOne('/api/ping').request.headers.get('X-Requested-With')).toBe('XMLHttpRequest');
    expect(ctrl.expectOne('https://tiles.openfreemap.org/styles/liberty').request.headers.has('X-Requested-With')).toBe(false);
  });

  it('sends the tenant preview host when set', () => {
    tenantPreview.set('kairos.example.com');
    http.get('/api/site').subscribe();
    expect(ctrl.expectOne('/api/site').request.headers.get('X-Tenant-Host')).toBe('kairos.example.com');
  });

  it('drops the cached user on 401 and shows a snack on 5xx', () => {
    auth.me.set(me);
    http.get('/api/secret').subscribe({ error: () => undefined });
    ctrl.expectOne('/api/secret').flush('', { status: 401, statusText: 'Unauthorized' });
    expect(auth.me()).toBeNull();
    expect(snack.open).not.toHaveBeenCalled();
    http.get('/api/boom').subscribe({ error: () => undefined });
    ctrl.expectOne('/api/boom').flush('', { status: 500, statusText: 'Server Error' });
    expect(snack.open).toHaveBeenCalledOnce();
  });

  it('AuthService.load stores the profile or null', async () => {
    const p = auth.load();
    ctrl.expectOne('/api/auth/me').flush(me);
    expect(await p).toEqual(me);
    const p2 = auth.load();
    ctrl.expectOne('/api/auth/me').flush('', { status: 401, statusText: 'Unauthorized' });
    expect(await p2).toBeNull();
    expect(auth.me()).toBeNull();
  });
});

describe('errorMessage', () => {
  const err = (status: number, error: unknown) => new HttpErrorResponse({ status, error });
  it('prefers validation errors, then the problem title, then status texts', () => {
    expect(errorMessage(err(400, { errors: { name: ['Name fehlt.'], x: ['y'] } }))).toBe('Name fehlt. y');
    expect(errorMessage(err(409, { title: 'Bereits in der Crew.' }))).toBe('Bereits in der Crew.');
    expect(errorMessage(err(401, null))).toBe('Bitte anmelden.');
    expect(errorMessage(err(403, null))).toBe('Keine Berechtigung.');
    expect(errorMessage(err(502, null))).toBe('Fehler 502');
    expect(errorMessage(new Error('x'))).toContain('x');
  });
});
