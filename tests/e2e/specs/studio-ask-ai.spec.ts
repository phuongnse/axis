import { randomUUID } from 'node:crypto'
import { cp, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { type Page } from '@playwright/test'
import { expect, test } from '../test'
import { startServer } from '../activate'
import { e2eApp, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'

// A second server starts in one test, which takes a few seconds.
test.setTimeout(240_000)

const checkBoxName = 'Accept when checks and review pass'

// The setting lives in the tenant database, kept per application id, so it is set through the API.
async function setAutoAccept(page: Page, serverUrl: string, autoAccept: boolean) {
  const response = await page.request.put(`${serverUrl}/api/studio/apps/PurchaseRequests/settings`, {
    headers: { 'X-Axis-Csrf': '1' },
    data: { autoAccept },
  })
  expect(response.status()).toBe(200)
}

async function openAskAi(page: Page, serverUrl: string) {
  await page.goto(`${serverUrl}/studio?app=PurchaseRequests`)
  await page.getByTestId('studio-ask-ai').click()
  await expect(page.getByTestId('studio-ask-ai-panel')).toBeVisible()
}

test.describe('Ask AI in Studio', () => {
  test.describe.configure({ mode: 'default' })

  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-ask-ai-'))
    await cp(purchaseRequests, copy, { recursive: true })
    // The auto-accept setting is kept per application id in the tenant database every E2E server shares, so the copy has
    // an id of its own and the other specs' settings do not reach it.
    const manifestFile = path.join(copy, 'application.json')
    const manifest = JSON.parse(await readFile(manifestFile, 'utf8')) as { id: string }
    manifest.id = randomUUID()
    await writeFile(manifestFile, JSON.stringify(manifest, null, 2))
    server = await startServer({
      ActivateOnStartup__0: e2eApp,
      Studio__Folders__0: copy,
      Ai__DefaultBinding: 'scripted',
    })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  test('the check box shows when the application allows auto-accept and is missing when it does not', async ({ page }) => {
    await useSession(page, 'author')
    await setAutoAccept(page, server.url, true)
    try {
      await openAskAi(page, server.url)
      await expect(page.getByRole('checkbox', { name: checkBoxName, exact: true })).toBeVisible()
      await expect(page.getByTestId('studio-mcp-address')).toContainText(`${server.url}/mcp`)

      await setAutoAccept(page, server.url, false)
      await page.reload()
      await page.getByTestId('studio-ask-ai').click()
      await expect(page.getByTestId('studio-ask-ai-panel')).toBeVisible()
      await expect(page.getByRole('checkbox', { name: checkBoxName, exact: true })).toHaveCount(0)
    } finally {
      await setAutoAccept(page, server.url, false)
    }
  })

  test('a requester sees the forbidden page and no Ask AI, and so does an author on a server without a default binding', async ({
    page,
  }) => {
    await useSession(page, 'requester')
    await page.goto(`${server.url}/studio`)
    await expect(page.getByTestId('studio-forbidden')).toBeVisible()
    await expect(page.getByTestId('studio-ask-ai')).toHaveCount(0)

    const noBinding = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
    try {
      await useSession(page, 'author')
      await page.goto(`${noBinding.url}/studio?app=PurchaseRequests`)
      await expect(page.getByTestId('studio-publish')).toBeVisible()
      await expect(page.getByTestId('studio-ask-ai')).toHaveCount(0)
    } finally {
      await noBinding.stop()
    }
  })
})
