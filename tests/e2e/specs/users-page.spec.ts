import { expect, test } from '../test'
import { useSession } from '../sessions'

test('an administrator opens Users from the app switcher and finds a user by email', async ({ page }) => {
  await useSession(page, 'administrator')
  await page.goto('/')

  await page.getByRole('banner').getByRole('button', { name: 'Switch to', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Users', exact: true }).click()

  await expect(page).toHaveURL(/\/users$/)
  const main = page.getByRole('main')
  await expect(main.getByRole('cell', { name: 'Administrator User', exact: true })).toBeVisible()
  await expect(main.getByRole('cell', { name: 'auditor@axis.local', exact: true })).toBeVisible()

  await main.getByRole('searchbox', { name: 'Search by name or email', exact: true }).fill('auditor@axis.local')
  await page.keyboard.press('Enter')

  await expect(main.getByRole('row').filter({ hasText: 'Auditor User' })).toHaveCount(1)
  await expect(main.getByRole('cell', { name: 'Administrator User', exact: true })).toHaveCount(0)
})

test('a person without the administrator role is told the page is for administrators', async ({ page }) => {
  await useSession(page, 'author')
  await page.goto('/users')

  await expect(page.getByText('This page is for administrators.', { exact: true })).toBeVisible()
})
