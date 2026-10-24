import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { expectChosen, pickOption } from '../select'
import { sessionFile, useSession } from '../sessions'

// The record API needs a session with a grant, so these journeys start signed in as the employee anna.
// anna saves a new request as a draft, because she may write only draft and returned requests.
test.use({ storageState: sessionFile('anna') })

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

test('a purchase request is created with line items, shows the computed total, is edited, and is kept after reload, in light and dark mode', async ({
  page,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `Request ${run}`
  const cell = (label: string) => page.getByLabel(label, { exact: true })
  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')

  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/new$/)
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(title)
  // The server sets the requester to the signed-in user, so the form shows it read-only.
  await expect(page.getByLabel('Requester', { exact: true })).toHaveAttribute('readonly')
  await choose(page, 'department', 'Department', 'Finance')
  await choose(page, 'supplier', 'Supplier', 'Acme Supplies')
  await select(page, 'Currency', 'VND')
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await cell('Description 1').fill('Desk')
  await cell('Quantity 1').fill('2')
  await cell('Unit price 1').fill('1250.45')
  await cell('Description 2').fill('Cable tray')
  await cell('Quantity 2').fill('1')
  await cell('Unit price 2').fill('50.00')
  await select(page, 'Status', 'draft')
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(tablePath)
  const row = page.getByRole('row', { name: new RegExp(`${title} `) })
  for (const text of ['anna', 'Finance', 'Acme Supplies', 'VND', '2550.90', 'draft']) {
    await expect(row).toContainText(text)
  }
  await page.reload()
  await expect(row).toContainText('2550.90')

  // The server computes each line amount and the total on save, so the form shows them once reopened.
  await row.getByRole('button', { name: 'Open', exact: true }).click()
  await expect(page.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(title)
  await expect(cell('Amount 1')).toHaveValue('2500.90')
  await expect(cell('Amount 2')).toHaveValue('50.00')
  await expect(cell('Total amount')).toHaveValue('2550.90')
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(`${title} edited`)
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(tablePath)
  const edited = page.getByRole('row', { name: new RegExp(`${title} edited`) })
  await expect(edited).toContainText('Finance')
  await page.reload()
  await expect(edited).toContainText('Finance')
  await expect(edited).toContainText('Acme Supplies')

  await page.getByRole('switch', { name: 'Dark mode', exact: true }).click()
  await edited.getByRole('button', { name: 'Open', exact: true }).click()
  const dark = page.locator('[data-theme-mode="dark"]')
  await expect(dark.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(`${title} edited`)
  await expect(dark.getByLabel('Department', { exact: true })).toHaveValue('Finance')
  await expect(dark.getByLabel('Total amount', { exact: true })).toHaveValue('2550.90')
  await dark.getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(page).toHaveURL(tablePath)
})

test('a new purchase request gets a number from the sequence on save, and the form shows it read-only', async ({
  page,
}) => {
  const title = `Numbered ${Date.now()}-${test.info().retry}`
  const number = page.getByLabel('Number', { exact: true })
  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')

  // The server numbers the request on create, so the new form shows the field empty and read-only.
  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/new$/)
  await expect(number).toHaveValue('')
  await expect(number).toHaveAttribute('readonly')
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(title)
  await choose(page, 'department', 'Department', 'Finance')
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await page.getByLabel('Description 1', { exact: true }).fill('Paper')
  await page.getByLabel('Quantity 1', { exact: true }).fill('1')
  await page.getByLabel('Unit price 1', { exact: true }).fill('10')
  await select(page, 'Status', 'draft')
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(tablePath)
  await page
    .getByRole('row', { name: new RegExp(`${title} `) })
    .getByRole('button', { name: 'Open', exact: true })
    .click()
  await expect(page.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(title)
  await expect(number).toHaveAttribute('readonly')
  await expect(number).toHaveValue(new RegExp(`^PR-${new Date().getUTCFullYear()}-\\d{5}$`))
})

test('the purchase request list reads a data source, shows department names, and keeps paging and sorting after reload, in light and dark mode', async ({
  page,
  request,
}) => {
  // Other specs title their requests in Latin letters. Titles that start with the Greek Ω sort
  // after those under any collation, and titles of this run sort after those of earlier runs.
  // So with sort=-title the first ten are on page 1 and the eleventh opens page 2. A later spec
  // that titles requests in a script sorting after Greek, such as CJK, would break this.
  const run = `${Date.now()}-${test.info().retry}`
  const prefix = `Ω List ${run}`
  const finance = await departmentId(request, 'Finance')
  // It sorts after every "Zz …" title that other specs use, so each run checks that those titles
  // never reach page 2.
  const decoy = await request.post(`${appPath}/entities/PurchaseRequest/records`, {
    data: {
      values: {
        title: `Zzzz Decoy ${run}`,
        department: finance,
        status: 'draft',
        lineItems: [{ description: 'Item', quantity: 1, unitPrice: 10 }],
      },
    },
    headers: csrfHeaders,
  })
  expect(decoy.status()).toBe(201)
  for (let index = 1; index <= 11; index++) {
    const response = await request.post(`${appPath}/entities/PurchaseRequest/records`, {
      data: {
        values: {
          title: `${prefix} ${String(index).padStart(2, '0')}`,
          department: finance,
          status: 'draft',
          lineItems: [{ description: 'Item', quantity: 1, unitPrice: 10 }],
        },
      },
      headers: csrfHeaders,
    })
    expect(response.status()).toBe(201)
  }

  const rowsLoad = page.waitForResponse((response) => response.url().includes(rowsPath))
  await page.goto('/purchasing/purchaserequests?page=2&pageSize=10&sort=-title')
  expect((await rowsLoad).status()).toBe(200)

  const listPath = /\/purchasing\/purchaserequests\?page=2&pageSize=10&sort=-title$/
  const first = page.getByRole('row').filter({ has: page.getByRole('cell') }).first()
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode', exact: true }).click()
    }
    await page.reload()

    await expect(page.locator(`[data-theme-mode="${mode}"]`)).toBeVisible()
    await expect(page).toHaveURL(listPath)
    await expect(page.getByRole('main').locator('[aria-current="page"]')).toHaveText('2')
    await expect(page.getByRole('columnheader', { name: 'Title', exact: true })).toHaveAttribute('aria-sort', 'descending')
    await expect(page.getByRole('columnheader', { name: 'Department', exact: true })).toBeVisible()
    await expect(first).toContainText(`${prefix} 01`)
    await expect(first).toContainText('Finance')
  }
})

test('the purchase request list filters by status, and keeps the filter and its rows after reload', async ({
  page,
  request,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const finance = await departmentId(request, 'Finance')
  for (const status of ['draft', 'returned']) {
    const response = await request.post(`${appPath}/entities/PurchaseRequest/records`, {
      data: {
        values: {
          title: `Zz Status ${run} ${status}`,
          department: finance,
          status,
          lineItems: [{ description: 'Item', quantity: 1, unitPrice: 10 }],
        },
      },
      headers: csrfHeaders,
    })
    expect(response.status()).toBe(201)
  }

  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')
  const draft = page.getByRole('row', { name: new RegExp(`Zz Status ${run} draft `) })
  const returned = page.getByRole('row', { name: new RegExp(`Zz Status ${run} returned `) })
  await expect(draft).toBeVisible()
  await expect(returned).toBeVisible()

  const filters = page.getByTestId('filter-bar')
  // The inline filter label ends with a colon, so its accessible name includes it.
  const statusFilter = filters.getByRole('combobox', { name: 'Status :', exact: true })
  await pickOption(statusFilter, 'returned')

  const filteredPath = /\/purchasing\/purchaserequests\?statusFilter=returned&pageSize=100&sort=-title$/
  for (const reload of [false, true]) {
    if (reload) {
      await page.reload()
    }
    await expect(page).toHaveURL(filteredPath)
    await expectChosen(statusFilter, 'returned')
    await expect(returned).toContainText('returned')
    await expect(draft).toHaveCount(0)
  }
})

test('the per-department page shows one row per department with its total, sorts by the total, and keeps the sort after reload', async ({
  page,
  request,
}) => {
  // anna submits one request in each department, so the finance reviewer dung reads them all.
  // Engineering < Finance < Operations by total, even with other tests adding Operations requests in parallel.
  const prices = { Engineering: 1, Finance: 100, Operations: 9000000 }
  const ids: string[] = []
  for (const [name, unitPrice] of Object.entries(prices)) {
    const response = await request.post(`${appPath}/entities/PurchaseRequest/records`, {
      data: {
        values: {
          title: `Zz Group ${Date.now()}-${test.info().retry} ${name}`,
          department: await departmentId(request, name),
          status: 'draft',
          lineItems: [{ description: 'Item', quantity: 1, unitPrice }],
        },
      },
      headers: csrfHeaders,
    })
    expect(response.status()).toBe(201)
    const { id } = (await response.json()) as { id: string }
    const started = await request.post(`${appPath}/processes/PurchaseRequestApproval/instances`, {
      data: { subjectId: id },
      headers: csrfHeaders,
    })
    expect(started.status()).toBe(201)
    ids.push(id)
  }
  // The worker submits each request some time after its start.
  for (const id of ids) {
    await expect
      .poll(
        async () => {
          const read = await request.get(`${appPath}/entities/PurchaseRequest/records/${id}`)
          return ((await read.json()) as { values: { status: string } }).values.status
        },
        { timeout: 30_000 },
      )
      .toBe('submitted')
  }

  // Only this test submits Engineering requests, and no one decides them, so their total stays fixed
  // during the run. The approval spec adds Operations requests in parallel, so the Operations total is
  // not checked.
  await useSession(page, 'dung')
  const groups = await page.request.get(`${appPath}/data-sources/RequestsByDepartment/rows?pageSize=100`)
  expect(groups.status()).toBe(200)
  const body = (await groups.json()) as {
    items: { id: null; values: { total: number }; labels: { department: string } }[]
  }
  const total = (name: string) => {
    const group = body.items.find((item) => item.labels.department === name)
    expect(group).toBeDefined()
    return Number(group!.values.total).toFixed(2)
  }
  const engineering = total('Engineering')

  await page.goto('/purchasing/requestsbydepartment')
  const rows = page.getByRole('row').filter({ has: page.getByRole('cell') })
  await expect(rows).toHaveCount(3)
  const row = (name: string) => page.getByRole('row', { name: new RegExp(`^${name} `) })
  await expect(row('Engineering')).toContainText(engineering)
  await expect(row('Operations')).toBeVisible()
  await expect(row('Finance')).toBeVisible()
  await expect(page.getByRole('button', { name: /Open/ })).toHaveCount(0)
  await expect(page.getByRole('button', { name: /New/ })).toHaveCount(0)

  const order = async (names: string[]) => {
    for (const [index, name] of names.entries()) {
      await expect(rows.nth(index)).toContainText(name)
    }
  }
  await page.getByRole('columnheader', { name: 'total', exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/requestsbydepartment\?sort=total$/)
  await order(['Engineering', 'Finance', 'Operations'])

  await page.getByRole('columnheader', { name: 'total', exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/requestsbydepartment\?sort=-total$/)
  await order(['Operations', 'Finance', 'Engineering'])
  await page.reload()
  await expect(page.getByRole('columnheader', { name: 'total', exact: true })).toHaveAttribute('aria-sort', 'descending')
  await order(['Operations', 'Finance', 'Engineering'])
})

test('line rules show under the Quantity and Unit price of the line, and the total rule under Total amount, in light and dark mode', async ({
  page,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `Lines ${run}`
  const cell = (label: string) => page.getByLabel(label, { exact: true })
  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')
  await page.getByRole('button', { name: 'New', exact: true }).click()
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(title)
  await choose(page, 'department', 'Department', 'Finance')
  await select(page, 'Status', 'draft')
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await cell('Description 1').fill('Desk lamp')
  await cell('Quantity 1').fill('0')
  await cell('Unit price 1').fill('10')
  await cell('Description 2').fill('Refund')
  await cell('Quantity 2').fill('1')
  await cell('Unit price 2').fill('-5')

  // One save reports both lines, and the lines add up to -5, so the total rule fires as well.
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode', exact: true }).click()
    }
    const theme = page.locator(`[data-theme-mode="${mode}"]`)
    await theme.getByRole('button', { name: /^(loading )?Save$/ }).click()
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
  // Ant Design adds "loading" to the name while the button is busy, and for a moment after the failed save.
  await dark.getByRole('button', { name: /^(loading )?Save$/ }).click()
  await expect(page).toHaveURL(tablePath)
  await expect(page.getByRole('row', { name: new RegExp(`${title} `) })).toContainText('10.00')
})

test('searching in the supplier lookup filters the suppliers by name, and picking one sets the field', async ({
  page,
}) => {
  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')
  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/new$/)

  await page.getByTestId('reference-supplier').getByRole('button', { name: 'Choose', exact: true }).click()
  const dialog = await waitForDialog(page)
  await expect(dialog.getByRole('row', { name: /Acme Supplies/ })).toBeVisible()
  await dialog.getByLabel('Search', { exact: true }).fill('glob')

  // The search ignores case, so `glob` finds Globex.
  await expect(dialog.getByRole('row', { name: /Globex/ })).toBeVisible()
  await expect(dialog.getByRole('row', { name: /Acme Supplies/ })).toHaveCount(0)
  await dialog.getByRole('row', { name: /Globex/ }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByLabel('Supplier', { exact: true })).toHaveValue('Globex')
})

test('switching the locale to Vietnamese shows the Vietnamese navigation', async ({ page }) => {
  // The per-department page is not in the employee's navigation, so this test runs as the finance reviewer.
  await useSession(page, 'dung')
  // The header actions take room. At the default width they can push the last Vietnamese
  // navigation entry into the overflow menu, so this test uses a wider window.
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/purchasing/purchaserequests')
  await expect(page.getByRole('menuitem', { name: 'Purchase requests', exact: true })).toBeVisible()

  // The radio input itself takes no pointer events: people click its label.
  await page.getByRole('radiogroup', { name: 'Language', exact: true }).getByText('Tiếng Việt', { exact: true }).click()

  // Exact names, because "Theo phòng ban" also holds "Phòng ban".
  for (const item of ['Yêu cầu mua hàng', 'Theo phòng ban', 'Phòng ban', 'Nhà cung cấp']) {
    await expect(page.getByRole('menuitem', { name: item, exact: true })).toBeVisible()
  }
})
