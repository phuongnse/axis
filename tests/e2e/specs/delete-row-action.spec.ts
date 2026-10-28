import { expect, test } from '../test'
import type { APIRequestContext } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { confirmDialog, waitForDialog } from '../dialogs'
import { sessionFile, useSession } from '../sessions'

// requester holds the delete grant on notes. The finance-reviewer user reads notes whose title contains
// 'Shared' and holds no delete grant.
test.use({ storageState: sessionFile('requester') })

// Specs run in parallel against one database, so these tests rely only on the notes they create. The
// 'zzz' prefix and the descending sort put them on the first page.
const notesPath = '/api/apps/E2eApp/entities/Note/records'
const deletesPage = '/e2e/notedeletes?pageSize=100&sort=-title'

async function createNote(request: APIRequestContext, title: string) {
  const response = await request.post(notesPath, { data: { values: { title } }, headers: csrfHeaders })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string }
}

test('a user with the delete grant deletes a note after confirming', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `zzz Delete ${run}`
  const note = await createNote(request, title)

  await page.goto(deletesPage)
  const row = page.getByRole('row', { name: new RegExp(title) })
  await row.getByRole('button', { name: 'Delete', exact: true }).click()
  const dialog = await waitForDialog(page)
  await expect(dialog).toContainText('Delete this record? This cannot be undone.')
  await confirmDialog(page, 'Delete')

  await expect(page.getByTestId('table-action-done')).toHaveText('The record was deleted.')
  await expect(row).toHaveCount(0)
  const read = await request.get(`${notesPath}/${note.id}`)
  expect(read.status()).toBe(404)
})

test('a user without the delete grant sees no Delete button, and the server refuses a direct delete', async ({
  page,
  request,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `zzz Shared delete ${run}`
  const note = await createNote(request, title)

  await useSession(page, 'finance-reviewer')
  await page.goto(deletesPage)
  const row = page.getByRole('row', { name: new RegExp(title) })
  await expect(row).toBeVisible()
  await expect(page.getByRole('button', { name: /Delete/i })).toHaveCount(0)

  const denied = await page.request.delete(`${notesPath}/${note.id}`, { headers: csrfHeaders })
  expect(denied.status()).toBe(403)
  const read = await request.get(`${notesPath}/${note.id}`)
  expect(read.status()).toBe(200)
})
