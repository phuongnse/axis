import { defineConfig, devices } from '@playwright/test'
import { e2eApp, leaveRequests, purchaseRequests, serverEnv } from './server-settings'

const port = Number(process.env.AXIS_E2E_PORT ?? 5280)
const baseURL = `http://127.0.0.1:${port}`
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
  projects: [
    // Signs the sample users in and saves their sessions. Journeys load them with useSession.
    { name: 'setup', testMatch: /.*\.setup\.ts/, use: { ...devices['Desktop Chrome'] } },
    { name: 'chromium', use: { ...devices['Desktop Chrome'] }, dependencies: ['setup'] },
  ],
  webServer: {
    // Playwright runs the command through a shell, so it can redirect the output.
    command: serverLog ? `${serverCommand} >> ${quote(serverLog)} 2>&1` : serverCommand,
    url: `${baseURL}/health/ready`,
    timeout: 120_000,
    reuseExistingServer: false,
    // The server compiles and activates all three applications in the tenant when it starts: the generic
    // test application, the purchase request sample and the leave request sample. Index 1 replaces the
    // entry of appsettings.Development.json, so the leave request sample is listed again at index 2.
    env: {
      ...serverEnv(baseURL),
      ActivateOnStartup__0: e2eApp,
      ActivateOnStartup__1: purchaseRequests,
      ActivateOnStartup__2: leaveRequests,
    },
  },
})
