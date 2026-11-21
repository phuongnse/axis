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

  await page.goto(`/subprocess/parents/${parentId}?tab=history`)
  await expect(page.getByTestId('inspector-instance')).toBeVisible()
  // The parent waits at its sub-process step, so the child is in its newest run, the first row of History.
  await page
    .getByTestId('inspector-history')
    .getByRole('button', { name: 'Show the rows in it', exact: true })
    .first()
    .click()
  await page.getByTestId(`inspector-subprocess-${childId}`).click()

  await expect(page).toHaveURL(new RegExp(`/subprocess/children/${childId}$`))
  await expect(page.getByTestId('inspector-instance')).toBeVisible()
  const facts = page.getByTestId('studio-section-inspector-facts')
  await expect(facts.getByTestId('inspector-parent-link')).toHaveAttribute('href', `/subprocess/parents/${parentId}`)
})

test('an instance without a subject shows "No subject" in the list and has no record fact on its page', async ({
  page,
}) => {
  const { parentId, childId } = await waitingParent(page)

  await page.goto('/subprocess/parents')
  await expect(page.locator(`tr[data-row-key="${parentId}"]`)).toContainText('No subject')

  for (const address of [`/subprocess/parents/${parentId}`, `/subprocess/children/${childId}`]) {
    await page.goto(address)
    await expect(page.getByTestId('studio-section-inspector-facts').getByText('Record', { exact: true })).toHaveCount(0)
  }
})

test("an operator opens the parent from its site's list, opens a step's run in the side panel and opens the child from it", async ({
  page,
}) => {
  const { parentId, childId } = await waitingParent(page)

  await page.goto('/subprocess/parents')
  await page.locator(`tr[data-row-key="${parentId}"]`).getByRole('link', { name: 'No subject', exact: true }).click()
  await expect(page.getByTestId('inspector-instance')).toBeVisible()
  await expect(page.getByTestId('inspector-step-run')).toHaveCount(0)

  await page.getByTestId('rf__node-runChild').getByRole('button', { name: 'runChild', exact: true }).click()

  const panel = page.getByTestId('inspector-step-run')
  await expect(panel).toBeVisible()
  await expect(panel.getByRole('heading', { name: 'runChild', exact: true })).toBeVisible()
  await expect(panel.getByText('Starts a sub-process', { exact: true })).toBeVisible()
  await expect(page).toHaveURL(/[?&]step=runChild(&|$)/)

  await panel.getByTestId(`inspector-run-subprocess-${childId}`).click()

  await expect(page).toHaveURL(new RegExp(`/subprocess/children/${childId}$`))
  await expect(page.getByTestId('inspector-instance')).toBeVisible()
})
