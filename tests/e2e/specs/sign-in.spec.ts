import { expect, test, type Page } from '@playwright/test'

// The platform home page stays open while nobody is signed in. Every application page needs a
// session and would start sign-in on its own, before the header's button is clicked.

/** Signs in as Anna on the Keycloak login page from the header's sign-in button on the home page. */
async function signInAsAnna(page: Page) {
  await page.goto('/')
  await page.getByRole('banner').getByRole('button', { name: 'Sign in', exact: true }).click()
  await page.locator('#username').fill('anna')
  await page.locator('#password').fill('axis')
  await page.locator('#kc-login').click()
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
      await page.getByRole('switch', { name: 'Dark mode', exact: true }).click()
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByRole('banner').getByRole('button', { name: 'Anna Employee', exact: true })).toBeVisible()
  }
})

test('after signing out from the header menu, the current user endpoint answers 401 and the header shows Sign in', async ({
  page,
  baseURL,
}) => {
  await signInAsAnna(page)
  const banner = page.getByRole('banner')
  await banner.getByRole('button', { name: 'Anna Employee', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Sign out', exact: true }).click()

  // The server keeps no ID token, so Keycloak may ask to confirm the sign-out.
  const confirm = page.locator('#kc-logout')
  const signIn = banner.getByRole('button', { name: 'Sign in', exact: true })
  await expect(confirm.or(signIn)).toBeVisible()
  if (await confirm.isVisible()) {
    await confirm.click()
  }

  await page.waitForURL(`${baseURL}/`)
  expect((await page.request.get('/api/me')).status()).toBe(401)
  await expect(signIn).toBeVisible()
})
