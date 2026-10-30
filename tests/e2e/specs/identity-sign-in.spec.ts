import { expect, test } from '../test'
import { totp, totpSecret } from '../totp'
import { signInUsername, submitCode, submitPassword } from '../keycloak'

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

    await submitPassword(page, 'requester', 'axis')

    // Keycloak switches to the language of the user once the password is right, and requester prefers Vietnamese.
    await expect(page.getByRole('heading', { name: 'Xác minh hai bước', exact: true })).toBeVisible()
    await expect(page.locator('#root > div')).toHaveCSS('background-color', canvas)
    await expect(page.locator('#root form').first().locator('input:not([type="hidden"])')).toHaveCount(6)

    await submitCode(page, totp(totpSecret('requester')))

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

// Startup activation made the brokered provider of the purchase request sample's company provider and listed it on the
// Purchasing site's client.
test('startup activation creates the company provider in the axis realm and allows it on the Purchasing site', async ({
  request,
}) => {
  const keycloak = process.env.AXIS_E2E_KEYCLOAK_URL!
  const tokenResponse = await request.post(`${keycloak}/realms/axis/protocol/openid-connect/token`, {
    form: {
      grant_type: 'client_credentials',
      client_id: 'axis-activation',
      client_secret: 'axis-activation-development-secret',
    },
  })
  expect(tokenResponse.status()).toBe(200)
  const { access_token: token } = (await tokenResponse.json()) as { access_token: string }
  const headers = { Authorization: `Bearer ${token}` }

  const provider = await request.get(`${keycloak}/admin/realms/axis/identity-provider/instances/oidc-companyissuer`, {
    headers,
  })
  expect(provider.status()).toBe(200)
  expect(((await provider.json()) as { config: { clientId: string } }).config.clientId).toBe('axis-broker')

  const clients = await request.get(`${keycloak}/admin/realms/axis/clients?clientId=site-purchasing`, { headers })
  expect(clients.status()).toBe(200)
  const [client, ...others] = (await clients.json()) as { attributes: Record<string, string> }[]
  expect(others).toEqual([])
  expect(client.attributes['axis.providers'].split(' ')).toContain('oidc-companyissuer')
  expect(client.attributes['axis.accounts']).toBe('true')
})

for (const { mode, canvas } of [
  { mode: 'light', canvas: 'rgb(245, 245, 245)' },
  { mode: 'dark', canvas: 'rgb(0, 0, 0)' },
] as const) {
  // The development realm has no mail server, so the test opens the page and does not submit it.
  test(`"Forgot password?" opens the Axis forgot-password page in ${mode} mode`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: mode })

    await page.goto('/')
    await page.getByRole('main').getByRole('button', { name: 'Sign in', exact: true }).click()
    await page.getByRole('link', { name: 'Forgot password?', exact: true }).click()

    await expect(page.getByRole('heading', { name: 'Reset your password', exact: true })).toBeVisible()
    await expect(page.locator('#root > div')).toHaveCSS('background-color', canvas)
    await expect(page.locator('#root form input[name="username"]')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Back to sign in', exact: true })).toBeVisible()
  })
}
