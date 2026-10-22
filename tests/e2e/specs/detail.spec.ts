import { expect, test, type APIRequestContext } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { sessionFile } from '../sessions'

// The record API needs a session with a grant, so these journeys start signed in as the employee anna.
test.use({ storageState: sessionFile('anna') })

// Specs run in parallel against one database, so these tests rely only on the notes they create.
const notesPath = '/api/apps/E2eApp/entities/Note/records'

async function createNote(request: APIRequestContext, values: Record<string, unknown>) {
  const response = await request.post(notesPath, { data: { values }, headers: csrfHeaders })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string }
}

test('a detail page shows a note read-only, its start button closes the note and its edit button opens the form', async ({
  page,
  request,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const note = await createNote(request, { title: `Detail ${run}`, status: 'open', priority: 3 })

  await page.goto(`/e2e/notedetail?note=${note.id}`)
  const detail = page.getByTestId('detail')
  await expect(detail).toContainText(`Detail ${run}`)
  await expect(detail).toContainText('Priority')
  await expect(page.getByRole('textbox')).toHaveCount(0)

  await page.getByRole('button', { name: 'Close note' }).click()
  await expect(page.getByTestId('detail-started')).toHaveText('The process has started.')
  // The worker runs the first step some time after the start.
  await expect(async () => {
    await page.reload()
    await expect(detail).toContainText('closed', { timeout: 1_000 })
  }).toPass({ timeout: 30_000 })

  await page.getByRole('button', { name: 'Edit note' }).click()
  await expect(page).toHaveURL(new RegExp(`/e2e/noteactions/${note.id}$`))
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue(`Detail ${run}`)
})

test('a detail page for a note that does not exist shows the not-found page', async ({ page }) => {
  await page.goto('/e2e/notedetail?note=0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b')

  await expect(page.getByTestId('not-found')).toBeVisible()
})
