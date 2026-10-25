import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { waitForDialog } from '../dialogs'
import { noSideways, phoneWidth } from '../phone'
import { pickOption } from '../select'
import { sessionFile } from '../sessions'

// anna saves a new request as a draft, because she may write only draft and returned requests.
test.use({ storageState: sessionFile('anna'), viewport: { width: phoneWidth, height: 812 } })

/** Opens the lookup of the reference field `field`, labelled `label`, and picks the row named `name`. */
async function choose(page: Page, field: string, label: string, name: string) {
  await page.getByTestId(`reference-${field}`).getByRole('button', { name: 'Choose', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByRole('row', { name: new RegExp(name) }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByLabel(label, { exact: true })).toHaveValue(name)
}

/** Opens a select and picks the option with the given value. */
async function select(page: Page, label: string, value: string) {
  await pickOption(page.getByRole('combobox', { name: label, exact: true }), value)
}

test('a purchase request is created and reopened at phone width, without the page scrolling sideways', async ({
  page,
}) => {
  const title = `Phone ${Date.now()}-${test.info().retry}`
  const cell = (label: string) => page.getByLabel(label, { exact: true })
  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')

  // The table is wider than the window, so it scrolls inside its widget and not the page.
  const table = page.getByRole('table').first()
  await expect(table).toBeVisible()
  expect((await table.boundingBox())!.width).toBeGreaterThan(phoneWidth)
  await noSideways(page)

  // The shell's navigation sits behind a Menu button that the keyboard opens and closes.
  const menu = page.getByRole('button', { name: 'Menu', exact: true })
  await expect(menu).toHaveAttribute('aria-expanded', 'false')
  await menu.focus()
  await page.keyboard.press('Enter')
  await expect(menu).toHaveAttribute('aria-expanded', 'true')
  await expect(page.getByRole('link', { name: 'Suppliers', exact: true })).toBeVisible()
  await noSideways(page)
  await page.keyboard.press('Escape')
  await expect(menu).toHaveAttribute('aria-expanded', 'false')
  await expect(page.getByRole('link', { name: 'Suppliers', exact: true })).toBeHidden()
  await expect(menu).toBeFocused()

  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/new$/)
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(title)
  await choose(page, 'department', 'Department', 'Finance')
  await select(page, 'Status', 'draft')
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await cell('Description 1').fill('Paper')
  await cell('Quantity 1').fill('2')
  await cell('Unit price 1').fill('10')
  await noSideways(page)
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  // The row actions fold into one Actions menu on a phone.
  await expect(page).toHaveURL(/\/purchasing\/purchaserequests\?pageSize=100&sort=-title$/)
  const row = page.getByRole('row', { name: new RegExp(`${title} `) })
  await row.getByRole('button', { name: 'Actions', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Open', exact: true }).click()
  await expect(page.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(title)
  await expect(cell('Total amount')).toHaveValue('20.00')
  await noSideways(page)
})
