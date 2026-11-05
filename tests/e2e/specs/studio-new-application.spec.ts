import { cp, mkdir, mkdtemp, readdir, rm, writeFile } from 'node:fs/promises'
import { randomUUID } from 'node:crypto'
import os from 'node:os'
import path from 'node:path'
import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { startServer } from '../activate'
import { waitForDialog } from '../dialogs'
import { e2eApp, templatesPath } from '../server-settings'
import { useSession } from '../sessions'
import { openKind, openNewApplication } from '../studio-resources'
import { chooseTheme } from '../shell'

// A second server starts in beforeAll, which takes a few seconds.
test.setTimeout(180_000)

// The journeys create folders under Studio:ApplicationsPath, which the main E2E server does not set. So these run
// on a second server whose applications folder is a temporary one. The Studio folder is only read here.
test.describe('Studio creates an application', () => {
  test.describe.configure({ mode: 'serial' })

  let applications: string
  let templates: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    applications = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-new-application-'))
    templates = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-new-application-templates-'))
    await cp(templatesPath, templates, { recursive: true })
    await addBuiltInTemplate(templates)
    // Index 0 of ActivateOnStartup is the purchase request sample, so it stays out of this server and the live
    // release of the main E2E server is not activated again.
    server = await startServer({
      ActivateOnStartup__0: e2eApp,
      Studio__ApplicationsPath: applications,
      Templates__Path: templates,
    })
  })

  test.afterAll(async () => {
    await server?.stop()
    if (applications) {
      await rm(applications, { recursive: true, force: true })
    }
    if (templates) {
      await rm(templates, { recursive: true, force: true })
    }
  })

  // The built-in templates come before the others, in order of id, so the installed approvals application comes
  // first and this one second.
  async function addBuiltInTemplate(root: string) {
    const folder = path.join(root, 'tracker-test', '1.0.0')
    await mkdir(path.join(folder, 'sites'), { recursive: true })
    await mkdir(path.join(folder, 'identity-providers'), { recursive: true })
    await writeJson(path.join(folder, 'template.json'), {
      kind: 'applicationTemplate',
      id: 'tracker-test',
      version: '1.0.0',
      builtIn: true,
      title: { en: 'Tracker test', vi: 'Kiểm thử theo dõi' },
      description: { en: 'A built-in test template.', vi: 'Một mẫu kiểm thử tích hợp sẵn.' },
    })
    await writeJson(path.join(folder, 'application.json'), {
      id: randomUUID(),
      kind: 'application',
      name: 'Tracker test',
      formatVersion: 1,
    })
    await writeJson(path.join(folder, 'sites', 'tracker.json'), {
      id: randomUUID(),
      kind: 'site',
      name: 'Tracker',
      formatVersion: 1,
      path: 'tracker',
      title: { textKey: 'site.title' },
      locales: { default: 'en', fallback: 'en', available: ['en'] },
      navigation: [{ page: 'Home', label: { textKey: 'nav.home' } }],
    })
    await writeJson(path.join(folder, 'identity-providers', 'accounts.json'), {
      id: randomUUID(),
      kind: 'identityProvider',
      name: 'accounts',
      formatVersion: 1,
      type: 'accounts',
    })
  }

  async function writeJson(file: string, value: unknown) {
    await writeFile(file, JSON.stringify(value, null, 2))
  }

  async function createApplication(page: Page, name: string, template?: string) {
    await openNewApplication(page)
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill(name)
    if (template !== undefined) {
      await dialog.getByRole('radio', { name: new RegExp(`^${template}`) }).click()
    }
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
  }

  test('an author creates a blank application, and Studio picks it', async ({ page }) => {
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await createApplication(page, 'Inventory')

    await expect(page).toHaveURL(/app=Inventory$/)
    await openKind(page, 'site')
    await expect(page.getByTestId('studio-group-site')).toBeVisible()
    await openKind(page, 'text')
    await expect(page.getByTestId('studio-group-text')).toBeVisible()
    await expect(page.getByTestId('studio-problems')).toHaveCount(0)
  })

  test('an author creates an application from the built-in Approvals application', async ({ page }) => {
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await createApplication(page, 'Onboarding', 'Approvals')

    await expect(page).toHaveURL(/app=Onboarding$/)
    await openKind(page, 'role')
    await expect(page.getByTestId('studio-group-role')).toBeVisible()
    await openKind(page, 'site')
    await expect(page.getByTestId('studio-group-site')).toBeVisible()
    const response = await page.request.get(`${server.url}/api/studio/apps/Onboarding/diagnostics`)
    expect(response.status()).toBe(200)
    const { diagnostics } = (await response.json()) as { diagnostics: { severity: string }[] }
    expect(diagnostics.filter((diagnostic) => diagnostic.severity === 'error')).toEqual([])
  })

  test('a taken name shows the conflict, and the folder and the picked application do not change', async ({ page }) => {
    await useSession(page, 'author')
    const before = await readdir(applications)
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await openNewApplication(page)
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill('PurchaseRequests')
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()

    await expect(dialog).toContainText('An application with this name already exists.')
    await expect(dialog).toBeVisible()
    await expect(page).toHaveURL(/app=PurchaseRequests$/)
    expect(await readdir(applications)).toEqual(before)
  })

  test('the built-in template is the first option after Blank, in light and dark mode', async ({ page }) => {
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await expectBuiltInTemplateFirst(page)
    await chooseTheme(page, 'Dark')
    await page.reload()
    await expect(page.locator('[data-theme-mode="dark"]')).toBeVisible()
    await expectBuiltInTemplateFirst(page)
  })

  async function expectBuiltInTemplateFirst(page: Page) {
    await openNewApplication(page)
    const dialog = await waitForDialog(page)
    await expect(dialog.getByRole('radio')).toHaveCount(3)
    await expect(dialog.getByRole('radio').nth(0)).toHaveAccessibleName(/^Blank/)
    await expect(dialog.getByRole('radio').nth(1)).toHaveAccessibleName(/^Approvals.*Built in/)
    await expect(dialog.getByRole('radio').nth(2)).toHaveAccessibleName(/^Tracker test.*Built in/)
    await dialog.getByRole('button', { name: 'Cancel', exact: true }).click()
  }

  test('the main E2E server offers no New application button to an author', async ({ page }) => {
    await useSession(page, 'author')
    const me = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/me')
    await page.goto('/studio?app=PurchaseRequests')
    await me

    await expect(page.getByTestId('studio-band')).toBeVisible()
    await expect(page.getByRole('button', { name: /new application/i })).toHaveCount(0)
  })
})
