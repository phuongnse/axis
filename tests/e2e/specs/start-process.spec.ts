import { expect, test } from '../test'
import type { APIRequestContext } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { expectChosen } from '../select'
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

test('a saved note closes through the action, which saves the edited title first', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const note = await createNote(request, { title: `Close ${run}`, status: 'open' })

  await page.goto(`/e2e/noteactions/${note.id}`)
  const title = page.getByRole('textbox', { name: '* Title', exact: true })
  await expect(title).toHaveValue(`Close ${run}`)
  await title.fill(`Close ${run} edited`)
  await page.getByRole('button', { name: 'Close note', exact: true }).click()

  await expect(page.getByTestId('form-started')).toHaveText('The process has started.')
  await expect(page).toHaveURL(new RegExp(`/e2e/noteactions/${note.id}$`))
  // The worker runs the first step some time after the start.
  await expect(async () => {
    await page.reload()
    await expectChosen(page.getByRole('combobox', { name: 'Status', exact: true }), 'Closed', { timeout: 1_000 })
  }).toPass({ timeout: 30_000 })
  await expect(title).toHaveValue(`Close ${run} edited`)

  const read = await request.get(`${notesPath}/${note.id}`)
  const record = await read.json()
  expect(record.values.title).toBe(`Close ${run} edited`)
  expect(record.values.status).toBe('closed')
})

test('a note that fails the start condition shows its message, and no instance starts', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const note = await createNote(request, { title: `Closed ${run}`, status: 'closed' })

  await page.goto(`/e2e/noteactions/${note.id}`)
  await expect(page.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(`Closed ${run}`)
  const start = page.waitForResponse((response) => response.url().endsWith('/processes/NoteClose/instances'))
  await page.getByRole('button', { name: 'Close note', exact: true }).click()

  expect((await start).status()).toBe(400)
  await expect(page.getByTestId('form-error')).toHaveText('Only an open note can be closed.')
  await expect(page.getByTestId('form-started')).toHaveCount(0)

  const history = await request.get(`${notesPath}/${note.id}/history`)
  expect(history.ok()).toBe(true)
  const { items } = (await history.json()) as { items: { action: string }[] }
  expect(items.map((item) => item.action)).not.toContain('process.started')
})
