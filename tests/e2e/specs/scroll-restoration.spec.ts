import { expect, test } from '../test'
import type { APIRequestContext, Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { cp, mkdtemp, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { startServer } from '../activate'
import { csrfHeaders } from '../csrf'
import { e2eApp, purchaseRequests } from '../server-settings'
import { sessionFile, useSession } from '../sessions'

// A server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

const scrollY = (page: Page) => page.evaluate(() => window.scrollY)

test.describe('Studio on a copy of its own', () => {
  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-scroll-'))
    await cp(purchaseRequests, copy, { recursive: true })
    for (let index = 0; index < 40; index++) {
      const name = `scroll-${String(index).padStart(2, '0')}`
      await writeFile(
        path.join(copy, 'roles', `${name}.json`),
        JSON.stringify({ id: randomUUID(), kind: 'role', name, formatVersion: 1, label: { textKey: 'role.employee' } }),
      )
    }
    server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  test('an author goes Back from a resource to the same place in a long kind list', async ({ page }) => {
    await useSession(page, 'author')
    await page.setViewportSize({ width: 1280, height: 600 })
    const rows = page.locator('[data-testid^="studio-resource-"]')

    // A kind's list shows 20 resources a page, so the page size of 100 keeps all 45 roles in one long page.
    await page.goto(`${server.url}/studio?app=PurchaseRequests&kind=role&pageSize=100`)
    // The added roles share one label, so the server orders them by id. The row to go back to is the one the table
    // shows second to last, whatever its id is.
    await expect.poll(() => rows.count()).toBeGreaterThan(40)
    const lateTestId = await rows.nth((await rows.count()) - 2).getAttribute('data-testid')
    const late = lateTestId!.slice('studio-resource-'.length)
    const lateButton = page.getByTestId(lateTestId!)
    await lateButton.scrollIntoViewIfNeeded()
    const before = await scrollY(page)
    expect(before).toBeGreaterThan(0)

    await lateButton.click()
    await expect(page).toHaveURL(new RegExp(`[?&]resource=${late}$`))
    await expect.poll(() => scrollY(page)).toBe(0)

    await page.goBack()
    await expect(page).toHaveURL(/[?&]kind=role&pageSize=100$/)
    await expect(lateButton).toBeVisible()
    await expect.poll(() => scrollY(page)).toBe(before)
  })
})

test.describe('A long table page of a site', () => {
  test.use({ storageState: sessionFile('requester') })

  async function createNote(request: APIRequestContext, title: string) {
    const response = await request.post('/api/apps/E2eApp/entities/Note/records', {
      data: { values: { title } },
      headers: csrfHeaders,
    })
    expect(response.status()).toBe(201)
    return ((await response.json()) as { id: string }).id
  }

  test('goes Back to the same place once its rows have loaded, and opens a record and another page at the top', async ({
    page,
    request,
  }) => {
    const run = `${Date.now()}-${test.info().retry}`
    const titles: string[] = []
    const ids: string[] = []
    for (let index = 1; index <= 40; index++) {
      const title = `Aaa scroll ${run} ${String(index).padStart(2, '0')}`
      titles.push(title)
      ids.push(await createNote(request, title))
    }
    const lateTitle = titles[titles.length - 2]
    const lateId = ids[ids.length - 2]
    const lateRow = page.getByRole('row', { name: new RegExp(lateTitle) })
    const rowsPath = /\/data-sources\/NoteRows\/rows/
    await page.setViewportSize({ width: 1280, height: 600 })

    await page.goto('/e2e/noterows')
    await expect(lateRow).toBeVisible()
    await lateRow.scrollIntoViewIfNeeded()
    const before = await scrollY(page)
    expect(before).toBeGreaterThan(0)

    await lateRow.getByRole('link', { name: lateTitle, exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`/e2e/noteform/${lateId}$`))
    await expect.poll(() => scrollY(page)).toBe(0)

    // Hold the rows response of the page after Back until the test lets it go.
    let release: () => void = () => {}
    const released = new Promise<void>((resolve) => {
      release = resolve
    })
    let held: () => void = () => {}
    const requested = new Promise<void>((resolve) => {
      held = resolve
    })
    await page.route(rowsPath, async (route) => {
      held()
      await released
      await route.continue()
    })

    await page.goBack()
    await requested
    await expect(page).toHaveURL(/\/e2e\/noterows$/)
    await expect(lateRow).toHaveCount(0)
    release()
    await expect(lateRow).toBeVisible()
    await expect.poll(() => scrollY(page)).toBe(before)
    await page.unroute(rowsPath)

    await page
      .getByRole('navigation', { name: 'Menu', exact: true })
      .getByRole('link', { name: 'Categories', exact: true })
      .click()
    await expect(page).toHaveURL(/\/e2e\/categories$/)
    await expect.poll(() => scrollY(page)).toBe(0)
  })
})
