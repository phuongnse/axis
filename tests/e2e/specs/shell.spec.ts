import { expect, test } from '@playwright/test'
import { useSession } from '../sessions'

test('the home page loads and reports a ready server', async ({ page }) => {
  await page.goto('/')

  await expect(page).toHaveTitle('Axis')
  await expect(page.getByRole('main').getByText('Welcome to Axis')).toBeVisible()
  await expect(page.getByTestId('server-status')).toContainText('Ready')
  // Nobody is signed in, so a sign-in prompt shows in place of the site list.
  await expect(page.getByTestId('sites-sign-in')).toHaveText('Sign in to see the sites.')
})

test('the shell header offers sign-in through the account button and no test user button', async ({ page }) => {
  await page.goto('/')

  const banner = page.getByRole('banner')
  await expect(banner.getByRole('button', { name: 'Sign in' })).toBeVisible()
  await expect(banner.getByRole('button', { name: 'Test user' })).toHaveCount(0)
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

test('the home page opens a site in its own shell, in light and dark mode', async ({ page }) => {
  // The site opens on its notes page, which reads records, and the record API needs a session.
  await useSession(page, 'anna')
  await page.goto('/')

  await page.getByRole('link', { name: 'E2E notes' }).click()

  await expect(page).toHaveURL(/\/e2e\/notes$/)
  await expect(page.getByRole('main').getByText('Notes', { exact: true })).toBeVisible()
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode' }).click()
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByRole('menuitem', { name: 'Notes' })).toBeVisible()
    await expect(shell.getByRole('menuitem', { name: 'Categories' })).toBeVisible()
  }
})

test('an unknown site is not found inside the platform shell', async ({ page }) => {
  // The site endpoints need a session, or the page would start sign-in.
  await useSession(page, 'anna')
  const response = await page.goto('/no-such-site')

  expect(response?.status()).toBe(200)
  await expect(page.getByTestId('not-found')).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Home' })).toBeVisible()
})

test('an unknown page is not found inside the site shell', async ({ page }) => {
  await useSession(page, 'anna')
  const response = await page.goto('/e2e/no-such-page')

  expect(response?.status()).toBe(200)
  await expect(page.getByTestId('not-found')).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Notes' })).toBeVisible()
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
