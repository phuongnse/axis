import { defineConfig, devices } from '@playwright/test'
import { e2eApp, journeyServerEnv, journeyURL, leaveRequests, purchaseRequests, serverEnv, serviceDesk } from './server-settings'

const port = Number(process.env.AXIS_E2E_PORT ?? 5280)
const baseURL = `http://127.0.0.1:${port}`
// scripts/e2e.sh keeps the server output in a log file and the JUnit results with the other suites.
const serverCommand = 'dotnet run --project ../../src/Axis.Server --no-launch-profile --no-build -c Release'
const serverLog = process.env.AXIS_E2E_SERVER_LOG
const journeyServerLog = process.env.AXIS_E2E_JOURNEY_SERVER_LOG
const quote = (value: string) => `'${value.replaceAll("'", `'\\''`)}'`

// Playwright runs the command through a shell, and a shell drops an environment variable whose name has a
// hyphen, such as Tenants__default-preview__ConnectionString. Those go through env in the command instead.
const environment = {
  ...serverEnv(baseURL),
  ActivateOnStartup__0: e2eApp,
  ActivateOnStartup__1: purchaseRequests,
  ActivateOnStartup__2: leaveRequests,
  ActivateOnStartup__3: serviceDesk,
}
const isShellName = (name: string) => /^[A-Za-z_][A-Za-z0-9_]*$/.test(name)
const hyphenated = Object.entries(environment).filter(([name]) => !isShellName(name))
const command = hyphenated.length
  ? `env ${hyphenated.map(([name, value]) => quote(`${name}=${value}`)).join(' ')} ${serverCommand}`
  : serverCommand

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
  // Playwright starts the entries in order and waits for each, so the two servers never migrate the shared
  // platform database at the same time.
  webServer: [
    {
      // Playwright runs the command through a shell, so it can redirect the output.
      command: serverLog ? `${command} >> ${quote(serverLog)} 2>&1` : command,
      url: `${baseURL}/health/ready`,
      timeout: 120_000,
      reuseExistingServer: false,
      // The server compiles and activates all four applications in the tenant when it starts: the generic
      // test application, the purchase request, leave request and service desk samples. Index 1 replaces the
      // entry of appsettings.Development.json, so the other samples are listed again at indexes 2 and 3.
      env: Object.fromEntries(Object.entries(environment).filter(([name]) => isShellName(name))),
    },
    {
      // The journey server serves only the journey tenant, with no application active at start.
      command: journeyServerLog ? `${serverCommand} >> ${quote(journeyServerLog)} 2>&1` : serverCommand,
      url: `${journeyURL}/health/ready`,
      timeout: 120_000,
      reuseExistingServer: false,
      env: journeyServerEnv(),
    },
  ],
})
