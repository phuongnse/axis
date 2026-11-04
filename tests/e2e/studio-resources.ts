import { expect, type Page } from '@playwright/test'
import { waitForDialog } from './dialogs'
import { pickOption } from './select'

/** The sidebar entry of a kind. Its address ends in `&kind=<kind>`, so it is found by that and not by its label. */
function kindEntry(page: Page, kind: string) {
  return page.getByRole('navigation', { name: 'Menu', exact: true }).locator(`a[href$="&kind=${kind}"]`)
}

/** Whether the sidebar has an entry for a kind, which it shows only while the application holds a resource of that kind. */
export async function hasKind(page: Page, kind: string) {
  return (await kindEntry(page, kind).count()) > 0
}

/** Opens a kind's list from its entry in the sidebar, and waits until the address shows the kind. */
export async function openKind(page: Page, kind: string) {
  await kindEntry(page, kind).click()
  await expect(page).toHaveURL(new RegExp(`[?&]kind=${kind}(&|$)`))
}

/**
 * Creates a resource of the kind `kind` through the New resource dialog. It returns once the dialog is gone, the
 * address holds the new resource, and the kind's entry in the sidebar is marked. `value` is typed into the dialog.
 */
export async function createResource(page: Page, kind: string, value: string) {
  await page.getByRole('button', { name: 'New resource…', exact: true }).click()
  const dialog = await waitForDialog(page, 'New resource')
  await pickOption(dialog.getByRole('combobox', { name: 'Kind', exact: true }), kind)
  await dialog.getByRole('textbox', { name: kind === 'text' ? 'Locale' : 'Name', exact: true }).fill(value)
  const create = dialog.getByRole('button', { name: 'Create', exact: true })
  await expect(create).toBeEnabled()
  await create.click()
  await expect(dialog).toBeHidden()
  await expect(page).toHaveURL(/[?&]resource=/)
  await expect(kindEntry(page, kind)).toHaveAttribute('aria-current', 'page')
}

/** Opens the New application… dialog from the application row of the sidebar, on a page that has an application open. */
export async function openNewApplication(page: Page) {
  await page.getByTestId('studio-application-row').click()
  await page.getByRole('menuitem', { name: 'New application…', exact: true }).click()
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
