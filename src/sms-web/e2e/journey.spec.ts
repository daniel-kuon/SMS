import { expect, Page, test } from '@playwright/test';

const MAILPIT = process.env['E2E_MAILPIT'] ?? 'http://localhost:8025';
const stamp = Date.now().toString(36);
const user = { name: `E2E Skipper ${stamp}`, email: `e2e-${stamp}@test.local`, password: 'E2eSkipper2026!' };

async function fillLabelled(page: Page, label: RegExp | string, value: string, scope = page.locator('mat-dialog-container')) {
  await scope.getByLabel(label).first().fill(value);
}

/** Confirmation link from the first Mailpit message sent to the address. */
async function confirmationLink(page: Page, email: string): Promise<string> {
  for (let i = 0; i < 20; i++) {
    const list = await (await page.request.get(`${MAILPIT}/api/v1/search?query=${encodeURIComponent('to:' + email)}`)).json();
    if (list.messages?.length) {
      const msg = await (await page.request.get(`${MAILPIT}/api/v1/message/${list.messages[0].ID}`)).json();
      const m = /href="([^"]+\/auth\/confirm[^"]+)"/.exec(msg.HTML as string);
      if (m) return m[1].replace(/&amp;/g, '&');
    }
    await page.waitForTimeout(500);
  }
  throw new Error('no confirmation mail for ' + email);
}

test.describe.serial('skipper journey', () => {
  let shipId = '';
  let tripId = '';

  test('register, confirm by mail, log in', async ({ page }) => {
    await page.goto('/auth/register');
    await page.getByLabel(/Anzeigename|Display name/).fill(user.name);
    await page.getByLabel(/E-Mail/).fill(user.email);
    await page.getByLabel(/Passwort|Password/).fill(user.password);
    await page.getByRole('button', { name: /Registrieren|Register/ }).click();
    await expect(page.getByRole('link', { name: /Anmelden|Log in/ })).toBeVisible();

    await page.goto(await confirmationLink(page, user.email));
    await expect(page.getByRole('link', { name: /Anmelden|Log in/ })).toBeVisible();

    await page.goto('/auth/login');
    await page.getByLabel(/E-Mail/).fill(user.email);
    await page.getByLabel(/Passwort|Password/).fill(user.password);
    await page.getByRole('button', { name: /^Anmelden$|^Log in$/ }).click();
    await expect(page.locator('mat-toolbar')).toContainText(user.name);
  });

  test('create a ship and a trip', async ({ page }) => {
    await login(page);
    await page.goto('/ships');
    await page.getByRole('button', { name: /Neues Schiff|New ship/ }).click();
    const dialog = page.locator('mat-dialog-container');
    await fillLabelled(page, /^Name/, `Kairos ${stamp}`);
    await dialog.getByRole('button', { name: /Speichern|Save/ }).click();
    await expect(dialog).toBeHidden();
    await page.getByText(`Kairos ${stamp}`).first().click(); // mat-card with routerLink
    await expect(page).toHaveURL(/\/ships\/[0-9a-f-]+$/);
    shipId = page.url().split('/').pop()!;

    await page.getByRole('button', { name: /Neuer Törn|New trip/ }).click();
    await fillLabelled(page, /^Name/, `Sommertörn ${stamp}`);
    await fillLabelled(page, /Start|Beginn/, '2026-07-01');
    await fillLabelled(page, /Ende|End/, '2026-07-14');
    await dialog.getByRole('button', { name: /Speichern|Save/ }).click();
    await expect(dialog).toBeHidden();
    await page.getByText(`Sommertörn ${stamp}`).first().click();
    await expect(page).toHaveURL(/\/trips\/[0-9a-f-]+/);
    tripId = page.url().match(/\/trips\/([0-9a-f-]+)/)![1];
    await expect(page.locator('h1')).toContainText(`Sommertörn ${stamp}`);
  });

  test('write a logbook entry and see it in the overview', async ({ page }) => {
    await login(page);
    await page.goto(`/trips/${tripId}/logbook`);
    await page.getByRole('button', { name: /Neuer Eintrag|New entry/ }).click();
    const dialog = page.locator('mat-dialog-container');
    await dialog.getByLabel(/Starthafen|Start harbour/).fill('Kiel');
    await page.getByRole('option', { name: /Kiel/ }).first().click();
    await dialog.getByLabel(/Zielhafen|Destination/).fill('Laboe');
    await page.getByRole('option', { name: /Laboe/ }).first().click();
    await dialog.getByLabel(/Abgelegt|Departed/).fill('2026-07-02T09:00');
    await dialog.getByLabel(/Angelegt|Arrived/).fill('2026-07-02T13:30');
    await dialog.getByLabel(/^Log Start|Log start/).fill('1000');
    await dialog.getByLabel(/^Log Ende|Log end/).fill('1008.5');
    await dialog.getByRole('button', { name: /Speichern|Save/ }).click();
    await expect(dialog).toBeHidden();
    await expect(page.locator('table')).toContainText('Laboe');

    await page.goto(`/trips/${tripId}`);
    await expect(page.locator('.maplibregl-canvas')).toBeVisible();
    await expect(page.locator('.side')).toContainText('Laboe');
    await expect(page.locator('.playback')).toBeVisible(); // track playback is offered once a leg exists
  });

  test('costs: expense, balances, CSV and PDF statement', async ({ page }) => {
    await login(page);
    await page.goto(`/trips/${tripId}/costs`);
    await page.getByRole('button', { name: /Neue Ausgabe|New expense/ }).click();
    const dialog = page.locator('mat-dialog-container');
    await dialog.getByLabel(/Betrag|Amount/).first().fill('36');
    await dialog.getByRole('button', { name: /Speichern|Save/ }).click();
    await expect(dialog).toBeHidden();
    await expect(page.locator('.costs')).toContainText('36');
    const pdf = await page.request.get(`/api/trips/${tripId}/costs/export.pdf`);
    expect(pdf.headers()['content-type']).toContain('application/pdf');
    expect((await pdf.body()).subarray(0, 5).toString()).toBe('%PDF-');
    const csv = await (await page.request.get(`/api/trips/${tripId}/costs/export`)).text();
    expect(csv).toContain('36');
  });

  test('ship website: page, domain and tenant mode', async ({ page }) => {
    await login(page);
    await page.goto(`/ships/${shipId}`);
    await page.getByRole('button', { name: /Neue Seite|New page/ }).click();
    const dialog = page.locator('mat-dialog-container');
    await dialog.getByLabel(/Titel|Title/).fill('Über uns');
    await dialog.getByLabel(/Text|Content/).fill('<p>Willkommen an Bord</p>');
    await dialog.getByRole('button', { name: /Speichern|Save/ }).click();
    await expect(dialog).toBeHidden();
    const host = `e2e-${stamp}.example.com`;
    const editor = page.locator('app-ship-site-editor');
    await editor.getByLabel(/Domain/).fill(host);
    await editor.locator('form').getByRole('button', { name: /^Neu$|^New$/ }).click();
    await expect(page.locator('app-ship-site-editor')).toContainText(host);

    // development preview: the SPA sends X-Tenant-Host and the portal turns into the ship's website
    await page.evaluate(h => localStorage.setItem('tenantHost', h), host);
    await page.goto('/');
    await expect(page.locator('mat-toolbar .brand')).toContainText(`Kairos ${stamp}`);
    await page.getByRole('link', { name: 'Über uns' }).first().click();
    await expect(page.locator('body')).toContainText('Willkommen an Bord');
    await page.evaluate(() => localStorage.removeItem('tenantHost'));
  });

  /** One real login per run (the auth endpoints are rate limited); later tests reuse the stored cookies. */
  let state: Awaited<ReturnType<Page['context']>>['storageState'] extends (...a: never[]) => Promise<infer T> ? T : never;
  async function login(page: Page): Promise<void> {
    if (!state) {
      const r = await page.request.post('/api/auth/login', { data: { email: user.email, password: user.password, rememberMe: true }, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
      expect(r.ok()).toBeTruthy();
      state = await page.context().storageState();
      return;
    }
    await page.context().addCookies(state.cookies);
  }
});
