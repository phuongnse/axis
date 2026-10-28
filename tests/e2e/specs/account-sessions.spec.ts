import { expect, test } from '../test'
import type { Page } from '@playwright/test'

// The account menu opens the sessions screen, where a person ends another of their own sessions. The test
// signs in as Lan in two fresh browser contexts, because the saved sessions of the other specs must stay alive.
// The sign-in at Keycloak is at most 5 minutes old, so the screen allows ending sessions.

/** Signs in as Lan on the Keycloak login page from the header's sign-in button on the home page. */
async function signInAsLan(page: Page) {
  await page.goto('/')
  await page.getByRole('banner').getByRole('button', { name: 'Sign in', exact: true }).click()
  await page.locator('#username').fill('lan')
  await page.locator('#password').fill('axis')
  await page.locator('#kc-login').click()
  await expect(page.getByRole('banner').getByRole('button', { name: 'Lan Employee', exact: true })).toBeVisible()
}

test('a person ends another session of their own from the sessions screen, and that browser shows the sign-in prompt', async ({
  browser,
  baseURL,
}) => {
  const contextA = await browser.newContext({ baseURL })
  const contextB = await browser.newContext({ baseURL })
  try {
    const pageA = await contextA.newPage()
    const pageB = await contextB.newPage()
    await signInAsLan(pageA)
    await signInAsLan(pageB)

    const listed = await pageB.request.get('/api/me/sessions')
    expect(listed.status()).toBe(200)
    const sessionsOfB = (await listed.json()) as { sessions: { id: string; current: boolean }[] }
    const idOfB = sessionsOfB.sessions.find((session) => session.current)?.id
    expect(idOfB).toBeTruthy()

    await pageA.getByRole('banner').getByRole('button', { name: 'Lan Employee', exact: true }).click()
    await pageA.getByRole('menuitem', { name: 'Sessions', exact: true }).click()
    await expect(pageA).toHaveURL(`${baseURL}/account`)

    const row = pageA.locator(`tr[data-row-key="${idOfB}"]`)
    await expect(row).toBeVisible()
    await row.getByRole('button', { name: 'End', exact: true }).click()
    await expect(row).toHaveCount(0)

    await pageB.goto('/account')
    await expect(pageB.getByTestId('sign-in-prompt')).toBeVisible()
  } finally {
    await contextA.close()
    await contextB.close()
  }
})
