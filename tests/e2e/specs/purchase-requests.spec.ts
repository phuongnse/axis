import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

// Specs run in parallel against one database, so these tests rely only on the seeded departments and
// suppliers of the purchase request sample, and on the requests they create.
const tablePath = /\/purchasing\/purchaserequests\?pageSize=100&sort=-title$/

const appPath = '/api/apps/PurchaseRequests'
const rowsPath = `${appPath}/data-sources/PurchaseRequestList/rows`

/** The id of the seeded department named `name`, read from the record API. */
async function departmentId(request: APIRequestContext, name: string) {
  const response = await request.get(`${appPath}/entities/Department/records?pageSize=100`)
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { items: { id: string; values: { name: string } }[] }
  const department = body.items.find((item) => item.values.name === name)
  expect(department).toBeDefined()
  return department!.id
}

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

test('a purchase request is created with line items, shows the computed total, is edited, and is kept after reload, in light and dark mode', async ({
  page,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `Request ${run}`
  const cell = (label: string) => page.getByLabel(label, { exact: true })
  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')

  await page.getByRole('link', { name: 'New' }).click()
  await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/new$/)
  await page.getByRole('textbox', { name: 'Title' }).fill(title)
  await page.getByLabel('Requester', { exact: true }).fill('Lan Nguyen')
  await choose(page, 'Department', 'Finance')
  await choose(page, 'Supplier', 'Acme Supplies')
  await select(page, 'Currency', 'VND')
  await page.getByRole('button', { name: 'Add row' }).click()
  await page.getByRole('button', { name: 'Add row' }).click()
  await cell('Description 1').fill('Desk')
  await cell('Quantity 1').fill('2')
  await cell('Unit price 1').fill('1250.45')
  await cell('Description 2').fill('Cable tray')
  await cell('Quantity 2').fill('1')
  await cell('Unit price 2').fill('50.00')
  await select(page, 'Status', 'draft')
  await page.getByRole('button', { name: 'Save' }).click()

  await expect(page).toHaveURL(tablePath)
  const row = page.getByRole('row', { name: new RegExp(`${title} `) })
  for (const text of ['Lan Nguyen', 'Finance', 'Acme Supplies', 'VND', '2550.90', 'draft']) {
    await expect(row).toContainText(text)
  }
  await page.reload()
  await expect(row).toContainText('2550.90')

  // The server computes each line amount and the total on save, so the form shows them once reopened.
  await row.getByRole('link', { name: 'Open' }).click()
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue(title)
  await expect(cell('Amount 1')).toHaveValue('2500.90')
  await expect(cell('Amount 2')).toHaveValue('50.00')
  await expect(cell('Total amount')).toHaveValue('2550.90')
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
  await expect(dark.getByLabel('Total amount', { exact: true })).toHaveValue('2550.90')
  await dark.getByRole('button', { name: 'Cancel' }).click()
  await expect(page).toHaveURL(tablePath)
})

test('the purchase request list reads a data source, shows department names, and keeps paging and sorting after reload, in light and dark mode', async ({
  page,
  request,
}) => {
  // Titles of this run sort after those of the other tests and of earlier retries, so with
  // sort=-title the first ten are on page 1 and the eleventh opens page 2.
  const run = `${Date.now()}-${test.info().retry}`
  const finance = await departmentId(request, 'Finance')
  for (let index = 1; index <= 11; index++) {
    const response = await request.post(`${appPath}/entities/PurchaseRequest/records`, {
      data: {
        values: {
          title: `Zz List ${run} ${String(index).padStart(2, '0')}`,
          department: finance,
          lineItems: [{ description: 'Item', quantity: 1, unitPrice: 10 }],
        },
      },
    })
    expect(response.status()).toBe(201)
  }

  const rowsLoad = page.waitForResponse((response) => response.url().includes(rowsPath))
  await page.goto('/purchasing/purchaserequests?page=2&pageSize=10&sort=-title')
  expect((await rowsLoad).status()).toBe(200)

  const listPath = /\/purchasing\/purchaserequests\?page=2&pageSize=10&sort=-title$/
  const first = page.getByRole('row', { name: new RegExp(`Zz List ${run} 01 `) })
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode' }).click()
    }
    await page.reload()

    await expect(page.locator(`[data-theme-mode="${mode}"]`)).toBeVisible()
    await expect(page).toHaveURL(listPath)
    await expect(page.locator('.ant-pagination-item-active')).toHaveText('2')
    await expect(page.getByRole('columnheader', { name: 'Title' })).toHaveAttribute('aria-sort', 'descending')
    await expect(page.getByRole('columnheader', { name: 'Department' })).toBeVisible()
    await expect(first).toContainText('Finance')
  }
})

test('line rules show under the Quantity and Unit price of the line, and the total rule under Total amount, in light and dark mode', async ({
  page,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `Lines ${run}`
  const cell = (label: string) => page.getByLabel(label, { exact: true })
  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')
  await page.getByRole('link', { name: 'New' }).click()
  await page.getByRole('textbox', { name: 'Title' }).fill(title)
  await choose(page, 'Department', 'Finance')
  await page.getByRole('button', { name: 'Add row' }).click()
  await page.getByRole('button', { name: 'Add row' }).click()
  await cell('Description 1').fill('Desk lamp')
  await cell('Quantity 1').fill('0')
  await cell('Unit price 1').fill('10')
  await cell('Description 2').fill('Refund')
  await cell('Quantity 2').fill('1')
  await cell('Unit price 2').fill('-5')

  // One save reports both lines, and the lines add up to -5, so the total rule fires as well.
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode' }).click()
    }
    const theme = page.locator(`[data-theme-mode="${mode}"]`)
    await theme.getByRole('button', { name: 'Save' }).click()
    await expect(theme.getByTestId('field-error-lineItems-0-quantity')).toHaveText(
      'Quantity must be greater than zero.',
    )
    await expect(theme.getByTestId('field-error-lineItems-1-unitPrice')).toHaveText('Unit price cannot be negative.')
    await expect(theme.getByTestId('field-error-totalAmount')).toHaveText('Total amount must be greater than zero.')
    await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/new$/)
  }

  // A free item, with a unit price of 0, passes the unit price rule.
  const dark = page.locator('[data-theme-mode="dark"]')
  await dark.getByLabel('Quantity 1', { exact: true }).fill('1')
  await dark.getByLabel('Unit price 2', { exact: true }).fill('0')
  await dark.getByRole('button', { name: 'Save' }).click()
  await expect(page).toHaveURL(tablePath)
  await expect(page.getByRole('row', { name: new RegExp(`${title} `) })).toContainText('10.00')
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
