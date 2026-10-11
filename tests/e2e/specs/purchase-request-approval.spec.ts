import { readFileSync, writeFileSync } from 'node:fs'
import { expect, test, type Page } from '@playwright/test'

// Specs run in parallel against one database, so each test looks only for the request and the tasks
// it creates. They use Operations, whose manager is chau, so that the per-department test keeps the
// Engineering total fixed.
const appPath = '/api/apps/PurchaseRequests'
const recordsPath = `${appPath}/entities/PurchaseRequest/records`

// The page's request context shares its cookies, so this signs the browser in.
async function signIn(page: Page, id: string) {
  expect((await page.request.post('/api/test-users/sign-in', { data: { id } })).ok()).toBe(true)
}

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

/** The value the Status select shows. */
function statusValue(page: Page) {
  return page
    .locator('.ant-form-item')
    .filter({ has: page.getByText('Status', { exact: true }) })
    .locator('.ant-select-selection-item')
}

/** Opens the request form until it shows `status`, since the worker ends the process later. */
async function expectStatus(page: Page, id: string, status: string) {
  await expect(async () => {
    await page.goto(`/purchasing/purchaserequestform/${id}`)
    await expect(statusValue(page)).toHaveText(status, { timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
}

/** Expects the history panel to list the submission by anna and a decision by each of `deciders`. */
async function expectHistory(page: Page, deciders: string[]) {
  const entries = page.getByTestId('record-history').getByTestId('history-entry')
  await expect(entries.filter({ hasText: 'Process started' }).filter({ hasText: 'Anna Employee' })).toHaveCount(1)
  for (const name of deciders) {
    await expect(entries.filter({ hasText: 'Task decided' }).filter({ hasText: name })).toHaveCount(1)
  }
}

test('a request below the threshold is approved by the department manager from My tasks, with a comment', async ({
  page,
}) => {
  await signIn(page, 'anna')
  const { title, id } = await createRequest(page, 'Approve small', 500)
  await submit(page, id, title)

  await signIn(page, 'chau')
  const comment = `Fine ${Date.now()}`
  await decide(page, title, 'Manager approval', 'Approve', comment)

  await expectStatus(page, id, 'approved')
  await expect(page.getByLabel('Decision comments', { exact: true })).toHaveValue(comment)
  await expectHistory(page, ['Chau Operations Manager'])
})

test('a request at the threshold needs the finance reviewer after the manager before it is approved', async ({
  page,
}) => {
  await signIn(page, 'anna')
  const { title, id } = await createRequest(page, 'Approve large', 10000)
  await submit(page, id, title)

  await signIn(page, 'chau')
  await decide(page, title, 'Manager approval', 'Approve')
  const read = await page.request.get(`${recordsPath}/${id}`)
  expect(read.ok()).toBe(true)
  expect(((await read.json()) as { values: { status: string } }).values.status).toBe('submitted')

  await signIn(page, 'dung')
  await decide(page, title, 'Finance approval', 'Approve')

  await expectStatus(page, id, 'approved')
  await expectHistory(page, ['Chau Operations Manager', 'Dung Finance Reviewer'])
})

test('a returned request is edited and submitted again, and the manager then approves it', async ({ page }) => {
  await signIn(page, 'anna')
  const { title, id } = await createRequest(page, 'Return', 500)
  await submit(page, id, title)

  await signIn(page, 'chau')
  const comment = `Two desks, please ${Date.now()}`
  await decide(page, title, 'Manager approval', 'Return', comment)
  await expectStatus(page, id, 'returned')

  await signIn(page, 'anna')
  await page.goto(`/purchasing/purchaserequestform/${id}`)
  await expect(page.getByLabel('Decision comments', { exact: true })).toHaveValue(comment)
  await page.getByLabel('Quantity 1', { exact: true }).fill('2')
  // Submit saves the edit before it starts the process.
  await page.getByRole('button', { name: 'Submit' }).click()
  await expect(page.getByTestId('form-started')).toBeVisible()

  await signIn(page, 'chau')
  await decide(page, title, 'Manager approval', 'Approve')

  await expectStatus(page, id, 'approved')
  const read = await page.request.get(`${recordsPath}/${id}`)
  expect(read.ok()).toBe(true)
  const record = (await read.json()) as { values: { lineItems: { quantity: number }[] } }
  expect(record.values.lineItems.map((item) => item.quantity)).toEqual([2])
  const entries = page.getByTestId('record-history').getByTestId('history-entry')
  await expect(entries.filter({ hasText: 'Process started' }).filter({ hasText: 'Anna Employee' })).toHaveCount(2)
})

test('a rejected request cannot be submitted again, and the manager gets no new task', async ({ page }) => {
  await signIn(page, 'anna')
  const { title, id } = await createRequest(page, 'Reject', 500)
  await submit(page, id, title)

  await signIn(page, 'chau')
  await decide(page, title, 'Manager approval', 'Reject')
  await expectStatus(page, id, 'rejected')

  await signIn(page, 'anna')
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

  await signIn(page, 'chau')
  const tasks = await page.request.get(`${appPath}/tasks?pageSize=100`)
  expect(tasks.status()).toBe(200)
  const { items } = (await tasks.json()) as { items: { subject: { id: string } }[] }
  expect(items.filter((item) => item.subject.id === id)).toEqual([])
})

test('a request waiting for the manager is approved after the worker restarts', async ({ page }) => {
  await signIn(page, 'anna')
  const { title, id } = await createRequest(page, 'Restart', 500)
  await submit(page, id, title)

  await signIn(page, 'chau')
  await waitForTask(page, title)
  await restartWorker()
  await decide(page, title, 'Manager approval', 'Approve')

  await expectStatus(page, id, 'approved')
})
