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

/**
 * Opens the More actions menu of the page heading, which holds the actions that fold into it, see
 * docs/reference/actions-and-controls.md#menus. It waits until the menu is shown and has the focus, and returns it. The
 * menu takes the focus a few frames after it opens, so an entry chosen before then can lose it to the menu.
 */
export async function openMoreActions(page: Page): Promise<Locator> {
  await page.getByTestId('page-actions').getByRole('button', { name: 'More actions', exact: true }).click()
  const menu = page.getByRole('menu', { name: 'More actions', exact: true })
  await expect(menu).toBeVisible()
  await expect.poll(() => menu.evaluate((element) => element.contains(document.activeElement))).toBe(true)
  return menu
}

/**
 * Chooses the folded action `testId` in the More actions menu of the page heading. It clicks the entry once the menu has
 * the focus, and waits until the menu has closed. An action that stays a button is clicked directly.
 */
export async function chooseMoreAction(page: Page, testId: string) {
  const menu = await openMoreActions(page)
  await menu.getByTestId(testId).click()
  await expect(menu).toHaveCount(0)
}
