import { expect, test } from '../test'
import type { APIRequestContext } from '@playwright/test'
import { expectChosen, pickOption } from '../select'
import { sessionFile } from '../sessions'
import { chooseLanguage } from '../shell'

// The record API needs a session with a grant, so this journey starts signed in as the employee requester.
test.use({ storageState: sessionFile('requester') })

// Specs run in parallel against one database, so this test relies only on the note it creates.
const notesPath = '/api/apps/E2eApp/entities/Note/records'
const tablePath = /\/e2e\/notes\?pageSize=100&sort=-title$/

async function readRecord(request: APIRequestContext, id: string) {
  const response = await request.get(`${notesPath}/${id}`)
  expect(response.status()).toBe(200)
  return (await response.json()) as { values: { status: string } }
}

test('a note status shows its label in English and Vietnamese, and the record keeps the raw value', async ({
  page,
  request,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const title = `Labels ${run}`
  await page.goto('/e2e/notes?pageSize=100&sort=-title')

  await page.getByRole('button', { name: 'New', exact: true }).click()
  await expect(page).toHaveURL(/\/e2e\/noteform\/new$/)
  await page.getByRole('textbox', { name: 'Title', exact: true }).fill(title)
  await pickOption(page.getByRole('combobox', { name: 'Status', exact: true }), 'In progress')
  const created = page.waitForResponse(
    (response) => response.url().includes('/entities/Note/records') && response.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  const { id } = (await (await created).json()) as { id: string }

  await expect(page).toHaveURL(tablePath)
  const row = page.getByRole('row', { name: new RegExp(`${title} `) })
  await expect(row).toContainText('In progress')
  await expect(row).not.toContainText('open')
  expect((await readRecord(request, id)).values.status).toBe('open')

  await page.goto(`/e2e/notedetail?note=${id}`)
  await expect(page.getByTestId('detail')).toContainText('In progress')

  await page.goto(`/e2e/noteform/${id}`)
  await expectChosen(page.getByRole('combobox', { name: 'Status', exact: true }), 'In progress')

  // Slows the switch so that leaving the page before it lands would lose the choice on every run.
  await page.route(
    '**/api/sites/e2e/texts/vi',
    async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 2_000))
      await route.continue()
    },
    { times: 1 },
  )
  await chooseLanguage(page, 'Tiếng Việt')
  // The site stores the chosen language only once its texts have loaded, so the page waits for that first.
  await expectChosen(page.getByRole('combobox', { name: 'Trạng thái', exact: true }), 'Đang mở')

  await page.goto('/e2e/notes?pageSize=100&sort=-title')
  const viRow = page.getByRole('row', { name: new RegExp(`${title} `) })
  await expect(viRow).toContainText('Đang mở')
  await expect(viRow).not.toContainText('In progress')

  await page.goto(`/e2e/notedetail?note=${id}`)
  await expect(page.getByTestId('detail')).toContainText('Đang mở')

  await page.goto(`/e2e/noteform/${id}`)
  await expectChosen(page.getByRole('combobox', { name: 'Trạng thái', exact: true }), 'Đang mở')
})
