import { expect, test } from '@playwright/test'

// The request fixture keeps its own cookies, so this sign-in does not reach other specs.
test('a test user signs in and the current user endpoint returns that user', async ({ request }) => {
  const signedIn = await request.post('/api/test-users/sign-in', { data: { id: 'anna' } })
  expect(signedIn.status()).toBe(200)

  const me = await request.get('/api/me')

  expect(me.status()).toBe(200)
  expect(await me.json()).toEqual({ id: 'anna', displayName: 'Anna Employee', roles: ['employee'] })
})

// Each test has its own browser context, so the sign-in cookie does not reach other specs.
test('a person picks a test user in the header and still sees the name after a reload, in light and dark mode', async ({
  page,
}) => {
  await page.goto('/')

  await page.getByRole('banner').getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('menuitemradio', { name: 'Binh Engineering Manager' }).click()
  await expect(page.getByRole('banner').getByRole('button', { name: 'Binh Engineering Manager' })).toBeVisible()
  await page.reload()

  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode' }).click()
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByRole('banner').getByRole('button', { name: 'Binh Engineering Manager' })).toBeVisible()
  }

  await page.goto('/e2e/notes')
  await expect(page.getByRole('banner').getByRole('button', { name: 'Binh Engineering Manager' })).toBeVisible()
})

test('after signing out from the header, the header shows the sign-in prompt', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('banner').getByRole('button', { name: 'Sign in' }).click()
  await page.getByRole('menuitemradio', { name: 'Binh Engineering Manager' }).click()
  const banner = page.getByRole('banner')
  await expect(banner.getByRole('button', { name: 'Binh Engineering Manager' })).toBeVisible()

  await banner.getByRole('button', { name: 'Binh Engineering Manager' }).click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()

  await expect(banner.getByRole('button', { name: 'Sign in' })).toBeVisible()
  await expect(banner.getByText('Binh Engineering Manager')).toHaveCount(0)
})
