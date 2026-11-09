import { chromium } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { startDirectoryServer } from '../directory-server'
import { submitCode, submitPassword } from '../keycloak'
import { addRealmAccount } from '../tenant-realm'
import { liveTotp } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(180_000)
test.describe.configure({ mode: 'serial' })

// A tenant of its own, so that the shared E2E tenant never asks for terms.
const tenant = 'terms-made'
const host = `${tenant}.localhost`
const termsUrl = `https://${tenant}.test/terms`
const privacyUrl = `https://${tenant}.test/privacy`

test('a first sign-in on a tenant with terms asks for them, asks again for an unticked box, and Decline ends on the declined card', async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', host, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  // The account has no required actions, so only the terms version that is set next gives it the terms step.
  const account = await addRealmAccount(tenant, { email: `person@${tenant}.test` })
  const legal = await runAdmin([
    'tenant', 'legal', 'set', tenant,
    '--terms-url', termsUrl,
    '--privacy-url', privacyUrl,
    '--terms-version', '2026-11-01',
  ])
  expect(legal.status, `${legal.stdout}\n${legal.stderr}`).toBe(0)

  // Offset 1, because the tenant create spec serves its tenant on the base port and may run at the same time.
  const server = await startDirectoryServer(1)
  // The browser sends the host on port 443 to the server's https port. Node's own requests would ignore this rule.
  const browser = await chromium.launch({
    args: [`--host-resolver-rules=MAP ${host}:443 127.0.0.1:${server.httpsPort}`],
  })
  try {
    const page = await (await browser.newContext({ ignoreHTTPSErrors: true })).newPage()

    await page.goto(`https://${host}/api/auth/sign-in?returnUrl=/`)
    await submitPassword(page, account.username, account.password)
    await submitCode(page, await liveTotp(account.secret))

    const checkbox = page.getByRole('checkbox')
    await expect(checkbox).toBeVisible()
    await expect(page.getByRole('link', { name: 'Terms of service', exact: true })).toHaveAttribute('href', termsUrl)
    await expect(page.getByRole('link', { name: 'Privacy policy', exact: true })).toHaveAttribute('href', privacyUrl)

    await page.getByRole('button', { name: 'Continue', exact: true }).click()
    await expect(page.getByText('Accept the terms to continue.', { exact: true })).toBeVisible()
    await expect(checkbox).not.toBeChecked()

    await page.getByRole('button', { name: 'Decline and sign out', exact: true }).click()
    await page.waitForURL(`https://${host}/?signIn=declined`)
    await expect(page.getByRole('alert').filter({ hasText: 'You need to accept the terms to sign in.' })).toBeVisible()
    await expect(page.getByRole('main').getByRole('button', { name: 'Sign in', exact: true })).toBeVisible()
  } finally {
    await browser.close()
    await server.stop()
  }
})
