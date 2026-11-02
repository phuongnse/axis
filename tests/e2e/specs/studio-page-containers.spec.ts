import { mkdtemp, readdir, readFile, rm } from 'node:fs/promises'
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
test.describe('Studio builds a page with two columns', () => {
  test.describe.configure({ mode: 'serial' })

  let applications: string
  let server: Awaited<ReturnType<typeof startServer>>

  test.beforeAll(async () => {
    applications = await mkdtemp(path.join(os.tmpdir(), 'axis-studio-page-containers-'))
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

  test('an author builds a two-column page, publishes it and opens it on the site', async ({ page }) => {
    const name = `Columns${Date.now().toString(36)}`
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

    await createResource(page, 'page', 'Overview')
    await openFormTab(page)
    const editor = page.getByTestId('studio-page-editor')
    await expect(editor.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('Overview')

    // The names load again, so the entities of the new application are offered below.
    await page.reload()
    await openFormTab(page)
    await expect(editor.getByRole('textbox', { name: 'Name', exact: true })).toHaveValue('Overview')

    const title = page.getByTestId('studio-field-/title')
    await title.getByRole('textbox', { name: 'en', exact: true }).fill(`Overview ${stamp}`)
    await title.getByRole('textbox', { name: 'vi', exact: true }).fill(`Tổng hợp ${stamp}`)
    await title.getByRole('button', { name: 'Write texts', exact: true }).click()
    await expect(title.getByTestId('studio-label-written')).toBeVisible()

    await pickAction(addWidgetPicker(page), 'columns', page.getByTestId('studio-widget-/widgets/1'))
    const firstColumn = page.getByTestId('studio-widget-list-/widgets/1/columns/0/widgets')
    await pickAction(
      firstColumn.locator('xpath=..').locator('xpath=./div[not(@data-testid)]').getByRole('combobox'),
      'table',
      page.getByTestId('studio-widget-/widgets/1/columns/0/widgets/0'),
    )
    await page.getByTestId('studio-widget-/widgets/1/columns/0/widgets/0').click()
    await pick(page, 'studio-field-/widgets/1/columns/0/widgets/0/entity', 'Request')

    // The starter task inbox goes into the second column. The columns widget is then the first widget.
    await pickAction(
      page.getByTestId('studio-widget-row-/widgets/0').getByRole('combobox', { name: 'Move to', exact: true }),
      'Page › columns 2 › Column 2',
      page.getByTestId('studio-widget-/widgets/0/columns/1/widgets/0'),
    )
    await page.getByTestId('studio-widget-/widgets/0').click()
    const first = page.getByTestId('studio-field-/widgets/0/columns/0/width').getByRole('spinbutton')
    await first.fill('8')
    const second = page.getByTestId('studio-field-/widgets/0/columns/1/width').getByRole('spinbutton')
    await second.fill('4')
    await expect(first).toHaveValue('8')
    await expect(second).toHaveValue('4')

    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByTestId('studio-saved')).toBeVisible()

    const folder = await applicationFolder(name)
    const saved = JSON.parse(await readFile(path.join(folder, 'pages', 'overview.json'), 'utf8')) as {
      widgets: unknown[]
    }
    expect(saved.widgets).toEqual([
      {
        type: 'columns',
        name: 'columns1',
        columns: [
          { name: 'column1', width: 8, widgets: [{ type: 'table', name: 'table1', entity: 'Request' }] },
          { name: 'column2', width: 4, widgets: [{ type: 'taskInbox', name: 'taskInbox1' }] },
        ],
      },
    ])

    await page.getByRole('button', { name: 'Publish', exact: true }).click()
    await confirmDialog(page, 'Publish')
    await expect(page.getByTestId('studio-request-status')).toHaveAttribute('data-status', 'succeeded', {
      timeout: 60_000,
    })

    const sites = await readdir(path.join(folder, 'sites'))
    const site = JSON.parse(await readFile(path.join(folder, 'sites', sites[0]), 'utf8')) as { path: string }
    await page.goto(`${server.url}/${site.path}/overview`)
    await expect(page.getByRole('heading', { name: `Overview ${stamp}`, exact: true })).toBeVisible()
  })
})
