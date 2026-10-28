import { randomUUID } from 'node:crypto'
import { expect, test } from '../test'
import type { APIRequest, APIRequestContext } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { expectShown, fieldLabel, fieldValue } from '../fields'
import { sessionFile, useSession } from '../sessions'

// The record API needs a session with a grant, so these journeys start signed in as the employee requester.
test.use({ storageState: sessionFile('requester') })

const notesPath = '/api/apps/E2eApp/entities/Note/records'
const tasksPath = '/api/apps/E2eApp/tasks'

/**
 * Creates a note, starts a review of it and returns the note and its task. The task is assigned to
 * binh. Specs run in parallel against one database, so each test looks only for the task it starts.
 */
async function startReview(
  request: APIRequestContext,
  apiRequest: APIRequest,
  baseURL: string | undefined,
  prefix: string,
) {
  const title = `${prefix} ${Date.now()}`
  const created = await request.post(notesPath, { data: { values: { title } }, headers: csrfHeaders })
  expect(created.status()).toBe(201)
  const note = await created.json()
  const started = await request.post('/api/apps/E2eApp/processes/NoteReview/instances', {
    data: { subjectId: note.id },
    headers: { ...csrfHeaders, 'Idempotency-Key': randomUUID() },
  })
  expect(started.status()).toBe(201)

  // This context holds binh's saved session and shares no cookies with the browser or the request fixture.
  const binh = await apiRequest.newContext({ baseURL, storageState: sessionFile('binh') })
  let taskId = ''
  try {
    // The worker creates the task some time after the start.
    await expect(async () => {
      const response = await binh.get(`${tasksPath}?pageSize=100`)
      expect(response.ok()).toBe(true)
      const page = await response.json()
      const task = page.items.find((item: { subject: { id: string } }) => item.subject.id === note.id)
      expect(task).toBeDefined()
      taskId = task.id
    }).toPass({ timeout: 30_000 })
  } finally {
    await binh.dispose()
  }
  return { title, noteId: note.id as string, taskId }
}

test('the manager opens a task from the inbox, fills in a field, approves and returns to an inbox without it', async ({
  page,
  request,
  playwright,
  baseURL,
}) => {
  const { title, noteId } = await startReview(request, playwright.request, baseURL, 'Approve')
  await useSession(page, 'binh')

  await page.goto('/e2e/tasks')
  const row = page.getByRole('row', { name: new RegExp(title) })
  await row.getByRole('link', { name: 'Review the note', exact: true }).click()
  await expectShown(fieldValue(page, 'title'), title)
  await page.getByLabel(fieldLabel('Priority')).fill('5')
  const inboxLoad = page.waitForResponse((response) => new URL(response.url()).pathname === tasksPath)
  await page.getByRole('button', { name: 'Approve', exact: true }).click()

  await expect(page).toHaveURL(/\/e2e\/tasks$/)
  expect((await inboxLoad).ok()).toBe(true)
  // The inbox has rendered its answer once the table stops loading.
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0)
  await expect(page.getByText(new RegExp(title, 'i'))).toHaveCount(0)
  const note = await (await request.get(`${notesPath}/${noteId}`)).json()
  expect(note.values.priority).toBe(5)
})

test('a user who is not the assignee sees that the task is assigned to someone else', async ({
  page,
  request,
  playwright,
  baseURL,
}) => {
  const { taskId } = await startReview(request, playwright.request, baseURL, 'Elsewhere')
  await useSession(page, 'requester')

  await page.goto(`/e2e/tasks/${taskId}`)

  await expect(page.getByText('This task is assigned to someone else.', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: /Approve/i })).toHaveCount(0)
})

test('choosing an outcome after the task was decided shows that it is already completed', async ({
  page,
  request,
  playwright,
  baseURL,
}) => {
  const { taskId } = await startReview(request, playwright.request, baseURL, 'Decided')
  await useSession(page, 'binh')

  await page.goto(`/e2e/tasks/${taskId}`)
  const approve = page.getByRole('button', { name: 'Approve', exact: true })
  await expect(approve).toBeVisible()
  const completed = await page.request.post(`${tasksPath}/${taskId}/complete`, {
    data: { outcome: 'approve' },
    headers: csrfHeaders,
  })
  expect(completed.status()).toBe(200)
  await approve.click()

  await expect(page.getByText('This task is already completed.', { exact: true })).toBeVisible()
  await expect(page.getByRole("button", { name: /Approve/i })).toHaveCount(0)
})
