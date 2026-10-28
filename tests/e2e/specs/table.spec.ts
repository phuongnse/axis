import { expect, test } from '../test'
import type { APIRequestContext } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { sessionFile } from '../sessions'

// The record API needs a session with a grant, so these journeys start signed in as the employee requester.
test.use({ storageState: sessionFile('requester') })

// Specs run in parallel against one database, so these tests rely only on the records they create:
// at least eleven notes for two pages of ten, and fewer than one hundred for the label check.
const notesPath = '/api/apps/E2eApp/entities/Note/records'

async function createRecord(request: APIRequestContext, entity: string, values: Record<string, unknown>) {
  const response = await request.post(`/api/apps/E2eApp/entities/${entity}/records`, {
    data: { values },
    headers: csrfHeaders,
  })
  expect(response.status()).toBe(201)
  return (await response.json()) as { id: string }
}

test('paging and sorting go through the record API and stay in the URL', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  for (let index = 1; index <= 11; index++) {
    await createRecord(request, 'Note', { title: `Paging ${run} ${String(index).padStart(2, '0')}` })
  }

  const firstLoad = page.waitForResponse((response) => response.url().includes(`${notesPath}?`))
  await page.goto('/e2e/notes?pageSize=10')
  await firstLoad

  // A new sort starts again at page 1.
  const sortRequest = page.waitForRequest(
    (request) => request.url().includes(`${notesPath}?`) && request.url().includes('sort=title'),
  )
  await page.getByRole('columnheader', { name: 'Title', exact: true }).click()
  expect((await sortRequest).url()).not.toContain('page=')
  await expect(page).toHaveURL(/\/e2e\/notes\?pageSize=10&sort=title$/)

  const pageRequest = page.waitForRequest(
    (request) => request.url().includes(`${notesPath}?`) && request.url().includes('page=2'),
  )
  await page.getByTitle('2', { exact: true }).click()
  expect((await pageRequest).url()).toContain('sort=title')
  await expect(page).toHaveURL(/\/e2e\/notes\?page=2&pageSize=10&sort=title$/)

  await page.reload()

  await expect(page).toHaveURL(/\/e2e\/notes\?page=2&pageSize=10&sort=title$/)
  await expect(page
        .getByRole('main')
        .getByRole('list')
        .filter({ has: page.getByTitle('Next Page', { exact: true }) })
        .locator('[aria-current="page"]')).toHaveText('2')
  await expect(page.getByRole('columnheader', { name: 'Title', exact: true })).toHaveAttribute('aria-sort', 'ascending')
})

test('a note shows the name of the category it references', async ({ page, request }) => {
  const run = `${Date.now()}-${test.info().retry}`
  const category = await createRecord(request, 'Category', { name: `Label ${run}` })
  await createRecord(request, 'Note', { title: `Labelled ${run}`, category: category.id })

  await page.goto('/e2e/notes?pageSize=100')

  await expect(page.getByRole('row', { name: new RegExp(`Labelled ${run}`) })).toContainText(`Label ${run}`)
})
