import { expect, test } from '../test'
import { logInAtKeycloak } from '../keycloak'

// Change password on the account page sends the browser to the identity provider's set-password page, and Cancel there
// returns to the account page with nothing changed. The test signs in as the employee in a fresh browser context,
// because the saved sessions of the other specs must stay alive.

test('Change password opens the identity provider page, and Cancel returns to the account page', async ({
  browser,
  baseURL,
}) => {
  const context = await browser.newContext({ baseURL })
  try {
    const page = await context.newPage()
    await page.goto('/')
    await logInAtKeycloak(page, 'employee')
    await expect(page.getByRole('banner').getByRole('button', { name: 'Employee User', exact: true })).toBeVisible()

    await page.goto('/account')
    // The development realm's employee has exactly one authenticator, which must stay.
    await expect(page.getByRole('button', { name: 'Remove', exact: true })).toBeDisabled()
    await page.getByRole('button', { name: 'Change password', exact: true }).click()

    await expect(page.locator('#root input[name="password-new"]')).toBeVisible()
    await page.locator('#root button[name="cancel-aia"]').click()

    await expect(page).toHaveURL(`${baseURL}/account`)
    await expect(page.getByRole('button', { name: 'Change password', exact: true })).toBeVisible()
  } finally {
    await context.close()
  }
})
