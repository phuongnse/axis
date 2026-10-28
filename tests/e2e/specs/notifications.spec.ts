import { randomUUID } from 'node:crypto'
import { expect, test } from '../test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { sessionFile, useSession } from '../sessions'

// The record and process APIs need a session with a grant, so this journey starts signed in as the employee requester.
test.use({ storageState: sessionFile('requester') })

const notesPath = '/api/apps/E2eApp/entities/Note/records'
const notificationsPath = '/api/apps/E2eApp/notifications'

test('a user opens a notification from the bell with the keyboard, and another user sees no count', async ({
  page,
  request,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  // Clears what earlier runs left, so the count below is this run's notification.
  const cleared = await request.post(`${notificationsPath}/read-all`, { headers: csrfHeaders })
  expect(cleared.status()).toBe(204)
  const created = await request.post(notesPath, { data: { values: { title: `Notify ${run}` } }, headers: csrfHeaders })
  expect(created.status()).toBe(201)
  const note = (await created.json()) as { id: string }
  const started = await request.post('/api/apps/E2eApp/processes/NoteNotify/instances', {
    data: { subjectId: note.id },
    headers: { ...csrfHeaders, 'Idempotency-Key': randomUUID() },
  })
  expect(started.status()).toBe(201)

  await page.goto('/e2e/notes')
  // The worker runs the step some time after the start, and the count loads with the page.
  const bell = page.getByRole('button', { name: 'Notifications, 1 unread', exact: true })
  await expect(async () => {
    await page.reload()
    await expect(bell).toBeVisible({ timeout: 1_000 })
  }).toPass({ timeout: 30_000 })

  await bell.focus()
  await page.keyboard.press('Enter')
  const dialog = await waitForDialog(page, 'Notifications')
  // The texts follow the user's profile locale, so the row is found by the title in its values.
  const row = dialog.getByRole('button').filter({ hasText: `Notify ${run}` })
  await expect(row).toBeVisible()
  await row.focus()
  await page.keyboard.press('Enter')

  await expect(page).toHaveURL(new RegExp(`/e2e/notedetail\\?note=${note.id}$`))
  await expect(page.getByRole('button', { name: 'Notifications', exact: true })).toBeVisible()

  await useSession(page, 'employee')
  await page.goto('/e2e/notes')
  await expect(page.getByRole('button', { name: 'Notifications', exact: true })).toBeVisible()
})
