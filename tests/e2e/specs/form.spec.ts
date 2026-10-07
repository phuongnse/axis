import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

// Specs run in parallel against one database, so these tests rely only on the records they create,
// and fewer than one hundred notes in all for the list check.
const notesPath = '/api/apps/E2eApp/entities/Note/records'

async function createRecord(request: APIRequestContext, entity: string, values: Record<string, unknown>) {
  const response = await request.post(`/api/apps/E2eApp/entities/${entity}/records`, { data: { values } })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string }
}

/** Types a value into a date or date-time picker and confirms it with Enter, as a user does. */
async function pick(page: Page, label: string, text: string) {
  const input = page.getByLabel(label, { exact: true })
  await input.click()
  await input.fill(text)
  await input.press('Enter')
  await expect(input).toHaveValue(text)
}

test('a note is created and edited in the form, in light and dark mode', async ({ page }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `Form ${run}`
  const tablePath = /\/e2e\/notes\?pageSize=100&sort=-title$/
  await page.goto('/e2e/notes?pageSize=100&sort=-title')

  await page.getByRole('link', { name: 'New' }).click()
  await expect(page).toHaveURL(/\/e2e\/noteform\/new$/)
  await page.getByLabel('Title', { exact: true }).fill(title)
  await page.getByLabel('Code', { exact: true }).fill(`C-${run}`)
  await page.getByLabel('Priority', { exact: true }).fill('7')
  await page.getByLabel('Amount', { exact: true }).fill('12.50')
  await page.getByLabel('Done', { exact: true }).check()
  await pick(page, 'Due on', '2026-10-07')
  await pick(page, 'Due at', '2026-10-07 10:30:00')
  // Enter in a picker confirms the value and does not send the form.
  await expect(page).toHaveURL(/\/e2e\/noteform\/new$/)
  await page.getByLabel('Status', { exact: true }).click()
  await page.locator('.ant-select-item-option[title="open"]').click()
  await page.getByRole('button', { name: 'Save' }).click()

  await expect(page).toHaveURL(tablePath)
  const row = page.getByRole('row', { name: new RegExp(`${title} `) })
  for (const text of [`C-${run}`, '7', '12.50', 'Yes', 'open', 'Oct 7, 2026', '10:30:00']) {
    await expect(row).toContainText(text)
  }

  await row.getByRole('link', { name: 'Open' }).click()
  await expect(page.getByLabel('Title', { exact: true })).toHaveValue(title)
  await page.getByLabel('Title', { exact: true }).fill(`${title} edited`)
  await page.getByRole('button', { name: 'Save' }).click()

  await expect(page).toHaveURL(tablePath)
  const edited = page.getByRole('row', { name: new RegExp(`${title} edited`) })
  await expect(edited).toContainText(`C-${run}`)
  await page.reload()
  await expect(edited).toContainText(`C-${run}`)

  await page.getByRole('switch', { name: 'Dark mode' }).click()
  await edited.getByRole('link', { name: 'Open' }).click()
  const dark = page.locator('[data-theme-mode="dark"]')
  await expect(dark.getByLabel('Title', { exact: true })).toHaveValue(`${title} edited`)
  await dark.getByRole('button', { name: 'Cancel' }).click()
  await expect(page).toHaveURL(tablePath)
})

test('an empty form shows the server error under the title', async ({ page }) => {
  await page.goto('/e2e/noteform/new')

  await page.getByRole('button', { name: 'Save' }).click()

  await expect(page.getByTestId('field-error-title')).toHaveText('Required.')
  await expect(page).toHaveURL(/\/e2e\/noteform\/new$/)
})

test('editing the title leaves an untouched date-time unchanged', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const note = await createRecord(request, 'Note', { title: `Fraction ${run}`, dueAt: '2026-10-06T02:00:00.123456Z' })

  await page.goto(`/e2e/noteform/${note.id}`)
  await page.getByLabel('Title', { exact: true }).fill(`Fraction ${run} edited`)
  await page.getByRole('button', { name: 'Save' }).click()
  // Opened without a table, the form returns to the site, which opens its first page.
  await expect(page).toHaveURL(/\/e2e\/notes$/)

  const read = await request.get(`${notesPath}/${note.id}`)
  const record = await read.json()
  expect(record.version).toBe(2)
  expect(record.values.title).toBe(`Fraction ${run} edited`)
  expect(record.values.dueAt).toBe('2026-10-06T02:00:00.123456Z')
})

test('a stale version shows a conflict, and a duplicate code an error under Code', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const a = await createRecord(request, 'Note', { title: `Conflict ${run} A`, code: `A-${run}` })
  await createRecord(request, 'Note', { title: `Conflict ${run} B`, code: `B-${run}` })

  await page.goto(`/e2e/noteform/${a.id}`)
  const titleInput = page.getByLabel('Title', { exact: true })
  await expect(titleInput).toHaveValue(`Conflict ${run} A`)
  const patched = await request.patch(`${notesPath}/${a.id}`, {
    data: { version: 1, values: { title: 'Changed elsewhere' } },
  })
  expect(patched.status()).toBe(200)

  await titleInput.fill('Changed here')
  await page.getByRole('button', { name: 'Save' }).click()

  await expect(page.getByTestId('form-conflict')).toBeVisible()
  await page.getByRole('button', { name: 'Reload' }).click()
  await expect(titleInput).toHaveValue('Changed elsewhere')
  await expect(page.getByTestId('form-conflict')).toBeHidden()

  await page.getByLabel('Code', { exact: true }).fill(`B-${run}`)
  await page.getByRole('button', { name: 'Save' }).click()

  await expect(page.getByTestId('field-error-code')).toHaveText('Must be unique.')
})
