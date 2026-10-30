import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { useSession } from '../sessions'
import { chooseTheme } from '../shell'

// The leave request sample lists the Departments page of its departments module in the site navigation. An
// employee opens it and sees the module's seeded departments.
const departments = ['Finance', 'Engineering', 'Operations']

async function expectDepartments(page: Page) {
  await expect(page.getByRole('heading', { name: 'Departments', exact: true })).toBeVisible()
  for (const department of departments) {
    await expect(page.getByRole('row', { name: new RegExp(department) })).toBeVisible()
  }
}

test('an employee opens the module page from the navigation and sees the departments in light and dark mode', async ({
  page,
}) => {
  await useSession(page, 'employee')
  await page.goto('/leave')

  await page.getByRole('menuitem', { name: 'Departments', exact: true }).click()

  await expect(page).toHaveURL(/\/leave\/departments\/departments$/)
  await expect(page.locator('[data-theme-mode="light"]').first()).toBeVisible()
  await expectDepartments(page)

  await chooseTheme(page, 'Dark')
  await page.reload()

  await expect(page.locator('[data-theme-mode="dark"]').first()).toBeVisible()
  await expectDepartments(page)
})
