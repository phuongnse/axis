import { expect, test } from '@playwright/test'

test('the home page loads and reports a ready server', async ({ page }) => {
  await page.goto('/')

  await expect(page).toHaveTitle('Axis')
  await expect(page.getByRole('main').getByText('Welcome to Axis')).toBeVisible()
  await expect(page.getByTestId('server-status')).toContainText('Ready')
})

test('dark mode can be switched on and survives a reload', async ({ page }) => {
  await page.goto('/')
  const toggle = page.getByRole('switch', { name: 'Dark mode' })
  await expect(toggle).not.toBeChecked()

  await toggle.click()
  await expect(toggle).toBeChecked()
  await page.reload()

  await expect(page.getByRole('switch', { name: 'Dark mode' })).toBeChecked()
})

test('deep links are served by the single-page app', async ({ page }) => {
  const response = await page.goto('/apps/unknown/page')

  expect(response?.status()).toBe(200)
  await expect(page.getByRole('main').getByText('Welcome to Axis')).toBeVisible()
})

test('the readiness endpoint reports the database check', async ({ request }) => {
  const response = await request.get('/health/ready')

  expect(response.status()).toBe(200)
  expect(await response.json()).toEqual({ status: 'Healthy', checks: { database: 'Healthy' } })
})
