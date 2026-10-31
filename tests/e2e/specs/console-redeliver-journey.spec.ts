import { spawnSync } from 'node:child_process'
import { waitForDialog } from '../dialogs'
import { fieldLabel } from '../fields'
import { appPath, createRequest, decide, env, requestNumber, submit, webhookDeliveries } from '../purchase-requests'
import { expect, test } from '../test'
import { useSession } from '../sessions'

const processPath = `${appPath}/processes/PurchaseRequestApproval`

/** Runs `query` in the E2E PostgreSQL container and returns its trimmed output. */
function psql(query: string) {
  const result = spawnSync(
    'docker',
    ['exec', env('AXIS_E2E_POSTGRES_CONTAINER'), 'psql', '-U', 'axis', '-d', 'axis', '-tAc', query],
    { encoding: 'utf8' },
  )
  if (result.status !== 0) {
    throw new Error(`psql failed with status ${result.status}: ${result.stderr}`)
  }
  return result.stdout.trim()
}

test('the platform operator redelivers a failed webhook from the console, and the delivery is accepted', async ({
  page,
}) => {
  // The failing delivery waits one 30 second backoff before it dead-letters, with the worker's delivery limit of 2.
  test.setTimeout(180_000)
  const stub = env('AXIS_E2E_PURCHASING_URL')

  await useSession(page, 'requester')
  const request = await createRequest(page, 'Zz Console redeliver', 500)
  const number = await requestNumber(page, request.id)
  // The route must be set before the approval, which raises the event.
  const fail = await page.request.post(`${stub}/test/fail-webhook/${number}`)
  expect(fail.status()).toBe(204)
  await submit(page, request.id, request.title)

  await useSession(page, 'manager-2')
  await decide(page, request.title, 'Manager approval', 'Approve')

  // The platform operator holds no application role, so the operator with the inspect permission finds the instance.
  await useSession(page, 'operator')
  let instanceId = ''
  await expect(async () => {
    const response = await page.request.get(`${processPath}/instances?pageSize=100`)
    expect(response.status()).toBe(200)
    const body = (await response.json()) as { items: { id: string; subjectId: string }[] }
    const item = body.items.find((candidate) => candidate.subjectId === request.id)
    expect(item).toBeDefined()
    instanceId = item!.id
  }).toPass({ timeout: 30_000 })

  await useSession(page, 'platform-operator')
  let letterId = ''
  let eventId = ''
  let webhookId = ''
  await expect(async () => {
    const response = await page.request.get('/api/console/dead-letters?state=open&pageSize=100')
    expect(response.status()).toBe(200)
    const body = (await response.json()) as {
      items: { id: string; kind: string; eventId: string | null; webhookId: string | null; processInstanceId: string | null }[]
    }
    const item = body.items.find(
      (candidate) => candidate.kind === 'webhook.deliver' && candidate.processInstanceId === instanceId,
    )
    expect(item).toBeDefined()
    letterId = item!.id
    eventId = item!.eventId!
    webhookId = item!.webhookId!
  }).toPass({ timeout: 90_000 })

  const deliveries = await page.request.get(`/api/console/webhook-deliveries?webhook=${webhookId}&pageSize=100`)
  expect(deliveries.status()).toBe(200)
  const deliveryBody = (await deliveries.json()) as { items: { id: string; eventId: string; webhookId: string }[] }
  const deliveryId = deliveryBody.items.find((item) => item.eventId === eventId && item.webhookId === webhookId)?.id
  expect(deliveryId).toBeDefined()

  await page.goto('/console')
  const deliveryRow = page.getByTestId('console-webhook-deliveries').locator(`tr[data-row-key="${deliveryId}"]`)
  await expect(deliveryRow).toContainText('Dead-lettered')
  await expect(deliveryRow).toContainText('500')
  const letterRow = page.getByTestId('console-dead-letters').locator(`tr[data-row-key="${letterId}"]`)
  await expect(letterRow).toContainText('Open')

  const accept = await page.request.post(`${stub}/test/accept-webhook/${number}`)
  expect(accept.status()).toBe(204)
  await page.getByTestId(`console-dead-letter-redeliver-${letterId}`).click()
  const dialog = await waitForDialog(page)
  const reason = `The purchasing system is back ${Date.now()}`
  await dialog.getByLabel(fieldLabel('Reason')).fill(reason)
  await dialog.getByRole('button', { name: 'Send', exact: true }).click()
  await expect(dialog).toBeHidden()
  await expect(letterRow).toContainText('Redelivered')

  // The webhook deliveries view does not refresh after a redeliver, so the page is reloaded until the row shows it.
  await expect(async () => {
    await page.reload()
    await expect(deliveryRow).toContainText('Delivered', { timeout: 1_000 })
    await expect(deliveryRow).toContainText('204')
  }).toPass({ timeout: 30_000 })

  const received = await webhookDeliveries(page, number)
  expect(received).toHaveLength(1)
  expect(received[0].webhookId).toBe(`${eventId}_${webhookId}`)
  expect(received[0].type).toBe('PurchaseRequestApproved')

  const audit = psql(
    `SELECT actor || '|' || (details->>'reason') FROM axis.audit_records ` +
      `WHERE action = 'deadLetter.redelivered' AND details->>'deadLetterId' = '${letterId}'`,
  )
  expect(audit).toBe(`platform-operator|${reason}`)

  // The requester does not hold the operator role.
  await useSession(page, 'requester')
  await page.goto('/console')
  await expect(page.getByTestId('console-forbidden')).toBeVisible()
  expect((await page.request.get('/api/console/dead-letters')).status()).toBe(403)
})
