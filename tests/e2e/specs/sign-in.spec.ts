import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { chooseTheme } from '../shell'
import { logInAtKeycloak, signInUsername } from '../keycloak'

// Opening the platform home page without a session goes straight to the identity provider's sign-in page. A card
// shows only after a refused sign-in and after sign-out. Every application page needs a session and shows the
// sign-in prompt, and only a click on a Sign in button there starts sign-in.

/** Signs in as the requester on the Keycloak login page, which the home page opens at once without a session. */
async function signInAsAnna(page: Page) {
  await page.goto('/')
  await logInAtKeycloak(page, 'requester')
}

// Each test has its own browser context, so the session cookie does not reach other specs.
test('a person signs in on the Keycloak login page, returns to the home page, and sees the name in light and dark mode', async ({
  page,
  baseURL,
}) => {
  await signInAsAnna(page)

  await expect(page).toHaveURL(`${baseURL}/`)
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await chooseTheme(page, 'Dark')
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByRole('banner').getByRole('button', { name: 'Requester User', exact: true })).toBeVisible()
  }
})

test('opening the home page without a session lands on the Keycloak sign-in page', async ({ page }) => {
  await page.goto('/')

  await expect(signInUsername(page)).toBeVisible()
  expect(new URL(page.url()).origin).toBe(new URL(process.env.AXIS_E2E_KEYCLOAK_URL!).origin)
})

test('after signing out from the header menu, no identity provider page shows, the current user endpoint answers 401 and the signed-out card can sign in again', async ({
  page,
  baseURL,
}) => {
  await signInAsAnna(page)
  const banner = page.getByRole('banner')
  await banner.getByRole('button', { name: 'Requester User', exact: true }).click()
  // The server ends the identity provider's session itself, so the browser never visits Keycloak to sign out.
  const visited: string[] = []
  page.on('framenavigated', (frame) => {
    if (frame === page.mainFrame()) {
      visited.push(frame.url())
    }
  })
  await page.getByRole('menuitem', { name: 'Sign out', exact: true }).click()

  const signedOut = page.getByRole('main').getByText('You have signed out', { exact: true })
  await page.waitForURL(`${baseURL}/?signedOut`)
  const keycloakOrigin = new URL(process.env.AXIS_E2E_KEYCLOAK_URL!).origin
  expect(visited.filter((url) => new URL(url).origin === keycloakOrigin)).toEqual([])
  expect((await page.request.get('/api/me')).status()).toBe(401)
  await expect(signedOut).toBeVisible()
  await expect(page.getByText('You can close this page, or sign in again.', { exact: true })).toBeVisible()

  await page.getByRole('main').getByRole('button', { name: 'Sign in again', exact: true }).click()
  // Keycloak keeps the language of the person who signed out in a cookie, and may show its page in Vietnamese.
  const gear = page.getByRole('button', { name: 'Ngôn ngữ và giao diện', exact: true })
  await expect(signInUsername(page).or(gear)).toBeVisible()
  if (await gear.isVisible()) {
    await gear.click()
    await page.getByRole('menuitemradio', { name: 'Tiếng Anh (English)', exact: true }).click()
    await expect(signInUsername(page)).toBeVisible()
  }
  await logInAtKeycloak(page, 'requester')

  await expect(page).toHaveURL(`${baseURL}/`)
  await expect(banner.getByRole('button', { name: 'Requester User', exact: true })).toBeVisible()
})

test('a sign-in that the identity provider refuses lands on the home page with a message and can sign in again', async ({
  page,
  baseURL,
}) => {
  const banner = page.getByRole('banner')
  const signIn = page.getByRole('main').getByRole('button', { name: 'Sign in', exact: true })
  await page.goto('/')
  await signInUsername(page).waitFor()
  const state = new URL(page.url()).searchParams.get('state')
  // This is the answer Keycloak sends when it cannot sign the person in. access_denied is a declined terms step instead.
  await page.goto(`/api/auth/callback?error=temporarily_unavailable&state=${encodeURIComponent(state!)}`)

  await expect(page).toHaveURL(`${baseURL}/?signIn=failed`)
  await expect(page.getByRole('alert').filter({ hasText: 'Sign-in did not complete.' })).toBeVisible()
  await expect(signIn).toBeVisible()
  // The card never starts sign-in on its own.
  await expect(page).toHaveURL(`${baseURL}/?signIn=failed`)

  await signIn.click()
  await logInAtKeycloak(page, 'requester')

  await expect(page).toHaveURL(`${baseURL}/`)
  await expect(banner.getByRole('button', { name: 'Requester User', exact: true })).toBeVisible()
  await expect(page.getByRole('alert').filter({ hasText: 'Sign-in did not complete.' })).toHaveCount(0)
})
