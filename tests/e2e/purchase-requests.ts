import { expect, test, type Page } from '@playwright/test'
import { csrfHeaders } from './csrf'

// Specs run in parallel against one database, so each test looks only for the request and the tasks
// it creates. They use Operations, whose manager is chau, so that the per-department test keeps the
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
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue(title)
  await page.getByRole('button', { name: 'Submit' }).click()
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
  await row.getByRole('link', { name: task }).click()
  await expect(page.getByLabel('Title', { exact: true })).toHaveValue(title)
  if (comment !== undefined) {
    await page.getByLabel('Decision comments', { exact: true }).fill(comment)
  }
  await page.getByRole('button', { name: outcome }).click()
  await expect(page).toHaveURL(/\/purchasing\/mytasks$/)
}

/**
 * The Status input. The managers and the finance reviewer may not update a request, so they see
 * it read-only.
 */
export function statusValue(page: Page) {
  return page.getByLabel('Status', { exact: true })
}

/** Opens the request form until it shows `status`, since the worker ends the process later. */
export async function expectStatus(page: Page, id: string, status: string) {
  await expect(async () => {
    await page.goto(`/purchasing/purchaserequestform/${id}`)
    await expect(statusValue(page)).toHaveValue(status, { timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
}
