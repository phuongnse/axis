import { expect, test } from '../test'
import { logInAtKeycloak } from '../keycloak'

// A person signs in from the home page, opens a site from its application tile, uses the navigation, opens a form
// with the table's main action and returns through the sidebar. Below 992 px there is no sidebar, so the journey
// opens and closes the Menu panel instead of collapsing it.
for (const { name, width } of [
  { name: '992 px', width: 992 },
  { name: 'narrow width', width: 375 },
]) {
  test(`a person signs in from the home page, opens a site from its card and returns through the sidebar at ${name}`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 })
    await page.goto('/')
    const main = page.getByRole('main')
    await logInAtKeycloak(page, 'requester')

    await expect(main.getByRole('heading', { name: 'Applications', exact: true })).toBeVisible()
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
    // The form page matches no sidebar entry, so the sidebar marks the table the form was opened from.
    if (width >= 992) {
      await page.getByRole('button', { name: 'Expand navigation', exact: true }).click()
      const sidebar = page.locator('#shell-sidebar')
      await expect(sidebar.getByRole('link', { name: 'Notes', exact: true })).toHaveAttribute('aria-current', 'page')
      await sidebar.getByRole('link', { name: 'Notes', exact: true }).click()
    } else {
      await page.getByRole('button', { name: 'Menu', exact: true }).click()
      await page.locator('#shell-menu').getByRole('link', { name: 'Notes', exact: true }).click()
    }
    await expect(page).toHaveURL(/\/e2e\/notes$/)
  })
}
