import path from 'node:path'
import { defineConfig, devices } from '@playwright/test'

const port = Number(process.env.AXIS_E2E_PORT ?? 5280)
const baseURL = `http://127.0.0.1:${port}`
const connectionString = process.env.AXIS_E2E_DATABASE
if (!connectionString) {
  throw new Error('AXIS_E2E_DATABASE must hold the PostgreSQL connection string. Run scripts/e2e.sh.')
}
const keycloakUrl = process.env.AXIS_E2E_KEYCLOAK_URL
if (!keycloakUrl) {
  throw new Error('AXIS_E2E_KEYCLOAK_URL must hold the address of Keycloak. Run scripts/e2e.sh.')
}
const purchasingUrl = process.env.AXIS_E2E_PURCHASING_URL
if (!purchasingUrl) {
  throw new Error('AXIS_E2E_PURCHASING_URL must hold the address of the purchasing system stub. Run scripts/e2e.sh.')
}

// scripts/e2e.sh keeps the server output in a log file and the JUnit results with the other suites.
const serverCommand = 'dotnet run --project ../../src/Axis.Server --no-launch-profile --no-build -c Release'
const serverLog = process.env.AXIS_E2E_SERVER_LOG
const quote = (value: string) => `'${value.replaceAll("'", `'\\''`)}'`

// The server compiles and activates both applications in the tenant when it starts: the generic test
// application and the purchase request sample.
const e2eApp = path.resolve(import.meta.dirname, 'fixtures', 'e2e-app')
const purchaseRequests = path.resolve(import.meta.dirname, '..', '..', 'samples', 'apps', 'purchase-requests')
const extensionsPath = path.resolve(import.meta.dirname, '..', '..', 'artifacts', 'extensions')

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
    env: {
      ASPNETCORE_URLS: baseURL,
      // Only Development lets a connector call the stub over http. Every setting the run needs is
      // set here, so it does not depend on which appsettings files the server finds.
      ASPNETCORE_ENVIRONMENT: 'Development',
      ConnectionStrings__Platform: connectionString,
      // The browser reaches the server as 127.0.0.1, so that host is the tenant's.
      Tenants__default__Hosts__0: '127.0.0.1',
      Tenants__default__ConnectionString: connectionString,
      // The development realm and its client, as in appsettings.Development.json.
      Tenants__default__Oidc__Authority: `${keycloakUrl}/realms/axis`,
      Tenants__default__Oidc__ClientId: 'axis',
      Tenants__default__Oidc__ClientSecret: 'axis-development-secret',
      // Endpoint calls need a token for this audience, as in appsettings.Development.json.
      Tenants__default__Oidc__EndpointAudience: 'axis-endpoints',
      // The purchase request sample activates only with its purchasing system bindings. The stub
      // ignores the token. The worker gets the same bindings from scripts/e2e.sh.
      Tenants__default__Bindings__PurchasingSystemUrl__Kind: 'url',
      Tenants__default__Bindings__PurchasingSystemUrl__Value: purchasingUrl,
      Tenants__default__Bindings__PurchasingSystemToken__Kind: 'secret',
      Tenants__default__Bindings__PurchasingSystemToken__Value: 'e2e-token',
      // The sample extension package payment-references, which scripts/build.sh installs. The worker gets
      // the same folder from scripts/e2e.sh.
      Extensions__Path: extensionsPath,
      ActivateOnStartup__0: e2eApp,
      ActivateOnStartup__1: purchaseRequests,
    },
  },
})
