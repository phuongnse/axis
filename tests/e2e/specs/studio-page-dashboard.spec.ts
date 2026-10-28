import { randomUUID } from 'node:crypto'
import { mkdtemp, readdir, readFile, rm, writeFile } from 'node:fs/promises'
import os from 'node:os'
import path from 'node:path'
import { expect, test } from '../test'
import type { Page } from '@playwright/test'
import { startServer } from '../activate'
import { confirmDialog, waitForDialog } from '../dialogs'
import { pickAction, pickOption } from '../select'
import { e2eApp } from '../server-settings'
import { useSession } from '../sessions'
import { createResource, openFormTab } from '../studio-resources'

// A second server starts in beforeAll, and the journey creates an application and publishes it.
test.setTimeout(240_000)

// The journey publishes a new application, which would change the live release of the shared purchase request copy
// that other specs use. So it runs on a second server whose applications folder is a temporary one.
test.describe('Studio builds a dashboard page with a KPI and a chart', () => {
  test.describe.configure({ mode: 'serial' })

  let applications: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    applications = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-page-dashboard-'))
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

  /** The folder under the applications folder whose manifest has the name. */
  async function applicationFolder(name: string): Promise<string> {
    for (const folder of await readdir(applications)) {
      try {
        const manifest = JSON.parse(await readFile(path.join(applications, folder, 'application.json'), 'utf8'))
        if (manifest.name === name) return path.join(applications, folder)
      } catch {
        // A folder without a manifest is not an application.
      }
    }
    throw new Error(`No application called ${name} under ${applications}.`)
  }

  async function pick(page: Page, field: string, option: string) {
    await pickOption(page.getByTestId(field).getByRole('combobox'), option)
  }

  /** The Add widget picker of the page's own widget list, not of a list inside a container. */
  function addWidgetPicker(page: Page) {
    return page
      .getByTestId('studio-widget-list-/widgets')
      .locator('xpath=..')
      .locator('xpath=./div[not(@data-testid)]')
      .getByRole('combobox')
  }

  /** Writes the text of a label field in both locales and saves the texts. */
  async function writeTexts(page: Page, field: string, en: string, vi: string) {
    const label = page.getByTestId(field)
    await label.getByRole('textbox', { name: 'en', exact: true }).fill(en)
    await label.getByRole('textbox', { name: 'vi', exact: true }).fill(vi)
    await label.getByRole('button', { name: 'Write texts', exact: true }).click()
    await expect(label.getByTestId('studio-label-written')).toBeVisible()
  }

  /** Writes a data source with one measure, `requests`, that counts the requests, and a read grant for the requester. */
  async function addAggregate(folder: string, file: string, name: string, groupBy: string[]) {
    await writeFile(
      path.join(folder, 'data-sources', `${file}.json`),
      JSON.stringify({
        id: randomUUID(),
        kind: 'dataSource',
        name,
        formatVersion: 1,
        entity: 'Request',
        fields: [{ name: 'status', path: 'status' }],
        aggregate: { groupBy, measures: [{ name: 'requests', function: 'count' }] },
      }),
    )
    const policyFile = path.join(folder, 'policies', 'requester-access.json')
    const policy = JSON.parse(await readFile(policyFile, 'utf8')) as { grants: unknown[] }
    policy.grants.push({ dataSource: name, actions: ['read'] })
    await writeFile(policyFile, JSON.stringify(policy, null, 2))
  }

  test('an author builds a dashboard with a KPI and a chart, publishes it and opens it on the site', async ({ page }) => {
    const name = `Dashboard${Date.now().toString(36)}`
    const stamp = Date.now().toString(36)
    await useSession(page, 'author')
    await page.goto(`${server.url}/studio?app=PurchaseRequests`)

    await page.getByRole('button', { name: 'New application', exact: true }).click()
    const dialog = await waitForDialog(page)
    await dialog.getByRole('textbox', { name: 'Name', exact: true }).fill(name)
    await dialog.getByRole('radio', { name: /^Request approval/ }).click()
    await dialog.getByRole('button', { name: 'Create', exact: true }).click()
    await expect(page).toHaveURL(new RegExp(`app=${name}$`))
    await expect(dialog).toBeHidden()

    // The template has no aggregate data source, so the journey adds a total one and a grouped one.
    const folder = await applicationFolder(name)
    await addAggregate(folder, 'request-totals', 'RequestTotals', [])
    await addAggregate(folder, 'requests-by-status', 'RequestsByStatus', ['status'])

    await createResource(page, 'page', 'Dashboard')
    await openFormTab(page)
    const editor = page.getByTestId('studio-page-editor')
    await expect(editor.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('Dashboard')

    // The names load again, so the new data sources are offered below.
    await page.reload()
    await openFormTab(page)
    await expect(editor.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('Dashboard')

    await writeTexts(page, 'studio-field-/title', `Dashboard ${stamp}`, `Bảng điều khiển ${stamp}`)

    await pickAction(addWidgetPicker(page), 'kpi', page.getByTestId('studio-widget-/widgets/1'))
    await page.getByTestId('studio-widget-/widgets/1').click()
    await pick(page, 'studio-field-/widgets/1/dataSource', 'RequestTotals')
    await pick(page, 'studio-field-/widgets/1/measure', 'requests')
    await writeTexts(page, 'studio-field-/widgets/1/label', `Requests ${stamp}`, `Yêu cầu ${stamp}`)

    await pickAction(addWidgetPicker(page), 'chart', page.getByTestId('studio-widget-/widgets/2'))
    await page.getByTestId('studio-widget-/widgets/2').click()
    await pick(page, 'studio-field-/widgets/2/dataSource', 'RequestsByStatus')
    await pick(page, 'studio-field-/widgets/2/category', 'status')
    await pick(page, 'studio-field-/widgets/2/measures/0/name', 'requests')
    await writeTexts(page, 'studio-field-/widgets/2/measures/0/label', `Count ${stamp}`, `Số lượng ${stamp}`)
    await writeTexts(page, 'studio-field-/widgets/2/title', `By status ${stamp}`, `Theo trạng thái ${stamp}`)

    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()

    const saved = JSON.parse(await readFile(path.join(folder, 'pages', 'dashboard.json'), 'utf8')) as {
      widgets: Record<string, unknown>[]
    }
    expect(saved.widgets[1]).toMatchObject({ type: 'kpi', dataSource: 'RequestTotals', measure: 'requests' })
    expect(saved.widgets[2]).toMatchObject({
      type: 'chart',
      dataSource: 'RequestsByStatus',
      kind: 'bar',
      category: 'status',
      measures: [{ name: 'requests' }],
    })

    await page.getByRole('button', { name: 'Publish', exact: true }).click()
    await confirmDialog(page, 'Publish')
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 60_000,
    })

    const sites = await readdir(path.join(folder, 'sites'))
    const site = JSON.parse(await readFile(path.join(folder, 'sites', sites[0]), 'utf8')) as { path: string }
    // The requester holds the requester role, whose policy reads both data sources.
    await useSession(page, 'requester')
    await page.goto(`${server.url}/${site.path}/dashboard`)
    await expect(page.getByTestId('kpi-tile')).toContainText(`Requests ${stamp}`)
    await expect(page.getByTestId('chart-widget')).toContainText(`By status ${stamp}`)
  })
})
