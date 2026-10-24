import { mkdtemp, readdir, rm } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test, type Page } from '@playwright/test'
import { startServer } from '../activate'
import { waitForDialog } from '../dialogs'
import { e2eApp } from '../server-settings'
import { useSession } from '../sessions'

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

// The journeys create folders under Studio:ApplicationsPath, which the main E2E server does not set. So these run
// on a second server whose applications folder is a temporary one. The Studio folder is only read here.
test.describe('Studio creates an application', () => {
  test.describe.configure({ mode: 'serial' })

  let applications: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    applications = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-new-application-'))
    // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server and the live
    // release of the main E2E server is not activated again.
    server = await startServer({ ActivateOnStartup__0: e2eApp, Studio__ApplicationsPath: applications })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (applications) {
      await rm(applications, { recursive: true, force: true })
    }
  })

  async function createApplication(page: Page, name: string, template?: string) {
    await page.getByRole('button', { name: 'New application', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill(name)
    if (template !== undefined) {
      await dialog.getByRole('radio', { name: new RegExp(`^${template}`) }).click()
    }
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
  }

  test('an author creates a blank application, and Studio picks it', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await createApplication(page, 'Inventory')

    await expect(page).toHaveURL(/app=Inventory$/)
    await expect(page.getByTestId('studio-group-site')).toBeVisible()
    await expect(page.getByTestId('studio-group-text')).toBeVisible()
    await expect(page.getByTestId('studio-diagnostics')).toContainText('No diagnostics')
  })

  test('an author creates an application from the request-approval template', async ({ page }) => {
    await useSession(page, 'hoa')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await createApplication(page, 'Onboarding', 'Request approval')

    await expect(page).toHaveURL(/app=Onboarding$/)
    await expect(page.getByTestId('studio-group-entity')).toBeVisible()
    await expect(page.getByTestId('studio-group-process')).toBeVisible()
    await expect(page.getByTestId('studio-group-site')).toBeVisible()
  })

  test('a taken name shows the conflict, and the folder and the picked application do not change', async ({ page }) => {
    await useSession(page, 'hoa')
    const before = await readdir(applications)
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await page.getByRole('button', { name: 'New application', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('PurchaseRequests')
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()

    await expect(dialog).toContainText('An application with this name already exists.')
    await expect(dialog).toBeVisible()
    await expect(page).toHaveURL(/app=PurchaseRequests$/)
    expect(await readdir(applications)).toEqual(before)
  })

  test('the main E2E server offers no New application button to an author', async ({ page }) => {
    await useSession(page, 'hoa')
    const me = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/me')
    await page.goto('/studio?app=PurchaseRequests')
    await me

    await expect(page.getByTestId('studio-diagnostics')).toBeVisible()
    await expect(page.getByRole('button', { name: /new application/i })).toHaveCount(0)
  })
})
