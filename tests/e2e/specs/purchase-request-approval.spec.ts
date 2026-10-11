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

/** Opens the task named `task` for the request titled `title` from My tasks, and approves it. */
async function approve(page: Page, title: string, task: string, comment?: string) {
  const row = page.getByRole('row', { name: new RegExp(title) })
  // The worker creates the task some time after the start or the previous decision.
  await expect(async () => {
    await page.goto('/purchasing/mytasks?pageSize=100')
    await expect(row).toBeVisible({ timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
  await row.getByRole('link', { name: task }).click()
  await expect(page.getByLabel('Title', { exact: true })).toHaveValue(title)
  if (comment !== undefined) {
    await page.getByLabel('Decision comments', { exact: true }).fill(comment)
  }
  await page.getByRole('button', { name: 'Approve' }).click()
  await expect(page).toHaveURL(/\/purchasing\/mytasks$/)
}

/** The value the Status select shows. */
function statusValue(page: Page) {
  return page
    .locator('.ant-form-item')
    .filter({ has: page.getByText('Status', { exact: true }) })
    .locator('.ant-select-selection-item')
}

/** Opens the request form until it shows `approved`, since the worker ends the process later. */
async function expectApproved(page: Page, id: string) {
  await expect(async () => {
    await page.goto(`/purchasing/purchaserequestform/${id}`)
    await expect(statusValue(page)).toHaveText('approved', { timeout: 1_000 })
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
  await approve(page, title, 'Manager approval', comment)

  await expectApproved(page, id)
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
  await approve(page, title, 'Manager approval')
  const read = await page.request.get(`${recordsPath}/${id}`)
  expect(read.ok()).toBe(true)
  expect(((await read.json()) as { values: { status: string } }).values.status).toBe('submitted')

  await signIn(page, 'dung')
  await approve(page, title, 'Finance approval')

  await expectApproved(page, id)
  await expectHistory(page, ['Chau Operations Manager', 'Dung Finance Reviewer'])
})
