import { expect, test } from '../test'
import { useSession } from '../sessions'

test('the platform operator opens the operator console from the app switcher and sees the tenant', async ({ page }) => {
  await useSession(page, 'platform-operator')
  await page.goto('/')

  await page.getByRole('banner').getByRole('button', { name: 'Switch to', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Operator console', exact: true }).click()

  await expect(page).toHaveURL(/\/console$/)
  const tenant = page.getByTestId('console-tenant')
  await expect(tenant.getByText('Active', { exact: true })).toBeVisible()
  await expect(tenant.getByRole('cell', { name: 'E2eApp', exact: true })).toBeVisible()
  await expect(tenant.getByRole('cell', { name: 'PurchaseRequests', exact: true })).toBeVisible()
})

test('a person without the operator role is told the page is for operators', async ({ page }) => {
  await useSession(page, 'author')
  await page.goto('/console')

  await expect(page.getByTestId('console-forbidden')).toBeVisible()
  await expect(page.getByText('This page is for operators.', { exact: true })).toBeVisible()
})
