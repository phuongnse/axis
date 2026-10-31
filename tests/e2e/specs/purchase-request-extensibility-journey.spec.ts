import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { waitForDialog } from '../dialogs'
import { fieldLabel } from '../fields'
import { pickOption } from '../select'
import {
  appPath,
  createRequest,
  decide,
  env,
  expectPurchaseOrderNumber,
  purchaseOrders,
  requestNumber,
  submit,
  webhookDeliveries,
} from '../purchase-requests'
import { sessionFile, useSession } from '../sessions'

// The requester creates both requests. The approved one reaches the purchasing system stub through the
// OAuth2 connector and announces itself by a signed webhook. The other one has a receiver that fails.
test.use({ storageState: sessionFile('requester') })

const processPath = `${appPath}/processes/PurchaseRequestApproval`

/** Opens the lookup of the reference field `field`, labelled `label`, and picks the row named `name`. */
async function choose(page: Page, field: string, label: string, name: string) {
  await page.getByTestId(`reference-${field}`).getByRole('button', { name: 'Choose', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByRole('row', { name: new RegExp(name) }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByLabel(fieldLabel(label))).toHaveValue(name)
}

/** Creates an Operations request with two lines through the form, and returns its title and id. */
async function createTwoLineRequest(page: Page) {
  const title = `Zz Journey ${Date.now()}-${test.info().retry}`
  const cell = (label: string) => page.getByLabel(fieldLabel(label))
  await page.goto('/purchasing/purchaserequests?pageSize=100&sort=-title')
  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/new$/)
  await page.getByRole('textbox', { name: 'Title', exact: true }).fill(title)
  await choose(page, 'department', 'Department', 'Operations')
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await cell('Description 1').fill('Desk lamp')
  await cell('Quantity 1').fill('2')
  await cell('Unit price 1').fill('120')
  await cell('Description 2').fill('Cable tray')
  await cell('Quantity 2').fill('1')
  await cell('Unit price 2').fill('35.50')
  await pickOption(page.getByRole('combobox', { name: 'Status', exact: true }), 'draft')
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(/\/purchasing\/purchaserequests\?pageSize=100&sort=-title$/)
  await page.getByRole('row', { name: new RegExp(`${title} `) }).getByRole('link', { name: title, exact: true }).click()
  await expect(page).toHaveURL(/\/purchasing\/purchaserequestform\/[0-9a-f-]{36}$/)
  return { title, id: page.url().split('/').pop()! }
}

test('an approved request reaches the purchasing system and announces itself, and a failing receiver leaves a dead letter that only the operator sees', async ({
  page,
}) => {
  // The failing delivery waits one 30 second backoff before it dead-letters, with the worker's delivery limit of 2.
  test.setTimeout(180_000)
  const stub = env('AXIS_E2E_PURCHASING_URL')

  const first = await createTwoLineRequest(page)
  const second = await createRequest(page, 'Zz Webhook fails', 500)
  const firstNumber = await requestNumber(page, first.id)
  const secondNumber = await requestNumber(page, second.id)
  // The route must be set before the approval, which raises the event.
  const fail = await page.request.post(`${stub}/test/fail-webhook/${secondNumber}`)
  expect(fail.status()).toBe(204)
  await submit(page, first.id, first.title)
  await submit(page, second.id, second.title)

  await useSession(page, 'manager-2')
  await decide(page, first.title, 'Manager approval', 'Approve')
  await decide(page, second.title, 'Manager approval', 'Approve')

  // The first request shows its purchase order number, and the stub's order holds both lines.
  await useSession(page, 'requester')
  const orderNumber = await expectPurchaseOrderNumber(page, first.id)
  const orders = await purchaseOrders(page, firstNumber)
  expect(orders).toHaveLength(1)
  expect(orders[0].orderNumber).toBe(orderNumber)
  expect(orders[0].clientId).toBe('axis-purchasing')
  expect(orders[0].lines).toEqual([
    { description: 'Desk lamp', quantity: 2, unitPrice: 120, amount: 240 },
    { description: 'Cable tray', quantity: 1, unitPrice: 35.5, amount: 35.5 },
  ])
  await expect.poll(async () => (await webhookDeliveries(page, firstNumber)).length, { timeout: 30_000 }).toBeGreaterThan(0)
  expect(await webhookDeliveries(page, secondNumber)).toHaveLength(0)

  // The second request's delivery fails twice and becomes an open dead letter of its approval instance.
  await useSession(page, 'operator')
  let instanceId = ''
  await expect(async () => {
    const response = await page.request.get(`${processPath}/instances?pageSize=100`)
    expect(response.status()).toBe(200)
    const body = (await response.json()) as { items: { id: string; subjectId: string }[] }
    const item = body.items.find((candidate) => candidate.subjectId === second.id)
    expect(item).toBeDefined()
    instanceId = item!.id
  }).toPass({ timeout: 30_000 })
  let deadLetterId = ''
  await expect(async () => {
    const response = await page.request.get(`${processPath}/dead-letters?state=open&pageSize=100`)
    expect(response.status()).toBe(200)
    const body = (await response.json()) as { items: { id: string; kind: string; processInstanceId: string | null }[] }
    const item = body.items.find(
      (candidate) => candidate.kind === 'webhook.deliver' && candidate.processInstanceId === instanceId,
    )
    expect(item).toBeDefined()
    deadLetterId = item!.id
  }).toPass({ timeout: 90_000 })

  await page.goto('/purchasing/approvals')
  const row = page.locator(`tr[data-row-key="${deadLetterId}"]`)
  await expect(row).toBeVisible()
  await expect(row).toContainText('webhook.deliver')
  await expect(row).toContainText('HTTP 500')
  await expect(page.getByTestId(/^inspector-dead-letter-(redeliver|discard)-/)).toHaveCount(0)

  // The requester does not hold the operator role.
  await useSession(page, 'requester')
  await page.goto('/purchasing/approvals')
  await expect(page.getByTestId('shell-error')).toBeVisible()
  await expect(page.locator(`tr[data-row-key="${deadLetterId}"]`)).toHaveCount(0)
  expect((await page.request.get(`${processPath}/dead-letters`)).status()).toBe(403)
})
