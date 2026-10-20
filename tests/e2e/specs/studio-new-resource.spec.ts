import { cp, mkdtemp, readFile, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'
import { startServer } from '../activate'
import { e2eApp, studioFolder } from '../server-settings'
import { useSession } from '../sessions'

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

// The journeys create files, and the other Studio specs compare the diagnostics of the shared copy. So these run on
// a second server whose Studio edits a copy of its own, without texts/vi.json. The copy is not the E2E server's.
test.describe('Studio creates a resource from a starter', () => {
  test.describe.configure({ mode: 'serial' })

  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-new-resource-'))
    await cp(studioFolder, copy, { recursive: true })
    await rm(path.join(copy, 'texts', 'vi.json'), { force: true })
    // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server and the live
    // release of the main E2E server is not activated again.
    server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  async function create(page: Page, kind: string, label: string, value: string) {
    await page.getByRole('button', { name: 'New resource' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('combobox', { name: 'Kind' }).click()
    await page.getByTitle(kind, { exact: true }).click()
    await dialog.getByRole('textbox', { name: label }).fill(value)
    await dialog.getByRole('button', { name: 'Create' }).click()
  }

  test('an author creates an entity and a page, and each opens in the editor', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await create(page, 'entity', 'Name', 'CostCentre')
    await expect(page).toHaveURL(/resource=/)
    await expect(page.getByTestId('studio-resource-details')).toContainText('entities/cost-centre.json')
    await expect(page.getByTestId('studio-editor')).toBeVisible()

    await create(page, 'page', 'Name', 'Overview')
    await expect(page).toHaveURL(/resource=/)
    await expect(page.getByTestId('studio-resource-details')).toContainText('pages/overview.json')
    await expect(page.getByTestId('studio-editor')).toBeVisible()
  })

  test('an author adds a vi text resource that holds every key of en', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await create(page, 'text', 'Locale', 'vi')
    await expect(page).toHaveURL(/resource=/)
    await expect(page.getByTestId('studio-resource-details')).toContainText('texts/vi.json')
    await expect(page.getByTestId('studio-editor')).toBeVisible()

    const keys = async (locale: string) => {
      const { texts } = JSON.parse(await readFile(path.join(copy, 'texts', `${locale}.json`), 'utf8')) as {
        texts: Record<string, string>
      }
      return Object.keys(texts).toSorted()
    }
    const english = await keys('en')
    expect(english.length).toBeGreaterThan(0)
    expect(await keys('vi')).toEqual(english)
  })
})
