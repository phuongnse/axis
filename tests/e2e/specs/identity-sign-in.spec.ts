import { expect, test } from '../test'
import { logInAtKeycloak, signInUsername, submitPassword } from '../keycloak'

// The development realm signs people in on the Axis theme. The theme reads prefers-color-scheme, because the origin of
// Keycloak has no stored mode. The canvas colours are the canvas token of web/src/platform/designTokens.ts.
for (const { mode, canvas } of [
  { mode: 'light', canvas: 'rgb(245, 245, 245)' },
  { mode: 'dark', canvas: 'rgb(0, 0, 0)' },
] as const) {
  test(`a person signs in on the Axis sign-in page in ${mode} mode and reaches the home page`, async ({
    page,
    baseURL,
  }) => {
    await page.emulateMedia({ colorScheme: mode })
    // Only the errors of Keycloak's page count here. The SPA's home page may log failed requests before sign-in.
    const keycloakOrigin = new URL(process.env.AXIS_E2E_KEYCLOAK_URL!).origin
    const errors: string[] = []
    page.on('console', (message) => {
      if (message.type() === 'error' && new URL(message.location().url || 'about:blank').origin === keycloakOrigin) {
        errors.push(message.text())
      }
    })
    page.on('pageerror', (error) => {
      if (new URL(page.url()).origin === keycloakOrigin) errors.push(error.message)
    })

    await page.goto('/')
    await page.getByRole('main').getByRole('button', { name: 'Sign in', exact: true }).click()

    await expect(page.getByRole('heading', { name: 'Sign in', exact: true })).toBeVisible()
    await expect(page.getByText(/^© \d{4} Axis$/)).toBeVisible()
    await expect(page.locator('#root > div')).toHaveCSS('background-color', canvas)

    await logInAtKeycloak(page, 'requester')

    await expect(page).toHaveURL(`${baseURL}/`)
    await expect(page.getByRole('banner').getByRole('button', { name: 'Requester User', exact: true })).toBeVisible()
    expect(errors).toEqual([])
  })
}

test('a wrong password shows the sign-in error and keeps the username', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('main').getByRole('button', { name: 'Sign in', exact: true }).click()

  await submitPassword(page, 'requester', 'not-the-password')

  await expect(page.getByRole('alert')).toContainText('Invalid email, username or password.')
  await expect(signInUsername(page)).toHaveValue('requester')
})
