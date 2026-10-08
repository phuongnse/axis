import { expect, test, type Page } from '@playwright/test'

// Specs run in parallel against one database, so these tests rely only on the seeded departments and
// suppliers of the purchase request sample, and on the requests they create.
const tablePath = /\/purchasing\/purchaserequests\?pageSize=100&sort=-title$/

/** Opens the lookup of the reference field labelled `label` and picks the row named `name`. */
async function choose(page: Page, label: string, name: string) {
  const field = page.locator('.ant-form-item', { has: page.getByLabel(label, { exact: true }) })
  await field.getByRole('button', { name: 'Choose' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('row', { name: new RegExp(name) }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByLabel(label, { exact: true })).toHaveValue(name)
}

/** Opens a select and picks the option with the given value. */
async function select(page: Page, label: string, value: string) {
  await page.getByLabel(label, { exact: true }).click()
  await page.locator(`.ant-select-item-option[title="${value}"]`).click()
}

test('a purchase request is created with a seeded department and supplier, edited, and kept after reload, in light and dark mode', async ({
  page,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `Request ${run}`
  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')

  await page.getByRole('link', { name: 'New' }).click()
  await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/new$/)
  await page.getByRole('textbox', { name: 'Title' }).fill(title)
  await page.getByLabel('Requester', { exact: true }).fill('Lan Nguyen')
  await choose(page, 'Department', 'Finance')
  await choose(page, 'Supplier', 'Acme Supplies')
  await select(page, 'Currency', 'VND')
  await page.getByLabel('Total amount', { exact: true }).fill('1500.00')
  await select(page, 'Status', 'draft')
  await page.getByRole('button', { name: 'Save' }).click()

  await expect(page).toHaveURL(tablePath)
  const row = page.getByRole('row', { name: new RegExp(`${title} `) })
  for (const text of ['Lan Nguyen', 'Finance', 'Acme Supplies', 'VND', '1500.00', 'draft']) {
    await expect(row).toContainText(text)
  }

  await row.getByRole('link', { name: 'Open' }).click()
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue(title)
  await page.getByRole('textbox', { name: 'Title' }).fill(`${title} edited`)
  await page.getByRole('button', { name: 'Save' }).click()

  await expect(page).toHaveURL(tablePath)
  const edited = page.getByRole('row', { name: new RegExp(`${title} edited`) })
  await expect(edited).toContainText('Finance')
  await page.reload()
  await expect(edited).toContainText('Finance')
  await expect(edited).toContainText('Acme Supplies')

  await page.getByRole('switch', { name: 'Dark mode' }).click()
  await edited.getByRole('link', { name: 'Open' }).click()
  const dark = page.locator('[data-theme-mode="dark"]')
  await expect(dark.getByRole('textbox', { name: 'Title' })).toHaveValue(`${title} edited`)
  await expect(dark.getByLabel('Department', { exact: true })).toHaveValue('Finance')
  await dark.getByRole('button', { name: 'Cancel' }).click()
  await expect(page).toHaveURL(tablePath)
})

test('a total amount of zero or less shows the sample rule under Total amount, in light and dark mode', async ({ page }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `Negative ${run}`
  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')
  await page.getByRole('link', { name: 'New' }).click()
  await page.getByRole('textbox', { name: 'Title' }).fill(title)
  await choose(page, 'Department', 'Finance')
  await page.getByLabel('Total amount', { exact: true }).fill('-1')
  await page.getByRole('button', { name: 'Save' }).click()

  const error = page.getByTestId('field-error-totalAmount')
  await expect(error).toHaveText('Total amount must be greater than zero.')
  await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/new$/)

  await page.getByRole('switch', { name: 'Dark mode' }).click()
  const dark = page.locator('[data-theme-mode="dark"]')
  await dark.getByRole('button', { name: 'Save' }).click()
  await expect(dark.getByTestId('field-error-totalAmount')).toHaveText('Total amount must be greater than zero.')
  await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/new$/)

  await dark.getByLabel('Total amount', { exact: true }).fill('1')
  await dark.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(tablePath)
  await expect(page.getByRole('row', { name: new RegExp(`${title} `) })).toContainText('1.00')
})

test('switching the locale to Vietnamese shows the Vietnamese navigation', async ({ page }) => {
  await page.goto('/purchasing/purchaserequests')
  await expect(page.getByRole('menuitem', { name: 'Purchase requests' })).toBeVisible()

  // The radio input itself takes no pointer events: people click its label.
  await page.getByRole('radiogroup', { name: 'Language' }).getByText('Tiếng Việt').click()

  for (const item of ['Yêu cầu mua hàng', 'Phòng ban', 'Nhà cung cấp']) {
    await expect(page.getByRole('menuitem', { name: item })).toBeVisible()
  }
})
