import { expect, test } from '../test'
import type { APIRequestContext, Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { sessionFile } from '../sessions'

// The record API needs a session with a grant, so this journey starts signed in as the employee anna.
test.use({ storageState: sessionFile('anna') })

// Specs run in parallel against one database, so this test relies only on the records it creates.
async function createRecord(request: APIRequestContext, entity: string, values: Record<string, unknown>) {
  const response = await request.post(`/api/apps/E2eApp/entities/${entity}/records`, {
    data: { values },
    headers: csrfHeaders,
  })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string }
}

/** Opens the lookup of the Software field, searches for the record and picks it. */
async function addSoftware(page: Page, name: string, run: string) {
  await page.getByRole('button', { name: 'Add Software', exact: true }).click()
  const dialog = await waitForDialog(page)
  await dialog.getByTestId('reference-lookup-search').fill(run)
  await dialog.getByRole('row', { name: new RegExp(name) }).click()
  await expect(dialog).toBeHidden()
}

/** Clicks Save and waits until the server has answered the update, so the next page loads the saved record. */
async function saveAsset(page: Page, id: string) {
  const saved = page.waitForResponse(
    (response) => response.url().includes(`/entities/Asset/records/${id}`) && response.request().method() === 'PATCH',
  )
  await page.getByRole('button', { name: 'Save', exact: true }).click()
  expect((await saved).ok()).toBe(true)
}

test('two software records are picked on an asset form, shown on its detail, and one is removed', async ({
  page,
  request,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  const word = `Word ${run}`
  const excel = `Excel ${run}`
  await createRecord(request, 'Software', { name: word })
  await createRecord(request, 'Software', { name: excel })
  const asset = await createRecord(request, 'Asset', { name: `Laptop ${run}` })

  await page.goto(`/e2e/assetform/${asset.id}`)
  await expect(page.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue(`Laptop ${run}`)
  await addSoftware(page, word, run)
  await expect(page.getByTestId('references-software-announcement')).toHaveText(`Added ${word}`)
  await addSoftware(page, excel, run)
  const list = page.getByRole('list', { name: 'Software', exact: true })
  await expect(list.getByRole('listitem')).toHaveCount(2)
  await saveAsset(page, asset.id)

  await page.goto(`/e2e/assetdetail?asset=${asset.id}`)
  const labels = page.getByTestId('detail-references-software')
  await expect(labels).toContainText(word)
  await expect(labels).toContainText(excel)
  await expect(labels).toHaveText(`${word}${excel}`)

  await page.goto(`/e2e/assetform/${asset.id}`)
  await page.getByRole('button', { name: `Remove ${word}`, exact: true }).click()
  await expect(page.getByTestId('references-software-announcement')).toHaveText(`Removed ${word}`)
  await saveAsset(page, asset.id)

  await page.goto(`/e2e/assetdetail?asset=${asset.id}`)
  await expect(page.getByTestId('detail-references-software')).toHaveText(excel)
})
