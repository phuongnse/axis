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

test('the shell renders in light and dark mode', async ({ page }) => {
  await page.goto('/')

  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode' }).click()
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByRole('menuitem', { name: 'Home' })).toBeVisible()
    await expect(shell.getByRole('main').getByText('Welcome to Axis')).toBeVisible()
  }
})

test('switching the locale changes the texts and survives a reload', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('main').getByText('Welcome to Axis')).toBeVisible()

  // The radio input itself takes no pointer events: people click its label.
  await page.getByRole('radiogroup', { name: 'Language' }).getByText('Tiếng Việt').click()

  await expect(page.getByRole('main').getByText('Chào mừng đến với Axis')).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Trang chủ' })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('main').getByText('Chào mừng đến với Axis')).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Trang chủ' })).toBeVisible()
  await expect(page.getByRole('radio', { name: 'Tiếng Việt' })).toBeChecked()
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

test('the texts of an unknown locale are not found', async ({ request }) => {
  const response = await request.get('/api/texts/xx')

  expect(response.status()).toBe(404)
  expect(response.headers()['content-type']).toContain('application/problem+json')
})
