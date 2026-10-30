import { expect, test } from '../test'
import type { Locator } from '@playwright/test'
import { useSession } from '../sessions'
import { chooseLanguage, chooseTheme, expectChosen } from '../shell'

test('the home page loads and reports a ready server', async ({ page }) => {
  await page.goto('/')

  await expect(page).toHaveTitle('Axis')
  await expect(page.getByTestId('footer-server-status')).toContainText('Ready')
  // Nobody is signed in, so the home page is the sign-in card.
  await expect(page.getByRole('main').getByText('Sign in to Axis', { exact: true })).toBeVisible()
  await expect(page.getByRole('main').getByRole('button', { name: 'Sign in', exact: true })).toBeVisible()
})

test('the sign-in screen has one Sign in button, in the page and not in the header', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('main').getByRole('button', { name: 'Sign in', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Sign in', exact: true })).toHaveCount(1)
  const banner = page.getByRole('banner')
  await expect(banner.getByRole('button', { name: 'Sign in', exact: true })).toHaveCount(0)
  await expect(banner.getByRole('button', { name: /Test user/i })).toHaveCount(0)
})

test('dark mode can be switched on and survives a reload', async ({ page }) => {
  await page.goto('/')
  await expectChosen(page, 'System')

  await chooseTheme(page, 'Dark')
  await expectChosen(page, 'Dark')
  await page.reload()

  await expect(page.locator('[data-theme-mode="dark"]')).toBeVisible()
  await expectChosen(page, 'Dark')
})

test('the shell renders in light and dark mode', async ({ page }) => {
  await page.goto('/')

  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await chooseTheme(page, 'Dark')
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByRole('banner').getByRole('link', { name: 'Axis', exact: true })).toBeVisible()
    await expect(shell.getByRole('main').getByText('Sign in to Axis', { exact: true })).toBeVisible()
  }
})

// The colours were recorded before the theme moved to the Axis token file and are unchanged. The selected sidebar
// entry is drawn in primaryText.
const shellColours = {
  light: {
    header: 'rgb(255, 255, 255)',
    page: 'rgb(245, 245, 245)',
    title: 'rgba(0, 0, 0, 0.88)',
    selectedItem: 'rgb(91, 63, 217)',
  },
  dark: {
    header: 'rgb(20, 20, 20)',
    page: 'rgb(0, 0, 0)',
    title: 'rgba(255, 255, 255, 0.85)',
    selectedItem: 'rgb(163, 142, 231)',
  },
}

// The page function runs in the browser, so the CSS property goes in as its argument.
const style = (target: Locator, property: string) =>
  target.evaluate((element, name) => getComputedStyle(element).getPropertyValue(name), property)

test('the shell keeps its colours in light and dark mode', async ({ page }) => {
  await page.goto('/')

  for (const mode of ['light', 'dark'] as const) {
    if (mode === 'dark') {
      await chooseTheme(page, 'Dark')
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    const banner = shell.getByRole('banner')
    await expect(banner.getByText('Axis', { exact: true })).toBeVisible()

    const expected = shellColours[mode]
    expect(await style(banner, 'background-color')).toBe(expected.header)
    expect(await shell.getByRole('main').evaluate((main) => getComputedStyle(main.parentElement!).backgroundColor)).toBe(
      expected.page,
    )
    expect(await style(banner.getByText('Axis', { exact: true }), 'color')).toBe(expected.title)
  }
})

test('the shell fills the window on canvas in light and dark mode', async ({ page }) => {
  for (const mode of ['light', 'dark'] as const) {
    await page.goto('/')
    await chooseTheme(page, mode === 'dark' ? 'Dark' : 'Light')
    await page.goto('/')
    await expect(page.locator(`[data-theme-mode="${mode}"]`).getByRole('banner')).toBeVisible()

    const body = page.locator('body')
    expect(await style(body, 'margin')).toBe('0px')
    expect(await style(body, 'background-color')).toBe(shellColours[mode].page)
    expect(await page.evaluate(() => document.documentElement.scrollHeight <= window.innerHeight)).toBe(true)

    await page.getByRole('banner').getByRole('button', { name: 'Language and theme', exact: true }).click()
    const menu = page.getByRole('menu', { name: 'Language and theme', exact: true })
    await expect(menu).toBeVisible()
    expect(await style(menu, 'padding')).toBe('8px')
    await page.keyboard.press('Escape')
    await expect(menu).toBeHidden()
  }
})

test('the sidebar draws the current entry in the selected colour in light and dark mode', async ({ page }) => {
  await useSession(page, 'requester')
  await page.goto('/e2e/notes')

  for (const mode of ['light', 'dark'] as const) {
    if (mode === 'dark') {
      await chooseTheme(page, 'Dark')
    }
    const selectedItem = page
      .locator(`[data-theme-mode="${mode}"]`)
      .getByRole('navigation', { name: 'Menu', exact: true })
      .getByRole('menuitem', { name: 'Notes', exact: true })
    await expect(selectedItem).toBeVisible()

    expect(await style(selectedItem, 'color')).toBe(shellColours[mode].selectedItem)
  }
})

test('switching the locale changes the texts and survives a reload', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('main').getByText('Sign in to Axis', { exact: true })).toBeVisible()

  await chooseLanguage(page, 'Tiếng Việt')

  await expect(page.getByRole('main').getByText('Đăng nhập vào Axis', { exact: true })).toBeVisible()
  await expect(page.getByRole('contentinfo').getByText('Trạng thái máy chủ:', { exact: true })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('main').getByText('Đăng nhập vào Axis', { exact: true })).toBeVisible()
  await expect(page.getByRole('contentinfo').getByText('Trạng thái máy chủ:', { exact: true })).toBeVisible()
  await expectChosen(page, 'Tiếng Việt', 'Ngôn ngữ và giao diện')
})

test('the home page opens a site in its own shell, in light and dark mode', async ({ page }) => {
  // The site opens on its notes page, which reads records, and the record API needs a session.
  await useSession(page, 'requester')
  await page.goto('/')

  await page.getByRole('link', { name: 'E2E notes', exact: true }).click()

  await expect(page).toHaveURL(/\/e2e\/notes$/)
  await expect(page.getByRole('main').getByRole('heading', { name: 'Notes', exact: true })).toBeVisible()
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await chooseTheme(page, 'Dark')
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByRole('menuitem', { name: 'Notes', exact: true })).toBeVisible()
    await expect(shell.getByRole('menuitem', { name: 'Categories', exact: true })).toBeVisible()
  }
})

test('Users is in the app switcher and the welcome band of an administrator only', async ({ page }) => {
  await useSession(page, 'administrator')
  await page.goto('/')

  const main = page.getByRole('main')
  const tile = main.getByRole('link', { name: /^Users/ })
  await expect(tile).toHaveAttribute('href', '/users')
  await expect(tile).toContainText('Invite people and give them roles.')
  await page.getByRole('banner').getByRole('button', { name: 'Switch to', exact: true }).click()
  await expect(page.getByRole('menuitem', { name: 'Users', exact: true })).toBeVisible()

  await useSession(page, 'author')
  await page.goto('/')

  await expect(main.getByText('Welcome to Axis', { exact: true })).toBeVisible()
  await expect(main.getByRole('link', { name: /^Users/ })).toHaveCount(0)
  await page.getByRole('banner').getByRole('button', { name: 'Switch to', exact: true }).click()
  await expect(page.getByRole('menuitem', { name: 'Studio', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Users', exact: true })).toHaveCount(0)
})

test('an unknown site is not found inside the platform shell', async ({ page }) => {
  // The site endpoints need a session, or the page would show the sign-in prompt.
  await useSession(page, 'requester')
  const response = await page.goto('/no-such-site')

  expect(response?.status()).toBe(200)
  await expect(page.getByTestId('not-found')).toBeVisible()
  await expect(page.getByRole('banner').getByRole('link', { name: 'Axis', exact: true })).toBeVisible()
})

test('an unknown page is not found inside the site shell', async ({ page }) => {
  await useSession(page, 'requester')
  const response = await page.goto('/e2e/no-such-page')

  expect(response?.status()).toBe(200)
  await expect(page.getByTestId('not-found')).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Notes', exact: true })).toBeVisible()
})

test('a site collapses its sidebar to icons and keeps it collapsed on the next page', async ({ page }) => {
  await useSession(page, 'requester')
  await page.goto('/e2e/notes')
  const sidebar = page.locator('#shell-sidebar')
  await expect(sidebar).toBeVisible()
  expect((await sidebar.boundingBox())?.width).toBe(224)

  await page.getByRole('button', { name: 'Collapse navigation', exact: true }).click()

  await expect(page.getByRole('button', { name: 'Expand navigation', exact: true })).toHaveAttribute(
    'aria-expanded',
    'false',
  )
  await expect.poll(async () => (await sidebar.boundingBox())?.width).toBe(64)
  await page.getByRole('menuitem', { name: 'Categories', exact: true }).click()
  await expect(page).toHaveURL(/\/e2e\/categories$/)
  await expect(page.getByRole('button', { name: 'Expand navigation', exact: true })).toBeVisible()
  expect((await sidebar.boundingBox())?.width).toBe(64)
})

test('the platform site has no sidebar and no collapse button', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('main')).toBeVisible()
  await expect(page.locator('#shell-sidebar')).toHaveCount(0)
  await expect(page.getByRole('button', { name: /navigation$/ })).toHaveCount(0)
})

test('the footer shows the version only to a signed-in person', async ({ page }) => {
  const year = new Date().getFullYear()
  await page.goto('/')
  const footer = page.getByRole('contentinfo')
  await expect(footer).toContainText(`© ${year} Axis`)
  await expect(footer.getByTestId('footer-server-status')).toContainText('Ready')
  await expect(footer).not.toContainText(' · v')

  await useSession(page, 'requester')
  await page.goto('/')

  await expect(page.getByRole('contentinfo')).toContainText(new RegExp(`© ${year} Axis · v\\S+`))
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
