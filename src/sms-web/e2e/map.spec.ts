import { expect, test } from '@playwright/test';

test('harbour map renders the vector base map, harbours and the sea-mark overlay', async ({ page }) => {
  const harbours = page.waitForResponse(r => r.url().includes('/api/harbours') && r.ok());
  const seamarkTile = page.waitForResponse(r => r.url().includes('/tiles/seamark/') && r.ok(), { timeout: 30_000 });
  await page.goto('/');
  await expect(page.locator('.maplibregl-canvas')).toBeVisible();
  await harbours;
  await seamarkTile;
  await expect(page.locator('.maplibregl-ctrl-attrib')).toContainText('OpenSeaMap');
  // the map worker must be alive (it would log "Worker failed to load" otherwise)
  const errors: string[] = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  await page.waitForTimeout(1500);
  expect(errors.filter(e => /worker/i.test(e))).toEqual([]);
});

test('ship website mode is off on the portal host', async ({ page }) => {
  const site = await page.request.get('/api/site');
  expect((await site.json()).mode).toBe('portal');
});
