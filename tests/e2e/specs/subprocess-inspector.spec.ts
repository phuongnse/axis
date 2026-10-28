import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { useSession } from '../sessions'

// The operator inspects a parent process that started a child sub-process. Neither has a subject.
// The child waits on a 24-hour timer, so both stay waiting for the whole run.
const instancesPath = '/api/apps/SubprocessApp/processes/ParentJob/instances'

interface Detail {
  subProcesses: { id: string }[]
}

/**
 * Starts the parent unless it already runs, and returns the ids of the waiting parent and of its child. A process
 * without a subject allows one running instance, so a second start is a 409 and the instance that waits is reused.
 */
async function waitingParent(page: Page): Promise<{ parentId: string; childId: string }> {
  await useSession(page, 'operator')
  const started = await page.request.post(instancesPath, {
    data: { parameters: { label: 'e2e' } },
    headers: csrfHeaders,
  })
  expect([201, 409]).toContain(started.status())

  let parentId = ''
  let childId = ''
  // The worker starts the child some time after the parent starts.
  await expect(async () => {
    const list = await page.request.get(`${instancesPath}?state=waiting`)
    expect(list.status()).toBe(200)
    const body = (await list.json()) as { items: { id: string }[] }
    expect(body.items.length).toBeGreaterThan(0)
    parentId = body.items[0].id
    const detail = await page.request.get(`${instancesPath}/${parentId}`)
    expect(detail.status()).toBe(200)
    const { subProcesses } = (await detail.json()) as Detail
    expect(subProcesses).toHaveLength(1)
    childId = subProcesses[0].id
  }).toPass({ timeout: 30_000 })
  return { parentId, childId }
}

test('an operator opens the child sub-process from its parent, and the child links back', async ({ page }) => {
  const { parentId, childId } = await waitingParent(page)

  await page.goto(`/subprocess/parents/${parentId}`)
  await expect(page.getByTestId('inspector-instance')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Sub-processes', exact: true })).toBeVisible()
  await page.getByTestId(`inspector-subprocess-${childId}`).click()

  await expect(page).toHaveURL(new RegExp(`/subprocess/children/${childId}$`))
  const instance = page.getByTestId('inspector-instance')
  await expect(instance).toBeVisible()
  await expect(instance.getByText('Due', { exact: true })).toBeVisible()
  await expect(page.getByTestId('inspector-parent-link')).toHaveAttribute('href', `/subprocess/parents/${parentId}`)
})

test('an instance without a subject shows "No subject" in the list and on its page', async ({ page }) => {
  const { parentId, childId } = await waitingParent(page)

  await page.goto('/subprocess/parents')
  await expect(page.locator(`tr[data-row-key="${parentId}"]`)).toContainText('No subject')

  for (const address of [`/subprocess/parents/${parentId}`, `/subprocess/children/${childId}`]) {
    await page.goto(address)
    await expect(page.getByTestId('inspector-instance')).toContainText('No subject')
  }
})
