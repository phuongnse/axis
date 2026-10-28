import { expect, test } from '../test'
import type { APIRequestContext } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { waitForDialog } from '../dialogs'
import { sessionFile, useSession } from '../sessions'

// requester creates the records through the record API, then dung looks at them. dung's finance-reviewer
// role reads only notes and categories whose title or name contains 'Shared'.
test.use({ storageState: sessionFile('requester') })

async function createRecord(request: APIRequestContext, entity: string, values: Record<string, unknown>) {
  const response = await request.post(`/api/apps/E2eApp/entities/${entity}/records`, {
    data: { values },
    headers: csrfHeaders,
  })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string }
}

test('a data source table and a reference lookup show only the records the user may read', async ({
  page,
  request,
}) => {
  const run = `${Date.now()}-${test.info().retry}`
  await createRecord(request, 'Note', { title: `Shared ${run}` })
  await createRecord(request, 'Note', { title: `Private ${run}` })
  // Other specs create categories too. A leading digit sorts these onto the first lookup page.
  await createRecord(request, 'Category', { name: `0 Shared ${run}` })
  await createRecord(request, 'Category', { name: `0 Private ${run}` })

  await useSession(page, 'dung')
  await page.goto('/e2e/noterows')
  await expect(page.getByRole('row', { name: new RegExp(`Shared ${run}`) })).toBeVisible()
  await expect(page.getByRole('row', { name: new RegExp(`Private ${run}`) })).toHaveCount(0)

  await page.goto('/e2e/notes')
  await page.getByRole('button', { name: 'New', exact: true }).click()
  await page.getByRole('button', { name: 'Choose', exact: true }).click()
  const dialog = await waitForDialog(page)
  await expect(dialog.getByRole('row', { name: new RegExp(`0 Shared ${run}`) })).toBeVisible()
  await expect(dialog.getByRole('row', { name: new RegExp(`0 Private ${run}`) })).toHaveCount(0)
})
