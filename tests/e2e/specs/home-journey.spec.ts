import { expect, test } from '../test'
import { logInAtKeycloak } from '../keycloak'

// A person signs in from the home page, opens a site from its application card, uses the navigation, opens a form
// with the table's main action and returns through the breadcrumb. Below 992 px there is no sidebar, so the journey
// opens and closes the Menu panel instead of collapsing it.
for (const { name, width } of [
  { name: '992 px', width: 992 },
  { name: 'narrow width', width: 375 },
]) {
  test(`a person signs in from the home page, opens a site from its card and returns by the breadcrumb at ${name}`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 })
    await page.goto('/')
    const main = page.getByRole('main')
    await logInAtKeycloak(page, 'requester')

    await expect(main.getByText('Welcome to Axis', { exact: true })).toBeVisible()
    await main.getByRole('link', { name: 'E2E notes', exact: true }).click()
    await expect(page).toHaveURL(/\/e2e\/notes$/)

    if (width >= 992) {
      const sidebar = page.locator('#shell-sidebar')
      await page.getByRole('button', { name: 'Collapse navigation', exact: true }).click()
      await expect.poll(async () => (await sidebar.boundingBox())?.width).toBe(64)
    } else {
      const menu = page.getByRole('button', { name: 'Menu', exact: true })
      await menu.click()
      await expect(page.locator('#shell-menu')).toBeVisible()
      await menu.click()
      await expect(page.locator('#shell-menu')).toBeHidden()
    }

    await main.getByRole('button', { name: 'New', exact: true }).click()
    await expect(page).toHaveURL(/\/e2e\/noteform\/new$/)
    await page
      .getByRole('navigation', { name: 'Breadcrumb', exact: true })
      .getByRole('link', { name: 'Notes', exact: true })
      .click()
    await expect(page).toHaveURL(/\/e2e\/notes$/)
  })
}
