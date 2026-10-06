import { defineConfig, devices } from '@playwright/test'

const port = Number(process.env.AXIS_E2E_PORT ?? 5280)
const baseURL = `http://127.0.0.1:${port}`
const connectionString = process.env.AXIS_E2E_DATABASE
if (!connectionString) {
  throw new Error('AXIS_E2E_DATABASE must hold the PostgreSQL connection string. Run scripts/e2e.sh.')
}

// scripts/e2e.sh keeps the server output in a log file and the JUnit results with the other suites.
const serverCommand = 'dotnet run --project ../../src/Axis.Server --no-launch-profile --no-build -c Release'
const serverLog = process.env.AXIS_E2E_SERVER_LOG
const quote = (value: string) => `'${value.replaceAll("'", `'\\''`)}'`

export default defineConfig({
  testDir: './specs',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [
    ['list'],
    ['html', { open: 'never' }],
    ['junit', { outputFile: process.env.AXIS_E2E_JUNIT ?? 'test-results/junit.xml' }],
  ],
  use: {
    baseURL,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    // Playwright runs the command through a shell, so it can redirect the output.
    command: serverLog ? `${serverCommand} >> ${quote(serverLog)} 2>&1` : serverCommand,
    url: `${baseURL}/health/ready`,
    timeout: 120_000,
    reuseExistingServer: false,
    env: {
      ASPNETCORE_URLS: baseURL,
      ASPNETCORE_ENVIRONMENT: 'Production',
      ConnectionStrings__Platform: connectionString,
      // The browser reaches the server as 127.0.0.1, so that host is the tenant's.
      Tenants__default__Hosts__0: '127.0.0.1',
      Tenants__default__ConnectionString: connectionString,
    },
  },
})
