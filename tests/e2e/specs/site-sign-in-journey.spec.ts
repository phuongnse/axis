import type { Browser, Page } from '@playwright/test'
import { expect, test } from '../test'
import { logInAtKeycloak, signInUsername } from '../keycloak'

// A site lists the sign-ins it allows. Purchasing allows accounts and the company provider, which brokers to the
// `company` realm that plays the organization's own identity provider. The Service desk allows only accounts, and the
// callback refuses a person who arrives through a provider the site does not list, even when the provider is forced with
// `kc_idp_hint`. See docs/reference/identity-provider.md#sites-and-sign-in.
//
// The three sign-ins run one after the other, each in a fresh browser context. Two parallel broker sign-ins as Pat could
// race to create the same user in the `axis` realm on their first login.

const companyProvider = 'oidc-companyissuer'

/** Runs `body` in a fresh browser context and closes the context afterwards. */
async function inFreshContext(browser: Browser, baseURL: string, body: (page: Page) => Promise<void>) {
  const context = await browser.newContext({ baseURL })
  try {
    await body(await context.newPage())
  } finally {
    await context.close()
  }
}

/** Opens `address` while signed out and clicks **Sign in** on the prompt. */
async function startSignIn(page: Page, address: string) {
  await page.goto(address)
  await page.getByTestId('sign-in-prompt').getByRole('button', { name: 'Sign in', exact: true }).click()
}

/** Signs Pat in on the default Keycloak login page of the `company` realm. Pat has no code. */
async function logInAtCompany(page: Page) {
  await page.locator('#username').fill('pat')
  await page.locator('#password').fill('axis')
  await page.locator('#kc-login').click()
}

test('Pat signs in to Purchasing through the organization, an account signs in to the Service desk, and the Service desk refuses the organization', async ({
  browser,
  baseURL,
}) => {
  const base = baseURL!

  await inFreshContext(browser, base, async (page) => {
    await startSignIn(page, '/purchasing')
    await page.getByRole('button', { name: 'Continue with Example Company', exact: true }).click()
    await logInAtCompany(page)

    await expect(page).toHaveURL((url) => url.href.startsWith(`${base}/purchasing`))
    // The realm keeps the whole display name as the first name and may add the provider's family name after it.
    await expect(page.getByRole('banner').getByRole('button', { name: /^Pat Example\b/ })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Purchase requests', exact: true })).toBeVisible()
    // Only the employee role reads this data source, so the answer shows that the groups claim gave Pat that role.
    const rows = await page.request.get('/api/apps/PurchaseRequests/data-sources/PurchaseRequestList/rows?pageSize=1')
    expect(rows.status()).toBe(200)
  })

  await inFreshContext(browser, base, async (page) => {
    await startSignIn(page, '/service-desk')
    await expect(signInUsername(page)).toBeVisible()
    await expect(page.getByRole('button', { name: /^Continue with/ })).toHaveCount(0)
    await logInAtKeycloak(page, 'agent')

    await expect(page).toHaveURL((url) => url.href.startsWith(`${base}/service-desk`))
    await expect(page.getByRole('banner').getByRole('button', { name: 'Agent User', exact: true })).toBeVisible()
  })

  await inFreshContext(browser, base, async (page) => {
    await startSignIn(page, '/service-desk')
    await expect(signInUsername(page)).toBeVisible()
    const authorize = new URL(page.url())
    expect(authorize.pathname).toMatch(/\/protocol\/openid-connect\/auth$/)
    expect(authorize.searchParams.get('client_id')).toBe('site-service-desk')
    expect(authorize.searchParams.has('kc_idp_hint')).toBe(false)

    authorize.searchParams.set('kc_idp_hint', companyProvider)
    await page.goto(authorize.toString())
    await logInAtCompany(page)

    await expect(page).toHaveURL(`${base}/?signIn=failed`)
    await expect(page.getByRole('alert').filter({ hasText: 'Sign-in did not complete.' })).toBeVisible()
  })
})
