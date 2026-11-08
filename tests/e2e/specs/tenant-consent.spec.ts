import { createHash, randomBytes } from 'node:crypto'
import type { Page } from '@playwright/test'
import { expect, test } from '../test'
import { runAdmin } from '../admin-tool'
import { submitCode, submitPassword } from '../keycloak'
import { addConsentClient, addRealmAccount } from '../tenant-realm'
import { liveTotp } from '../totp'

// tenant create makes a database, two roles and a realm, and the realm step waits on Keycloak.
test.setTimeout(180_000)
test.describe.configure({ mode: 'serial' })

// A tenant of its own, whose realm has one client that requires consent.
const tenant = 'consent-made'
const clientId = 'consent-client'
const clientName = 'Draft helper'
const homeUrl = `https://${tenant}.test/home`
const redirectUri = `https://${tenant}.test/callback`

test.beforeAll(async () => {
  const created = await runAdmin(['tenant', 'create', tenant, '--host', `${tenant}.localhost`, '--administrator', `first-admin@${tenant}.test`])
  expect(created.status, `${created.stdout}\n${created.stderr}`).toBe(0)
  await addConsentClient(tenant, { clientId, name: clientName, baseUrl: homeUrl, redirectUri })
})

/** The address that the client's redirect address is requested with once the person has answered. Nothing serves it, so the page fails to load and the test reads the request. */
async function callbackAfter(page: Page, answer: () => Promise<void>): Promise<URL> {
  const request = page.waitForRequest((candidate) => candidate.url().startsWith(redirectUri))
  await answer()
  return new URL((await request).url())
}

/** Signs a new account in through the consent client with the scope `axis:authoring`, and stops at the consent screen. */
async function signInToConsent(page: Page, name: string) {
  // Keycloak remembers consent for each person, so each test signs its own account in.
  const account = await addRealmAccount(tenant, { email: `${name}@${tenant}.test` })
  const verifier = randomBytes(32).toString('base64url')
  const challenge = createHash('sha256').update(verifier).digest('base64url')
  const query = new URLSearchParams({
    client_id: clientId,
    redirect_uri: redirectUri,
    response_type: 'code',
    scope: 'openid axis:authoring',
    state: 's',
    code_challenge: challenge,
    code_challenge_method: 'S256',
  })
  await page.goto(`${process.env.AXIS_E2E_KEYCLOAK_URL}/realms/${tenant}/protocol/openid-connect/auth?${query}`)
  await submitPassword(page, account.username, account.password)
  await submitCode(page, await liveTotp(account.secret))
}

test('the consent screen names the client, links its home page and lists the axis:authoring text, and Allow sends the client a code', async ({ page }) => {
  await signInToConsent(page, 'allow')

  await expect(page.getByText(clientName, { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: homeUrl, exact: true })).toHaveAttribute('href', homeUrl)
  await expect(
    page.getByText('Read and change drafts of your applications, and accept them when your roles allow', { exact: true }),
  ).toBeVisible()

  const callback = await callbackAfter(page, () => page.getByRole('button', { name: 'Allow', exact: true }).click())
  expect(callback.searchParams.get('code')).toBeTruthy()
  expect(callback.searchParams.get('error')).toBeNull()
})

test('Deny sends the client back with access_denied and no code', async ({ page }) => {
  await signInToConsent(page, 'deny')

  const callback = await callbackAfter(page, () => page.getByRole('button', { name: 'Deny', exact: true }).click())
  expect(callback.searchParams.get('error')).toBe('access_denied')
  expect(callback.searchParams.get('code')).toBeNull()
})
