import { expect, test } from '@playwright/test';

/** window hooks set by core/map.ts */
interface SmsWindow { smsMap?: { weatherOverlays?: { mode: string; selected: Set<string>; apply(): Promise<void>; glLayers: Map<string, string[]> } } }

test('harbour panel shows the forecast and the map context menu offers one for any point', async ({ page }) => {
  const harbours = await (await page.request.get('/api/harbours?q=Meldorf')).json() as { id: string; name: string }[];
  await page.goto(`/harbours/${harbours[0].id}`);
  await expect(page.locator('.maplibregl-canvas')).toBeVisible();
  await page.getByRole('tab', { name: 'Wetter' }).click();
  await expect(page.locator('app-weather-card .current')).toContainText('°C', { timeout: 30_000 });
  await expect(page.locator('app-weather-card .hour')).toHaveCount(24);
  await page.waitForTimeout(1500); // let the easeTo to the harbour finish: a context menu during the animation is dropped
  const canvas = await page.locator('.maplibregl-canvas').boundingBox();
  await page.mouse.click(canvas!.x + 120, canvas!.y + 300, { button: 'right' });
  await page.getByRole('button', { name: 'Wettervorhersage hier' }).click({ timeout: 10_000 });
  await expect(page.getByRole('dialog')).toContainText('°C', { timeout: 30_000 });
  await page.keyboard.press('Escape');
});

test('weather overlays can be combined and render through MapsGL when credentials are configured', async ({ page }) => {
  const cfg = await (await page.request.get('/api/weather/overlay/layers')).json() as { configured: boolean; mapsGl: unknown };
  test.skip(!cfg.configured, 'no Xweather credentials on this machine');
  page.on('console', m => { if (/MapsGL|mapsgl|xweather/i.test(m.text())) console.log('[browser]', m.type(), m.text()); });
  page.on('pageerror', e => console.log('[pageerror]', e.message));
  await page.goto('/');
  await expect(page.locator('.maplibregl-canvas')).toBeVisible();
  const button = page.locator('.weather-overlay-button');
  await expect(button).toBeVisible();
  await button.click();
  const panel = page.locator('.weather-overlay-panel');
  await panel.getByLabel('Wind').check();
  await panel.getByLabel('Wellen').check();
  await expect(button).toHaveText(/2/);
  await page.waitForTimeout(8_000); // SDK download + layer data
  const state = await page.evaluate(() => {
    const o = (window as unknown as SmsWindow).smsMap?.weatherOverlays;
    return o ? { mode: o.mode, selected: [...o.selected], gl: Object.fromEntries(o.glLayers) } : null;
  });
  expect(state?.selected.sort()).toEqual(['waves', 'wind']);
  if (cfg.mapsGl) {
    expect(state?.mode).toBe('gl');
    expect(state?.gl['wind']).toEqual(['wind-speeds', 'wind-particles']);
    expect(state?.gl['waves']?.length).toBeGreaterThan(0);
  }
  await page.screenshot({ path: process.env['E2E_SHOT'] ?? 'test-results/weather-overlays.png' });
  await panel.getByLabel('Wind').uncheck();
  await expect(button).toHaveText(/1/);
  await page.reload();
  await expect(page.locator('.weather-overlay-button')).toHaveText(/1/); // selection persists
});

test('a selected harbour shows the distance to a hovered harbour and to any clicked point', async ({ page }) => {
  const list = await (await page.request.get('/api/harbours?q=Meldorfer')).json() as { id: string }[];
  // listen before navigating: a cached nearby-ports answer can arrive before the canvas is visible
  const nearby = page.waitForResponse(r => r.url().includes('/api/routing/nearby-ports') && r.ok());
  await page.goto(`/harbours/${list[0].id}`);
  await expect(page.locator('.maplibregl-canvas')).toBeVisible();
  await nearby;
  await page.waitForTimeout(1500);
  await page.locator('.maplibregl-canvas').click({ position: { x: 60, y: 420 } });
  await expect(page.locator('.point-bar')).toContainText("' N"); // the clicked position, nautical
  const route = page.waitForResponse(r => r.url().includes('/api/routing/route?from=') && r.ok(), { timeout: 30_000 });
  await page.locator('.point-bar .route-here').click(); // no one-click routing: the bar's button routes from the selected harbour
  const r = await (await route).json() as { distanceNm: number; method: string };
  await expect(page.locator('.maplibregl-popup-content')).toContainText(`${r.distanceNm} sm`);
  await page.screenshot({ path: process.env['E2E_SHOT2'] ?? 'test-results/click-distance.png' });
  await page.locator('.maplibregl-popup-close-button').click();
  await expect(page.locator('.maplibregl-popup-content')).toHaveCount(0);
});

test('ship context hides deep hazards and routes carry the minimum depth', async ({ page }) => {
  const list = await (await page.request.get('/api/harbours?q=Meldorfer')).json() as { id: string }[];
  // listen before navigating: a cached nearby-ports answer can arrive before the canvas is visible
  const nearby = page.waitForResponse(r => r.url().includes('/api/routing/nearby-ports') && r.ok());
  await page.goto(`/harbours/${list[0].id}`);
  await expect(page.locator('.maplibregl-canvas')).toBeVisible();
  await nearby;
  await page.locator('.ship-ctx input').first().fill('2.3'); // minimum depth for routes (applied without extra margin)
  await page.locator('.ship-ctx input').first().dispatchEvent('input');
  await expect(page.locator('.ship-ctx .small')).toBeVisible();
  await page.waitForFunction(() => !!(window as unknown as { smsMap?: unknown }).smsMap); // the map handle is set once the style and overlays are up
  const filter = await page.evaluate(() => JSON.stringify((window as unknown as { smsMap: { getFilter(id: string): unknown } }).smsMap.getFilter('seamark-icons')));
  expect(filter).toContain('2.3');
  const route = page.waitForRequest(r => r.url().includes('/api/routing/route?') && r.url().includes('minDepth=2.3'), { timeout: 30_000 });
  await page.waitForTimeout(1000);
  await page.locator('.maplibregl-canvas').click({ position: { x: 60, y: 420 } });
  await page.locator('.point-bar .route-here').click();
  await route;
  await page.reload();
  await expect(page.locator('.ship-ctx input').first()).toHaveValue('2.3'); // remembered
});
