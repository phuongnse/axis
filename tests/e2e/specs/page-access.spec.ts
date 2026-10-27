import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { csrfHeaders } from '../csrf'
import { sessionFile, useSession } from '../sessions'

// The employee anna may create, update and submit purchase requests. The department manager chau
// may only read them and holds no start grant, so the pages hide what chau may not do.
test.use({ storageState: sessionFile('anna') })

// Specs run in parallel against one database, so each test looks only at the request it creates.
const appPath = '/api/apps/PurchaseRequests'
const recordsPath = `${appPath}/entities/PurchaseRequest/records`

/** The id of the seeded department named `name`, read from the record API. */
async function departmentId(page: Page, name: string) {
  const response = await page.request.get(`${appPath}/entities/Department/records?pageSize=100`)
  expect(response.status()).toBe(200)
  const body = (await response.json()) as { items: { id: string; values: { name: string } }[] }
  const department = body.items.find((item) => item.values.name === name)
  expect(department).toBeDefined()
  return department!.id
}

/** Creates a draft Operations request as the signed-in user, and returns its title and id. */
async function createRequest(page: Page) {
  const title = `Access ${Date.now()}-${test.info().retry}`
  const response = await page.request.post(recordsPath, {
    data: {
      values: {
        title,
        status: 'draft',
        department: await departmentId(page, 'Operations'),
        lineItems: [{ description: 'Item', quantity: 1, unitPrice: 500 }],
      },
    },
    headers: csrfHeaders,
  })
  expect(response.status()).toBe(201)
  return { title, id: ((await response.json()) as { id: string }).id }
}

test('a user without a create grant sees no New button, and one without a start grant no Submit button', async ({
  page,
}) => {
  const { title, id } = await createRequest(page)

  await page.goto('/purchasing/purchaserequests')
  await expect(page.getByRole('button', { name: 'New', exact: true })).toBeVisible()
  await page.goto(`/purchasing/purchaserequestform/${id}`)
  await expect(page.getByRole('textbox', { name: 'Title', exact: true })).toHaveValue(title)
  await expect(page.getByRole('button', { name: 'Submit', exact: true })).toBeVisible()

  await useSession(page, 'chau')
  await page.goto('/purchasing/purchaserequests')
  // The rows show once the page has loaded, so the missing button is not just not rendered yet.
  await expect(page.getByRole('table').getByRole('link').first()).toBeVisible()
  await expect(page.getByRole('button', { name: /New/i })).toHaveCount(0)
  await page.goto(`/purchasing/purchaserequestform/${id}`)
  await expect(page.getByRole('textbox', { name: 'Title', exact: true })).toHaveValue(title)
  await expect(page.getByRole('button', { name: /Submit/i })).toHaveCount(0)
})

test('a user without an update grant sees the form read-only and no Save button', async ({ page }) => {
  const { title, id } = await createRequest(page)

  await page.goto(`/purchasing/purchaserequestform/${id}`)
  const titleInput = page.getByRole('textbox', { name: 'Title', exact: true })
  await expect(titleInput).toHaveValue(title)
  await expect(titleInput).not.toHaveAttribute('readonly')
  await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeVisible()

  await useSession(page, 'chau')
  await page.goto(`/purchasing/purchaserequestform/${id}`)
  await expect(titleInput).toHaveValue(title)
  await expect(titleInput).toHaveAttribute('readonly')
  await expect(page.getByRole('button', { name: /Save/i })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Cancel', exact: true })).toBeVisible()
})
