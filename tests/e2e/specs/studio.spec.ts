import { expect, test } from '@playwright/test'
import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { studioFolder } from '../server-settings'
import { useSession } from '../sessions'

test('an author follows the Studio link, searches for a resource and opens it', async ({ page }) => {
  await useSession(page, 'hoa')
  const list = await page.request.get('/api/studio/apps/PurchaseRequests/resources')
  const { items } = (await list.json()) as { items: { id: string; file: string }[] }
  const entity = items.find((item) => item.file === 'entities/purchase-request.json')!

  await page.goto('/')
  await page.getByRole('banner').getByRole('link', { name: 'Studio' }).click()
  // The sample is the only application, so it is picked without a choice.
  await expect(page).toHaveURL(/\/studio\?app=PurchaseRequests$/)
  await page.getByRole('searchbox', { name: 'Search by name or id' }).fill('PurchaseRequest')
  await page.getByTestId(`studio-resource-${entity.id}`).click()

  await expect(page.getByTestId('studio-resource-details')).toContainText('entities/purchase-request.json')
  await expect(page).toHaveURL(/resource=/)
  await expect(page.getByTestId('studio-diagnostics')).toBeVisible()
})

test('an author changes the maxLength of an entity field and the file holds only that change', async ({ page }) => {
  await useSession(page, 'hoa')
  const list = await page.request.get('/api/studio/apps/PurchaseRequests/resources')
  const { items } = (await list.json()) as { items: { id: string; file: string }[] }
  const entity = items.find((item) => item.file === 'entities/purchase-request.json')!
  const file = path.join(studioFolder, 'entities/purchase-request.json')
  const before = JSON.parse(await readFile(file, 'utf8')) as { fields: { name: string; maxLength: number }[] }
  const index = before.fields.findIndex((field) => field.name === 'title')
  // One more than the file holds, so a retry after a failed run still changes the value.
  const next = before.fields[index].maxLength + 1

  await page.goto(`/studio?app=PurchaseRequests&resource=${entity.id}`)
  await page.getByTestId(`studio-field-/fields/${index}/maxLength`).getByRole('spinbutton').fill(String(next))
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByTestId('studio-saved')).toBeVisible()

  const after: unknown = JSON.parse(await readFile(file, 'utf8'))
  before.fields[index].maxLength = next
  // The string form also compares the order of the keys.
  expect(JSON.stringify(after)).toBe(JSON.stringify(before))
})

test('an author follows a Used by entry from a department to the entity that uses it', async ({ page }) => {
  await useSession(page, 'hoa')
  const list = await page.request.get('/api/studio/apps/PurchaseRequests/resources')
  const { items } = (await list.json()) as { items: { id: string; file: string }[] }
  const department = items.find((item) => item.file === 'entities/department.json')!
  const entity = items.find((item) => item.file === 'entities/purchase-request.json')!

  await page.goto(`/studio?app=PurchaseRequests&resource=${department.id}`)
  await page
    .getByTestId('studio-used-by')
    .getByTestId('studio-reference')
    .filter({ hasText: 'entities/purchase-request.json · Path: /fields/3/target' })
    .getByRole('button', { name: 'PurchaseRequest' })
    .click()

  await expect(page).toHaveURL(new RegExp(`resource=${entity.id}`))
  await expect(page.getByTestId('studio-resource-details')).toContainText('entities/purchase-request.json')
})

test('an author selects a diagnostic and the editor shows the field at its path', async ({ page }) => {
  await useSession(page, 'hoa')
  const list = await page.request.get('/api/studio/apps/PurchaseRequests/resources')
  const { items } = (await list.json()) as { items: { id: string; file: string }[] }
  const endpoint = items.find((item) => item.file === 'endpoints/submit-purchase-request.json')!
  const file = path.join(studioFolder, 'endpoints/submit-purchase-request.json')
  const original = await readFile(file, 'utf8')

  try {
    await page.goto(`/studio?app=PurchaseRequests&resource=${endpoint.id}`)
    await page.getByTestId('studio-field-/input/2/target').getByRole('textbox').fill('NoSuchEntity')
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()

    await page.goto('/studio?app=PurchaseRequests')
    const diagnostic = page
      .getByTestId('studio-diagnostics')
      .getByRole('button')
      .filter({ hasText: 'Path: /input/2/target' })
    await expect(diagnostic).toContainText('AXC0012')
    await diagnostic.click()

    await expect(page).toHaveURL(/path=%2Finput%2F2%2Ftarget/)
    await expect(page.getByTestId('studio-field-/input/2/target')).toBeInViewport()
  } finally {
    await writeFile(file, original)
  }
})

test('someone without the author role sees no Studio link and a forbidden page', async ({ page }) => {
  await useSession(page, 'anna')

  await page.goto('/')
  const banner = page.getByRole('banner')
  await expect(banner.getByRole('button', { name: 'Anna Employee' })).toBeVisible()
  await expect(banner.getByRole('link', { name: 'Studio' })).toHaveCount(0)
  await page.goto('/studio')

  await expect(page.getByTestId('studio-forbidden')).toBeVisible()
})
