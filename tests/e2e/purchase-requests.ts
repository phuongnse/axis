import { expect, test } from './test'
import type { Page } from '@playwright/test'
import { csrfHeaders } from './csrf'
import { expectShown, fieldLabel, fieldValue, shownValue } from './fields'

// Specs run in parallel against one database, so each test looks only for the request and the tasks
// it creates. They use Operations, whose manager is manager-2, so that the per-department test keeps the
// Engineering total fixed.
export const appPath = '/api/apps/PurchaseRequests'
export const recordsPath = `${appPath}/entities/PurchaseRequest/records`

/** The id of the seeded department named `name`, read from the record API. */
export async function departmentId(page: Page, name: string) {
  const response = await page.request.get(`${appPath}/entities/Department/records?pageSize=100`)
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { items: { id: string; values: { name: string } }[] }
  const department = body.items.find((item) => item.values.name === name)
  expect(department).toBeDefined()
  return department!.id
}

/** Creates a draft Operations request with one line of `unitPrice`, and returns its title and id. */
export async function createRequest(page: Page, prefix: string, unitPrice: number) {
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
export async function submit(page: Page, id: string, title: string) {
  await page.goto(`/purchasing/purchaserequestform/${id}`)
  await expect(page.getByRole('textbox', { name: 'Title', exact: true })).toHaveValue(title)
  await page.getByRole('button', { name: 'Submit', exact: true }).click()
  await expect(page.getByTestId('form-started')).toBeVisible()
}

/** Opens My tasks until it lists a task for the request titled `title`, and returns its row. */
export async function waitForTask(page: Page, title: string) {
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
export async function decide(
  page: Page,
  title: string,
  task: string,
  outcome: 'Approve' | 'Return' | 'Reject',
  comment?: string,
) {
  const row = await waitForTask(page, title)
  await row.getByRole('link', { name: task, exact: true }).click()
  await expectShown(fieldValue(page, 'title'), title)
  if (comment !== undefined) {
    await page.getByLabel(fieldLabel('Decision comments')).fill(comment)
  }
  await page.getByRole('button', { name: outcome, exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/mytasks$/)
}

/**
 * The Status input. The managers and the finance reviewer may not update a request, so they see
 * it read-only.
 */
export function statusValue(page: Page) {
  return fieldValue(page, 'status')
}

/** Opens the request form until it shows `status`, since the worker ends the process later. */
export async function expectStatus(page: Page, id: string, status: string) {
  await expect(async () => {
    await page.goto(`/purchasing/purchaserequestform/${id}`)
    await expectShown(statusValue(page), status, { timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
}

/**
 * Opens the request form until it shows a purchase order number, since the worker calls the
 * purchasing system after approval, and returns it. It waits up to `timeout` milliseconds.
 */
export async function expectPurchaseOrderNumber(page: Page, id: string, timeout = 30_000) {
  const field = fieldValue(page, 'purchaseOrderNumber')
  await expect(async () => {
    await page.goto(`/purchasing/purchaserequestform/${id}`)
    await expect.poll(() => shownValue(field), { timeout: 1_000 }).not.toBe('Not set')
  }).toPass({ timeout })
  return shownValue(field)
}

/** The value of the environment variable `name`, which scripts/e2e.sh sets. */
export function env(name: string) {
  const value = process.env[name]
  if (!value) {
    throw new Error(`${name} is not set. Run the E2E tests with scripts/e2e.sh.`)
  }
  return value
}

/** The request's number, read through the record API. */
export async function requestNumber(page: Page, id: string) {
  const read = await page.request.get(`${recordsPath}/${id}`)
  expect(read.ok()).toBe(true)
  const number = ((await read.json()) as { values: { number: string | null } }).values.number
  expect(number).toBeTruthy()
  return number!
}

/** The orders that the purchasing system stub holds for the request numbered `number`. */
export async function purchaseOrders(page: Page, number: string) {
  const response = await page.request.get(`${env('AXIS_E2E_PURCHASING_URL')}/purchase-orders`)
  expect(response.ok()).toBe(true)
  const orders = (await response.json()) as {
    requestNumber: string
    orderNumber: string
    clientId: string
    lines: { description: string; quantity: number; unitPrice: number; amount: number }[]
  }[]
  return orders.filter((order) => order.requestNumber === number)
}

/** The deliveries of the approved webhook that the purchasing system stub received for the request numbered `number`. */
export async function webhookDeliveries(page: Page, number: string) {
  const response = await page.request.get(`${env('AXIS_E2E_PURCHASING_URL')}/webhooks/purchase-request-approved`)
  expect(response.ok()).toBe(true)
  const deliveries = (await response.json()) as { webhookId: string; type: string; requestNumber: string }[]
  return deliveries.filter((delivery) => delivery.requestNumber === number)
}
