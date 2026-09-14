import { defineConfig, devices } from '@playwright/test';

const uiPort = 4200;
const baseURL = `http://localhost:${uiPort}`;

/**
 * End-to-end tests run against the whole stack: real browser → Angular → the real API → the live
 * Hacker News feed. Nothing is stubbed.
 *
 * The Angular dev server is started here; the **API must already be running**:
 *   cd backend && dotnet run --project src/HackerNews.Api
 *
 * Specs skip themselves with a clear message if the API is unreachable, so a forgotten backend
 * reads as "skipped", never as a false pass.
 */
export default defineConfig({
  testDir: './e2e',
  outputDir: './e2e/.results',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  // The live feed is a third party: one retry absorbs a transient blip without hiding a real break.
  retries: process.env['CI'] ? 1 : 0,
  workers: process.env['CI'] ? 1 : undefined,
  reporter: process.env['CI'] ? [['github'], ['list']] : [['list']],

  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },

  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],

  webServer: {
    command: 'npm start',
    url: baseURL,
    // Locally this reuses the dev server you already have running.
    reuseExistingServer: !process.env['CI'],
    timeout: 180_000,
    stdout: 'ignore',
    stderr: 'pipe',
  },
});
