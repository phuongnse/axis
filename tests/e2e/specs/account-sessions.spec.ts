import { expect, test } from '../test'
import type { Page } from '@playwright/test'

// The account menu opens the sessions screen, where a person ends another of their own sessions. The test
// signs in as the employee in two fresh browser contexts, because the saved sessions of the other specs must stay alive.
// The sign-in at Keycloak is at most 5 minutes old, so the screen allows ending sessions.

/** Signs in as the employee on the Keycloak login page from the sign-in card on the home page. */
async function signInAsEmployee(page: Page) {
  await page.goto('/')
  await page.getByRole('main').getByRole('button', { name: 'Sign in', exact: true }).click()
  await page.locator('#username').fill('employee')
  await page.locator('#password').fill('axis')
  await page.locator('#kc-login').click()
  await expect(page.getByRole('banner').getByRole('button', { name: 'Employee User', exact: true })).toBeVisible()
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
    await signInAsEmployee(pageA)
    await signInAsEmployee(pageB)

    const listed = await pageB.request.get('/api/me/sessions')
    expect(listed.status()).toBe(200)
    const sessionsOfB = (await listed.json()) as { sessions: { id: string; current: boolean }[] }
    const idOfB = sessionsOfB.sessions.find((session) => session.current)?.id
    expect(idOfB).toBeTruthy()

    await pageA.getByRole('banner').getByRole('button', { name: 'Employee User', exact: true }).click()
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
