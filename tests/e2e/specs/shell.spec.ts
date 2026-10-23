import { expect, test, type Locator } from '@playwright/test'
import { useSession } from '../sessions'

test('the home page loads and reports a ready server', async ({ page }) => {
  await page.goto('/')

  await expect(page).toHaveTitle('Axis')
  await expect(page.getByRole('main').getByText('Welcome to Axis', { exact: true })).toBeVisible()
  await expect(page.getByTestId('server-status')).toContainText('Ready')
  // Nobody is signed in, so a sign-in prompt shows in place of the site list.
  await expect(page.getByTestId('sites-sign-in')).toHaveText('Sign in to see the sites.')
})

test('the shell header offers sign-in through the account button and no test user button', async ({ page }) => {
  await page.goto('/')

  const banner = page.getByRole('banner')
  await expect(banner.getByRole('button', { name: 'Sign in', exact: true })).toBeVisible()
  await expect(banner.getByRole('button', { name: /Test user/i })).toHaveCount(0)
})

test('dark mode can be switched on and survives a reload', async ({ page }) => {
  await page.goto('/')
  const toggle = page.getByRole('switch', { name: 'Dark mode', exact: true })
  await expect(toggle).not.toBeChecked()

  await toggle.click()
  await expect(toggle).toBeChecked()
  await page.reload()

  await expect(page.getByRole('switch', { name: 'Dark mode', exact: true })).toBeChecked()
})

test('the shell renders in light and dark mode', async ({ page }) => {
  await page.goto('/')

  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode', exact: true }).click()
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByRole('menuitem', { name: 'Home', exact: true })).toBeVisible()
    await expect(shell.getByRole('main').getByText('Welcome to Axis', { exact: true })).toBeVisible()
  }
})

// The colours below were recorded before the theme moved to the Axis token file. They show the shell is unchanged.
const shellColours = {
  light: {
    header: 'rgb(255, 255, 255)',
    page: 'rgb(245, 245, 245)',
    title: 'rgba(0, 0, 0, 0.88)',
    selectedItem: 'rgb(47, 84, 235)',
  },
  dark: {
    header: 'rgb(20, 20, 20)',
    page: 'rgb(0, 0, 0)',
    title: 'rgba(255, 255, 255, 0.85)',
    selectedItem: 'rgb(43, 74, 203)',
  },
}

test('the shell keeps its colours in light and dark mode', async ({ page }) => {
  await page.goto('/')

  for (const mode of ['light', 'dark'] as const) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode', exact: true }).click()
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    const banner = shell.getByRole('banner')
    const selectedItem = banner.getByRole('menuitem', { name: 'Home', exact: true })
    await expect(selectedItem).toBeVisible()

    // The page function runs in the browser, so the CSS property goes in as its argument.
    const style = (target: Locator, property: string) =>
      target.evaluate((element, name) => getComputedStyle(element).getPropertyValue(name), property)
    const expected = shellColours[mode]
    expect(await style(banner, 'background-color')).toBe(expected.header)
    expect(await shell.getByRole('main').evaluate((main) => getComputedStyle(main.parentElement!).backgroundColor)).toBe(
      expected.page,
    )
    expect(await style(banner.getByText('Axis', { exact: true }), 'color')).toBe(expected.title)
    expect(await style(selectedItem, 'color')).toBe(expected.selectedItem)
  }
})

test('switching the locale changes the texts and survives a reload', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('main').getByText('Welcome to Axis', { exact: true })).toBeVisible()

  // The radio input itself takes no pointer events: people click its label.
  await page.getByRole('radiogroup', { name: 'Language', exact: true }).getByText('Tiếng Việt', { exact: true }).click()

  await expect(page.getByRole('main').getByText('Chào mừng đến với Axis', { exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Trang chủ', exact: true })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('main').getByText('Chào mừng đến với Axis', { exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Trang chủ', exact: true })).toBeVisible()
  await expect(page.getByRole('radio', { name: 'Tiếng Việt', exact: true })).toBeChecked()
})

test('the home page opens a site in its own shell, in light and dark mode', async ({ page }) => {
  // The site opens on its notes page, which reads records, and the record API needs a session.
  await useSession(page, 'anna')
  await page.goto('/')

  await page.getByRole('link', { name: 'E2E notes', exact: true }).click()

  await expect(page).toHaveURL(/\/e2e\/notes$/)
  await expect(page.getByRole('main').getByText('Notes', { exact: true })).toBeVisible()
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode', exact: true }).click()
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByRole('menuitem', { name: 'Notes', exact: true })).toBeVisible()
    await expect(shell.getByRole('menuitem', { name: 'Categories', exact: true })).toBeVisible()
  }
})

test('an unknown site is not found inside the platform shell', async ({ page }) => {
  // The site endpoints need a session, or the page would start sign-in.
  await useSession(page, 'anna')
  const response = await page.goto('/no-such-site')

  expect(response?.status()).toBe(200)
  await expect(page.getByTestId('not-found')).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Home', exact: true })).toBeVisible()
})

test('an unknown page is not found inside the site shell', async ({ page }) => {
  await useSession(page, 'anna')
  const response = await page.goto('/e2e/no-such-page')

  expect(response?.status()).toBe(200)
  await expect(page.getByTestId('not-found')).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Notes', exact: true })).toBeVisible()
})

test('the readiness endpoint reports healthy', async ({ request }) => {
  const response = await request.get('/health/ready')

  expect(response.status()).toBe(200)
  expect(await response.json()).toEqual({ status: 'Healthy' })
})

test('the texts of an unknown locale are not found', async ({ request }) => {
  const response = await request.get('/api/texts/xx')

  expect(response.status()).toBe(404)
  expect(response.headers()['content-type']).toContain('application/problem+json')
})
