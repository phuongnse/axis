import AxeBuilder from '@axe-core/playwright'
import { chromium, type Browser, type Page } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { startDirectoryServer } from '../directory-server'
import { submitCode, submitPassword } from '../keycloak'
import { required } from '../server-settings'
import { addRealmAccount, type RealmAccount } from '../tenant-realm'
import { liveTotp, totpSecret } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'console-made'
const tenantHost = `${tenant}.localhost`
const consoleUrl = 'https://console.localhost/'

let server: Awaited<ReturnType<typeof startDirectoryServer>>
let browser: Browser
let member: RealmAccount

test.beforeAll(async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', tenantHost, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  member = await addRealmAccount(tenant, { email: `member@${tenant}.test` })

  // Offset 3, because the tenant create spec, the terms spec and the read-only spec serve their tenants on offsets 0 to 2.
  server = await startDirectoryServer(3, {
    Cell__Id: 'vn-1',
    Cell__Region: 'vn',
    Cell__TenantDomain: 'localhost',
    Cell__ConsoleHost: 'console.localhost',
    Cell__ConsoleClientSecret: 'axis-cell-console-development-secret',
    IdentityProvider__Url: `${required('AXIS_E2E_KEYCLOAK_URL', 'the address of Keycloak')}/`,
  })
  browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP *.localhost:443 127.0.0.1:${server.httpsPort}`],
  })
})

test.afterAll(async () => {
  await browser?.close()
  await server?.stop()
})

async function expectNoViolations(page: Page) {
  const { violations } = await new AxeBuilder({ page }).analyze()
  expect(violations).toEqual([])
}

function navigate(page: Page, name: string) {
  return page.getByRole('navigation').getByRole('link', { name, exact: true }).click()
}

test('a platform operator signs in with a password and a code, sees the tenants and opens one', async () => {
  const page = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()

  // The SPA sends a person with no session to the console's sign-in, which sends them to the operator realm.
  await page.goto(consoleUrl)
  await submitPassword(page, 'cell-operator', 'axis')
  await submitCode(page, await liveTotp(totpSecret('cell-operator', 'axis-operators')))
  await page.waitForURL(consoleUrl)
  await expect(page.getByTestId('cell-account')).toHaveText('Cell Operator')

  await expect(page.getByTestId('cell-overview')).toBeVisible()
  await expectNoViolations(page)

  await navigate(page, 'Jobs')
  await expect(page.getByTestId('cell-jobs')).toBeVisible()
  await expectNoViolations(page)

  await navigate(page, 'Tenants')
  await page.getByRole('searchbox', { name: 'Search by ID or host', exact: true }).fill(tenant)
  const link = page.getByTestId('cell-tenants').getByRole('link', { name: tenant, exact: true })
  await expect(link).toBeVisible()
  await expectNoViolations(page)

  await link.click()
  await expect(page.getByTestId('cell-tenant')).toContainText(tenantHost)
  await expectNoViolations(page)
})

test('a user of a tenant is refused on the console host', async () => {
  const page = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()

  // The member signs in on the tenant's own host first, so a tenant session exists in this browser.
  await page.goto(`https://${tenantHost}/api/auth/sign-in?returnUrl=/`)
  await submitPassword(page, member.username, member.password)
  await submitCode(page, await liveTotp(member.secret))
  await page.waitForURL(`https://${tenantHost}/`)

  // The console host does not count that session, and the operator realm holds no account of the member.
  await page.goto(consoleUrl)
  await page.waitForURL((url) => url.pathname.includes('/realms/axis-operators/'))
  const keycloakOrigin = new URL(page.url()).origin
  await submitPassword(page, member.username, member.password)
  await expect(page.getByRole('alert')).toBeVisible()
  expect(new URL(page.url()).origin).toBe(keycloakOrigin)

  // page.goto, not the context's request, because Node requests ignore the host resolver rules.
  const session = await page.goto('https://console.localhost/api/cell/session')
  expect(session?.status()).toBe(401)
})
