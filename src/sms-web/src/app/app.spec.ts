import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { signal } from '@angular/core';
import { App } from './app';
import { OfflineQueue } from './core/offline';
import { SiteService } from './core/site';
import { shipSite } from './core/site.spec';

describe('App', () => {
  const offline = { online: signal(true), pending: signal(0), flush: () => Promise.resolve([]) };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), provideTranslateService(), { provide: OfflineQueue, useValue: offline }],
    }).compileComponents();
  });

  it('shows the portal navigation by default', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const html = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(html).toContain('nav.harbours');
    expect(html).not.toContain('Kairos');
  });

  it('shows the ship website navigation on a ship domain', async () => {
    TestBed.inject(SiteService).site.set(shipSite);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.brand')?.textContent).toContain('Kairos');
    expect(el.querySelector('a[href="/pages/ueber-uns"]')).not.toBeNull();
    expect(el.textContent).not.toContain('nav.harbours');
  });

  it('shows the offline chip and pending uploads', async () => {
    offline.online.set(false); offline.pending.set(2);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('offline');
    expect(text).toContain('2');
    offline.online.set(true); offline.pending.set(0);
  });
});
