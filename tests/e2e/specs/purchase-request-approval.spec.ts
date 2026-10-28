import { readFileSync, writeFileSync } from 'node:fs'
import { expectShown, fieldLabel, fieldValue, shownValue } from '../fields'
import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import {
  appPath,
  createRequest,
  decide,
  env,
  expectStatus,
  purchaseOrders,
  recordsPath,
  requestNumber,
  submit,
  waitForTask,
} from '../purchase-requests'
import { useSession } from '../sessions'

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
 * Opens the request form until it shows a purchase order number, since the worker calls the
 * purchasing system after approval, and returns it. It waits up to `timeout` milliseconds.
 */
async function expectPurchaseOrderNumber(page: Page, id: string, timeout = 30_000) {
  const field = fieldValue(page, 'purchaseOrderNumber')
  await expect(async () => {
    await page.goto(`/purchasing/purchaserequestform/${id}`)
    await expect.poll(() => shownValue(field), { timeout: 1_000 }).not.toBe('Not set')
  }).toPass({ timeout })
  return shownValue(field)
}

/**
 * Waits up to `timeout` milliseconds for the form of the request `id` to show a Payment reference that
 * matches `pattern`. The sample's extension package writes it after the purchase order is created.
 */
async function expectPaymentReference(page: Page, id: string, pattern: RegExp, timeout = 30_000) {
  const field = fieldValue(page, 'paymentReference')
  await expect(async () => {
    await page.goto(`/purchasing/purchaserequestform/${id}`)
    await expectShown(field, pattern, { timeout: 1_000 })
  }).toPass({ timeout })
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
  const next = history.getByTitle('Next Page', { exact: true })
  await expect(entries.first()).toBeVisible()
  // An earlier count leaves the panel on its last page.
  const first = history.getByTitle('1', { exact: true })
  if ((await first.count()) > 0) {
    await first.click()
    await expect(history.locator('[aria-current="page"]')).toHaveText('1')
    await expect(history).toHaveAttribute('aria-busy', 'false')
  }
  let count = await matching.count()
  for (let current = 2; (await next.count()) > 0; current++) {
    if ((await next.getAttribute('aria-disabled')) === 'true') {
      break
    }
    await next.click()
    await expect(history.locator('[aria-current="page"]')).toHaveText(String(current))
    await expect(history).toHaveAttribute('aria-busy', 'false')
    count += await matching.count()
  }
  return count
}

/** Expects the history panel to list the submission by requester and a decision by each of `deciders`. */
async function expectHistory(page: Page, deciders: string[]) {
  expect(await countHistory(page, 'Process started', 'Requester User')).toBe(1)
  for (const name of deciders) {
    expect(await countHistory(page, 'Task decided', name)).toBe(1)
  }
}

test('a request below the threshold is approved by the department manager from My tasks, with a comment', async ({
  page,
}) => {
  await useSession(page, 'requester')
  const { title, id } = await createRequest(page, 'Approve small', 500)
  await submit(page, id, title)

  await useSession(page, 'chau')
  const comment = `Fine ${Date.now()}`
  await decide(page, title, 'Manager approval', 'Approve', comment)

  await expectStatus(page, id, 'approved')
  await expectShown(fieldValue(page, 'decisionComments'), comment)
  await expectHistory(page, ['Chau Operations Manager'])
  await expectOnePurchaseOrder(page, id)
  // ISO 11649: RF, two check digits, then the request number without its hyphens.
  const number = await requestNumber(page, id)
  await expectPaymentReference(page, id, new RegExp(`^RF\\d{2}${number.replaceAll('-', '')}$`))
})

test('a request whose purchase order response is lost still gets exactly one purchase order', async ({ page }) => {
  // The worker sends the call again only after the sample's 30 second backoff.
  test.setTimeout(150_000)
  await useSession(page, 'requester')
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
  await useSession(page, 'requester')
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
  await useSession(page, 'requester')
  const { title, id } = await createRequest(page, 'Return', 500)
  await submit(page, id, title)

  await useSession(page, 'chau')
  const comment = `Two desks, please ${Date.now()}`
  await decide(page, title, 'Manager approval', 'Return', comment)
  await expectStatus(page, id, 'returned')

  await useSession(page, 'requester')
  await page.goto(`/purchasing/purchaserequestform/${id}`)
  await expectShown(fieldValue(page, 'decisionComments'), comment)
  await page.getByLabel(fieldLabel('Quantity 1')).fill('2')
  // Submit saves the edit before it starts the process.
  await page.getByRole('button', { name: 'Submit', exact: true }).click()
  await expect(page.getByTestId('form-started')).toBeVisible()

  await useSession(page, 'chau')
  await decide(page, title, 'Manager approval', 'Approve')

  await expectStatus(page, id, 'approved')
  const read = await page.request.get(`${recordsPath}/${id}`)
  expect(read.ok()).toBe(true)
  const record = (await read.json()) as { values: { lineItems: { quantity: number }[] } }
  expect(record.values.lineItems.map((item) => item.quantity)).toEqual([2])
  expect(await countHistory(page, 'Process started', 'Requester User')).toBe(2)
})

test('a rejected request cannot be submitted again, and the manager gets no new task', async ({ page }) => {
  await useSession(page, 'requester')
  const { title, id } = await createRequest(page, 'Reject', 500)
  await submit(page, id, title)

  await useSession(page, 'chau')
  await decide(page, title, 'Manager approval', 'Reject')
  await expectStatus(page, id, 'rejected')
  // The process ends with the rejection, so the purchasing system is never called.
  await expectShown(fieldValue(page, 'purchaseOrderNumber'), 'Not set')
  expect(await purchaseOrders(page, await requestNumber(page, id))).toEqual([])

  await useSession(page, 'requester')
  await page.goto(`/purchasing/purchaserequestform/${id}`)
  await expect(page.getByRole('textbox', { name: 'Title', exact: true })).toHaveValue(title)
  const start = page.waitForResponse((response) =>
    response.url().endsWith('/processes/PurchaseRequestApproval/instances'),
  )
  await page.getByRole('button', { name: 'Submit', exact: true }).click()

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
  await useSession(page, 'requester')
  const { title, id } = await createRequest(page, 'Restart', 500)
  await submit(page, id, title)

  await useSession(page, 'chau')
  await waitForTask(page, title)
  await restartWorker()
  await decide(page, title, 'Manager approval', 'Approve')

  await expectStatus(page, id, 'approved')
})
