// The end-to-end suite (README.md here; src/docs/testing.md, "The end-to-end tests"). One spec per demo scenario in
// specs/, named as src/docs/demo/scenarios.md and src/demo/kit/scenarios.json say. Two projects: a desktop (1280 px)
// and a phone (390 px). The installed Chrome (channel chrome), never a downloaded browser.
//
// Tests run in file order on one worker: the scenarios share one store and follow the 15-minute run's order. Each run
// writes to runs/<run>/: screenshots/ (one per step and width), summary.json, report/ (the HTML report) and
// test-results/ (traces of failed steps). The settings come from environment variables (lib/env.ts).
import path from 'node:path';
import { defineConfig } from '@playwright/test';
import { env } from './lib/env';

const blob = process.env.E2E_BLOB_NAME ?? '';

export default defineConfig<object, { width: number; height: number }>({
  testDir: './specs',
  testMatch: /S-\d\d-[a-z0-9-]+\.spec\.ts$/,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  // A step with a side sheet, a check first and an answer takes well under a minute; a slowed "demo play" longer.
  timeout: env.play ? 600_000 : 180_000,
  expect: { timeout: 10_000 },
  outputDir: path.join(env.runDir, 'test-results'),
  globalSetup: './lib/global-setup.ts',
  reporter: [
    [env.play ? 'list' : 'line'],
    blob
      ? ['blob', { outputDir: path.join(env.runDir, 'blob'), fileName: `${blob}.zip` }]
      : ['html', { outputFolder: path.join(env.runDir, 'report'), open: 'never' }],
    ['./lib/summary-reporter.ts'],
  ],
  use: {
    channel: 'chrome',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
  projects: [
    { name: 'desktop-1280', use: { width: 1280, height: 900 } },
    { name: 'phone-390', use: { width: 390, height: 844 } },
  ],
});
