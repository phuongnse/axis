import { randomUUID } from 'node:crypto'
import { expect, test } from '../test'
import { csrfHeaders } from '../csrf'
import { sessionFile, useSession } from '../sessions'
import { chooseTheme } from '../shell'

// The record API needs a session with a grant, so these journeys start signed in as the employee requester.
test.use({ storageState: sessionFile('requester') })

// Specs run in parallel against one database, so this test looks only for the task it starts.
test('the manager a task is assigned to sees it in the inbox in light and dark mode, and another user does not', async ({
  page,
  browser,
  baseURL,
  request,
}) => {
  const title = `Inbox ${Date.now()}`
  const created = await request.post('/api/apps/E2eApp/entities/Note/records', {
    data: { values: { title } },
    headers: csrfHeaders,
  })
  expect(created.status()).toBe(201)
  const note = await created.json()
  const started = await request.post('/api/apps/E2eApp/processes/NoteReview/instances', {
    data: { subjectId: note.id },
    headers: { ...csrfHeaders, 'Idempotency-Key': randomUUID() },
  })
  expect(started.status()).toBe(201)

  await useSession(page, 'binh')
  await page.goto('/e2e/tasks')
  await expect(page.getByRole('banner').getByRole('button', { name: 'Binh Engineering Manager', exact: true })).toBeVisible()
  const row = page.getByRole('row', { name: new RegExp(title) })
  // The worker creates the task some time after the start, so the inbox is loaded again until it shows.
  await expect(async () => {
    await page.reload()
    await expect(row).toBeVisible({ timeout: 2_000 })
  }).toPass({ timeout: 30_000 })

  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await chooseTheme(page, 'Dark')
    }
    const shell = page.locator(`[data-theme-mode="${mode}"]`)
    const themedRow = shell.getByRole('row', { name: new RegExp(title) })
    await expect(themedRow.getByRole('link', { name: 'Review the note', exact: true })).toHaveAttribute(
      'href',
      /^\/e2e\/tasks\/[0-9a-f-]{36}$/,
    )
    await expect(themedRow.getByRole('cell', { name: title, exact: true })).toBeVisible()
  }

  // A new context has its own cookies, so the requester's session does not change the manager's.
  const otherContext = await browser.newContext({ baseURL, storageState: sessionFile('requester') })
  const other = await otherContext.newPage()
  await other.goto('/e2e/tasks')
  await expect(other.getByRole('banner').getByRole('button', { name: 'Requester User', exact: true })).toBeVisible()
  await expect(other.getByRole('main').getByText('No open tasks.', { exact: true })).toBeVisible()
  await expect(other.getByText(new RegExp(title, 'i'))).toHaveCount(0)
  await otherContext.close()
})
