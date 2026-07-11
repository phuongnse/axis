import { defineConfig, devices } from '@playwright/test';

const defaultWebServerHost = '127.0.0.1';
const defaultWebServerPort = '4173';
const baseURL =
  process.env.E2E_BASE_URL ?? `http://${defaultWebServerHost}:${defaultWebServerPort}`;
const skipWebServer = process.env.E2E_SKIP_WEB_SERVER === '1';
const browserHome = process.env.E2E_BROWSER_HOME;
const outputDir = process.env.E2E_OUTPUT_DIR ?? './test-results/e2e';
const reportDir = process.env.E2E_REPORT_DIR ?? './playwright-report';
const baseUrlParts = new URL(baseURL);
const webServerHost =
  process.env.E2E_WEB_SERVER_HOST ??
  (baseUrlParts.hostname === 'localhost' ? defaultWebServerHost : baseUrlParts.hostname);
const webServerPort =
  process.env.E2E_WEB_SERVER_PORT ??
  (baseUrlParts.port || (baseUrlParts.protocol === 'https:' ? '443' : '80'));

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.pw.ts',
  outputDir,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: [['list'], ['html', { outputFolder: reportDir, open: 'never' }]],
  use: {
    baseURL,
    ...(browserHome ? { launchOptions: { env: { HOME: browserHome } } } : {}),
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  ...(skipWebServer
    ? {}
    : {
        webServer: {
          command: `npm run dev -- --host ${webServerHost} --port ${webServerPort} --strictPort`,
          reuseExistingServer: !process.env.CI,
          timeout: 120_000,
          url: baseURL,
        },
      }),
});
