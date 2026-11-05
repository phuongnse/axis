import { chromium } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { startDirectoryServer } from '../directory-server'
import { addAuthenticator, submitCode, submitPassword } from '../keycloak'
import { addRealmAccount, setRealmPassword } from '../tenant-realm'
import { liveTotp } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const tenant = 'read-only-made'
const host = `${tenant}.localhost`
const signIn = `https://${host}/api/auth/sign-in?returnUrl=/`
const billing = `https://account.localhost/tenants/${tenant}`

test('a read-only tenant of a cell shows its banner to everyone, and the billing link only to an administrator', async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', host, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  const member = await addRealmAccount(tenant, { email: `member@${tenant}.test` })
  const readOnly = await runAdmin(['tenant', 'read-only', tenant])
  expect(readOnly.status, `${readOnly.stdout}\n${readOnly.stderr}`).toBe(0)

  // Offset 2, because the tenant create spec and the terms spec serve their tenants on offsets 0 and 1.
  const server = await startDirectoryServer(2, { Cell__Id: 'vn-1', Cell__Region: 'vn', Cell__TenantDomain: 'localhost' })
  const browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })
  try {
    const memberPage = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()
    const adminContext = await browser.newContext({ ignoreHTTPSErrors: true })
    const adminPage = await adminContext.newPage()

    // A member sees the banner and no billing link.
    await memberPage.goto(signIn)
    await submitPassword(memberPage, member.username, member.password)
    await submitCode(memberPage, await liveTotp(member.secret))
    await memberPage.waitForURL(`https://${host}/`)
    await expect(memberPage.getByRole('alert').filter({ hasText: 'This workspace is read-only.' })).toBeVisible()
    await expect(memberPage.getByRole('link', { name: 'Open billing', exact: true })).toHaveCount(0)

    // The administrator that tenant create invited sees the banner with the link, which opens the billing portal.
    const firstAdmin = await setRealmPassword(tenant, `first-admin@${tenant}.test`)
    await adminPage.goto(signIn)
    await submitPassword(adminPage, firstAdmin.username, firstAdmin.password)
    await addAuthenticator(adminPage)
    await adminPage.waitForURL(`https://${host}/`)
    // The portal is not part of the cell, so a stub page stands in for it.
    await adminContext.route('https://account.localhost/**', (route) =>
      route.fulfill({ contentType: 'text/html', body: '<title>Billing</title>' }),
    )
    const banner = adminPage.getByRole('alert').filter({ hasText: 'This workspace is read-only.' })
    await expect(banner).toBeVisible()
    await banner.getByRole('link', { name: 'Open billing', exact: true }).click()
    await adminPage.waitForURL(billing)
  } finally {
    await browser.close()
    await server.stop()
  }
})
