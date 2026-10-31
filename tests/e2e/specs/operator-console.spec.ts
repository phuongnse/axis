import { spawnSync } from 'node:child_process'
import type { Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { fieldLabel } from '../fields'
import { appPath, createRequest, env } from '../purchase-requests'
import { expect, test } from '../test'
import { useSession } from '../sessions'

const instancesPath = `${appPath}/processes/PurchaseRequestApproval/instances`

/**
 * Starts the approval of a new request as the requester and waits, as the operator, until the instance waits at
 * manager approval. Then it marks the instance as failed with a history error at that step, through the E2E database,
 * because the engine fails no step of the sample. It returns the instance's id.
 */
async function failedInstance(page: Page) {
  await useSession(page, 'requester')
  const { id } = await createRequest(page, 'Zz Console', 500)
  const started = await page.request.post(instancesPath, { data: { subjectId: id }, headers: csrfHeaders })
  expect(started.status()).toBe(201)

  await useSession(page, 'operator')
  let instanceId = ''
  await expect(async () => {
    const response = await page.request.get(`${instancesPath}?state=waiting&pageSize=100`)
    expect(response.status()).toBe(200)
    const body = (await response.json()) as { items: { id: string; subjectId: string; step: string }[] }
    const item = body.items.find((candidate) => candidate.subjectId === id && candidate.step === 'managerApproval')
    expect(item).toBeDefined()
    instanceId = item!.id
  }).toPass({ timeout: 30_000 })

  const query =
    `UPDATE axis.process_instances SET state = 'failed', ended_at = now() WHERE id = '${instanceId}'; ` +
    'INSERT INTO axis.process_step_history (id, process_instance_id, step, revision, input, error, started_at, finished_at) ' +
    `SELECT gen_random_uuid(), id, 'managerApproval', revision, '{}'::jsonb, 'System.InvalidOperationException', now(), now() ` +
    `FROM axis.process_instances WHERE id = '${instanceId}'`
  const result = spawnSync(
    'docker',
    ['exec', env('AXIS_E2E_POSTGRES_CONTAINER'), 'psql', '-U', 'axis', '-d', 'axis', '-tAc', query],
    { encoding: 'utf8' },
  )
  if (result.status !== 0) {
    throw new Error(`psql failed with status ${result.status}: ${result.stderr}`)
  }
  return instanceId
}

test('the platform operator opens the operator console from the app switcher and sees the tenant', async ({ page }) => {
  await useSession(page, 'platform-operator')
  await page.goto('/')

  await page.getByRole('banner').getByRole('button', { name: 'Switch to', exact: true }).click()
  await page.getByRole('menuitem', { name: 'Operator console', exact: true }).click()

  await expect(page).toHaveURL(/\/console$/)
  const tenant = page.getByTestId('console-tenant')
  await expect(tenant.getByText('Active', { exact: true })).toBeVisible()
  await expect(tenant.getByRole('cell', { name: 'E2eApp', exact: true })).toBeVisible()
  await expect(tenant.getByRole('cell', { name: 'PurchaseRequests', exact: true })).toBeVisible()
})

test('a person without the operator role is told the page is for operators', async ({ page }) => {
  await useSession(page, 'author')
  await page.goto('/console')

  await expect(page.getByTestId('console-forbidden')).toBeVisible()
  await expect(page.getByText('This page is for operators.', { exact: true })).toBeVisible()
})

test('the platform operator opens a failed instance from Failed work and sees its graph and failed step', async ({
  page,
}) => {
  const instanceId = await failedInstance(page)

  await useSession(page, 'platform-operator')
  await page.goto('/console')
  await page.getByTestId(`console-failed-work-open-${instanceId}`).click()

  await expect(page).toHaveURL(new RegExp(`/console/instances/${instanceId}$`))
  await expect(page.getByTestId('inspector-step-submit')).toContainText('Taken')
  const failed = page.getByTestId('inspector-step-managerApproval')
  await expect(failed).toContainText('Current')
  await expect(page.getByRole('row', { name: /managerApproval.*System\.InvalidOperationException/ })).toBeVisible()
  await expect(page.getByTestId('inspector-command-retry')).toBeVisible()
})

test('the platform operator retries a failed instance from the console with a reason and it leaves Failed work', async ({
  page,
}) => {
  const instanceId = await failedInstance(page)

  await useSession(page, 'platform-operator')
  await page.goto(`/console/instances/${instanceId}`)
  await page.getByTestId('inspector-command-retry').click()
  const dialog = await waitForDialog(page)
  await dialog.getByLabel(fieldLabel('Reason')).fill('The manager is set now')
  await dialog.getByRole('button', { name: 'Send', exact: true }).click()
  await expect(dialog).toHaveCount(0)
  await expect(page.getByTestId('inspector-command-retry')).toHaveCount(0)

  await page.goto('/console')
  await expect(page.getByTestId('console-failed-work')).toBeVisible()
  await expect(page.getByTestId(`console-failed-work-open-${instanceId}`)).toHaveCount(0)
})
