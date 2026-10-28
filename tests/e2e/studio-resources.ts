import { expect, type Page } from '@playwright/test'
import { waitForDialog } from './dialogs'
import { pickOption } from './select'

/**
 * Creates a resource of the kind `kind` through the New resource dialog. It returns once the dialog is gone and the
 * resource list marks the new resource as the open one. `value` is typed into the dialog. `shownAs` is the name the
 * resource list shows, which differs for a text resource: `Texts` plus the locale, such as `TextsVi`.
 */
export async function createResource(page: Page, kind: string, value: string, shownAs = value) {
  await page.getByRole('button', { name: 'New resource', exact: true }).click()
  const dialog = await waitForDialog(page, 'New resource')
  await pickOption(dialog.getByRole('combobox', { name: 'Kind', exact: true }), kind)
  await dialog.getByRole('textbox', { name: kind === 'text' ? 'Locale' : 'Name', exact: true }).fill(value)
  const create = dialog.getByRole('button', { name: 'Create', exact: true })
  await expect(create).toBeEnabled()
  await create.click()
  await expect(dialog).toBeHidden()
  await expect(
    page.getByTestId(`studio-group-${kind}`).getByRole('button', { name: shownAs, exact: true }),
  ).toHaveAttribute('aria-current', 'true')
}

/**
 * Selects a tab of the open resource and waits until it is selected. It clicks again when the click did not select the
 * tab. The layout can shift just before or during the click, as the web font arrives or as the release history above
 * the editor loads, and a click that lands during the shift can miss.
 */
export async function openTab(page: Page, name: 'Design' | 'Form' | 'JSON') {
  const tab = page.getByRole('tab', { name, exact: true })
  await page.evaluate(() => document.fonts.ready)
  await expect(async () => {
    await tab.click()
    await expect(tab).toHaveAttribute('aria-selected', 'true', { timeout: 2000 })
  }).toPass()
}

/**
 * Selects the Form tab of the open resource. Under D65 and D66 a page or a form opens on its Design tab, so a spec that
 * uses the fields of its Form tab selects Form first. It does no harm when Form is selected.
 */
export async function openFormTab(page: Page) {
  await openTab(page, 'Form')
}
