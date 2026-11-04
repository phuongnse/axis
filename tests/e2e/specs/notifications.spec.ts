import { spawnSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { env } from '../purchase-requests'
import { expect, test } from '../test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { sessionFile, useSession } from '../sessions'

// The record and process APIs need a session with a grant, so this journey starts signed in as the employee requester.
test.use({ storageState: sessionFile('requester') })

const notesPath = '/api/apps/E2eApp/entities/Note/records'
const notificationsPath = '/api/apps/E2eApp/notifications'

/** Runs `query` in the E2E PostgreSQL container. */
function psql(query: string) {
  const result = spawnSync(
    'docker',
    ['exec', env('AXIS_E2E_POSTGRES_CONTAINER'), 'psql', '-U', 'axis', '-d', 'axis', '-tAc', query],
    { encoding: 'utf8' },
  )
  if (result.status !== 0) {
    throw new Error(`psql failed with status ${result.status}: ${result.stderr}`)
  }
}

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

test('an administrator reads a tenant notice in the bell of the platform site, and an author has no bell', async ({
  page,
}) => {
  await useSession(page, 'administrator')
  // Clears what earlier runs left, so the count below is this run's notice.
  const cleared = await page.request.post('/api/notifications/read-all', { headers: csrfHeaders })
  expect(cleared.status()).toBe(204)
  // The worker stores this notice only in a cell with an email quota, which the E2E stack does not run.
  const limit = Date.now() % 1_000_000_000
  psql(
    `INSERT INTO axis.notifications (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", value_types, created_at) ` +
      `VALUES ('${randomUUID()}', NULL, NULL, 'platformRole', 'administrator', 'notices.emailsHeld.subject', 'notices.emailsHeld.body', '{"limit":${limit}}', '{"limit":"integer"}', now())`,
  )

  await page.goto('/')
  await page.getByRole('button', { name: 'Notifications, 1 unread', exact: true }).click()
  const dialog = await waitForDialog(page, 'Notifications')
  const row = dialog.getByRole('button').filter({ hasText: limit.toLocaleString('en-US') })
  await expect(row).toContainText('Emails are held until tomorrow')
  await row.click()

  // A notice has no page, so the row only marks it read.
  await expect(page.getByRole('button', { name: 'Notifications', exact: true })).toBeVisible()
  await expect(page).toHaveURL(/\/$/)

  await useSession(page, 'author')
  await page.goto('/')
  const banner = page.getByRole('banner')
  await expect(banner.getByRole('button', { name: 'Switch to', exact: true })).toBeVisible()
  await expect(banner.getByRole('button', { name: /^Notifications/ })).toHaveCount(0)
})
