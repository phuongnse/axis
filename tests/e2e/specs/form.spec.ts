import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { pickOption } from '../select'
import { sessionFile, useSession } from '../sessions'

// The record API needs a session with a grant, so these journeys start signed in as the employee anna.
test.use({ storageState: sessionFile('anna') })

// Specs run in parallel against one database, so these tests rely only on the records they create,
// and fewer than one hundred notes in all for the list check.
const notesPath = '/api/apps/E2eApp/entities/Note/records'

async function createRecord(request: APIRequestContext, entity: string, values: Record<string, unknown>) {
  const response = await request.post(`/api/apps/E2eApp/entities/${entity}/records`, {
    data: { values },
    headers: csrfHeaders,
  })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string }
}

/** Presses Tab, or Shift+Tab with `back`, until the control has focus, as a keyboard user reaches it. */
async function tabTo(page: Page, control: Locator, back = false) {
  for (let presses = 0; presses < 40; presses++) {
    if (await control.evaluate((element) => element === document.activeElement)) {
      return
    }
    await page.keyboard.press(back ? 'Shift+Tab' : 'Tab')
  }
  await expect(control).toBeFocused()
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

  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/e2e\/noteform\/new$/)
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(title)
  await page.getByLabel('Code', { exact: true }).fill(`C-${run}`)
  await page.getByLabel('Priority', { exact: true }).fill('7')
  await page.getByLabel('Amount', { exact: true }).fill('12.50')
  await page.getByLabel('Done', { exact: true }).check()
  await pick(page, 'Due on', '2026-10-07')
  await pick(page, 'Due at', '2026-10-07 10:30:00')
  // Enter in a picker confirms the value and does not send the form.
  await expect(page).toHaveURL(/\/e2e\/noteform\/new$/)
  await pickOption(page.getByRole('combobox', { name: 'Status', exact: true }), 'open')
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(tablePath)
  const row = page.getByRole('row', { name: new RegExp(`${title} `) })
  for (const text of [`C-${run}`, '7', '12.50', 'Yes', 'open', 'Oct 7, 2026', '10:30:00']) {
    await expect(row).toContainText(text)
  }

  await row.getByRole('button', { name: 'Open', exact: true }).click()
  await expect(page.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(title)
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(`${title} edited`)
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(tablePath)
  const edited = page.getByRole('row', { name: new RegExp(`${title} edited`) })
  await expect(edited).toContainText(`C-${run}`)
  await page.reload()
  await expect(edited).toContainText(`C-${run}`)

  await page.getByRole('switch', { name: 'Dark mode', exact: true }).click()
  await edited.getByRole('button', { name: 'Open', exact: true }).click()
  const dark = page.locator('[data-theme-mode="dark"]')
  await expect(dark.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(`${title} edited`)
  await dark.getByRole('button', { name: 'Cancel', exact: true }).click()
  await expect(page).toHaveURL(tablePath)
})

test('a note saved by a signed-in user shows its creator read-only', async ({ page }) => {
  const title = `Creator ${Date.now()}-${test.info().retry}`
  await useSession(page, 'binh')
  const me = (await (await page.request.get('/api/me')).json()) as { id: string }
  await page.goto('/e2e/notes?pageSize=100&sort=-title')

  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/e2e\/noteform\/new$/)
  await expect(page.getByLabel('Created by', { exact: true })).toHaveValue('')
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(title)
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(/\/e2e\/notes\?pageSize=100&sort=-title$/)
  await page
    .getByRole('row', { name: new RegExp(`${title} `) })
    .getByRole('button', { name: 'Open', exact: true })
    .click()
  await expect(page.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(title)
  const createdBy = page.getByLabel('Created by', { exact: true })
  await expect(createdBy).toHaveValue(me.id)
  await expect(createdBy).not.toBeEditable()
})

test('line items are added, edited and removed in the form, in light and dark mode', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `Lines ${run}`
  const tablePath = /\/e2e\/notes\?pageSize=100&sort=-title$/
  const cell = (label: string) => page.getByLabel(label, { exact: true })
  await page.goto('/e2e/notes?pageSize=100&sort=-title')

  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/e2e\/noteform\/new$/)
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(title)
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await cell('Description 1').fill('Pens')
  await cell('Quantity 1').fill('2')
  await cell('Description 2').fill('Ink')
  await cell('Quantity 2').fill('1')
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(tablePath)
  const row = page.getByRole('row', { name: new RegExp(`${title} `) })
  await row.getByRole('button', { name: 'Open', exact: true }).click()
  await expect(cell('Description 1')).toHaveValue('Pens')
  await expect(cell('Quantity 1')).toHaveValue('2')
  await expect(cell('Description 2')).toHaveValue('Ink')
  await expect(cell('Quantity 2')).toHaveValue('1')
  const id = /\/e2e\/noteform\/([0-9a-f-]+)$/.exec(page.url())![1]

  await page.getByRole('switch', { name: 'Dark mode', exact: true }).click()
  const dark = page.locator('[data-theme-mode="dark"]')
  await dark.getByLabel('Quantity 1', { exact: true }).fill('5')
  await dark.getByRole('button', { name: 'Remove', exact: true }).nth(1).click()
  await expect(page.getByLabel(/Description 2/i)).toHaveCount(0)
  await dark.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(tablePath)
  await row.getByRole('button', { name: 'Open', exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/e2e/noteform/${id}$`))
  await page.reload()
  await expect(dark.getByLabel('Description 1', { exact: true })).toHaveValue('Pens')
  await expect(dark.getByLabel('Quantity 1', { exact: true })).toHaveValue('5')
  await expect(page.getByLabel(/Description 2/i)).toHaveCount(0)

  const read = await request.get(`${notesPath}/${id}`)
  const record = await read.json()
  expect(record.values.lines).toEqual([{ description: 'Pens', quantity: 5, category: null }])
})

// Each test has its own browser context, so the saved session is loaded only into this test's context.
test('after a signed-in user edits a note, its history shows the update first, in light and dark mode', async ({
  page,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `History ${run}`
  const tablePath = /\/e2e\/notes\?pageSize=100&sort=-title$/
  await useSession(page, 'binh')
  await page.goto('/e2e/notes?pageSize=100&sort=-title')
  await expect(page.getByRole('banner').getByRole('button', { name: 'Binh Engineering Manager', exact: true })).toBeVisible()
  // The page's request context shares the session cookie, so Binh creates the note.
  await createRecord(page.request, 'Note', { title })
  await page.reload()

  const row = page.getByRole('row', { name: new RegExp(`${title} `) })
  await row.getByRole('button', { name: 'Open', exact: true }).click()
  await expect(page.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(title)
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(`${title} edited`)
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page).toHaveURL(tablePath)

  const edited = page.getByRole('row', { name: new RegExp(`${title} edited`) })
  for (const mode of ['light', 'dark']) {
    if (mode === 'dark') {
      await page.getByRole('switch', { name: 'Dark mode', exact: true }).click()
    }
    await edited.getByRole('button', { name: 'Open', exact: true }).click()
    const history = page.locator(`[data-theme-mode="${mode}"]`).getByTestId('record-history')
    await expect(history).toContainText('History')
    const entries = history.getByTestId('history-entry')
    await expect(entries).toHaveCount(2)
    for (const text of ['Updated', 'Binh Engineering Manager', 'Changed: Title']) {
      await expect(entries.first()).toContainText(text)
    }
    await expect(entries.nth(1)).toContainText('Created')
    await expect(entries.nth(1)).toContainText('Binh Engineering Manager')
    await page.getByRole('button', { name: 'Cancel', exact: true }).click()
    await expect(page).toHaveURL(tablePath)
  }
})

test('an empty form shows the server error under the title', async ({ page }) => {
  await page.goto('/e2e/noteform/new')

  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page.getByTestId('field-error-title')).toHaveText('Required.')
  await expect(page).toHaveURL(/\/e2e\/noteform\/new$/)
})

test('editing the title leaves an untouched date-time unchanged', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const note = await createRecord(request, 'Note', { title: `Fraction ${run}`, dueAt: '2026-10-06T02:00:00.123456Z' })

  await page.goto(`/e2e/noteform/${note.id}`)
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(`Fraction ${run} edited`)
  await page.getByRole('button', { name: 'Save', exact: true }).click()
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
  const titleInput = page.getByRole('textbox', { name: '* Title', exact: true })
  await expect(titleInput).toHaveValue(`Conflict ${run} A`)
  const patched = await request.patch(`${notesPath}/${a.id}`, {
    data: { version: 1, values: { title: 'Changed elsewhere' } },
    headers: csrfHeaders,
  })
  expect(patched.status()).toBe(200)

  await titleInput.fill('Changed here')
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page.getByTestId('form-conflict')).toBeVisible()
  await page.getByRole('button', { name: 'Reload', exact: true }).click()
  await expect(titleInput).toHaveValue('Changed elsewhere')
  await expect(page.getByTestId('form-conflict')).toBeHidden()

  await page.getByLabel('Code', { exact: true }).fill(`B-${run}`)
  // Ant Design adds "loading" to the name while the button is busy.
  await page.getByRole('button', { name: /^(loading )?Save$/ }).click()

  await expect(page.getByTestId('field-error-code')).toHaveText('Must be unique.')
})

test('a category is picked in the lookup and shows on the saved note', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  // Other specs create categories too. A leading digit sorts this one onto the first lookup page.
  const category = `0 Lookup ${run}`
  await createRecord(request, 'Category', { name: category })
  const tablePath = /\/e2e\/notes\?pageSize=100&sort=-title$/
  await page.goto('/e2e/notes?pageSize=100&sort=-title')

  await page.getByRole('button', { name: 'New', exact: true }).click()
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(`Lookup ${run}`)
  await page.getByRole('button', { name: 'Choose', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByRole('row', { name: new RegExp(category) }).click()

  await expect(dialog).toBeHidden()
  await expect(page.getByLabel('Category', { exact: true })).toHaveValue(category)
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(tablePath)
  await expect(page.getByRole('row', { name: new RegExp(`Lookup ${run} `) })).toContainText(category)
})

test('a category is picked in the lookup of a line item and shows on the saved line', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  // Other specs create categories too. A leading digit sorts this one onto the first lookup page.
  const category = `0 Line lookup ${run}`
  await createRecord(request, 'Category', { name: category })
  const title = `Line lookup ${run}`
  await page.goto('/e2e/notes?pageSize=100&sort=-title')

  await page.getByRole('button', { name: 'New', exact: true }).click()
  await page.getByRole('textbox', { name: '* Title', exact: true }).fill(title)
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await page.getByLabel('Description 1', { exact: true }).fill('Pens')
  await page.getByRole('button', { name: 'Choose Category 1', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByRole('row', { name: new RegExp(category) }).click()

  await expect(dialog).toBeHidden()
  await expect(page.getByLabel('Category 1', { exact: true })).toHaveValue(category)
  await page.getByRole('button', { name: 'Save', exact: true }).click()

  await expect(page).toHaveURL(/\/e2e\/notes\?pageSize=100&sort=-title$/)
  await page
    .getByRole('row', { name: new RegExp(`${title} `) })
    .getByRole('button', { name: 'Open', exact: true })
    .click()
  await expect(page.getByLabel('Category 1', { exact: true })).toHaveValue(category)
})

test('a form in two sections shows both titles, and saving keeps its read-only field', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const note = await createRecord(request, 'Note', { title: `Sections ${run}`, code: `S-${run}` })

  await page.goto(`/e2e/notesections/${note.id}`)
  await expect(page.getByRole('heading', { name: 'Main', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Details', exact: true })).toBeVisible()
  await expect(page.getByRole('textbox', { name: '* Title', exact: true })).toHaveValue(`Sections ${run}`)
  const code = page.getByLabel('Code', { exact: true })
  await expect(code).toHaveValue(`S-${run}`)
  await expect(code).not.toBeEditable()
  // A field the form does not list is not shown.
  await expect(page.getByLabel(/Done/)).toBeHidden()

  await page.getByLabel('Priority', { exact: true }).fill('4')
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(page).toHaveURL(/\/e2e\/notes$/)

  const read = await request.get(`${notesPath}/${note.id}`)
  const record = await read.json()
  expect(record.version).toBe(2)
  expect(record.values.code).toBe(`S-${run}`)
  expect(record.values.priority).toBe(4)
})

test('a wizard is filled with the keyboard only: Next checks the step, Back keeps the values', async ({ page }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = page.getByRole('textbox', { name: '* Title', exact: true })
  const code = page.getByLabel('Code', { exact: true })
  const priority = page.getByLabel('Priority', { exact: true })
  const next = page.getByRole('button', { name: 'Next', exact: true })
  const back = page.getByRole('button', { name: 'Back', exact: true })
  const save = page.getByRole('button', { name: 'Save', exact: true })

  await page.goto('/e2e/notewizard/new')
  await expect(page.getByTestId('form-steps')).toContainText('Basics')
  await expect(page.getByTestId('form-steps')).toContainText('Extras')
  await expect(save).toBeHidden()

  // An empty title blocks Next, so the form stays on the first step with the message.
  await tabTo(page, next)
  await page.keyboard.press('Enter')
  await expect(page.getByTestId('field-error-title')).toBeVisible()
  await expect(priority).toBeHidden()
  await expect(save).toBeHidden()

  await tabTo(page, title, true)
  await page.keyboard.type(`Wizard ${run}`)
  await page.keyboard.press('Tab')
  await expect(code).toBeFocused()
  await page.keyboard.type(`W-${run}`)
  await page.keyboard.press('Tab')
  await expect(next).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(priority).toBeFocused()
  await expect(title).toBeHidden()
  await page.keyboard.type('5')

  // Back keeps what was typed on both steps.
  await page.keyboard.press('Tab')
  await expect(back).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(title).toBeFocused()
  await expect(title).toHaveValue(`Wizard ${run}`)
  await expect(code).toHaveValue(`W-${run}`)
  await page.keyboard.press('Tab')
  await page.keyboard.press('Tab')
  await expect(next).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(priority).toBeFocused()
  await expect(priority).toHaveValue('5')

  await page.keyboard.press('Tab')
  await expect(back).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(save).toBeFocused()
  const created = page.waitForResponse(
    (response) => response.url().includes('/entities/Note/records') && response.request().method() === 'POST',
  )
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/e2e\/notes$/)

  const { id } = (await (await created).json()) as { id: string }
  const record = await (await page.request.get(`${notesPath}/${id}`)).json()
  expect(record.values.title).toBe(`Wizard ${run}`)
  expect(record.values.code).toBe(`W-${run}`)
  expect(record.values.priority).toBe(5)
})
