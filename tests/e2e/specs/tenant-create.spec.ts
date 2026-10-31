import { spawnSync } from 'node:child_process'
import { chromium } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { startDirectoryServer } from '../directory-server'
import { waitForDialog } from '../dialogs'
import { addAuthenticator, submitCode, submitPassword } from '../keycloak'
import { addRealmAccount, realmClientIds, setRealmPassword, userCredentialTypes } from '../tenant-realm'
import { nextTotp, totp } from '../totp'

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
  return psql('axis', `SELECT status FROM platform.tenants WHERE id = '${tenant}'`)[0] ?? ''
}

/** The actor of every audit record of `action` about the user `userId`, from the tenant's own database. */
function auditActors(action: string, userId: string): string[] {
  return psql(
    `axis_${tenant}`,
    `SELECT actor FROM axis.audit_records WHERE action = '${action}' AND details->>'userId' = '${userId}'`,
  )
}

/** The lines that `sql` returns from `database` in the E2E PostgreSQL container. */
function psql(database: string, sql: string): string[] {
  const container = process.env.AXIS_E2E_POSTGRES_CONTAINER
  if (!container) {
    throw new Error('AXIS_E2E_POSTGRES_CONTAINER is not set. Run the E2E tests with scripts/e2e.sh.')
  }
  const result = spawnSync('docker', ['exec', container, 'psql', '-U', 'axis', '-d', database, '-tAc', sql], {
    encoding: 'utf8',
  })
  if (result.status !== 0) {
    throw new Error(`psql failed with status ${result.status}: ${result.stderr}`)
  }
  return result.stdout.split('\n').filter((line) => line !== '')
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

test('the administrator disables an account on the users page, its next sign-in is refused, and after enable it signs in again', async () => {
  test.setTimeout(240_000)
  const host = `${tenant}.localhost`
  const signIn = `https://${host}/api/auth/sign-in?returnUrl=/`
  const memberEmail = `member@${tenant}.test`
  const member = await addRealmAccount(tenant, { email: memberEmail })
  const server = await startDirectoryServer()
  const browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })
  try {
    const memberPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
    const adminPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()

    // A person who is not an administrator is told that the users page is not theirs.
    await memberPage.goto(signIn)
    await submitPassword(memberPage, member.username, member.password)
    const firstCode = totp(member.secret)
    await submitCode(memberPage, firstCode)
    await memberPage.waitForURL(`https://${host}/`)
    await memberPage.goto(`https://${host}/users`)
    await expect(memberPage.getByTestId('users-forbidden')).toBeVisible()

    // The administrator that tenant create invited signs in, adds an authenticator and disables the member.
    const firstAdmin = await setRealmPassword(tenant, `first-admin@${tenant}.test`)
    await adminPage.goto(signIn)
    await submitPassword(adminPage, firstAdmin.username, firstAdmin.password)
    await addAuthenticator(adminPage)
    await adminPage.waitForURL(`https://${host}/`)
    await expect(adminPage.getByRole('banner')).toBeVisible()
    await adminPage.goto(`https://${host}/users`)

    const main = adminPage.getByRole('main')
    async function findMember() {
      await main.getByRole('searchbox', { name: 'Search by name or email', exact: true }).fill(memberEmail)
      await adminPage.keyboard.press('Enter')
      const row = main.getByRole('row').filter({ hasText: memberEmail })
      await expect(row).toHaveCount(1)
      return row
    }
    await (await findMember()).getByRole('button', { name: 'Disable', exact: true }).click()
    const disableDialog = await waitForDialog(adminPage, 'Disable this person?')
    await disableDialog.getByRole('button', { name: 'Disable', exact: true }).click()
    await expect(adminPage.getByTestId('users-done')).toHaveText('The person was disabled.')
    await expect(await findMember()).toContainText('Disabled')
    expect(auditActors('user.disabled', member.id)).toEqual([firstAdmin.id])

    // The next sign-in of the disabled account stops at the sign-in page with an error.
    await memberPage.goto(signIn)
    await submitPassword(memberPage, member.username, member.password)
    await expect(memberPage.getByRole('alert')).toBeVisible()
    expect(new URL(memberPage.url()).origin).toBe(new URL(process.env.AXIS_E2E_KEYCLOAK_URL ?? '').origin)

    await (await findMember()).getByRole('button', { name: 'Enable', exact: true }).click()
    const enableDialog = await waitForDialog(adminPage, 'Enable this person?')
    await enableDialog.getByRole('button', { name: 'Enable', exact: true }).click()
    await expect(adminPage.getByTestId('users-done')).toHaveText('The person was enabled.')

    // Keycloak refuses a code that was used in its period, so the second code waits for the next one when needed.
    await memberPage.goto(signIn)
    await submitPassword(memberPage, member.username, member.password)
    await submitCode(memberPage, await nextTotp(member.secret, firstCode))
    await memberPage.waitForURL(`https://${host}/`)
    await expect(memberPage.getByRole('banner')).toBeVisible()
  } finally {
    await browser.close()
    await server.stop()
  }
})

test('after tenant host add a person signs in on the added host and reaches the home page', async () => {
  const host = `${tenant}-second.localhost`
  const added = await runAdmin(['tenant', 'host', 'add', tenant, host])
  expect(added.status, `${added.stdout}\n${added.stderr}`).toBe(0)

  const account = await addRealmAccount(tenant, { email: `second-host@${tenant}.test` })
  // The server reads the tenant directory when it starts, so this one serves the added host.
  const server = await startDirectoryServer()
  const browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })
  try {
    const page = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()

    await page.goto(`https://${host}/api/auth/sign-in?returnUrl=/`)
    await submitPassword(page, account.username, account.password)
    await submitCode(page, totp(account.secret))
    await page.waitForURL(`https://${host}/`)
    await expect(page.getByRole('banner')).toBeVisible()
  } finally {
    await browser.close()
    await server.stop()
  }
})
