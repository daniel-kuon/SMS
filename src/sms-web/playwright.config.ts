import { defineConfig } from '@playwright/test';

/**
 * End-to-end smoke tests against the running AppHost (web on http://localhost:5173, Mailpit on http://localhost:8025).
 * Run: npm run e2e   (AppHost must be up; the API seeds harbours and a dev admin).
 */
export default defineConfig({
  testDir: 'e2e',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  retries: 0,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:5173',
    ignoreHTTPSErrors: true,
    locale: 'de-DE',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
});
