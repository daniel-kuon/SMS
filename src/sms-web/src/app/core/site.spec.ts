import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ShipSite, SiteService, tenantPreview } from './site';

export const shipSite: ShipSite = {
  mode: 'ship',
  ship: { id: 's1', name: 'Kairos', type: null, description: null, lengthM: null, beamM: null, draftM: null, sailNumber: null, homeHarbour: null, homeHarbourId: null, photoUrl: null, canEdit: false },
  pages: [{ id: 'p1', shipId: 's1', parentId: null, slug: 'ueber-uns', path: 'ueber-uns', title: 'Über uns', content: '', order: 0, published: true, showInNav: true, language: 'de', updatedAt: '', children: [] }],
  latestLogbook: [],
  trips: [],
};

describe('SiteService', () => {
  let service: SiteService;
  let ctrl: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(SiteService);
    ctrl = TestBed.inject(HttpTestingController);
  });
  afterEach(() => ctrl.verify());

  it('starts in portal mode and switches to ship mode from /api/site', async () => {
    expect(service.shipSite).toBeNull();
    const p = service.load();
    ctrl.expectOne('/api/site').flush(shipSite);
    await p;
    expect(service.site().mode).toBe('ship');
    expect(service.shipSite?.ship.name).toBe('Kairos');
  });

  it('keeps the previous site when the request fails', async () => {
    const p = service.load();
    ctrl.expectOne('/api/site').flush('', { status: 503, statusText: 'down' });
    expect((await p).mode).toBe('portal');
  });
});

describe('tenantPreview', () => {
  afterEach(() => tenantPreview.set(null));
  it('round-trips through localStorage and clears on null', () => {
    tenantPreview.set('kairos.example.com');
    expect(tenantPreview.get()).toBe('kairos.example.com');
    tenantPreview.set(null);
    expect(tenantPreview.get()).toBeNull();
  });
});
