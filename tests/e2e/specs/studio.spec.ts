import { expect, test } from '@playwright/test'
import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { startServer } from '../activate'
import { e2eApp, studioFolder } from '../server-settings'
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

test('an author sees an expression error as they type, and its type once fixed, without saving', async ({ page }) => {
  await useSession(page, 'hoa')
  const list = await page.request.get('/api/studio/apps/PurchaseRequests/resources')
  const { items } = (await list.json()) as { items: { id: string; file: string }[] }
  const entity = items.find((item) => item.file === 'entities/purchase-request.json')!
  const file = path.join(studioFolder, 'entities/purchase-request.json')
  const before = await readFile(file, 'utf8')

  await page.goto(`/studio?app=PurchaseRequests&resource=${entity.id}`)
  const field = page.getByTestId('studio-field-/validations/0/expression')
  const editor = field.getByRole('textbox')
  await expect(field.getByTestId('studio-expression-type')).toContainText('boolean')

  await editor.fill('title > 5')
  await expect(field.locator('.cm-lintRange-error')).toBeVisible()
  await expect(field.getByTestId('studio-expression-error')).toBeVisible()

  await editor.fill('totalAmount > 0')
  await expect(field.locator('.cm-lintRange-error')).toHaveCount(0)
  await expect(field.getByTestId('studio-expression-type')).toContainText('boolean')

  expect(await readFile(file, 'utf8')).toBe(before)
})


test('an author completes the names of a policy filter without saving', async ({ page }) => {
  await useSession(page, 'hoa')
  const list = await page.request.get('/api/studio/apps/PurchaseRequests/resources')
  const { items } = (await list.json()) as { items: { id: string; file: string }[] }
  const policy = items.find((item) => item.file === 'policies/employee-access.json')!
  const file = path.join(studioFolder, 'policies/employee-access.json')
  const before = await readFile(file, 'utf8')

  await page.goto(`/studio?app=PurchaseRequests&resource=${policy.id}`)
  const field = page.getByTestId('studio-field-/grants/0/filter')
  await expect(field.getByTestId('studio-expression-type')).toContainText('boolean')
  const editor = field.getByRole('textbox')
  await editor.click()
  await editor.press('ControlOrMeta+A')
  await editor.pressSequentially('req')
  await expect(page.getByRole('option', { name: 'requester' })).toBeVisible()

  await editor.press('ControlOrMeta+A')
  await editor.pressSequentially('cur')
  await expect(page.getByRole('option', { name: 'currentUser()' })).toBeVisible()

  expect(await readFile(file, 'utf8')).toBe(before)
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

test('an author saves a resource while the server restarts, and sees the saved version after the reconnect', async ({
  page,
}) => {
  test.setTimeout(240_000)
  // A second server on a spare port, so the restart does not touch the server the other specs use.
  let server = await startServer({ ActivateOnStartup__0: e2eApp })
  try {
    await useSession(page, 'hoa')
    expect((await page.request.get(`${server.url}/api/me`)).status()).toBe(200)
    const list = await page.request.get(`${server.url}/api/studio/apps/PurchaseRequests/resources`)
    const { items } = (await list.json()) as { items: { id: string; file: string }[] }
    const supplier = items.find((item) => item.file === 'entities/supplier.json')!
    const department = items.find((item) => item.file === 'entities/department.json')!
    const file = path.join(studioFolder, 'entities/supplier.json')
    const before = JSON.parse(await readFile(file, 'utf8')) as { fields: { maxLength: number }[] }
    const next = before.fields[1].maxLength + 1

    await page.goto(`${server.url}/studio?app=PurchaseRequests&resource=${supplier.id}`)
    await page.getByTestId('studio-field-/fields/1/maxLength').getByRole('spinbutton').fill(String(next))
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()

    const { port } = server
    await server.stop()
    await page.getByTestId(`studio-resource-${department.id}`).click()
    await expect(page.getByTestId('studio-reconnecting')).toBeVisible()

    server = await startServer({ ActivateOnStartup__0: e2eApp }, port)
    await expect(page.getByTestId('studio-reconnecting')).toBeHidden({ timeout: 90_000 })
    await page.getByTestId(`studio-resource-${supplier.id}`).click()
    await expect(page.getByTestId('studio-field-/fields/1/maxLength').getByRole('spinbutton')).toHaveValue(
      String(next),
    )
  } finally {
    await server.stop()
  }
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

test('an author adds a department in the seed grid and the file holds only that new record', async ({ page }) => {
  await useSession(page, 'hoa')
  const list = await page.request.get('/api/studio/apps/PurchaseRequests/resources')
  const { items } = (await list.json()) as { items: { id: string; file: string }[] }
  const seed = items.find((item) => item.file === 'seeds/departments.json')!
  const file = path.join(studioFolder, 'seeds/departments.json')
  const original = await readFile(file, 'utf8')
  const before = JSON.parse(original) as { records: { id: string; values: Record<string, unknown> }[] }
  const diagnosticsBefore = await (await page.request.get('/api/studio/apps/PurchaseRequests/diagnostics')).json()
  // Short and unique, because the code holds at most 20 characters.
  const code = `T${Date.now().toString(36)}`

  try {
    await page.goto(`/studio?app=PurchaseRequests&resource=${seed.id}`)
    await expect(page.getByTestId('studio-seed-row')).toHaveCount(before.records.length)
    await page.getByRole('button', { name: 'Add record' }).click()
    const row = page.getByTestId('studio-seed-row').nth(before.records.length)
    await row.getByRole('textbox', { name: 'code' }).fill(code)
    await row.getByRole('textbox', { name: 'name' }).fill('Test department')
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()

    const diagnosticsAfter = await (await page.request.get('/api/studio/apps/PurchaseRequests/diagnostics')).json()
    expect(diagnosticsAfter).toEqual(diagnosticsBefore)
    const after = JSON.parse(await readFile(file, 'utf8')) as typeof before
    expect(after.records).toHaveLength(before.records.length + 1)
    expect(after.records.slice(0, before.records.length)).toEqual(before.records)
    const added = after.records[before.records.length]
    expect(added.id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/)
    expect(added.values).toEqual({ code, name: 'Test department' })
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

test('an author without the publisher role sees Validate and no Publish', async ({ page }) => {
  // A second server whose publisher role nobody holds, so hoa is an author but not a publisher.
  const server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__PublisherRole: 'release-manager' })
  try {
    await useSession(page, 'hoa')

    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await expect(page.getByRole('button', { name: 'Validate' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Publish' })).toHaveCount(0)
  } finally {
    await server.stop()
  }
})
