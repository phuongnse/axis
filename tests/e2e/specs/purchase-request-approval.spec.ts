import { readFileSync, writeFileSync } from 'node:fs'
import { expect, test, type Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { useSession } from '../sessions'

// Specs run in parallel against one database, so each test looks only for the request and the tasks
// it creates. They use Operations, whose manager is chau, so that the per-department test keeps the
// Engineering total fixed.
const appPath = '/api/apps/PurchaseRequests'
const recordsPath = `${appPath}/entities/PurchaseRequest/records`

/** The id of the seeded department named `name`, read from the record API. */
async function departmentId(page: Page, name: string) {
  const response = await page.request.get(`${appPath}/entities/Department/records?pageSize=100`)
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { items: { id: string; values: { name: string } }[] }
  const department = body.items.find((item) => item.values.name === name)
  expect(department).toBeDefined()
  return department!.id
}

/** Creates a draft Operations request with one line of `unitPrice`, and returns its title and id. */
async function createRequest(page: Page, prefix: string, unitPrice: number) {
  const title = `${prefix} ${Date.now()}-${test.info().retry}`
  const response = await page.request.post(recordsPath, {
    data: {
      values: {
        title,
        status: 'draft',
        department: await departmentId(page, 'Operations'),
        lineItems: [{ description: 'Item', quantity: 1, unitPrice }],
      },
    },
    headers: csrfHeaders,
  })
  expect(response.status()).toBe(201)
  return { title, id: ((await response.json()) as { id: string }).id }
}

/** Opens the request form and submits the request. */
async function submit(page: Page, id: string, title: string) {
  await page.goto(`/purchasing/purchaserequestform/${id}`)
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue(title)
  await page.getByRole('button', { name: 'Submit' }).click()
  await expect(page.getByTestId('form-started')).toBeVisible()
}

/** Opens My tasks until it lists a task for the request titled `title`, and returns its row. */
async function waitForTask(page: Page, title: string) {
  const row = page.getByRole('row', { name: new RegExp(title) })
  // The worker creates the task some time after the start or the previous decision.
  await expect(async () => {
    await page.goto('/purchasing/mytasks?pageSize=100')
    await expect(row).toBeVisible({ timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
  return row
}

/**
 * Opens the task named `task` for the request titled `title` from My tasks, and decides it with
 * `outcome` and an optional comment.
 */
async function decide(
  page: Page,
  title: string,
  task: string,
  outcome: 'Approve' | 'Return' | 'Reject',
  comment?: string,
) {
  const row = await waitForTask(page, title)
  await row.getByRole('link', { name: task }).click()
  await expect(page.getByLabel('Title', { exact: true })).toHaveValue(title)
  if (comment !== undefined) {
    await page.getByLabel('Decision comments', { exact: true }).fill(comment)
  }
  await page.getByRole('button', { name: outcome }).click()
  await expect(page).toHaveURL(/\/purchasing\/mytasks$/)
}

/** The value of the environment variable `name`, which scripts/e2e.sh sets. */
function env(name: string) {
  const value = process.env[name]
  if (!value) {
    throw new Error(`${name} is not set. Run the E2E tests with scripts/e2e.sh.`)
  }
  return value
}

/**
 * Stops the E2E worker process and waits until scripts/e2e.sh has started another one and it is
 * ready for work. The restart file tells the script that the stop is asked for.
 */
async function restartWorker() {
  const log = env('AXIS_E2E_WORKER_LOG')
  const pidFile = env('AXIS_E2E_WORKER_PID_FILE')
  const restartFile = env('AXIS_E2E_WORKER_RESTART_FILE')
  const readyCount = () => readFileSync(log, 'utf8').split('Tenant default is ready for work').length - 1
  const before = readyCount()
  const pid = Number(readFileSync(pidFile, 'utf8').trim())
  writeFileSync(restartFile, '')
  process.kill(pid, 'SIGTERM')
  await expect.poll(readyCount, { timeout: 60_000 }).toBeGreaterThan(before)
  expect(Number(readFileSync(pidFile, 'utf8').trim())).not.toBe(pid)
}

/**
 * The Status input. The managers and the finance reviewer may not update a request, so they see
 * it read-only.
 */
function statusValue(page: Page) {
  return page.getByLabel('Status', { exact: true })
}

/** Opens the request form until it shows `status`, since the worker ends the process later. */
async function expectStatus(page: Page, id: string, status: string) {
  await expect(async () => {
    await page.goto(`/purchasing/purchaserequestform/${id}`)
    await expect(statusValue(page)).toHaveValue(status, { timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
}

/** The request's number, read through the record API. */
async function requestNumber(page: Page, id: string) {
  const read = await page.request.get(`${recordsPath}/${id}`)
  expect(read.ok()).toBe(true)
  const number = ((await read.json()) as { values: { number: string | null } }).values.number
  expect(number).toBeTruthy()
  return number!
}

/**
 * Opens the request form until it shows a purchase order number, since the worker calls the
 * purchasing system after approval, and returns it. It waits up to `timeout` milliseconds.
 */
async function expectPurchaseOrderNumber(page: Page, id: string, timeout = 30_000) {
  const field = page.getByLabel('Purchase order number', { exact: true })
  await expect(async () => {
    await page.goto(`/purchasing/purchaserequestform/${id}`)
    await expect(field).not.toHaveValue('', { timeout: 1_000 })
  }).toPass({ timeout })
  return field.inputValue()
}

/**
 * Waits up to `timeout` milliseconds for the form of the request `id` to show a Payment reference that
 * matches `pattern`. The sample's extension package writes it after the purchase order is created.
 */
async function expectPaymentReference(page: Page, id: string, pattern: RegExp, timeout = 30_000) {
  const field = page.getByLabel('Payment reference', { exact: true })
  await expect(async () => {
    await page.goto(`/purchasing/purchaserequestform/${id}`)
    await expect(field).toHaveValue(pattern, { timeout: 1_000 })
  }).toPass({ timeout })
}

/** The orders that the purchasing system stub holds for the request numbered `number`. */
async function purchaseOrders(page: Page, number: string) {
  const response = await page.request.get(`${env('AXIS_E2E_PURCHASING_URL')}/purchase-orders`)
  expect(response.ok()).toBe(true)
  const orders = (await response.json()) as { requestNumber: string; orderNumber: string }[]
  return orders.filter((order) => order.requestNumber === number)
}

/**
 * Expects the request to show a purchase order number within `timeout` milliseconds, and the stub to
 * hold exactly that one order for it.
 */
async function expectOnePurchaseOrder(page: Page, id: string, timeout = 30_000) {
  const orderNumber = await expectPurchaseOrderNumber(page, id, timeout)
  const number = await requestNumber(page, id)
  expect((await purchaseOrders(page, number)).map((order) => order.orderNumber)).toEqual([orderNumber])
}

/**
 * Counts the history panel entries that hold every one of `texts`, on all pages of the panel. The
 * purchase order call adds entries, so an approved request's history can take more than one page.
 */
async function countHistory(page: Page, ...texts: string[]) {
  const history = page.getByTestId('record-history')
  const entries = history.getByTestId('history-entry')
  const matching = texts.reduce((locator, text) => locator.filter({ hasText: text }), entries)
  const next = history.locator('.ant-pagination-next')
  await expect(entries.first()).toBeVisible()
  // An earlier count leaves the panel on its last page.
  const first = history.locator('.ant-pagination-item-1')
  if ((await first.count()) > 0) {
    await first.click()
    await expect(history.locator('.ant-pagination-item-active')).toHaveText('1')
    await expect(history.locator('.ant-spin-spinning')).toHaveCount(0)
  }
  let count = await matching.count()
  for (let current = 2; (await next.count()) > 0; current++) {
    if ((await next.getAttribute('class'))?.includes('ant-pagination-disabled')) {
      break
    }
    await next.click()
    await expect(history.locator('.ant-pagination-item-active')).toHaveText(String(current))
    await expect(history.locator('.ant-spin-spinning')).toHaveCount(0)
    count += await matching.count()
  }
  return count
}

/** Expects the history panel to list the submission by anna and a decision by each of `deciders`. */
async function expectHistory(page: Page, deciders: string[]) {
  expect(await countHistory(page, 'Process started', 'Anna Employee')).toBe(1)
  for (const name of deciders) {
    expect(await countHistory(page, 'Task decided', name)).toBe(1)
  }
}

test('a request below the threshold is approved by the department manager from My tasks, with a comment', async ({
  page,
}) => {
  await useSession(page, 'anna')
  const { title, id } = await createRequest(page, 'Approve small', 500)
  await submit(page, id, title)

  await useSession(page, 'chau')
  const comment = `Fine ${Date.now()}`
  await decide(page, title, 'Manager approval', 'Approve', comment)

  await expectStatus(page, id, 'approved')
  await expect(page.getByLabel('Decision comments', { exact: true })).toHaveValue(comment)
  await expectHistory(page, ['Chau Operations Manager'])
  await expectOnePurchaseOrder(page, id)
  // ISO 11649: RF, two check digits, then the request number without its hyphens.
  const number = await requestNumber(page, id)
  await expectPaymentReference(page, id, new RegExp(`^RF\\d{2}${number.replaceAll('-', '')}$`))
})

test('a request whose purchase order response is lost still gets exactly one purchase order', async ({ page }) => {
  // The worker sends the call again only after the sample's 30 second backoff.
  test.setTimeout(150_000)
  await useSession(page, 'anna')
  const { title, id } = await createRequest(page, 'Lost response', 500)
  const number = await requestNumber(page, id)
  // The stub stores the order for this request, then closes the connection without answering.
  const drop = await page.request.post(`${env('AXIS_E2E_PURCHASING_URL')}/test/drop-request/${number}`)
  expect(drop.status()).toBe(204)
  await submit(page, id, title)

  await useSession(page, 'chau')
  await decide(page, title, 'Manager approval', 'Approve')

  await expectStatus(page, id, 'approved')
  await expectOnePurchaseOrder(page, id, 90_000)
})

test('a request at the threshold needs the finance reviewer after the manager before it is approved', async ({
  page,
}) => {
  await useSession(page, 'anna')
  const { title, id } = await createRequest(page, 'Approve large', 10000)
  await submit(page, id, title)

  await useSession(page, 'chau')
  await decide(page, title, 'Manager approval', 'Approve')
  const read = await page.request.get(`${recordsPath}/${id}`)
  expect(read.ok()).toBe(true)
  expect(((await read.json()) as { values: { status: string } }).values.status).toBe('submitted')

  await useSession(page, 'dung')
  await decide(page, title, 'Finance approval', 'Approve')

  await expectStatus(page, id, 'approved')
  await expectHistory(page, ['Chau Operations Manager', 'Dung Finance Reviewer'])
  await expectOnePurchaseOrder(page, id)
})

test('a returned request is edited and submitted again, and the manager then approves it', async ({ page }) => {
  await useSession(page, 'anna')
  const { title, id } = await createRequest(page, 'Return', 500)
  await submit(page, id, title)

  await useSession(page, 'chau')
  const comment = `Two desks, please ${Date.now()}`
  await decide(page, title, 'Manager approval', 'Return', comment)
  await expectStatus(page, id, 'returned')

  await useSession(page, 'anna')
  await page.goto(`/purchasing/purchaserequestform/${id}`)
  await expect(page.getByLabel('Decision comments', { exact: true })).toHaveValue(comment)
  await page.getByLabel('Quantity 1', { exact: true }).fill('2')
  // Submit saves the edit before it starts the process.
  await page.getByRole('button', { name: 'Submit' }).click()
  await expect(page.getByTestId('form-started')).toBeVisible()

  await useSession(page, 'chau')
  await decide(page, title, 'Manager approval', 'Approve')

  await expectStatus(page, id, 'approved')
  const read = await page.request.get(`${recordsPath}/${id}`)
  expect(read.ok()).toBe(true)
  const record = (await read.json()) as { values: { lineItems: { quantity: number }[] } }
  expect(record.values.lineItems.map((item) => item.quantity)).toEqual([2])
  expect(await countHistory(page, 'Process started', 'Anna Employee')).toBe(2)
})

test('a rejected request cannot be submitted again, and the manager gets no new task', async ({ page }) => {
  await useSession(page, 'anna')
  const { title, id } = await createRequest(page, 'Reject', 500)
  await submit(page, id, title)

  await useSession(page, 'chau')
  await decide(page, title, 'Manager approval', 'Reject')
  await expectStatus(page, id, 'rejected')
  // The process ends with the rejection, so the purchasing system is never called.
  await expect(page.getByLabel('Purchase order number', { exact: true })).toHaveValue('')
  expect(await purchaseOrders(page, await requestNumber(page, id))).toEqual([])

  await useSession(page, 'anna')
  await page.goto(`/purchasing/purchaserequestform/${id}`)
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue(title)
  const start = page.waitForResponse((response) =>
    response.url().endsWith('/processes/PurchaseRequestApproval/instances'),
  )
  await page.getByRole('button', { name: 'Submit' }).click()

  expect((await start).status()).toBe(400)
  await expect(page.getByTestId('form-error')).toHaveText(
    'Only a draft or returned request with a total can be submitted.',
  )
  await expect(page.getByTestId('form-started')).toHaveCount(0)

  await useSession(page, 'chau')
  const tasks = await page.request.get(`${appPath}/tasks?pageSize=100`)
  expect(tasks.status()).toBe(200)
  const { items } = (await tasks.json()) as { items: { subject: { id: string } }[] }
  expect(items.filter((item) => item.subject.id === id)).toEqual([])
})

test('a request waiting for the manager is approved after the worker restarts', async ({ page }) => {
  await useSession(page, 'anna')
  const { title, id } = await createRequest(page, 'Restart', 500)
  await submit(page, id, title)

  await useSession(page, 'chau')
  await waitForTask(page, title)
  await restartWorker()
  await decide(page, title, 'Manager approval', 'Approve')

  await expectStatus(page, id, 'approved')
})
