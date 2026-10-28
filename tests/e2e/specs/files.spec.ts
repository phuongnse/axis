import { expect, test } from '../test'
import type { APIRequestContext, Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { cellValue } from '../fields'
import { sessionFile } from '../sessions'

// The record and file APIs need a session with a grant, so these journeys start signed in as the employee anna.
test.use({ storageState: sessionFile('anna') })

// Specs run in parallel against one database, so each test relies only on the records it creates.
async function createAsset(request: APIRequestContext, name: string) {
  const response = await request.post('/api/apps/E2eApp/entities/Asset/records', {
    data: { values: { name } },
    headers: csrfHeaders,
  })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string }
}

// The server checks that a PDF starts with its signature.
function pdf(name: string) {
  return {
    name,
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.4\n%test\n'),
  }
}

/** Picks the file with the upload button called `button` and waits for its name to show as a link. */
async function upload(page: Page, button: string, file: ReturnType<typeof pdf>) {
  const chooser = page.waitForEvent('filechooser')
  await page.getByRole('button', { name: button, exact: true }).click()
  await (await chooser).setFiles(file)
  await expect(page.getByRole('link', { name: file.name, exact: true })).toBeVisible()
}

/** Saves the form and waits until the save is done and the form is left. */
async function save(page: Page) {
  const saved = page.waitForResponse(
    (response) => response.request().method() === 'PATCH' && response.url().includes('/entities/Asset/records/'),
  )
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  expect((await saved).status()).toBe(200)
  await expect(page).not.toHaveURL(/assetform/)
}

test('a PDF is attached on an asset form, linked in the table and the detail, downloaded and replaced', async ({
  page,
  request,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  // Names that sort last come first in a descending sort, so the row is on the first page.
  const name = `Zz receipt ${run}`
  const first = `receipt-${run}.pdf`
  const second = `second-${run}.pdf`
  const asset = await createAsset(request, name)

  await page.goto(`/e2e/assetform/${asset.id}`)
  await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue(name)
  await upload(page, 'Upload Receipt', pdf(first))
  await expect(page.getByTestId('file-receipt-announcement')).toHaveText(`Uploaded ${first}`)
  await save(page)

  await page.goto('/e2e/assets?pageSize=100&sort=-name')
  const row = page.getByRole('row').filter({ hasText: name })
  await expect(row.getByRole('link', { name: first, exact: true })).toBeVisible()

  await page.goto(`/e2e/assetdetail?asset=${asset.id}`)
  const link = page.getByRole('link', { name: first, exact: true })
  const download = page.waitForEvent('download')
  await link.click()
  expect((await download).suggestedFilename()).toBe(first)

  await page.goto(`/e2e/assetform/${asset.id}`)
  await upload(page, 'Upload Receipt', pdf(second))
  await expect(page.getByRole('link', { name: first, exact: true })).toBeHidden()
  await save(page)

  await page.goto(`/e2e/assetdetail?asset=${asset.id}`)
  await expect(page.getByRole('link', { name: second, exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: first, exact: true })).toBeHidden()
})

test('a file is removed on an asset form and the detail shows none', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const file = `gone-${run}.pdf`
  const asset = await createAsset(request, `Remove ${run}`)

  await page.goto(`/e2e/assetform/${asset.id}`)
  await upload(page, 'Upload Receipt', pdf(file))
  await save(page)

  await page.goto(`/e2e/assetform/${asset.id}`)
  await page.getByRole('button', { name: 'Remove Receipt', exact: true }).click()
  await expect(page.getByTestId('file-receipt-announcement')).toHaveText(`Removed ${file}`)
  await expect(page.getByRole('button', { name: 'Upload Receipt', exact: true })).toBeFocused()
  await save(page)

  await page.goto(`/e2e/assetdetail?asset=${asset.id}`)
  await expect(page.getByRole('link', { name: file, exact: true })).toBeHidden()
})

test('a child row with a file is added on an asset form and its link shows on the detail', async ({
  page,
  request,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const file = `scan-${run}.pdf`
  const asset = await createAsset(request, `Scanner ${run}`)

  await page.goto(`/e2e/assetform/${asset.id}`)
  await page.getByRole('button', { name: 'Add row', exact: true }).click()
  await upload(page, 'Upload File 1', pdf(file))
  await page.getByRole('textbox', { name: 'Note 1', exact: true }).fill('Signed copy')
  await save(page)

  await page.goto(`/e2e/assetdetail?asset=${asset.id}`)
  const link = page.getByRole('link', { name: file, exact: true })
  await expect(link).toBeVisible()
  await expect(cellValue(page, 'attachments', 0, 'note')).toHaveText('Signed copy')
})
