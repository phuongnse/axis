import { randomUUID } from 'node:crypto'
import { spawnSync } from 'node:child_process'
import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { fieldLabel } from '../fields'
import {
  appPath,
  createRequest,
  decide,
  env,
  purchaseOrders,
  recordsPath,
  requestNumber,
  submit,
} from '../purchase-requests'
import { sessionFile, useSession, type SampleUser } from '../sessions'
import { chooseTheme } from '../shell'

// The employee requester starts the approval. The operator giang or the finance reviewer dung inspects it.
test.use({ storageState: sessionFile('requester') })

const instancesPath = `${appPath}/processes/PurchaseRequestApproval/instances`

// The ids of the sample application and of its approval process, from samples/apps/purchase-requests.
const applicationId = '219102bc-864f-42ed-874a-c19eaee0d1ba'
const processId = '4b60c03e-02d0-4329-80c6-75f972190399'

interface InstanceItem {
  id: string
  subjectId: string
  step: string
}

/**
 * Inserts an open dead letter of the approval process into the E2E database and returns its id. No sample
 * declares an async or schedule trigger, so a delivery never dead-letters here. The trigger of the row is
 * a random id, which is not in the active release.
 */
function seedDeadLetter(): string {
  const container = process.env.AXIS_E2E_POSTGRES_CONTAINER
  if (!container) {
    throw new Error('AXIS_E2E_POSTGRES_CONTAINER is not set. Run the E2E tests with scripts/e2e.sh.')
  }
  const id = randomUUID()
  const query =
    'INSERT INTO axis.dead_letters (id, kind, application_id, process_id, trigger_id, occurrence_at, attempts, last_error, state, created_at) ' +
    `VALUES ('${id}', 'trigger.fanOut', '${applicationId}', '${processId}', '${randomUUID()}', '2026-01-01T05:00:00Z', 5, 'System.InvalidOperationException', 'open', now())`
  const result = spawnSync('docker', ['exec', container, 'psql', '-U', 'axis', '-d', 'axis', '-tAc', query], {
    encoding: 'utf8',
  })
  if (result.status !== 0) {
    throw new Error(`psql failed with status ${result.status}: ${result.stderr}`)
  }
  return id
}

/**
 * Starts the approval of a new Operations request as requester, then signs in as the inspecting user,
 * giang unless the test names another. It returns the request's title and id, and the id of the
 * instance once the instance waits at manager approval. Specs run in
 * parallel against one database, so the list holds many instances. The newest come first, and the
 * test looks only for its own by subject id.
 */
async function startWaitingInstance(page: Page, inspector: SampleUser = 'giang') {
  await useSession(page, 'requester')
  const { title, id } = await createRequest(page, 'Zz Inspect', 500)
  const started = await page.request.post(instancesPath, { data: { subjectId: id }, headers: csrfHeaders })
  expect(started.status()).toBe(201)

  await useSession(page, inspector)
  let instanceId = ''
  // The worker moves the instance to the manager's task some time after the start.
  await expect(async () => {
    const response = await page.request.get(`${instancesPath}?state=waiting&pageSize=100`)
    expect(response.status()).toBe(200)
    const body = (await response.json()) as { items: InstanceItem[] }
    const item = body.items.find((candidate) => candidate.subjectId === id && candidate.step === 'managerApproval')
    expect(item).toBeDefined()
    instanceId = item!.id
  }).toPass({ timeout: 30_000 })
  return { title, requestId: id, instanceId }
}

test('the operator opens an instance waiting for the manager and sees the path and the open task in light and dark mode', async ({
  page,
}) => {
  const { title, instanceId } = await startWaitingInstance(page)

  await page.goto('/purchasing/approvals?state=waiting&pageSize=100')
  await expect(page.getByRole('banner').getByRole('button', { name: 'Giang Operator', exact: true })).toBeVisible()
  await page
    .locator(`tr[data-row-key="${instanceId}"]`)
    .getByRole('link', { name: title, exact: true })
    .click()
  await expect(page).toHaveURL(new RegExp(`/purchasing/approvals/${instanceId}$`))

  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await chooseTheme(page, 'Dark')
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByTestId('inspector-step-submit')).toContainText('Taken')
    const approval = shell.getByTestId('inspector-step-managerApproval')
    await expect(approval).toContainText('Manager approval')
    await expect(approval).toContainText('Current')
    await expect(shell.getByRole('row', { name: /Manager approval.*user: chau.*open/ })).toBeVisible()
  }
})

test('the operator follows a history entry to its approval instance and an employee sees no link', async ({ page }) => {
  const { requestId, instanceId } = await startWaitingInstance(page)

  await page.goto(`/purchasing/purchaserequestform/${requestId}`)
  await page.getByTestId('record-history').getByTestId('history-inspect-link').first().click()
  await expect(page).toHaveURL(new RegExp(`/purchasing/approvals/${instanceId}$`))
  await expect(page.getByTestId('inspector-instance')).toBeVisible()

  await useSession(page, 'requester')
  await page.goto(`/purchasing/purchaserequestform/${requestId}`)
  await expect(page.getByTestId('history-entry').first()).toBeVisible()
  await expect(page.getByTestId('history-inspect-link')).toHaveCount(0)
})

test('an employee has no Approvals entry, sees no instance and is refused by the inspection API', async ({ page }) => {
  const { instanceId } = await startWaitingInstance(page)

  await useSession(page, 'requester')
  await page.goto('/purchasing/mytasks')
  await expect(page.getByRole('menuitem', { name: 'My tasks', exact: true })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: /Approvals/ })).toHaveCount(0)

  await page.goto(`/purchasing/approvals/${instanceId}`)
  await expect(page.getByTestId('shell-error')).toBeVisible()
  await expect(page.getByTestId('inspector-instance')).toHaveCount(0)

  expect((await page.request.get(`${instancesPath}?pageSize=100`)).status()).toBe(403)
  expect((await page.request.get(`${instancesPath}/${instanceId}`)).status()).toBe(403)
  expect((await page.request.get('/api/sites/purchasing/pages/Approvals')).status()).toBe(403)
})

test('the list endpoint answers 403 to an employee and 200 to the operator', async ({ page }) => {
  await useSession(page, 'requester')
  expect((await page.request.get(instancesPath)).status()).toBe(403)

  await useSession(page, 'giang')
  expect((await page.request.get(instancesPath)).status()).toBe(200)
})

test('a finance reviewer cancels a waiting request in the inspector and its task leaves the manager inbox', async ({
  page,
}) => {
  const { requestId, instanceId } = await startWaitingInstance(page, 'dung')

  await page.goto(`/purchasing/approvals/${instanceId}`)
  await expect(page.getByTestId('inspector-instance')).toBeVisible()
  await expect(page.getByTestId('inspector-command-retry')).toHaveCount(0)
  await page.getByTestId('inspector-command-cancel').click()
  const dialog = await waitForDialog(page)
  await dialog.getByLabel(fieldLabel('Reason')).fill('The request was raised by mistake')
  await dialog.getByRole('button', { name: 'Send', exact: true }).click()

  await expect(page.getByTestId('inspector-instance')).toContainText('Cancelled')
  await expect(page.getByTestId('inspector-command-cancel')).toHaveCount(0)

  await useSession(page, 'chau')
  await expect(async () => {
    const response = await page.request.get(`${appPath}/tasks?pageSize=100`)
    expect(response.status()).toBe(200)
    const body = (await response.json()) as { items: { subject: { id: string } }[] }
    expect(body.items.some((item) => item.subject.id === requestId)).toBe(false)
  }).toPass({ timeout: 30_000 })
})

test('the operator sees no command buttons and is refused a direct cancel', async ({ page }) => {
  const { instanceId } = await startWaitingInstance(page)

  await page.goto(`/purchasing/approvals/${instanceId}`)
  await expect(page.getByTestId('inspector-instance')).toBeVisible()
  await expect(page.getByTestId(/^inspector-command-/)).toHaveCount(0)

  const cancel = await page.request.post(`${instancesPath}/${instanceId}/cancel`, {
    data: { reason: 'x' },
    headers: csrfHeaders,
  })
  expect(cancel.status()).toBe(403)
})

test('a finance reviewer resolves an unknown purchase order call in the inspector and the request is approved', async ({
  page,
}) => {
  // The worker marks the call unknown, then runs its first lookup, which answers 503.
  test.setTimeout(120_000)
  await useSession(page, 'requester')
  const { title, id } = await createRequest(page, 'Zz Resolve', 500)
  const number = await requestNumber(page, id)
  // The stub stores the order but answers without its number, and every lookup for it answers 503.
  const unknown = await page.request.post(`${env('AXIS_E2E_PURCHASING_URL')}/test/unknown-request/${number}`)
  expect(unknown.status()).toBe(204)
  await submit(page, id, title)

  await useSession(page, 'chau')
  await decide(page, title, 'Manager approval', 'Approve')

  await useSession(page, 'dung')
  let instanceId = ''
  await expect(async () => {
    const response = await page.request.get(`${instancesPath}?pageSize=100`)
    expect(response.status()).toBe(200)
    const body = (await response.json()) as { items: InstanceItem[] }
    const item = body.items.find((candidate) => candidate.subjectId === id)
    expect(item).toBeDefined()
    instanceId = item!.id
  }).toPass({ timeout: 30_000 })
  let callId = ''
  await expect(async () => {
    const response = await page.request.get(`${instancesPath}/${instanceId}`)
    expect(response.status()).toBe(200)
    const body = (await response.json()) as { calls: { id: string; resolvable: boolean }[] }
    const call = body.calls.find((candidate) => candidate.resolvable)
    expect(call).toBeDefined()
    callId = call!.id
  }).toPass({ timeout: 60_000 })
  const orders = await purchaseOrders(page, number)
  expect(orders).toHaveLength(1)
  const orderNumber = orders[0].orderNumber

  await page.goto(`/purchasing/approvals/${instanceId}`)
  await page.getByTestId(`inspector-resolve-${callId}`).click()
  const dialog = await waitForDialog(page)
  await dialog.getByLabel(fieldLabel('Reason')).fill('The order exists in the purchasing system')
  await dialog.getByLabel(fieldLabel('Succeeded')).check()
  await dialog.getByLabel('orderNumber (text)', { exact: true }).fill(orderNumber)
  await dialog.getByRole('button', { name: 'Send', exact: true }).click()
  await expect(dialog).toHaveCount(0)

  await expect(async () => {
    const response = await page.request.get(`${instancesPath}/${instanceId}`)
    expect(response.status()).toBe(200)
    expect(((await response.json()) as { state: string }).state).toBe('completed')
  }).toPass({ timeout: 30_000 })

  await useSession(page, 'requester')
  const read = await page.request.get(`${recordsPath}/${id}`)
  expect(read.ok()).toBe(true)
  const values = ((await read.json()) as { values: { status: string; purchaseOrderNumber: string } }).values
  expect(values.status).toBe('approved')
  expect(values.purchaseOrderNumber).toBe(orderNumber)
  expect((await purchaseOrders(page, number)).map((order) => order.orderNumber)).toEqual([orderNumber])
})

test('a finance reviewer is refused a redeliver and discards a dead letter, and its row leaves the list', async ({
  page,
}) => {
  const id = seedDeadLetter()
  await useSession(page, 'dung')

  await page.goto('/purchasing/approvals')
  const row = page.locator(`tr[data-row-key="${id}"]`)
  await expect(row).toContainText('trigger.fanOut')
  await expect(row).toContainText('System.InvalidOperationException')

  await row.getByTestId(`inspector-dead-letter-redeliver-${id}`).click()
  let dialog = await waitForDialog(page)
  await dialog.getByLabel(fieldLabel('Reason')).fill('The trigger is back')
  await dialog.getByRole('button', { name: 'Send', exact: true }).click()
  await expect(dialog.getByTestId('inspector-command-error')).toHaveText(
    'The trigger of the dead letter is not in the active release.',
  )
  await page.keyboard.press('Escape')
  await expect(dialog).toHaveCount(0)
  await expect(row).toBeVisible()

  await row.getByTestId(`inspector-dead-letter-discard-${id}`).click()
  dialog = await waitForDialog(page)
  await dialog.getByLabel(fieldLabel('Reason')).fill('The delivery is not needed')
  await dialog.getByRole('button', { name: 'Send', exact: true }).click()
  await expect(dialog).toHaveCount(0)
  await expect(row).toHaveCount(0)
})

test('the operator sees an open dead letter and no Redeliver or Discard button', async ({ page }) => {
  const id = seedDeadLetter()
  await useSession(page, 'giang')

  await page.goto('/purchasing/approvals')
  await expect(page.locator(`tr[data-row-key="${id}"]`)).toBeVisible()
  await expect(page.getByTestId(/^inspector-dead-letter-(redeliver|discard)-/)).toHaveCount(0)
})
