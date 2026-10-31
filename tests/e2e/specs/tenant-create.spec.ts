import { spawnSync } from 'node:child_process'
import { chromium } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { startDirectoryServer } from '../directory-server'
import { submitCode, submitPassword } from '../keycloak'
import { addRealmAccount, realmClientIds, userCredentialTypes } from '../tenant-realm'
import { totp } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(180_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'operator-made'
const createArgs = [
  'tenant', 'create', tenant,
  '--host', `${tenant}.localhost`,
  '--administrator', `first-admin@${tenant}.test`,
]

/** The status of the tenant's entry in the tenant directory, from the platform database in the E2E PostgreSQL container. */
function directoryStatus(): string {
  const container = process.env.AXIS_E2E_POSTGRES_CONTAINER
  if (!container) {
    throw new Error('AXIS_E2E_POSTGRES_CONTAINER is not set. Run the E2E tests with scripts/e2e.sh.')
  }
  const result = spawnSync(
    'docker',
    ['exec', container, 'psql', '-U', 'axis', '-d', 'axis', '-tAc', `SELECT status FROM platform.tenants WHERE id = '${tenant}'`],
    { encoding: 'utf8' },
  )
  if (result.status !== 0) {
    throw new Error(`psql failed with status ${result.status}: ${result.stderr}`)
  }
  return result.stdout.trim()
}

test('tenant create makes an active tenant with a realm that has its three clients', async () => {
  const result = await runAdmin(createArgs)

  expect(result.status, `${result.stdout}\n${result.stderr}`).toBe(0)
  expect(directoryStatus()).toBe('active')
  expect(await realmClientIds(tenant)).toEqual(expect.arrayContaining(['axis', 'axis-activation', 'axis-users']))
})

test('tenant create refuses a tenant that exists and leaves its entry active', async () => {
  const result = await runAdmin(createArgs)

  expect(result.status, `${result.stdout}\n${result.stderr}`).toBe(1)
  expect(result.stdout).toContain(`${tenant} entry failed: the tenant already exists`)
  expect(directoryStatus()).toBe('active')
})

test('an account added to the realm has a password and an authenticator', async () => {
  const account = await addRealmAccount(tenant, { email: `agent@${tenant}.test` })

  expect(await userCredentialTypes(tenant, account.id)).toEqual(expect.arrayContaining(['password', 'otp']))
})

test('a person signs in over https on the host of the new tenant and reaches the home page', async () => {
  const account = await addRealmAccount(tenant, { email: `person@${tenant}.test` })
  const host = `${tenant}.localhost`
  const server = await startDirectoryServer()
  // The browser sends the host on port 443 to the server's https port. Node's own requests would ignore this rule.
  const browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: true })
    const page = await context.newPage()

    await page.goto(`https://${host}/api/auth/sign-in?returnUrl=/`)
    await submitPassword(page, account.username, account.password)
    await submitCode(page, totp(account.secret))
    await page.waitForURL(`https://${host}/`)
    await expect(page.getByRole('banner')).toBeVisible()

    const me = await page.goto(`https://${host}/api/me`)
    expect(me?.status()).toBe(200)
    expect(await me?.json()).toMatchObject({ id: account.id, displayName: 'Test Account' })
  } finally {
    await browser.close()
    await server.stop()
  }
})
