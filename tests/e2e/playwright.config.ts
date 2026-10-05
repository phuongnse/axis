import { defineConfig, devices } from '@playwright/test'

const port = Number(process.env.AXIS_E2E_PORT ?? 5280)
const baseURL = `http://127.0.0.1:${port}`
const connectionString = process.env.AXIS_E2E_DATABASE
if (!connectionString) {
  throw new Error('AXIS_E2E_DATABASE must hold the PostgreSQL connection string. Run scripts/e2e.sh.')
}

export default defineConfig({
  testDir: './specs',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }], ['junit', { outputFile: 'test-results/junit.xml' }]],
  use: {
    baseURL,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'dotnet run --project ../../src/Axis.Server --no-launch-profile --no-build -c Release',
    url: `${baseURL}/health/ready`,
    timeout: 120_000,
    reuseExistingServer: false,
    env: {
      ASPNETCORE_URLS: baseURL,
      ASPNETCORE_ENVIRONMENT: 'Production',
      ConnectionStrings__Platform: connectionString,
    },
  },
})
