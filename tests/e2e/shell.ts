import { expect, type Locator, type Page } from '@playwright/test'

/** Opens a "Language and theme" menu, found by its button within `within`, and clicks one of its items, which closes the menu again. */
async function choose(
  page: Page,
  item: string,
  button = 'Language and theme',
  within: Page | Locator = page.getByRole('banner'),
) {
  await within.getByRole('button', { name: button, exact: true }).click()
  await page.getByRole('menuitemradio', { name: item, exact: true }).click()
}

/** Picks System, Light or Dark in the header's "Language and theme" menu. */
export async function chooseTheme(page: Page, theme: 'System' | 'Light' | 'Dark') {
  await choose(page, theme)
}

/**
 * Picks a language by its name, such as "Tiếng Việt", in the "Language and theme" menu. A page without a header
 * banner passes its own button name and the area the button is in.
 */
export async function chooseLanguage(
  page: Page,
  language: string,
  button = 'Language and theme',
  within: Page | Locator = page.getByRole('banner'),
) {
  await choose(page, language, button, within)
}

/** Expects the menu item to be the current choice. The menu is opened to look and closed again. */
export async function expectChosen(page: Page, item: string, button = 'Language and theme') {
  await page.getByRole('banner').getByRole('button', { name: button, exact: true }).click()
  await expect(page.getByRole('menuitemradio', { name: item, exact: true })).toHaveAttribute('aria-checked', 'true')
  await page.keyboard.press('Escape')
  await expect(page.getByRole('menu', { name: button, exact: true })).toBeHidden()
}
