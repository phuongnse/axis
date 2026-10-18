import { expect, test, type Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { appPath, createRequest } from '../purchase-requests'
import { sessionFile, useSession, type SampleUser } from '../sessions'

// The employee anna starts the approval. The operator giang or the finance reviewer dung inspects it.
test.use({ storageState: sessionFile('anna') })

const instancesPath = `${appPath}/processes/PurchaseRequestApproval/instances`

interface InstanceItem {
  id: string
  subjectId: string
  step: string
}

/**
 * Starts the approval of a new Operations request as anna, then signs in as the inspecting user,
 * giang unless the test names another. It returns the
 * request id and the id of the instance once the instance waits at manager approval. Specs run in
 * parallel against one database, so the list holds many instances. The newest come first, and the
 * test looks only for its own by subject id.
 */
async function startWaitingInstance(page: Page, inspector: SampleUser = 'giang') {
  await useSession(page, 'anna')
  const { id } = await createRequest(page, 'Zz Inspect', 500)
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
  return { requestId: id, instanceId }
}

test('the operator opens an instance waiting for the manager and sees the path and the open task in light and dark mode', async ({
  page,
}) => {
  const { requestId, instanceId } = await startWaitingInstance(page)

  await page.goto('/purchasing/approvals?state=waiting&pageSize=100')
  await expect(page.getByRole('banner').getByRole('button', { name: 'Giang Operator' })).toBeVisible()
  await page
    .getByRole('row', { name: new RegExp(requestId) })
    .getByRole('link', { name: 'Open' })
    .click()
  await expect(page).toHaveURL(new RegExp(`/purchasing/approvals/${instanceId}$`))

  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode' }).click()
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    await expect(shell.getByTestId('inspector-step-submit')).toContainText('Taken')
    const approval = shell.getByTestId('inspector-step-managerApproval')
    await expect(approval).toContainText('Manager approval')
    await expect(approval).toContainText('Current')
    await expect(shell.getByRole('row', { name: /Manager approval.*user: chau.*open/ })).toBeVisible()
  }
})

test('an employee has no Approvals entry, sees no instance and is refused by the inspection API', async ({ page }) => {
  const { instanceId } = await startWaitingInstance(page)

  await useSession(page, 'anna')
  await page.goto('/purchasing/mytasks')
  await expect(page.getByRole('menuitem', { name: 'My tasks' })).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Approvals' })).toHaveCount(0)

  await page.goto(`/purchasing/approvals/${instanceId}`)
  await expect(page.getByTestId('shell-error')).toBeVisible()
  await expect(page.getByTestId('inspector-instance')).toHaveCount(0)

  expect((await page.request.get(`${instancesPath}?pageSize=100`)).status()).toBe(403)
  expect((await page.request.get(`${instancesPath}/${instanceId}`)).status()).toBe(403)
  expect((await page.request.get('/api/sites/purchasing/pages/Approvals')).status()).toBe(403)
})

test('the list endpoint answers 403 to an employee and 200 to the operator', async ({ page }) => {
  await useSession(page, 'anna')
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
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Reason').fill('The request was raised by mistake')
  await dialog.getByRole('button', { name: 'Send' }).click()

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
