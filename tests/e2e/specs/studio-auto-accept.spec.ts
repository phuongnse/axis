import { type Page } from '@playwright/test'
import { expect, test } from '../test'
import { cp, mkdtemp, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { startServer } from '../activate'
import { e2eApp, purchaseRequests } from '../server-settings'
import { useSession } from '../sessions'

// Two servers start in the test, which takes a few seconds each.
test.setTimeout(240_000)

const switchName = 'Let AI accept its own drafts'

// The setting lives in the tenant database, which every E2E server shares, so it is set through the API and left off.
async function setAutoAccept(page: Page, serverUrl: string, autoAccept: boolean) {
  const response = await page.request.put(`${serverUrl}/api/studio/apps/PurchaseRequests/settings`, {
    headers: { 'X-Axis-Csrf': '1' },
    data: { autoAccept },
  })
  expect(response.status()).toBe(200)
}

async function openSettings(page: Page, serverUrl: string) {
  await page.goto(`${serverUrl}/studio?app=PurchaseRequests`)
  await page.getByRole('navigation', { name: 'Menu', exact: true }).getByRole('link', { name: 'Settings', exact: true }).click()
  await expect(page.getByTestId('studio-auto-accept')).toBeVisible()
}

test.describe('Auto-accept in Studio', () => {
  test.describe.configure({ mode: 'default' })

  let copy: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    copy = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-auto-accept-'))
    await cp(purchaseRequests, copy, { recursive: true })
    server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__Folders__0: copy })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (copy) {
      await rm(copy, { recursive: true, force: true })
    }
  })

  test('a publisher turns the setting on in Settings and it stays on after a reload, and an author who is not a publisher sees it read-only', async ({
    page,
  }) => {
    await useSession(page, 'author')
    await setAutoAccept(page, server.url, false)
    try {
      await openSettings(page, server.url)
      const autoAccept = page.getByRole('switch', { name: switchName, exact: true })
      await expect(autoAccept).not.toBeChecked()
      await expect(autoAccept).toBeEnabled()
      await expect(page.getByTestId('studio-auto-accept-publisher-only')).toHaveCount(0)

      await autoAccept.click()
      await expect(autoAccept).toBeChecked()
      await expect(page.getByTestId('studio-auto-accept-save-error')).toHaveCount(0)

      await page.reload()
      await expect(page.getByRole('switch', { name: switchName, exact: true })).toBeChecked()

      // A second server whose publisher role nobody holds, on the same tenant database, so author is not a publisher.
      const noPublisher = await startServer({
        ActivateOnStartup__0: e2eApp,
        Studio__PublisherRole: 'release-manager',
        Studio__Folders__0: copy,
      })
      try {
        await openSettings(page, noPublisher.url)
        const readOnly = page.getByRole('switch', { name: switchName, exact: true })
        await expect(readOnly).toBeChecked()
        await expect(readOnly).toBeDisabled()
        await expect(page.getByTestId('studio-auto-accept-publisher-only')).toHaveText('Only a publisher can change this.')
      } finally {
        await noPublisher.stop()
      }
    } finally {
      await setAutoAccept(page, server.url, false)
    }
  })
})
